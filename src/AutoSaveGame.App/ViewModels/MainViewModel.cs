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
    private string statusMessage = "Đăng nhập để tải danh sách game.";

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
        runtime.OperationChanged += OnOperationChanged;
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

    public string CloudUsageText
    {
        get
        {
            var bytes = Games.Sum(game => game.ArchiveSize);
            return $"{Games.Count} game · {VietnameseText.FormatBytes(bytes)} snapshot đang hoạt động";
        }
    }

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

    public bool IsProgressVisible =>
        IsBusy || runtime.CurrentOperation?.Outcome == Core.Models.OperationOutcome.Running;

    public bool IsProgressIndeterminate => runtime.CurrentOperation?.Percent is null;

    public double OperationPercent => runtime.CurrentOperation?.Percent ?? 0;

    public string ProgressStatus => runtime.CurrentOperation is null
        ? StatusMessage
        : VietnameseText.OperationStage(runtime.CurrentOperation.Stage);

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
            "Đã lưu cấu hình game.",
            busyMessage: "Đang lưu cấu hình game...");
    }

    public async Task SetWatchingAsync(GameItemViewModel game, bool enabled)
    {
        await RunBusyAsync(
            () => runtime.SetWatchingAsync(
                game.GameId,
                enabled,
                CancellationToken.None),
            enabled ? "Đã bật theo dõi." : "Đã tắt theo dõi.",
            busyMessage: "Đang cập nhật theo dõi...");
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
            "Đã tải danh sách game.",
            "Đăng nhập Google",
            "Đang kết nối Google Drive...");
        OnPropertyChanged(nameof(IsSignedIn));
    }

    private async Task SignOutAsync()
    {
        await RunBusyAsync(
            () => runtime.SignOutAsync(CancellationToken.None),
            "Đã đăng xuất.",
            "Đăng xuất Google",
            "Đang đăng xuất an toàn...");
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
            "Khôi phục hoàn tất.",
            busyMessage: $"Đang khôi phục {game.DisplayName}...");
    }

    private Task BackupNowAsync(GameItemViewModel game) =>
        RunBusyAsync(
            () => runtime.BackupNowAsync(game.GameId, CancellationToken.None),
            "Sao lưu hoàn tất.",
            busyMessage: $"Đang sao lưu {game.DisplayName}...");

    private async Task DeleteGameAsync(GameItemViewModel game)
    {
        if (!await prompts.ConfirmDeleteAsync(game.DisplayName))
        {
            return;
        }

        await RunBusyAsync(
            () => runtime.DeleteGameAsync(game.GameId, CancellationToken.None),
            "Đã xóa game.",
            busyMessage: $"Đang xóa {game.DisplayName}...");
    }

    private async Task RunBusyAsync(
        Func<Task> action,
        string successMessage,
        string operation = "Thao tác ứng dụng",
        string busyMessage = "Đang xử lý...")
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

    private void OnOperationChanged(object? sender, EventArgs e)
    {
        void Notify()
        {
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(OperationPercent));
            OnPropertyChanged(nameof(ProgressStatus));
        }

        if (uiDispatcher.CheckAccess())
        {
            Notify();
        }
        else
        {
            uiDispatcher.Post(Notify);
        }
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
        OnPropertyChanged(nameof(CloudUsageText));
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
