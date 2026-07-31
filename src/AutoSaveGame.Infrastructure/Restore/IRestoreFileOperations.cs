namespace AutoSaveGame.Infrastructure.Restore;

public interface IRestoreFileOperations
{
    string CreateWorkspace(string targetDirectory);

    Task<string> CopyAndHashAsync(
        Stream source,
        string destination,
        CancellationToken cancellationToken);

    Task<string> ComputeDirectoryHashAsync(
        string directory,
        CancellationToken cancellationToken);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    void MoveDirectory(string source, string destination);

    void DeleteDirectory(string path);

    void DeleteFile(string path);
}

