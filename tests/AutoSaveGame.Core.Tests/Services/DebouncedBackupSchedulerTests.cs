using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;
using AutoSaveGame.Core.Tests.Fakes;

namespace AutoSaveGame.Core.Tests.Services;

public sealed class DebouncedBackupSchedulerTests
{
    [Fact]
    public async Task MarkDirty_ResetsDebounceAndRunsOneBackupAfterThreeQuietSeconds()
    {
        var time = new ManualTimeProvider();
        var gameId = Guid.NewGuid();
        var calls = 0;
        var invoked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sut = new DebouncedBackupScheduler(
            (_, _) =>
            {
                calls++;
                invoked.TrySetResult();
                return Task.FromResult(new BackupResult(BackupKind.Success));
            },
            time,
            TimeSpan.FromSeconds(3));
        var state = new GameSyncStateMachine(GameSyncStatus.Watching);
        sut.RegisterGame(gameId, state);

        sut.MarkDirty(gameId);
        time.Advance(TimeSpan.FromSeconds(2));
        sut.MarkDirty(gameId);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, calls);
        time.Advance(TimeSpan.FromSeconds(1));
        await invoked.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        Assert.Equal(GameSyncStatus.Watching, state.Status);
    }

    [Fact]
    public async Task BackupNowAsync_CancelsPendingDebounce()
    {
        var time = new ManualTimeProvider();
        var gameId = Guid.NewGuid();
        var calls = 0;
        await using var sut = new DebouncedBackupScheduler(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(new BackupResult(BackupKind.Success));
            },
            time,
            TimeSpan.FromSeconds(3));
        sut.RegisterGame(
            gameId,
            new GameSyncStateMachine(GameSyncStatus.Watching));

        sut.MarkDirty(gameId);
        await sut.BackupNowAsync(gameId, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(10));
        await Task.Yield();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DebouncedBackup_DoesNotCancelItsOwnOperationToken()
    {
        var time = new ManualTimeProvider();
        var gameId = Guid.NewGuid();
        var invoked = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sut = new DebouncedBackupScheduler(
            (_, token) =>
            {
                invoked.TrySetResult(token.IsCancellationRequested);
                return Task.FromResult(new BackupResult(BackupKind.Success));
            },
            time,
            TimeSpan.FromSeconds(3));
        sut.RegisterGame(
            gameId,
            new GameSyncStateMachine(GameSyncStatus.Watching));

        sut.MarkDirty(gameId);
        time.Advance(TimeSpan.FromSeconds(3));
        var wasCanceled = await invoked.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.False(wasCanceled);
    }
}
