using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Abstractions;

public interface ICatalogRepository
{
    Task<CatalogLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task<CatalogCommitResult> SaveCatalogAsync(
        Catalog expected,
        Catalog next,
        CancellationToken cancellationToken);

    Task<CatalogCommitResult> CommitSnapshotAsync(
        Catalog expected,
        Guid gameId,
        Stream archive,
        SnapshotBuildResult snapshot,
        Guid sourceMachineId,
        CancellationToken cancellationToken);

    Task CleanupOrphansAsync(CancellationToken cancellationToken);
}

