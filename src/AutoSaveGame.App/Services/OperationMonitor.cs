using AutoSaveGame.Core.Models;

namespace AutoSaveGame.App.Services;

public sealed class OperationMonitor :
    IProgress<CloudTransferProgress>,
    IProgress<OperationProgress>
{
    private const int HistoryLimit = 20;
    private readonly object sync = new();
    private readonly List<OperationProgress> history = [];
    private OperationProgress? current;

    public event EventHandler? Changed;

    public OperationProgress? Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public IReadOnlyList<OperationProgress> History
    {
        get
        {
            lock (sync)
            {
                return history.ToArray();
            }
        }
    }

    public void Report(OperationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        lock (sync)
        {
            current = progress;
            if (progress.Outcome != OperationOutcome.Running)
            {
                history.Add(progress);
                if (history.Count > HistoryLimit)
                {
                    history.RemoveRange(0, history.Count - HistoryLimit);
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    void IProgress<CloudTransferProgress>.Report(CloudTransferProgress value)
    {
        OperationProgress? updated;
        lock (sync)
        {
            if (current?.Outcome != OperationOutcome.Running)
            {
                return;
            }

            current = current with
            {
                BytesCompleted = value.BytesTransferred,
                TotalBytes = value.TotalBytes ?? current.TotalBytes,
            };
            updated = current;
        }

        if (updated is not null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
