using AutoSaveGame.Infrastructure.Restore;

namespace AutoSaveGame.Infrastructure.Tests.TestSupport;

internal sealed class CancelingRestoreFileOperations(
    IRestoreFileOperations inner) : IRestoreFileOperations
{
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
        Task.FromException<string>(new OperationCanceledException(cancellationToken));

    public bool DirectoryExists(string path) => inner.DirectoryExists(path);

    public void CreateDirectory(string path) => inner.CreateDirectory(path);

    public void MoveDirectory(string source, string destination) =>
        inner.MoveDirectory(source, destination);

    public void DeleteDirectory(string path) => inner.DeleteDirectory(path);

    public void DeleteFile(string path) => inner.DeleteFile(path);
}

