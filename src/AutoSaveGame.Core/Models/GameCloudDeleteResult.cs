namespace AutoSaveGame.Core.Models;

public enum GameCloudDeleteKind
{
    Deleted,
    AlreadyEmpty,
    NotFound,
    Conflict,
    Failed,
    CleanupIncomplete,
}

public sealed record GameCloudDeleteResult(
    GameCloudDeleteKind Kind,
    Catalog? Catalog,
    IReadOnlyList<string> DeletedFileIds,
    IReadOnlyList<string> FailedFileIds,
    string? Message = null);
