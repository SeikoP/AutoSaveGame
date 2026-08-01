using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoSaveGame.App.Services;

namespace AutoSaveGame.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IApplicationRuntime runtime;
    private readonly IUserPromptService prompts;
    private readonly SessionDiagnosticLog diagnosticLog;
    private readonly IUiDispatcher uiDispatcher;
    private bool isBusy;
    private string statusMessage = "Sign in to load your save games.";

    public MainViewModel(
        IApplicationRuntime runtime,
        IUserPromptService prompts,
        SessionDiagnosticLog? diagnosticLog = null,
        IUiDispatcher? uiDispatcher = null)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
        this.diagnosticLog = diagnosticLog ?? new SessionDiagnosticLog();
        this.uiDispatcher = uiDispatcher ?? new ImmediateUiDispatcher();
        runtime.GamesChanged += OnGamesChanged;
        SignInCommand = new AsyncCommand(SignInAsync, () => !IsBusy && !IsSignedIn);
        SignOutCommand = new AsyncCommand(SignOutAsync, () => !IsBusy && IsSignedIn);
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

    public AsyncCommand SignOutCommand { get; }

    public AsyncCommand<GameItemViewModel> RestoreCommand { get; }

    public AsyncCommand<GameItemViewModel> BackupNowCommand { get; }

    public AsyncCommand<GameItemViewModel> DeleteGameCommand { get; }

    public bool IsSignedIn => runtime.IsSignedIn;

    public bool HasGames => Games.Count > 0;

    public bool IsEmpty => !HasGames;

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
            OnPropertyChanged(nameof(IsProgressVisible));
            RaiseCommandStates();
        }
    }

    public bool IsProgressVisible => IsBusy;

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
            "Game configuration saved.",
            busyMessage: "Saving game settings...");
    }

    public async Task SetWatchingAsync(GameItemViewModel game, bool enabled)
    {
        await RunBusyAsync(
            () => runtime.SetWatchingAsync(
                game.GameId,
                enabled,
                CancellationToken.None),
            enabled ? "Watching enabled." : "Watching disabled.",
            busyMessage: "Updating monitoring...");
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
            "Games loaded.",
            "Google sign-in",
            "Connecting to Google Drive...");
        OnPropertyChanged(nameof(IsSignedIn));
    }

    private async Task SignOutAsync()
    {
        await RunBusyAsync(
            () => runtime.SignOutAsync(CancellationToken.None),
            "Signed out.",
            "Google sign-out",
            "Signing out securely...");
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
            "Restore completed.",
            busyMessage: $"Restoring {game.DisplayName}...");
    }

    private Task BackupNowAsync(GameItemViewModel game) =>
        RunBusyAsync(
            () => runtime.BackupNowAsync(game.GameId, CancellationToken.None),
            "Backup completed.",
            busyMessage: $"Backing up {game.DisplayName}...");

    private async Task DeleteGameAsync(GameItemViewModel game)
    {
        if (!await prompts.ConfirmDeleteAsync(game.DisplayName))
        {
            return;
        }

        await RunBusyAsync(
            () => runtime.DeleteGameAsync(game.GameId, CancellationToken.None),
            "Game removed.",
            busyMessage: $"Removing {game.DisplayName}...");
    }

    private async Task RunBusyAsync(
        Func<Task> action,
        string successMessage,
        string operation = "Application operation",
        string busyMessage = "Working...")
    {
        StatusMessage = busyMessage;
        IsBusy = true;
        try
        {
            await action();
            StatusMessage = successMessage;
            RefreshGames();
        }
        catch (Exception exception)
        {
            var error = UserFacingError.From(exception);
            var correlationId = diagnosticLog.Write(exception, operation);
            StatusMessage = error.Message;
            prompts.ShowError(error.Title, error.Message, correlationId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnGamesChanged(object? sender, EventArgs e)
    {
        if (uiDispatcher.CheckAccess())
        {
            RefreshGames();
            return;
        }

        uiDispatcher.Post(RefreshGames);
    }

    private void RefreshGames()
    {
        Games.Clear();
        foreach (var game in runtime.Games.OrderBy(
                     item => item.Config.DisplayName,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            Games.Add(new GameItemViewModel(game, uiDispatcher));
        }

        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RaiseCommandStates()
    {
        SignInCommand.RaiseCanExecuteChanged();
        SignOutCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
        BackupNowCommand.RaiseCanExecuteChanged();
        DeleteGameCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
