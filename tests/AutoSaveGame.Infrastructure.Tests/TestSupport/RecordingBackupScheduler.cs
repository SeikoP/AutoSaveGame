using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.Infrastructure.Tests.TestSupport;

internal sealed class RecordingBackupScheduler : IBackupScheduler
{
    private readonly TaskCompletionSource dirty =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int DirtyCount { get; private set; }

    public Task DirtyObserved => dirty.Task;

    public void RegisterGame(Guid gameId, GameSyncStateMachine stateMachine)
    {
    }

    public void UnregisterGame(Guid gameId)
    {
    }

    public void MarkDirty(Guid gameId)
    {
        DirtyCount++;
        dirty.TrySetResult();
    }

    public Task BackupNowAsync(Guid gameId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

