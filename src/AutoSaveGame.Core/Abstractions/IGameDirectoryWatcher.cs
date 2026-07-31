using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Abstractions;

public interface IGameDirectoryWatcher : IAsyncDisposable
{
    Task StartAsync(GameConfig game, CancellationToken cancellationToken);

    Task StopAsync(Guid gameId);
}

