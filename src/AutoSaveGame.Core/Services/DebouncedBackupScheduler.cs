using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Services;

public sealed class DebouncedBackupScheduler : IBackupScheduler, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Func<Guid, CancellationToken, Task<BackupResult>> backup;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan debounce;
    private readonly Dictionary<Guid, Entry> entries = [];
    private bool disposed;

    public DebouncedBackupScheduler(
        Func<Guid, CancellationToken, Task<BackupResult>> backup,
        TimeProvider timeProvider,
        TimeSpan debounce)
    {
        this.backup = backup ?? throw new ArgumentNullException(nameof(backup));
        this.timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        if (debounce < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounce));
        }

        this.debounce = debounce;
    }

    public void RegisterGame(Guid gameId, GameSyncStateMachine stateMachine)
    {
        ArgumentNullException.ThrowIfNull(stateMachine);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!entries.TryAdd(gameId, new Entry(stateMachine)))
            {
                throw new InvalidOperationException($"Game is already registered: {gameId}");
            }
        }
    }

    public void UnregisterGame(Guid gameId)
    {
        Entry? entry;
        lock (gate)
        {
            entries.Remove(gameId, out entry);
        }

        entry?.CancelDebounce();
        entry?.Semaphore.Dispose();
    }

    public void MarkDirty(Guid gameId)
    {
        var entry = GetEntry(gameId);
        lock (entry.Gate)
        {
            if (entry.State.Status == GameSyncStatus.BackingUp)
            {
                entry.DirtyDuringBackup = true;
                return;
            }

            if (entry.State.Status == GameSyncStatus.Watching)
            {
                entry.State.TransitionTo(GameSyncStatus.Dirty);
            }
            else if (entry.State.Status is not (
                         GameSyncStatus.Dirty or GameSyncStatus.Pending))
            {
                return;
            }

            ScheduleDebounce(gameId, entry);
        }
    }

    public async Task BackupNowAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        var entry = GetEntry(gameId);
        lock (entry.Gate)
        {
            entry.CancelDebounce();
            if (entry.State.Status == GameSyncStatus.Watching)
            {
                entry.State.TransitionTo(GameSyncStatus.Dirty);
            }
        }

        await ExecuteBackupAsync(gameId, entry, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        Entry[] snapshot;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            snapshot = entries.Values.ToArray();
            entries.Clear();
        }

        foreach (var entry in snapshot)
        {
            entry.CancelDebounce();
            entry.Semaphore.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private Entry GetEntry(Guid gameId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return entries.TryGetValue(gameId, out var entry)
                ? entry
                : throw new KeyNotFoundException($"Game is not registered: {gameId}");
        }
    }

    private void ScheduleDebounce(Guid gameId, Entry entry)
    {
        entry.CancelDebounce();
        entry.DebounceCancellation = new CancellationTokenSource();
        var token = entry.DebounceCancellation.Token;
        _ = RunDebounceAsync(gameId, entry, token);
    }

    private async Task RunDebounceAsync(
        Guid gameId,
        Entry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(debounce, timeProvider, cancellationToken)
                .ConfigureAwait(false);
            lock (entry.Gate)
            {
                if (entry.DebounceCancellation?.Token == cancellationToken)
                {
                    entry.DebounceCancellation.Dispose();
                    entry.DebounceCancellation = null;
                }
            }

            await ExecuteBackupAsync(gameId, entry, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExecuteBackupAsync(
        Guid gameId,
        Entry entry,
        CancellationToken cancellationToken)
    {
        await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (entry.Gate)
            {
                if (entry.State.Status is GameSyncStatus.Dirty or GameSyncStatus.Pending)
                {
                    entry.State.TransitionTo(GameSyncStatus.BackingUp);
                }
                else if (entry.State.Status != GameSyncStatus.BackingUp)
                {
                    return;
                }
            }

            var result = await backup(gameId, cancellationToken).ConfigureAwait(false);
            lock (entry.Gate)
            {
                entry.State.TransitionTo(result.Kind switch
                {
                    BackupKind.Success or BackupKind.Unchanged => GameSyncStatus.Watching,
                    BackupKind.Pending => GameSyncStatus.Pending,
                    BackupKind.Conflict => GameSyncStatus.Conflict,
                    _ => GameSyncStatus.Error,
                });

                if (entry.DirtyDuringBackup
                    && entry.State.Status == GameSyncStatus.Watching)
                {
                    entry.DirtyDuringBackup = false;
                    entry.State.TransitionTo(GameSyncStatus.Dirty);
                    ScheduleDebounce(gameId, entry);
                }
            }
        }
        finally
        {
            entry.Semaphore.Release();
        }
    }

    private sealed class Entry(GameSyncStateMachine state)
    {
        public object Gate { get; } = new();

        public GameSyncStateMachine State { get; } = state;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public CancellationTokenSource? DebounceCancellation { get; set; }

        public bool DirtyDuringBackup { get; set; }

        public void CancelDebounce()
        {
            DebounceCancellation?.Cancel();
            DebounceCancellation?.Dispose();
            DebounceCancellation = null;
        }
    }
}
