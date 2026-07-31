using AutoSaveGame.Infrastructure.Restore;

namespace AutoSaveGame.Infrastructure.Tests.TestSupport;

internal sealed class FaultingRestoreFileOperations(
    IRestoreFileOperations inner,
    int failOnMoveNumber) : IRestoreFileOperations
{
    private int moveCount;
    private bool faultInjected;

    public string CreateWorkspace(string targetDirectory) =>
        inner.CreateWorkspace(targetDirectory);

    public Task<string> CopyAndHashAsync(
        Stream source,
        string destination,
        CancellationToken cancellationToken) =>
        inner.CopyAndHashAsync(source, destination, cancellationToken);

    public Task<string> ComputeDirectoryHashAsync(
        string directory,
        CancellationToken cancellationToken) =>
        inner.ComputeDirectoryHashAsync(directory, cancellationToken);

    public bool DirectoryExists(string path) => inner.DirectoryExists(path);

    public void CreateDirectory(string path) => inner.CreateDirectory(path);

    public void MoveDirectory(string source, string destination)
    {
        moveCount++;
        if (!faultInjected && moveCount == failOnMoveNumber)
        {
            faultInjected = true;
            throw new IOException($"Injected move failure {moveCount}.");
        }

        inner.MoveDirectory(source, destination);
    }

    public void DeleteDirectory(string path) => inner.DeleteDirectory(path);

    public void DeleteFile(string path) => inner.DeleteFile(path);
}

