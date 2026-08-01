using AutoSaveGame.App.Services;
using AutoSaveGame.App.ViewModels;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task SignInCommand_ShowsPublicComputerWarningBeforeLoadingGames()
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events)
        {
            RuntimeGames = [CreateRuntimeGame()],
        };
        var prompts = new FakePrompts(events);
        var sut = new MainViewModel(runtime, prompts);

        await sut.SignInCommand.ExecuteAsync();

        Assert.Equal(["warning", "signin"], events);
        Assert.True(sut.IsSignedIn);
        Assert.Equal("Hades", sut.Games.Single().DisplayName);
        Assert.True(sut.HasGames);
        Assert.False(sut.IsEmpty);
    }

    [Fact]
    public async Task SignInCommand_ShowsEmptyStateWhenCloudCatalogHasNoGames()
    {
        var runtime = new FakeRuntime([]);
        var sut = new MainViewModel(runtime, new FakePrompts([]));

        await sut.SignInCommand.ExecuteAsync();

        Assert.True(sut.IsSignedIn);
        Assert.True(sut.IsEmpty);
        Assert.False(sut.HasGames);
    }

    [Fact]
    public async Task GamesChanged_FromBackgroundThread_IsMarshaledToUiDispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        var runtime = new FakeRuntime([])
        {
            RaiseGamesChangedOnBackgroundThread = true,
            RuntimeGames = [CreateRuntimeGame()],
        };
        var sut = new MainViewModel(
            runtime,
            new FakePrompts([]),
            uiDispatcher: dispatcher);

        await sut.SignInCommand.ExecuteAsync();

        Assert.True(dispatcher.PostCount > 0);
        Assert.Equal("Hades", sut.Games.Single().DisplayName);
    }

    [Fact]
    public async Task SignInCommand_ShowsProgressWhileAuthenticationIsRunning()
    {
        var releaseSignIn = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime([]) { SignInBlocker = releaseSignIn.Task };
        var sut = new MainViewModel(runtime, new FakePrompts([]));

        var signIn = sut.SignInCommand.ExecuteAsync();

        Assert.True(sut.IsBusy);
        Assert.True(sut.IsProgressVisible);
        Assert.Equal("Đang kết nối Google Drive...", sut.StatusMessage);

        releaseSignIn.SetResult();
        await signIn;

        Assert.False(sut.IsProgressVisible);
        Assert.Equal("Đã tải danh sách game.", sut.StatusMessage);
    }

    [Fact]
    public async Task SignOutCommand_ClearsSignedInDashboard()
    {
        var runtime = new FakeRuntime([])
        {
            RuntimeGames = [CreateRuntimeGame()],
        };
        var sut = new MainViewModel(runtime, new FakePrompts([]));
        await sut.SignInCommand.ExecuteAsync();

        await sut.SignOutCommand.ExecuteAsync();

        Assert.False(sut.IsSignedIn);
        Assert.True(sut.IsEmpty);
        Assert.Empty(sut.Games);
    }

    [Fact]
    public void OperationChanged_ShowsMeasuredVietnameseProgress()
    {
        var runtime = new FakeRuntime([]);
        var sut = new MainViewModel(runtime, new FakePrompts([]));

        runtime.EmitOperation(new OperationProgress(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OperationKind.Backup,
            OperationStage.UploadingArchive,
            50,
            100,
            TimeSpan.FromSeconds(2),
            null,
            OperationOutcome.Running));

        Assert.True(sut.IsProgressVisible);
        Assert.False(sut.IsProgressIndeterminate);
        Assert.Equal(50, sut.OperationPercent);
        Assert.Equal("Đang tải lên Google Drive", sut.ProgressStatus);
    }

    [Fact]
    public async Task RestoreCommand_DoesNothingWhenGameClosedConfirmationIsDeclined()
    {
        var events = new List<string>();
        var game = CreateRuntimeGame();
        var runtime = new FakeRuntime(events)
        {
            RuntimeGames = [game],
        };
        var prompts = new FakePrompts(events) { ConfirmGameClosed = false };
        var sut = new MainViewModel(runtime, prompts);
        await sut.SignInCommand.ExecuteAsync();
        events.Clear();

        await sut.RestoreCommand.ExecuteAsync(sut.Games.Single());

        Assert.Equal(["confirm-closed"], events);
    }

    [Fact]
    public async Task RestoreCommand_RestoresAfterGameClosedConfirmation()
    {
        var events = new List<string>();
        var game = CreateRuntimeGame();
        var runtime = new FakeRuntime(events)
        {
            RuntimeGames = [game],
        };
        var prompts = new FakePrompts(events) { ConfirmGameClosed = true };
        var sut = new MainViewModel(runtime, prompts);
        await sut.SignInCommand.ExecuteAsync();
        events.Clear();

        await sut.RestoreCommand.ExecuteAsync(sut.Games.Single());

        Assert.Equal(["confirm-closed", "restore"], events);
        Assert.Equal("Khôi phục hoàn tất.", sut.StatusMessage);
    }

    [Fact]
    public async Task SignInCommand_ShowsSafeMessageAndCorrelationIdWhenAuthenticationFails()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"AutoSaveGame-ViewModelTest-{Guid.NewGuid():N}");
        try
        {
            var events = new List<string>();
            var runtime = new FakeRuntime(events)
            {
                SignInError = new Infrastructure.GoogleDrive.UserAuthenticationException(
                    Infrastructure.GoogleDrive.AuthenticationFailureKind.Network,
                    "raw network details"),
            };
            var prompts = new FakePrompts(events);
            var sut = new MainViewModel(
                runtime,
                prompts,
                new SessionDiagnosticLog(root));

            await sut.SignInCommand.ExecuteAsync();

            Assert.Equal("Không thể đăng nhập Google", prompts.ErrorTitle);
            Assert.Contains("Không thể kết nối tới Google", prompts.ErrorMessage);
            Assert.DoesNotContain("raw network details", prompts.ErrorMessage);
            Assert.False(string.IsNullOrWhiteSpace(prompts.CorrelationId));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeleteCloudDataCommand_DoesNothingWhenConfirmationIsDeclined()
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events) { RuntimeGames = [CreateRuntimeGame()] };
        var prompts = new FakePrompts(events) { ConfirmCloudDelete = false };
        var sut = new MainViewModel(runtime, prompts);
        await sut.SignInCommand.ExecuteAsync();
        sut.SelectGameCommand.Execute(sut.Games.Single());
        events.Clear();

        await sut.DeleteCloudDataCommand.ExecuteAsync();

        Assert.Equal(["confirm-cloud-delete"], events);
    }

    [Fact]
    public async Task DeleteCloudDataCommand_DeletesSelectedGameAfterConfirmation()
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events) { RuntimeGames = [CreateRuntimeGame()] };
        var prompts = new FakePrompts(events) { ConfirmCloudDelete = true };
        var sut = new MainViewModel(runtime, prompts);
        await sut.SignInCommand.ExecuteAsync();
        sut.SelectGameCommand.Execute(sut.Games.Single());
        events.Clear();

        await sut.DeleteCloudDataCommand.ExecuteAsync();

        Assert.Equal(["confirm-cloud-delete", "delete-cloud"], events);
        Assert.Equal("Đã xóa dữ liệu Drive của game.", sut.StatusMessage);
    }

    [Fact]
    public async Task SelectGameCommand_OpensDetailAndBackCommandReturnsToOverview()
    {
        var runtime = new FakeRuntime([]) { RuntimeGames = [CreateRuntimeGame()] };
        var sut = new MainViewModel(runtime, new FakePrompts([]));
        await sut.SignInCommand.ExecuteAsync();

        sut.SelectGameCommand.Execute(sut.Games.Single());

        Assert.True(sut.IsGameDetailVisible);
        Assert.False(sut.IsOverviewVisible);
        Assert.Equal("Hades", sut.SelectedGame?.DisplayName);

        sut.BackToOverviewCommand.Execute(null);

        Assert.False(sut.IsGameDetailVisible);
        Assert.True(sut.IsOverviewVisible);
        Assert.Null(sut.SelectedGame);
    }

    private static RuntimeGame CreateRuntimeGame()
    {
        var config = new GameConfig(
            Guid.Parse("8edcd84d-8294-4c1e-81c5-569991c58499"),
            "Hades",
            @"%USERPROFILE%\Documents\Hades",
            new SnapshotDescriptor(
                "file",
                new string('a', 64),
                new string('b', 64),
                8,
                DateTimeOffset.UnixEpoch,
                Guid.Parse("0de891ef-1e21-4d51-bacd-a5f1120437bb")),
            true);
        return new RuntimeGame(
            config,
            new GameSyncStateMachine(GameSyncStatus.Watching));
    }

    private sealed class FakeRuntime(List<string> events) : IApplicationRuntime
    {
        public Exception? SignInError { get; init; }

        public Task? SignInBlocker { get; init; }

        public bool RaiseGamesChangedOnBackgroundThread { get; init; }

        public bool IsSignedIn { get; private set; }

        public IReadOnlyList<RuntimeGame> RuntimeGames { get; set; } = [];

        public IReadOnlyList<RuntimeGame> Games => RuntimeGames;

        public bool HasUnsafeChanges => false;

        public OperationProgress? CurrentOperation { get; private set; }

        public event EventHandler? GamesChanged;

        public event EventHandler? OperationChanged;

        public void EmitOperation(OperationProgress progress)
        {
            CurrentOperation = progress;
            OperationChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task SignInAsync(CancellationToken cancellationToken)
        {
            events.Add("signin");
            if (SignInError is not null)
            {
                throw SignInError;
            }

            if (SignInBlocker is not null)
            {
                await SignInBlocker;
            }

            IsSignedIn = true;
            if (RaiseGamesChangedOnBackgroundThread)
            {
                await Task.Run(() => GamesChanged?.Invoke(this, EventArgs.Empty));
            }
            else
            {
                GamesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Task AddOrUpdateGameAsync(
            Guid? gameId,
            string displayName,
            string absolutePath,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteGameAsync(Guid gameId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<GameCloudDeleteResult> DeleteGameCloudDataAsync(
            Guid gameId,
            CancellationToken cancellationToken)
        {
            events.Add("delete-cloud");
            return Task.FromResult(new GameCloudDeleteResult(
                GameCloudDeleteKind.Deleted,
                null,
                ["archive-file"],
                []));
        }

        public Task RestoreAsync(Guid gameId, CancellationToken cancellationToken)
        {
            events.Add("restore");
            return Task.CompletedTask;
        }

        public Task BackupNowAsync(Guid gameId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetWatchingAsync(
            Guid gameId,
            bool enabled,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            IsSignedIn = false;
            RuntimeGames = [];
            GamesChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePrompts(List<string> events) : IUserPromptService
    {
        public bool ConfirmGameClosed { get; init; }

        public bool ConfirmCloudDelete { get; init; }

        public string? ErrorTitle { get; private set; }

        public string? ErrorMessage { get; private set; }

        public string? CorrelationId { get; private set; }

        public Task ShowPublicComputerWarningAsync()
        {
            events.Add("warning");
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmGameClosedAsync(string displayName)
        {
            events.Add("confirm-closed");
            return Task.FromResult(ConfirmGameClosed);
        }

        public Task<bool> ConfirmDeleteAsync(string displayName) =>
            Task.FromResult(true);

        public Task<bool> ConfirmDeleteCloudDataAsync(string displayName)
        {
            events.Add("confirm-cloud-delete");
            return Task.FromResult(ConfirmCloudDelete);
        }

        public Task<ExitChoice> ConfirmExitAsync() =>
            Task.FromResult(ExitChoice.ExitAnyway);

        public void ShowError(string title, string message, string? correlationId)
        {
            ErrorTitle = title;
            ErrorMessage = message;
            CorrelationId = correlationId;
        }
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public int PostCount { get; private set; }

        public bool CheckAccess() => false;

        public void Post(Action action)
        {
            PostCount++;
            action();
        }
    }
}
