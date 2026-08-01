using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Services;

public sealed class BackupService(
    ISnapshotArchive snapshotArchive,
    ICatalogRepository catalogRepository,
    PathTemplateService pathTemplates,
    Guid sourceMachineId,
    string temporaryRoot,
    IProgress<OperationProgress>? progress = null)
{
    private readonly ISnapshotArchive snapshotArchive = snapshotArchive
        ?? throw new ArgumentNullException(nameof(snapshotArchive));
    private readonly ICatalogRepository catalogRepository = catalogRepository
        ?? throw new ArgumentNullException(nameof(catalogRepository));
    private readonly PathTemplateService pathTemplates = pathTemplates
        ?? throw new ArgumentNullException(nameof(pathTemplates));
    private readonly Guid sourceMachineId = sourceMachineId != Guid.Empty
        ? sourceMachineId
        : throw new ArgumentException("Source machine ID is required.", nameof(sourceMachineId));
    private readonly string temporaryRoot = Path.GetFullPath(temporaryRoot);

    public async Task<BackupResult> BackupAsync(
        GameConfig game,
        Catalog loadedCatalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(loadedCatalog);
        var operationId = Guid.NewGuid();
        var started = TimeProvider.System.GetTimestamp();
        void Report(
            OperationStage stage,
            OperationOutcome outcome = OperationOutcome.Running,
            long bytes = 0,
            long? totalBytes = null,
            string? detail = null) =>
            progress?.Report(new OperationProgress(
                operationId,
                game.GameId,
                OperationKind.Backup,
                stage,
                bytes,
                totalBytes,
                TimeProvider.System.GetElapsedTime(started),
                detail,
                outcome));

        Report(OperationStage.BuildingArchive);
        Directory.CreateDirectory(temporaryRoot);
        var archivePath = Path.Combine(
            temporaryRoot,
            $"autosavegame-{game.GameId:N}-{Guid.NewGuid():N}.zip");

        try
        {
            var sourceDirectory = pathTemplates.Expand(game.PathTemplate);
            var snapshot = await snapshotArchive.BuildAsync(
                sourceDirectory,
                archivePath,
                cancellationToken).ConfigureAwait(false);
            if (snapshot.Kind == SnapshotBuildKind.Pending)
            {
                Report(
                    OperationStage.Completed,
                    OperationOutcome.Failed,
                    detail: snapshot.Message);
                return new BackupResult(BackupKind.Pending, snapshot.Message);
            }

            if (string.Equals(
                    game.Snapshot?.ContentSha256,
                    snapshot.ContentSha256,
                    StringComparison.Ordinal))
            {
                Report(OperationStage.Completed, OperationOutcome.Succeeded);
                return new BackupResult(BackupKind.Unchanged);
            }

            await using var archive = new FileStream(
                snapshot.ArchivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            Report(
                OperationStage.UploadingArchive,
                totalBytes: snapshot.ArchiveSize);
            var commit = await catalogRepository.CommitSnapshotAsync(
                loadedCatalog,
                game.GameId,
                archive,
                snapshot,
                sourceMachineId,
                cancellationToken).ConfigureAwait(false);
            var result = commit.Kind switch
            {
                CatalogCommitKind.Success => new BackupResult(BackupKind.Success),
                CatalogCommitKind.Unchanged => new BackupResult(BackupKind.Unchanged),
                CatalogCommitKind.Conflict =>
                    new BackupResult(BackupKind.Conflict, commit.Message),
                _ => new BackupResult(BackupKind.Failed, commit.Message),
            };
            Report(
                OperationStage.Completed,
                result.Kind is BackupKind.Success or BackupKind.Unchanged
                    ? OperationOutcome.Succeeded
                    : result.Kind == BackupKind.Conflict
                        ? OperationOutcome.Conflict
                        : OperationOutcome.Failed,
                snapshot.ArchiveSize,
                snapshot.ArchiveSize,
                result.Message);
            return result;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            Report(
                OperationStage.Completed,
                OperationOutcome.Failed,
                detail: exception.Message);
            return new BackupResult(BackupKind.Failed, exception.Message);
        }
        finally
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }
}
