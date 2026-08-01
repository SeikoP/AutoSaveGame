using AutoSaveGame.App.Services;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.App.Tests.Services;

public sealed class OperationMonitorTests
{
    [Fact]
    public void Report_ReplacesCurrentAndKeepsOnlyTwentyTerminalEntries()
    {
        var monitor = new OperationMonitor();

        for (var index = 0; index < 25; index++)
        {
            monitor.Report(Finished(index));
        }

        Assert.Equal(20, monitor.History.Count);
        Assert.Equal(Finished(24).OperationId, monitor.Current?.OperationId);
        Assert.Equal(Finished(5).OperationId, monitor.History[0].OperationId);
    }

    [Fact]
    public void Report_RaisesChangedAfterUpdatingCurrent()
    {
        var monitor = new OperationMonitor();
        OperationProgress? observed = null;
        monitor.Changed += (_, _) => observed = monitor.Current;

        var progress = Finished(1);
        monitor.Report(progress);

        Assert.Equal(progress, observed);
    }

    [Fact]
    public void CloudProgress_UpdatesTheRunningOperationWithoutChangingItsStage()
    {
        var monitor = new OperationMonitor();
        var operation = Finished(1) with
        {
            Stage = OperationStage.UploadingArchive,
            BytesCompleted = 0,
            TotalBytes = null,
            Outcome = OperationOutcome.Running,
        };
        monitor.Report(operation);

        ((IProgress<CloudTransferProgress>)monitor).Report(
            new CloudTransferProgress(50, 100));

        Assert.Equal(OperationStage.UploadingArchive, monitor.Current?.Stage);
        Assert.Equal(50, monitor.Current?.BytesCompleted);
        Assert.Equal(100, monitor.Current?.TotalBytes);
    }

    private static OperationProgress Finished(int index) =>
        new(
            new Guid(index, 0, 0, new byte[8]),
            null,
            OperationKind.Backup,
            OperationStage.Completed,
            100,
            100,
            TimeSpan.FromSeconds(index),
            null,
            OperationOutcome.Succeeded);
}
