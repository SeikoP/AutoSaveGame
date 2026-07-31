namespace AutoSaveGame.Core.Models;

public enum CloudStoreErrorKind
{
    Authentication,
    Quota,
    Network,
    NotFound,
    Unknown,
}

public class CloudStoreException(
    CloudStoreErrorKind kind,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public CloudStoreErrorKind Kind { get; } = kind;
}

