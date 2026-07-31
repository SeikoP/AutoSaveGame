namespace AutoSaveGame.Core.Models;

public sealed record SnapshotDescriptor(
    string ArchiveFileId,
    string ArchiveSha256,
    string ContentSha256,
    long ArchiveSize,
    DateTimeOffset LastBackupUtc,
    Guid SourceMachineId);

