using System.IO;

namespace AutoSaveGame.App.Services;

public sealed class SessionRestoreArchiveStore : IRestoreArchiveStore
{
    private readonly string rootDirectory;

    public SessionRestoreArchiveStore(string? rootDirectory = null)
    {
        this.rootDirectory = rootDirectory ?? Path.Combine(
            Path.GetTempPath(),
            "AutoSaveGame",
            $"session-{Environment.ProcessId}",
            "restore");
    }

    public ValueTask<IRestoreArchiveHandle> CreateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(
            rootDirectory,
            $"restore-{Guid.NewGuid():N}.zip");
        Stream stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ValueTask.FromResult<IRestoreArchiveHandle>(
            new RestoreArchiveHandle(path, stream));
    }

    private sealed class RestoreArchiveHandle(string path, Stream stream)
        : IRestoreArchiveHandle
    {
        private bool disposed;

        public Stream Stream { get; } = stream;

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            await Stream.DisposeAsync().ConfigureAwait(false);
            try
            {
                File.Delete(path);
            }
            catch (FileNotFoundException)
            {
                // Cleanup is idempotent when another recovery path removed the file.
            }
        }
    }
}
