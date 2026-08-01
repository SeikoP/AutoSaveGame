using System.Windows.Input;

namespace AutoSaveGame.App.ViewModels;

/// <summary>
/// An <see cref="ICommand"/> that does not guard against re-entrancy, so it can
/// be invoked again while a previous execution is still running. Used for the
/// sign-in button so a second click can cancel the running attempt.
/// </summary>
public sealed class ReentrantAsyncCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync() => await execute();

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
