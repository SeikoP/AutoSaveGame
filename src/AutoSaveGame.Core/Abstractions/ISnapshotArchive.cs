using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Abstractions;

public interface ISnapshotArchive
{
    Task<SnapshotBuildResult> BuildAsync(
        string sourceDirectory,
        string archivePath,
        CancellationToken cancellationToken);

    Task<SnapshotExtractResult> ExtractAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken cancellationToken);
}

