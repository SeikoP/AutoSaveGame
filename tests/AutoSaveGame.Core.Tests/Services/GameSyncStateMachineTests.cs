using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.Core.Tests.Services;

public sealed class GameSyncStateMachineTests
{
    [Fact]
    public void TransitionTo_AllowsTheBackupLifecycle()
    {
        var sut = new GameSyncStateMachine(GameSyncStatus.Watching);

        sut.TransitionTo(GameSyncStatus.Dirty);
        sut.TransitionTo(GameSyncStatus.BackingUp);
        sut.TransitionTo(GameSyncStatus.Watching);

        Assert.Equal(GameSyncStatus.Watching, sut.Status);
    }

    [Fact]
    public void TransitionTo_RejectsReportingWatchingDirectlyFromError()
    {
        var sut = new GameSyncStateMachine(GameSyncStatus.Error);

        Assert.Throws<InvalidOperationException>(
            () => sut.TransitionTo(GameSyncStatus.Watching));
    }
}

