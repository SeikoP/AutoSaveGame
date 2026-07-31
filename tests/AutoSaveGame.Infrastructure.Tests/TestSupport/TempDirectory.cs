namespace AutoSaveGame.Infrastructure.Tests.TestSupport;

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path)
    {
        Path = path;
        Directory.CreateDirectory(path);
    }

    public string Path { get; }

    public static TempDirectory Create() =>
        new(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "AutoSaveGame.Tests",
            Guid.NewGuid().ToString("N")));

    public string FilePath(string relativePath) =>
        System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Write(string relativePath, string content)
    {
        var path = FilePath(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public string Read(string relativePath) => File.ReadAllText(FilePath(relativePath));

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

