using AutoSaveGame.Core.Services;

namespace AutoSaveGame.Core.Abstractions;

public interface IBackupScheduler
{
    void RegisterGame(Guid gameId, GameSyncStateMachine stateMachine);

    void UnregisterGame(Guid gameId);

    void MarkDirty(Guid gameId);

    Task BackupNowAsync(Guid gameId, CancellationToken cancellationToken);
}

