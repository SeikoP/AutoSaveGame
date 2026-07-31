namespace AutoSaveGame.Core.Models;

public sealed record RestoreResult(
    bool Success,
    bool RolledBack,
    string Message,
    string? RecoveryPath = null);

