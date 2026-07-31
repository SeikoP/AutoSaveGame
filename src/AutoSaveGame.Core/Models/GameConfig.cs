namespace AutoSaveGame.Core.Models;

public sealed record GameConfig(
    Guid GameId,
    string DisplayName,
    string PathTemplate,
    SnapshotDescriptor? Snapshot,
    bool WatchEnabled);

