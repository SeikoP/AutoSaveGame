namespace AutoSaveGame.Core.Models;

public enum GameSyncStatus
{
    NotConfigured,
    Watching,
    Dirty,
    BackingUp,
    Pending,
    Restoring,
    Conflict,
    Error,
}

