using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.App.Services;

public interface IApplicationRuntime : IAsyncDisposable
{
    bool IsSignedIn { get; }

    IReadOnlyList<RuntimeGame> Games { get; }

    bool HasUnsafeChanges { get; }

    OperationProgress? CurrentOperation => null;

    event EventHandler? GamesChanged;

    event EventHandler? OperationChanged
    {
        add { }
        remove { }
    }

    Task SignInAsync(CancellationToken cancellationToken);

    Task AddOrUpdateGameAsync(
        Guid? gameId,
        string displayName,
        string absolutePath,
        CancellationToken cancellationToken);

    Task DeleteGameAsync(Guid gameId, CancellationToken cancellationToken);

    Task RestoreAsync(Guid gameId, CancellationToken cancellationToken);

    Task BackupNowAsync(Guid gameId, CancellationToken cancellationToken);

    Task SetWatchingAsync(
        Guid gameId,
        bool enabled,
        CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}

public sealed class RuntimeGame(
    GameConfig config,
    GameSyncStateMachine stateMachine,
    string? localPath = null)
{
    public GameConfig Config { get; private set; } = config;

    public GameSyncStateMachine StateMachine { get; } = stateMachine;

    public string LocalPath { get; } = localPath ?? config.PathTemplate;

    public void UpdateConfig(GameConfig config) => Config = config;
}
