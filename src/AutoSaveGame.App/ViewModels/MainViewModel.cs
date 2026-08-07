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
    private readonly IClipboard clipboard;
    private bool isBusy;
    private string statusMessage = "Đăng nhập để tải danh sách game.";
    private GameItemViewModel? selectedGame;
    private CancellationTokenSource? signInCts;
    private bool hasSignInError;

    public MainViewModel(
        IApplicationRuntime runtime,
        IUserPromptService prompts,
        SessionDiagnosticLog? diagnosticLog = null,
        IUiDispatcher? uiDispatcher = null,
        IClipboard? clipboard = null)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
        this.diagnosticLog = diagnosticLog ?? new SessionDiagnosticLog();
        this.uiDispatcher = uiDispatcher ?? new ImmediateUiDispatcher();
        this.clipboard = clipboard ?? new WindowsClipboard();
        runtime.GamesChanged += OnGamesChanged;
        runtime.OperationChanged += OnOperationChanged;
        runtime.AuthUrlGenerated += OnAuthUrlGenerated;
        SignInCommand = new ReentrantAsyncCommand(SignInAsync, () => !IsSignedIn);
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
        SelectGameCommand = new AsyncCommand<GameItemViewModel>(
            game =>
            {
                SelectedGame = game;
                return Task.CompletedTask;
            },
            game => game is not null);
        BackToOverviewCommand = new AsyncCommand(
            () =>
            {
                SelectedGame = null;
                return Task.CompletedTask;
            });
        DeleteCloudDataCommand = new AsyncCommand(
            DeleteCloudDataAsync,
            () => !IsBusy && SelectedGame is not null && SelectedGame.CanRestore);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GameItemViewModel> Games { get; } = [];

    public ReentrantAsyncCommand SignInCommand { get; }

    public AsyncCommand SignOutCommand { get; }

    public AsyncCommand<GameItemViewModel> RestoreCommand { get; }

    public AsyncCommand<GameItemViewModel> BackupNowCommand { get; }

    public AsyncCommand<GameItemViewModel> DeleteGameCommand { get; }

    public AsyncCommand<GameItemViewModel> SelectGameCommand { get; }

    public AsyncCommand BackToOverviewCommand { get; }

    public AsyncCommand DeleteCloudDataCommand { get; }

    public GameItemViewModel? SelectedGame
    {
        get => selectedGame;
        private set
        {
            if (ReferenceEquals(selectedGame, value))
            {
                return;
            }

            selectedGame = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOverviewVisible));
            OnPropertyChanged(nameof(IsGameDetailVisible));
            DeleteCloudDataCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsOverviewVisible => IsSignedIn && SelectedGame is null;

    public bool IsGameDetailVisible => SelectedGame is not null;

    public bool IsSignedIn => runtime.IsSignedIn;

    public bool IsSigningIn => signInCts is not null;

    public bool HasSignInError
    {
        get => hasSignInError;
        private set
        {
            if (hasSignInError == value)
            {
                return;
            }

            hasSignInError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SignInHint));
        }
    }

    public string SignInHint => IsSigningIn
        ? "Đang chờ xác thực trong trình duyệt. Bấm lần nữa để hủy."
        : HasSignInError
            ? "Đăng nhập thất bại. Bấm để thử lại."
            : "Liên kết đăng nhập sẽ được sao chép vào bảng tạm.";

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
        if (signInCts is not null)
        {
            signInCts.Cancel();
            return;
        }

        if (HasSignInError)
        {
            HasSignInError = false;
            StatusMessage = "Đăng nhập để tải danh sách game.";
        }

        await prompts.ShowPublicComputerWarningAsync();

        signInCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsSigningIn));
        OnPropertyChanged(nameof(SignInHint));
        var canceled = false;
        try
        {
            await RunBusyAsync(
                () => runtime.SignInAsync(signInCts.Token),
                "Đã tải danh sách game.",
                "Đăng nhập Google",
                "Đang mở trình duyệt để đăng nhập...");
            OnPropertyChanged(nameof(IsSignedIn));
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            StatusMessage = "Đã hủy đăng nhập.";
        }
        finally
        {
            signInCts.Dispose();
            signInCts = null;
            OnPropertyChanged(nameof(IsSigningIn));
            OnPropertyChanged(nameof(SignInHint));
        }

        HasSignInError = !canceled && !IsSignedIn;
    }

    private void OnAuthUrlGenerated(object? sender, string url)
    {
        void Copy()
        {
            try
            {
                clipboard.SetText(url);
                StatusMessage =
                    "Đã sao chép liên kết đăng nhập. Nếu trình duyệt chưa mở, hãy dán vào Chrome để đăng nhập.";
            }
            catch (Exception exception)
            {
                TryWriteDiagnostic(exception, "Sao chép liên kết đăng nhập");
                StatusMessage =
                    "Không thể sao chép liên kết đăng nhập. Trình duyệt sẽ mở để bạn tiếp tục.";
            }
        }

        if (uiDispatcher.CheckAccess())
        {
            Copy();
        }
        else
        {
            uiDispatcher.Post(Copy);
        }
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

    private async Task DeleteCloudDataAsync()
    {
        if (SelectedGame is null)
        {
            return;
        }

        var game = SelectedGame;
        if (!await prompts.ConfirmDeleteCloudDataAsync(game.DisplayName))
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var result = await runtime.DeleteGameAndCloudDataAsync(
                    game.GameId,
                    CancellationToken.None);
                if (result.Kind == Core.Models.GameCloudDeleteKind.CleanupIncomplete)
                {
                    StatusMessage = "Đã bỏ liên kết Drive, còn file cần dọn lại.";
                }
            },
            "Đã xóa game và dữ liệu Drive.",
            "Xóa game và dữ liệu Drive",
            $"Đang xóa {game.DisplayName} và dữ liệu Drive...");
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = UserFacingError.From(exception);
            var correlationId = TryWriteDiagnostic(exception, operation);
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
        var selectedGameId = SelectedGame?.GameId;
        Games.Clear();
        foreach (var game in runtime.Games.OrderBy(
                     item => item.Config.DisplayName,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            Games.Add(new GameItemViewModel(game, uiDispatcher));
        }

        SelectedGame = selectedGameId is null
            ? null
            : Games.SingleOrDefault(game => game.GameId == selectedGameId.Value);
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CloudUsageText));
        OnPropertyChanged(nameof(IsOverviewVisible));
        OnPropertyChanged(nameof(IsGameDetailVisible));
    }

    private void RaiseCommandStates()
    {
        SignInCommand.RaiseCanExecuteChanged();
        SignOutCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
        BackupNowCommand.RaiseCanExecuteChanged();
        DeleteGameCommand.RaiseCanExecuteChanged();
        DeleteCloudDataCommand.RaiseCanExecuteChanged();
    }

    private string? TryWriteDiagnostic(Exception exception, string operation)
    {
        try
        {
            return diagnosticLog.Write(exception, operation);
        }
        catch
        {
            return null;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
