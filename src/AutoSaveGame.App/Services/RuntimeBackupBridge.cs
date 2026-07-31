using AutoSaveGame.Core.Models;

namespace AutoSaveGame.App.Services;

internal sealed class RuntimeBackupBridge
{
    public ApplicationRuntime? Runtime { get; set; }

    public Task<BackupResult> InvokeAsync(
        Guid gameId,
        CancellationToken cancellationToken) =>
        Runtime?.PerformScheduledBackupAsync(gameId, cancellationToken)
        ?? Task.FromResult(
            new BackupResult(BackupKind.Failed, "Application runtime is not ready."));
}

