namespace AutoSaveGame.Core.Models;

public enum BackupKind
{
    Success,
    Unchanged,
    Pending,
    Conflict,
    Failed,
}

public sealed record BackupResult(BackupKind Kind, string? Message = null);

