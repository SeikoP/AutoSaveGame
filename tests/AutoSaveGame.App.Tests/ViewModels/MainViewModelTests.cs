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
        Assert.Equal("Restore completed.", sut.StatusMessage);
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
        public bool IsSignedIn { get; private set; }

        public IReadOnlyList<RuntimeGame> RuntimeGames { get; init; } = [];

        public IReadOnlyList<RuntimeGame> Games => RuntimeGames;

        public bool HasUnsafeChanges => false;

        public event EventHandler? GamesChanged;

        public Task SignInAsync(CancellationToken cancellationToken)
        {
            events.Add("signin");
            IsSignedIn = true;
            GamesChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task AddOrUpdateGameAsync(
            Guid? gameId,
            string displayName,
            string absolutePath,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteGameAsync(Guid gameId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

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

        public Task SignOutAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePrompts(List<string> events) : IUserPromptService
    {
        public bool ConfirmGameClosed { get; init; }

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

        public Task<ExitChoice> ConfirmExitAsync() =>
            Task.FromResult(ExitChoice.ExitAnyway);

        public void ShowError(string message)
        {
        }
    }
}
