using System.IO;

namespace AutoSaveGame.App.Services;

public interface IRestoreArchiveStore
{
    ValueTask<IRestoreArchiveHandle> CreateAsync(
        CancellationToken cancellationToken);
}

public interface IRestoreArchiveHandle : IAsyncDisposable
{
    Stream Stream { get; }
}
