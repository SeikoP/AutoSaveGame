using AutoSaveGame.Core.Models;

namespace AutoSaveGame.App.Services;

internal sealed class UnavailableApplicationRuntime(string message)
    : IApplicationRuntime
{
    public bool IsSignedIn => false;

    public IReadOnlyList<RuntimeGame> Games => [];

    public bool HasUnsafeChanges => false;

    public event EventHandler? GamesChanged
    {
        add { }
        remove { }
    }

    public Task SignInAsync(CancellationToken cancellationToken) =>
        Task.FromException(new Infrastructure.GoogleDrive.UserAuthenticationException(
            Infrastructure.GoogleDrive.AuthenticationFailureKind.InvalidBuild,
            message));

    public Task AddOrUpdateGameAsync(
        Guid? gameId,
        string displayName,
        string absolutePath,
        CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(message));

    public Task DeleteGameAsync(Guid gameId, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(message));

    public Task RestoreAsync(Guid gameId, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(message));

    public Task BackupNowAsync(Guid gameId, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(message));

    public Task<GameCloudDeleteResult> DeleteGameCloudDataAsync(
        Guid gameId,
        CancellationToken cancellationToken) =>
        Task.FromException<GameCloudDeleteResult>(new InvalidOperationException(message));

    public Task SetWatchingAsync(
        Guid gameId,
        bool enabled,
        CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(message));

    public Task SignOutAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
