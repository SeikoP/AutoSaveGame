namespace AutoSaveGame.Infrastructure.GoogleDrive;

public enum AuthenticationFailureKind
{
    Canceled,
    TimedOut,
    Network,
    Rejected,
    BrowserCallback,
    InvalidBuild,
}

public sealed class UserAuthenticationException : Exception
{
    public UserAuthenticationException(
        AuthenticationFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public AuthenticationFailureKind Kind { get; }
}
