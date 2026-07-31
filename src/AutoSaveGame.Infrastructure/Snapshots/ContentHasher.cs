using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AutoSaveGame.Infrastructure.Snapshots;

internal static class ContentHasher
{
    public static void AppendHeader(
        IncrementalHash hash,
        string normalizedRelativePath,
        long fileLength)
    {
        var pathBytes = Encoding.UTF8.GetBytes(normalizedRelativePath);
        Span<byte> pathLength = stackalloc byte[sizeof(int)];
        Span<byte> contentLength = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt32LittleEndian(pathLength, pathBytes.Length);
        BinaryPrimitives.WriteInt64LittleEndian(contentLength, fileLength);
        hash.AppendData(pathLength);
        hash.AppendData(pathBytes);
        hash.AppendData(contentLength);
    }

    public static string Finish(IncrementalHash hash) =>
        Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

    public static async Task<string> ComputeDirectoryAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var reader = new StableDirectoryReader(TimeSpan.Zero);
        var files = await reader.CaptureAsync(directory, cancellationToken)
            .ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];

        foreach (var file in files)
        {
            AppendHeader(hash, file.RelativePath, file.Length);
            await using var input = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return Finish(hash);
    }
}

