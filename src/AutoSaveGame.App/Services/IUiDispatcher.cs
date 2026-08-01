using System.Windows.Threading;

namespace AutoSaveGame.App.Services;

public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);
}

public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    private readonly Dispatcher dispatcher = dispatcher
        ?? throw new ArgumentNullException(nameof(dispatcher));

    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = dispatcher.BeginInvoke(action);
    }
}

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();
}
