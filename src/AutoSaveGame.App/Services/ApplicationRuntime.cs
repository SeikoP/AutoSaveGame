using System.IO;
using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.App.Services;

public sealed class ApplicationRuntime : IApplicationRuntime
{
    private readonly IUserSession session;
    private readonly ICatalogRepository catalogs;
    private readonly ICloudObjectStore cloud;
    private readonly IRestoreService restoreService;
    private readonly IBackupScheduler scheduler;
    private readonly IGameDirectoryWatcher watcher;
    private readonly PathTemplateService pathTemplates;
    private readonly IRestoreArchiveStore restoreArchiveStore;
    private readonly Func<GameConfig, Catalog, CancellationToken, Task<BackupResult>>
        backupOperation;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly List<RuntimeGame> games = [];
    private Catalog? currentCatalog;
    private bool disposed;

    public ApplicationRuntime(
        IUserSession session,
        ICatalogRepository catalogs,
        ICloudObjectStore cloud,
        IRestoreService restoreService,
        IBackupScheduler scheduler,
        IGameDirectoryWatcher watcher,
        PathTemplateService pathTemplates,
        IRestoreArchiveStore restoreArchiveStore,
        Func<GameConfig, Catalog, CancellationToken, Task<BackupResult>> backupOperation)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
        this.cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
        this.restoreService = restoreService
            ?? throw new ArgumentNullException(nameof(restoreService));
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        this.pathTemplates = pathTemplates
            ?? throw new ArgumentNullException(nameof(pathTemplates));
        this.restoreArchiveStore = restoreArchiveStore
            ?? throw new ArgumentNullException(nameof(restoreArchiveStore));
        this.backupOperation = backupOperation
            ?? throw new ArgumentNullException(nameof(backupOperation));
    }

    public bool IsSignedIn => session.IsSignedIn;

    public IReadOnlyList<RuntimeGame> Games => games;

    public bool HasUnsafeChanges => games.Any(game =>
        game.StateMachine.Status is
            GameSyncStatus.Dirty
            or GameSyncStatus.Pending
            or GameSyncStatus.Conflict
            or GameSyncStatus.Error);

    public event EventHandler? GamesChanged;

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await session.SignInAsync(cancellationToken).ConfigureAwait(false);
            var loaded = await catalogs.LoadAsync(cancellationToken).ConfigureAwait(false);
            currentCatalog = loaded.Kind switch
            {
                CatalogLoadKind.Empty => Catalog.Empty,
                CatalogLoadKind.Loaded when loaded.Catalog is not null => loaded.Catalog,
                CatalogLoadKind.Conflict =>
                    throw new InvalidOperationException(
                        "Google Drive contains conflicting catalog generations."),
                _ => throw new InvalidOperationException(
                    "Google Drive catalog is corrupt and was not overwritten."),
            };

            await catalogs.CleanupOrphansAsync(cancellationToken).ConfigureAwait(false);
            await ReplaceRuntimeGamesAsync(
                currentCatalog,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task AddOrUpdateGameAsync(
        Guid? gameId,
        string displayName,
        string absolutePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Guid id = default;
        var shouldBackup = false;
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = RequireCatalog();
            id = gameId ?? Guid.NewGuid();
            var existing = current.Games.SingleOrDefault(game => game.GameId == id);
            shouldBackup = existing is null;
            var config = new GameConfig(
                id,
                displayName.Trim(),
                pathTemplates.Collapse(absolutePath),
                existing?.Snapshot,
                existing?.WatchEnabled ?? true);
            var nextGames = current.Games
                .Where(game => game.GameId != id)
                .Append(config)
                .ToArray();
            await SaveAndReplaceAsync(
                current with
                {
                    Generation = current.Generation + 1,
                    Games = nextGames,
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }

        if (shouldBackup)
        {
            await scheduler.BackupNowAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteGameAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = RequireCatalog();
            var nextGames = current.Games
                .Where(game => game.GameId != gameId)
                .ToArray();
            if (nextGames.Length == current.Games.Count)
            {
                return;
            }

            await SaveAndReplaceAsync(
                current with
                {
                    Generation = current.Generation + 1,
                    Games = nextGames,
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task RestoreAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runtimeGame = FindGame(gameId);
            var snapshot = runtimeGame.Config.Snapshot
                ?? throw new InvalidOperationException(
                    "This game does not have a cloud backup yet.");
            await watcher.StopAsync(gameId).ConfigureAwait(false);

            if (runtimeGame.StateMachine.Status == GameSyncStatus.NotConfigured)
            {
                runtimeGame.StateMachine.TransitionTo(GameSyncStatus.Watching);
            }

            runtimeGame.StateMachine.TransitionTo(GameSyncStatus.Restoring);
            RestoreResult result;
            await using (var archiveHandle = await restoreArchiveStore.CreateAsync(
                             cancellationToken).ConfigureAwait(false))
            {
                var archive = archiveHandle.Stream;
                await cloud.DownloadAsync(
                    snapshot.ArchiveFileId,
                    archive,
                    cancellationToken).ConfigureAwait(false);
                await archive.FlushAsync(cancellationToken).ConfigureAwait(false);
                archive.Position = 0;
                result = await restoreService.RestoreAsync(
                    archive,
                    snapshot.ArchiveSha256,
                    snapshot.ContentSha256,
                    pathTemplates.Expand(runtimeGame.Config.PathTemplate),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!result.Success)
            {
                runtimeGame.StateMachine.TransitionTo(GameSyncStatus.Error);
                throw new InvalidOperationException(
                    result.RecoveryPath is null
                        ? result.Message
                        : $"{result.Message} Recovery path: {result.RecoveryPath}");
            }

            runtimeGame.StateMachine.TransitionTo(GameSyncStatus.Watching);
            if (runtimeGame.Config.WatchEnabled)
            {
                await watcher.StartAsync(
                    runtimeGame.Config,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public Task BackupNowAsync(
        Guid gameId,
        CancellationToken cancellationToken) =>
        scheduler.BackupNowAsync(gameId, cancellationToken);

    public async Task SetWatchingAsync(
        Guid gameId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = RequireCatalog();
            var nextGames = current.Games
                .Select(game => game.GameId == gameId
                    ? game with { WatchEnabled = enabled }
                    : game)
                .ToArray();
            await SaveAndReplaceAsync(
                current with
                {
                    Generation = current.Generation + 1,
                    Games = nextGames,
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ClearRuntimeGamesAsync().ConfigureAwait(false);
            currentCatalog = null;
            await session.SignOutAsync(cancellationToken).ConfigureAwait(false);
            GamesChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<BackupResult> PerformScheduledBackupAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runtimeGame = FindGame(gameId);
            var result = await backupOperation(
                runtimeGame.Config,
                RequireCatalog(),
                cancellationToken).ConfigureAwait(false);
            if (result.Kind == BackupKind.Success)
            {
                var loaded = await catalogs.LoadAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (loaded.Kind == CatalogLoadKind.Loaded && loaded.Catalog is not null)
                {
                    currentCatalog = loaded.Catalog;
                    foreach (var config in currentCatalog.Games)
                    {
                        games.Single(game => game.Config.GameId == config.GameId)
                            .UpdateConfig(config);
                    }

                    GamesChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            return result;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await ClearRuntimeGamesAsync().ConfigureAwait(false);
        await watcher.DisposeAsync().ConfigureAwait(false);
        if (scheduler is IAsyncDisposable asyncScheduler)
        {
            await asyncScheduler.DisposeAsync().ConfigureAwait(false);
        }

        operationGate.Dispose();
    }

    private async Task SaveAndReplaceAsync(
        Catalog next,
        CancellationToken cancellationToken)
    {
        var result = await catalogs.SaveCatalogAsync(
            RequireCatalog(),
            next,
            cancellationToken).ConfigureAwait(false);
        if (result.Kind != CatalogCommitKind.Success || result.Catalog is null)
        {
            throw new InvalidOperationException(
                result.Message ?? "Catalog update failed.");
        }

        currentCatalog = result.Catalog;
        await ReplaceRuntimeGamesAsync(
            currentCatalog,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaceRuntimeGamesAsync(
        Catalog catalog,
        CancellationToken cancellationToken)
    {
        await ClearRuntimeGamesAsync().ConfigureAwait(false);
        foreach (var config in catalog.Games)
        {
            var localPath = pathTemplates.Expand(config.PathTemplate);
            var pathExists = Directory.Exists(localPath);
            var state = new GameSyncStateMachine(
                pathExists ? GameSyncStatus.Watching : GameSyncStatus.NotConfigured);
            var runtimeGame = new RuntimeGame(config, state, localPath);
            games.Add(runtimeGame);
            scheduler.RegisterGame(config.GameId, state);
            if (config.WatchEnabled && pathExists)
            {
                await watcher.StartAsync(config, cancellationToken).ConfigureAwait(false);
            }
        }

        GamesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ClearRuntimeGamesAsync()
    {
        foreach (var game in games.ToArray())
        {
            await watcher.StopAsync(game.Config.GameId).ConfigureAwait(false);
            scheduler.UnregisterGame(game.Config.GameId);
        }

        games.Clear();
    }

    private Catalog RequireCatalog() =>
        currentCatalog
        ?? throw new InvalidOperationException("Sign in before changing games.");

    private RuntimeGame FindGame(Guid gameId) =>
        games.SingleOrDefault(game => game.Config.GameId == gameId)
        ?? throw new KeyNotFoundException($"Game is not configured: {gameId}");

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(disposed, this);
}
