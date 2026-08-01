using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.Infrastructure.Watching;

public sealed class GameDirectoryWatcher : IGameDirectoryWatcher
{
    private readonly object gate = new();
    private readonly IBackupScheduler scheduler;
    private readonly PathTemplateService pathTemplates;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan reconciliationInterval;
    private readonly bool enableNativeEvents;
    private readonly Dictionary<Guid, Registration> registrations = [];
    private bool disposed;

    public GameDirectoryWatcher(
        IBackupScheduler scheduler,
        PathTemplateService pathTemplates,
        TimeProvider timeProvider,
        TimeSpan? reconciliationInterval = null,
        bool enableNativeEvents = true)
    {
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.pathTemplates = pathTemplates
            ?? throw new ArgumentNullException(nameof(pathTemplates));
        this.timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        this.reconciliationInterval =
            reconciliationInterval ?? TimeSpan.FromMinutes(5);
        if (this.reconciliationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reconciliationInterval));
        }

        this.enableNativeEvents = enableNativeEvents;
    }

    public async Task StartAsync(
        GameConfig game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ObjectDisposedException.ThrowIf(disposed, this);
        await StopAsync(game.GameId).ConfigureAwait(false);

        var directory = pathTemplates.Expand(game.PathTemplate);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Save directory does not exist: {directory}");
        }

        var fingerprint = await DirectoryFingerprint.ComputeAsync(
            directory,
            cancellationToken).ConfigureAwait(false);
        var cancellation = new CancellationTokenSource();
        FileSystemWatcher? watcher = null;
        if (enableNativeEvents)
        {
            watcher = CreateWatcher(game.GameId, directory);
        }

        var registration = new Registration(
            game.GameId,
            directory,
            fingerprint,
            cancellation,
            watcher);
        lock (gate)
        {
            registrations.Add(game.GameId, registration);
        }

        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = true;
        }
        registration.ReconciliationTask = ReconcileAsync(registration);
    }

    public async Task StopAsync(Guid gameId)
    {
        Registration? registration;
        lock (gate)
        {
            registrations.Remove(gameId, out registration);
        }

        if (registration is null)
        {
            return;
        }

        registration.Watcher?.Dispose();
        registration.Cancellation.Cancel();
        try
        {
            await registration.ReconciliationTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            registration.Cancellation.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Guid[] gameIds;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            gameIds = registrations.Keys.ToArray();
        }

        foreach (var gameId in gameIds)
        {
            await StopAsync(gameId).ConfigureAwait(false);
        }
    }

    private FileSystemWatcher CreateWatcher(Guid gameId, string directory)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.CreationTime,
        };
        FileSystemEventHandler changed = (_, _) => scheduler.MarkDirty(gameId);
        RenamedEventHandler renamed = (_, _) => scheduler.MarkDirty(gameId);
        ErrorEventHandler error = (_, _) => scheduler.MarkDirty(gameId);
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Deleted += changed;
        watcher.Renamed += renamed;
        watcher.Error += error;
        return watcher;
    }

    private async Task ReconcileAsync(Registration registration)
    {
        while (true)
        {
            await Task.Delay(
                    reconciliationInterval,
                    timeProvider,
                    registration.Cancellation.Token)
                .ConfigureAwait(false);
            var fingerprint = await DirectoryFingerprint.ComputeAsync(
                registration.Directory,
                registration.Cancellation.Token).ConfigureAwait(false);
            if (!string.Equals(
                    fingerprint,
                    registration.Fingerprint,
                    StringComparison.Ordinal))
            {
                registration.Fingerprint = fingerprint;
                scheduler.MarkDirty(registration.GameId);
            }
        }
    }

    private sealed class Registration(
        Guid gameId,
        string directory,
        string fingerprint,
        CancellationTokenSource cancellation,
        FileSystemWatcher? watcher)
    {
        public Guid GameId { get; } = gameId;

        public string Directory { get; } = directory;

        public string Fingerprint { get; set; } = fingerprint;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public FileSystemWatcher? Watcher { get; } = watcher;

        public Task ReconciliationTask { get; set; } = Task.CompletedTask;
    }
}
