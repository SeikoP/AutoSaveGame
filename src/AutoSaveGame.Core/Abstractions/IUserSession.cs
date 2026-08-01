namespace AutoSaveGame.Core.Abstractions;

public interface IUserSession
{
    bool IsSignedIn { get; }

    /// <summary>Raised with the browser authorization URL once it is ready.</summary>
    event EventHandler<string>? AuthUrlGenerated;

    Task SignInAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}

