using AutoSaveGame.App.Services;
using AutoSaveGame.App.ViewModels;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.App.Tests.ViewModels;

public sealed class GameItemViewModelTests
{
    [Theory]
    [InlineData(GameSyncStatus.NotConfigured, "Chọn thư mục save")]
    [InlineData(GameSyncStatus.Dirty, "Đã phát hiện thay đổi")]
    [InlineData(GameSyncStatus.BackingUp, "Đang sao lưu")]
    [InlineData(GameSyncStatus.Pending, "Đang chờ sao lưu")]
    [InlineData(GameSyncStatus.Restoring, "Đang khôi phục")]
    [InlineData(GameSyncStatus.Conflict, "Cần xử lý xung đột")]
    [InlineData(GameSyncStatus.Error, "Cần kiểm tra")]
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

        Assert.Equal("Đang chờ bản sao lưu đầu tiên", viewModel.StatusText);
        Assert.False(viewModel.CanRestore);
    }

    [Fact]
    public void WatchingWithSnapshot_IsSafeAndRestorable()
    {
        var viewModel = Create(GameSyncStatus.Watching, hasSnapshot: true);

        Assert.Equal("Đã an toàn trên Google Drive", viewModel.StatusText);
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
