using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;
using AutoSaveGame.Infrastructure.Tests.TestSupport;
using AutoSaveGame.Infrastructure.Watching;

namespace AutoSaveGame.Infrastructure.Tests.Watching;

public sealed class GameDirectoryWatcherTests
{
    [Fact]
    public async Task DefaultReconciliation_WaitsFiveMinutes()
    {
        using var root = TempDirectory.Create();
        root.Write("slot.dat", "before");
        var scheduler = new RecordingBackupScheduler();
        var time = new ManualTimeProvider();
        await using var sut = new GameDirectoryWatcher(
            scheduler,
            new PathTemplateService(new Dictionary<string, string>()),
            time,
            enableNativeEvents: false);
        var interval = (TimeSpan?)typeof(GameDirectoryWatcher)
            .GetField(
                "reconciliationInterval",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(sut);
        Assert.Equal(TimeSpan.FromMinutes(5), interval);
        var game = new GameConfig(
            Guid.NewGuid(),
            "Hades",
            root.Path,
            null,
            true);
        await sut.StartAsync(game, TestContext.Current.CancellationToken);

        root.Write("slot.dat", "after");
        time.Advance(TimeSpan.FromSeconds(30));
        await Task.Yield();
        Assert.Equal(0, scheduler.DirtyCount);

        time.Advance(TimeSpan.FromMinutes(4.5));
        await scheduler.DirtyObserved.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, scheduler.DirtyCount);
    }

    [Fact]
    public async Task Reconciliation_MarksDirtyWhenNativeEventsAreDisabled()
    {
        using var root = TempDirectory.Create();
        root.Write("slot.dat", "before");
        var scheduler = new RecordingBackupScheduler();
        var time = new ManualTimeProvider();
        var paths = new PathTemplateService(
            new Dictionary<string, string>());
        await using var sut = new GameDirectoryWatcher(
            scheduler,
            paths,
            time,
            TimeSpan.FromSeconds(30),
            enableNativeEvents: false);
        var game = new GameConfig(
            Guid.NewGuid(),
            "Hades",
            root.Path,
            null,
            true);
        await sut.StartAsync(game, TestContext.Current.CancellationToken);

        root.Write("slot.dat", "after");
        time.Advance(TimeSpan.FromSeconds(30));
        await scheduler.DirtyObserved.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, scheduler.DirtyCount);
    }
}
