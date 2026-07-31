using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Services;

public sealed class GameSyncStateMachine(GameSyncStatus initialStatus)
{
    private static readonly IReadOnlyDictionary<GameSyncStatus, GameSyncStatus[]> Allowed =
        new Dictionary<GameSyncStatus, GameSyncStatus[]>
        {
            [GameSyncStatus.NotConfigured] = [GameSyncStatus.Watching],
            [GameSyncStatus.Watching] =
                [GameSyncStatus.Dirty, GameSyncStatus.Restoring],
            [GameSyncStatus.Dirty] =
                [GameSyncStatus.BackingUp, GameSyncStatus.Restoring],
            [GameSyncStatus.BackingUp] =
                [
                    GameSyncStatus.Watching,
                    GameSyncStatus.Pending,
                    GameSyncStatus.Conflict,
                    GameSyncStatus.Error,
                ],
            [GameSyncStatus.Pending] =
                [GameSyncStatus.BackingUp, GameSyncStatus.Restoring, GameSyncStatus.Error],
            [GameSyncStatus.Restoring] =
                [GameSyncStatus.Watching, GameSyncStatus.Error],
            [GameSyncStatus.Conflict] = [],
            [GameSyncStatus.Error] = [],
        };

    private readonly object gate = new();
    private GameSyncStatus status = initialStatus;

    public event EventHandler<GameSyncStatus>? StatusChanged;

    public GameSyncStatus Status
    {
        get
        {
            lock (gate)
            {
                return status;
            }
        }
    }

    public void TransitionTo(GameSyncStatus next)
    {
        EventHandler<GameSyncStatus>? handler;
        lock (gate)
        {
            if (!Allowed[status].Contains(next))
            {
                throw new InvalidOperationException(
                    $"Invalid sync state transition: {status} -> {next}.");
            }

            status = next;
            handler = StatusChanged;
        }

        handler?.Invoke(this, next);
    }
}

