using System.Security.Cryptography;
using AutoSaveGame.Infrastructure.Snapshots;

namespace AutoSaveGame.Infrastructure.Restore;

public sealed class RestoreFileOperations : IRestoreFileOperations
{
    public string CreateWorkspace(string targetDirectory)
    {
        var target = Path.GetFullPath(targetDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException(
                "The restore target must have a parent directory.");
        Directory.CreateDirectory(parent);
        var workspace = Path.Combine(
            parent,
            $".autosavegame-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    public async Task<string> CopyAndHashAsync(
        Stream source,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var output = new FileStream(
                         destination,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        await using var input = new FileStream(
            destination,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public Task<string> ComputeDirectoryHashAsync(
        string directory,
        CancellationToken cancellationToken) =>
        ContentHasher.ComputeDirectoryAsync(directory, cancellationToken);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void MoveDirectory(string source, string destination) =>
        Directory.Move(source, destination);

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

