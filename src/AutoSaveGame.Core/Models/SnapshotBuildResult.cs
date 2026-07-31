namespace AutoSaveGame.Core.Models;

public enum SnapshotBuildKind
{
    Success,
    Pending,
}

public sealed record SnapshotBuildResult(
    SnapshotBuildKind Kind,
    string? ContentSha256,
    string? ArchiveSha256,
    long ArchiveSize,
    string ArchivePath,
    string? Message)
{
    public static SnapshotBuildResult Pending(string archivePath, string message) =>
        new(SnapshotBuildKind.Pending, null, null, 0, archivePath, message);
}

public sealed record SnapshotExtractResult(string ContentSha256);

