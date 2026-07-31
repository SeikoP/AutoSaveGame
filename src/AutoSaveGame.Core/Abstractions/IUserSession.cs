namespace AutoSaveGame.Core.Abstractions;

public interface IUserSession
{
    bool IsSignedIn { get; }

    Task SignInAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}

