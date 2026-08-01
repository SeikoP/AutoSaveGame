using System.Threading;

namespace AutoSaveGame.App.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Semaphore ownership;
    private readonly EventWaitHandle activation;
    private RegisteredWaitHandle? listener;
    private bool ownsInstance;
    private bool disposed;

    public SingleInstanceCoordinator(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ownership = new Semaphore(1, 1, $"{name}.Ownership");
        activation = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $"{name}.Activate");
    }

    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ownsInstance)
        {
            return true;
        }

        ownsInstance = ownership.WaitOne(0);
        return ownsInstance;
    }

    public void StartListening(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ownsInstance)
        {
            throw new InvalidOperationException(
                "Only the primary instance can listen for activation.");
        }

        listener ??= ThreadPool.RegisterWaitForSingleObject(
            activation,
            (_, _) => activate(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void SignalPrimary()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        activation.Set();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        listener?.Unregister(null);
        if (ownsInstance)
        {
            ownership.Release();
            ownsInstance = false;
        }

        activation.Dispose();
        ownership.Dispose();
    }
}
