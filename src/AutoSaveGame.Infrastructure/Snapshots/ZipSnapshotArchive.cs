using System.IO.Compression;
using System.Security.Cryptography;
using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Infrastructure.Snapshots;

public sealed class ZipSnapshotArchive : ISnapshotArchive
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    ];

    private readonly StableDirectoryReader directoryReader;

    public ZipSnapshotArchive()
        : this(TimeSpan.FromSeconds(1))
    {
    }

    public ZipSnapshotArchive(TimeSpan stabilityDelay)
    {
        if (stabilityDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stabilityDelay));
        }

        directoryReader = new StableDirectoryReader(stabilityDelay);
    }

    public async Task<SnapshotBuildResult> BuildAsync(
        string sourceDirectory,
        string archivePath,
        CancellationToken cancellationToken)
    {
        var sourceRoot = NormalizeDirectory(sourceDirectory);
        var outputPath = Path.GetFullPath(archivePath);
        if (IsWithin(outputPath, sourceRoot))
        {
            throw new InvalidOperationException(
                "Snapshot archives must be created outside the watched save directory.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        Exception? lastError = null;

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(outputPath);

            try
            {
                var files = await directoryReader.CaptureAsync(
                    sourceRoot,
                    cancellationToken).ConfigureAwait(false);
                var contentHash = await CreateArchiveAsync(
                    files,
                    outputPath,
                    cancellationToken).ConfigureAwait(false);
                var archiveHash = await ComputeFileHashAsync(
                    outputPath,
                    cancellationToken).ConfigureAwait(false);
                var archiveSize = new FileInfo(outputPath).Length;

                return new SnapshotBuildResult(
                    SnapshotBuildKind.Success,
                    contentHash,
                    archiveHash,
                    archiveSize,
                    outputPath,
                    null);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                TryDelete(outputPath);
                if (attempt == RetryDelays.Length)
                {
                    break;
                }

                await Task.Delay(RetryDelays[attempt], cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return SnapshotBuildResult.Pending(
            outputPath,
            lastError?.Message ?? "The save files are not stable.");
    }

    public async Task<SnapshotExtractResult> ExtractAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var stagingRoot = NormalizeDirectory(stagingDirectory);
        Directory.CreateDirectory(stagingRoot);
        if (Directory.EnumerateFileSystemEntries(stagingRoot).Any())
        {
            throw new InvalidOperationException("The restore staging directory must be empty.");
        }

        var rootPrefix = stagingRoot.EndsWith(Path.DirectorySeparatorChar)
            ? stagingRoot
            : stagingRoot + Path.DirectorySeparatorChar;
        var extractedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var input = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectSymlink(entry);

            var normalizedName = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedName)
                || normalizedName.StartsWith("/", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Invalid ZIP entry: {entry.FullName}");
            }

            var destination = Path.GetFullPath(
                Path.Combine(
                    stagingRoot,
                    normalizedName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"ZIP entry escapes the staging directory: {entry.FullName}");
            }

            var isDirectory = normalizedName.EndsWith("/", StringComparison.Ordinal);
            var duplicateKey = destination.TrimEnd(Path.DirectorySeparatorChar);
            if (!extractedPaths.Add(duplicateKey))
            {
                throw new InvalidDataException(
                    $"ZIP contains duplicate paths: {entry.FullName}");
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var entryInput = entry.Open();
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await entryInput.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        var contentHash = await ContentHasher.ComputeDirectoryAsync(
            stagingRoot,
            cancellationToken).ConfigureAwait(false);
        return new SnapshotExtractResult(contentHash);
    }

    private static async Task<string> CreateArchiveAsync(
        IReadOnlyList<StableFile> files,
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];

        foreach (var file in files)
        {
            var before = new FileInfo(file.FullPath);
            if (before.Length != file.Length || before.LastWriteTimeUtc != file.LastWriteTimeUtc)
            {
                throw new SnapshotNotStableException(
                    $"Save file changed before snapshot: {file.RelativePath}");
            }

            var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            ContentHasher.AppendHeader(contentHash, file.RelativePath, file.Length);

            await using var input = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var entryOutput = entry.Open();
            int read;
            long totalRead = 0;
            while ((read = await input.ReadAsync(buffer, cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                contentHash.AppendData(buffer.AsSpan(0, read));
                await entryOutput.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
                totalRead += read;
            }

            var after = new FileInfo(file.FullPath);
            if (totalRead != file.Length
                || after.Length != file.Length
                || after.LastWriteTimeUtc != file.LastWriteTimeUtc)
            {
                throw new SnapshotNotStableException(
                    $"Save file changed during snapshot: {file.RelativePath}");
            }
        }

        return ContentHasher.Finish(contentHash);
    }

    private static async Task<string> ComputeFileHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static bool IsWithin(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(
            root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static void RejectSymlink(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymlink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        if (unixMode == UnixSymlink
            || (windowsAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"ZIP symlink entries are not allowed: {entry.FullName}");
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
