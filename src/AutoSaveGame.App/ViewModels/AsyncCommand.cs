using System.Windows.Input;

namespace AutoSaveGame.App.ViewModels;

public sealed class AsyncCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null) : ICommand
{
    private bool running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        running = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        finally
        {
            running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncCommand<T>(
    Func<T, Task> execute,
    Func<T, bool>? canExecute = null) : ICommand
    where T : class
{
    private bool running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !running
        && parameter is T value
        && (canExecute?.Invoke(value) ?? true);

    public async void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            await ExecuteAsync(value);
        }
    }

    public async Task ExecuteAsync(T value)
    {
        if (!CanExecute(value))
        {
            return;
        }

        running = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute(value);
        }
        finally
        {
            running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

