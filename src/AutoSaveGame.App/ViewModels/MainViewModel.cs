using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoSaveGame.App.Services;

namespace AutoSaveGame.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IApplicationRuntime runtime;
    private readonly IUserPromptService prompts;
    private bool isBusy;
    private string statusMessage = "Sign in to load your save games.";

    public MainViewModel(
        IApplicationRuntime runtime,
        IUserPromptService prompts)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
        runtime.GamesChanged += (_, _) => RefreshGames();
        SignInCommand = new AsyncCommand(SignInAsync, () => !IsBusy && !IsSignedIn);
        RestoreCommand = new AsyncCommand<GameItemViewModel>(
            RestoreAsync,
            game => !IsBusy && game.Status is not (
                Core.Models.GameSyncStatus.BackingUp
                or Core.Models.GameSyncStatus.Restoring));
        BackupNowCommand = new AsyncCommand<GameItemViewModel>(
            BackupNowAsync,
            game => !IsBusy && game.Status is not (
                Core.Models.GameSyncStatus.BackingUp
                or Core.Models.GameSyncStatus.Restoring
                or Core.Models.GameSyncStatus.NotConfigured));
        DeleteGameCommand = new AsyncCommand<GameItemViewModel>(
            DeleteGameAsync,
            _ => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GameItemViewModel> Games { get; } = [];

    public AsyncCommand SignInCommand { get; }

    public AsyncCommand<GameItemViewModel> RestoreCommand { get; }

    public AsyncCommand<GameItemViewModel> BackupNowCommand { get; }

    public AsyncCommand<GameItemViewModel> DeleteGameCommand { get; }

    public bool IsSignedIn => runtime.IsSignedIn;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (isBusy == value)
            {
                return;
            }

            isBusy = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            statusMessage = value;
            OnPropertyChanged();
        }
    }

    public async Task AddOrUpdateGameAsync(
        Guid? gameId,
        string displayName,
        string absolutePath)
    {
        await RunBusyAsync(
            () => runtime.AddOrUpdateGameAsync(
                gameId,
                displayName,
                absolutePath,
                CancellationToken.None),
            "Game configuration saved.");
    }

    public async Task SetWatchingAsync(GameItemViewModel game, bool enabled)
    {
        await RunBusyAsync(
            () => runtime.SetWatchingAsync(
                game.GameId,
                enabled,
                CancellationToken.None),
            enabled ? "Watching enabled." : "Watching disabled.");
    }

    public async Task<bool> RequestExitAsync()
    {
        if (runtime.HasUnsafeChanges)
        {
            var choice = await prompts.ConfirmExitAsync();
            if (choice == ExitChoice.Cancel)
            {
                return false;
            }

            if (choice == ExitChoice.BackupAndExit)
            {
                foreach (var game in Games.Where(item =>
                             item.Status is Core.Models.GameSyncStatus.Dirty
                                 or Core.Models.GameSyncStatus.Pending))
                {
                    await runtime.BackupNowAsync(game.GameId, CancellationToken.None);
                }
            }
        }

        await runtime.SignOutAsync(CancellationToken.None);
        return true;
    }

    private async Task SignInAsync()
    {
        await prompts.ShowPublicComputerWarningAsync();
        await RunBusyAsync(
            () => runtime.SignInAsync(CancellationToken.None),
            "Games loaded.");
        OnPropertyChanged(nameof(IsSignedIn));
    }

    private async Task RestoreAsync(GameItemViewModel game)
    {
        if (!await prompts.ConfirmGameClosedAsync(game.DisplayName))
        {
            return;
        }

        await RunBusyAsync(
            () => runtime.RestoreAsync(game.GameId, CancellationToken.None),
            "Restore completed.");
    }

    private Task BackupNowAsync(GameItemViewModel game) =>
        RunBusyAsync(
            () => runtime.BackupNowAsync(game.GameId, CancellationToken.None),
            "Backup completed.");

    private async Task DeleteGameAsync(GameItemViewModel game)
    {
        if (!await prompts.ConfirmDeleteAsync(game.DisplayName))
        {
            return;
        }

        await RunBusyAsync(
            () => runtime.DeleteGameAsync(game.GameId, CancellationToken.None),
            "Game removed.");
    }

    private async Task RunBusyAsync(Func<Task> action, string successMessage)
    {
        IsBusy = true;
        try
        {
            await action();
            StatusMessage = successMessage;
            RefreshGames();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            prompts.ShowError(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshGames()
    {
        Games.Clear();
        foreach (var game in runtime.Games.OrderBy(
                     item => item.Config.DisplayName,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            Games.Add(new GameItemViewModel(game));
        }

        OnPropertyChanged(nameof(IsSignedIn));
    }

    private void RaiseCommandStates()
    {
        SignInCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
        BackupNowCommand.RaiseCanExecuteChanged();
        DeleteGameCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
