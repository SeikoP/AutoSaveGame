using AutoSaveGame.App.Services;
using AutoSaveGame.App.ViewModels;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.App.Tests.ViewModels;

public sealed class GameItemViewModelTests
{
    [Theory]
    [InlineData(GameSyncStatus.NotConfigured, "Choose save folder")]
    [InlineData(GameSyncStatus.Dirty, "Changes detected")]
    [InlineData(GameSyncStatus.BackingUp, "Backing up")]
    [InlineData(GameSyncStatus.Pending, "Backing up")]
    [InlineData(GameSyncStatus.Restoring, "Restoring")]
    [InlineData(GameSyncStatus.Conflict, "Action required")]
    [InlineData(GameSyncStatus.Error, "Action required")]
    public void StatusText_UsesUserFacingLanguage(
        GameSyncStatus status,
        string expected)
    {
        var viewModel = Create(status, hasSnapshot: true);

        Assert.Equal(expected, viewModel.StatusText);
    }

    [Fact]
    public void WatchingWithoutSnapshot_WaitsForFirstBackup()
    {
        var viewModel = Create(GameSyncStatus.Watching, hasSnapshot: false);

        Assert.Equal("Waiting for first backup", viewModel.StatusText);
        Assert.False(viewModel.CanRestore);
    }

    [Fact]
    public void WatchingWithSnapshot_IsSafeAndRestorable()
    {
        var viewModel = Create(GameSyncStatus.Watching, hasSnapshot: true);

        Assert.Equal("Safe in Google Drive", viewModel.StatusText);
        Assert.True(viewModel.CanRestore);
    }

    private static GameItemViewModel Create(
        GameSyncStatus status,
        bool hasSnapshot)
    {
        var snapshot = hasSnapshot
            ? new SnapshotDescriptor(
                "file",
                new string('a', 64),
                new string('b', 64),
                8,
                DateTimeOffset.UnixEpoch,
                Guid.Parse("0de891ef-1e21-4d51-bacd-a5f1120437bb"))
            : null;
        var config = new GameConfig(
            Guid.Parse("8edcd84d-8294-4c1e-81c5-569991c58499"),
            "Hades",
            @"%USERPROFILE%\Documents\Hades",
            snapshot,
            true);
        return new GameItemViewModel(
            new RuntimeGame(config, new GameSyncStateMachine(status)));
    }
}
