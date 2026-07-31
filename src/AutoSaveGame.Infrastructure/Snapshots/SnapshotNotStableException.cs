namespace AutoSaveGame.Infrastructure.Snapshots;

internal sealed class SnapshotNotStableException(string message) : IOException(message);

