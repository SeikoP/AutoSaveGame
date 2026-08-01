using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Services;

public sealed class CatalogRepository(
    ICloudObjectStore cloud,
    CatalogCodec codec,
    CatalogSelector selector,
    TimeProvider timeProvider) : ICatalogRepository
{
    private readonly ICloudObjectStore cloud = cloud
        ?? throw new ArgumentNullException(nameof(cloud));
    private readonly CatalogCodec codec = codec
        ?? throw new ArgumentNullException(nameof(codec));
    private readonly CatalogSelector selector = selector
        ?? throw new ArgumentNullException(nameof(selector));
    private readonly TimeProvider timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<CatalogLoadResult> LoadAsync(
        CancellationToken cancellationToken)
    {
        var objects = await cloud.ListAsync("catalog-", cancellationToken)
            .ConfigureAwait(false);
        return await selector.SelectAsync(
            objects,
            DownloadToMemoryAsync,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CatalogCommitResult> SaveCatalogAsync(
        Catalog expected,
        Catalog next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(next);
        if (next.Generation != expected.Generation + 1)
        {
            throw new ArgumentException(
                "The next catalog generation must increment by exactly one.",
                nameof(next));
        }

        var preflight = await VerifyExpectedAsync(expected, cancellationToken)
            .ConfigureAwait(false);
        if (preflight.Result is not null)
        {
            return preflight.Result;
        }

        return await UploadAndVerifyCatalogAsync(
            next,
            preflight.CurrentFileIds,
            oldArchiveFileId: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CatalogCommitResult> CommitSnapshotAsync(
        Catalog expected,
        Guid gameId,
        Stream archive,
        SnapshotBuildResult snapshot,
        Guid sourceMachineId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(archive);
        if (gameId == Guid.Empty || sourceMachineId == Guid.Empty)
        {
            throw new ArgumentException("Game and source machine IDs are required.");
        }

        if (snapshot.Kind != SnapshotBuildKind.Success
            || snapshot.ContentSha256 is null
            || snapshot.ArchiveSha256 is null)
        {
            return new CatalogCommitResult(
                CatalogCommitKind.Failed,
                null,
                snapshot.Message ?? "Snapshot is not ready.");
        }

        var game = expected.Games.SingleOrDefault(item => item.GameId == gameId)
            ?? throw new InvalidOperationException($"Game is not in catalog: {gameId}");
        if (string.Equals(
                game.Snapshot?.ContentSha256,
                snapshot.ContentSha256,
                StringComparison.Ordinal))
        {
            return new CatalogCommitResult(
                CatalogCommitKind.Unchanged,
                expected,
                "Save content has not changed.");
        }

        var preflight = await VerifyExpectedAsync(expected, cancellationToken)
            .ConfigureAwait(false);
        if (preflight.Result is not null)
        {
            return preflight.Result;
        }

        CloudObject uploadedArchive;
        try
        {
            uploadedArchive = await cloud.UploadAsync(
                $"archive-{gameId:N}-{Guid.NewGuid():N}.zip",
                archive,
                "application/zip",
                cancellationToken).ConfigureAwait(false);
            if (uploadedArchive.Size != snapshot.ArchiveSize)
            {
                return new CatalogCommitResult(
                    CatalogCommitKind.Failed,
                    null,
                    "Uploaded archive size does not match the local snapshot.");
            }

            if (!string.IsNullOrWhiteSpace(uploadedArchive.Sha256Checksum)
                && !string.Equals(
                    uploadedArchive.Sha256Checksum,
                    snapshot.ArchiveSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new CatalogCommitResult(
                    CatalogCommitKind.Failed,
                    null,
                    "Uploaded archive checksum does not match the local snapshot.");
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            return new CatalogCommitResult(
                CatalogCommitKind.Failed,
                null,
                exception.Message);
        }

        var secondPreflight = await VerifyCatalogFilesUnchangedAsync(
            expected,
            preflight.CurrentFileIds,
            cancellationToken).ConfigureAwait(false);
        if (secondPreflight.Result is not null)
        {
            return secondPreflight.Result;
        }

        var descriptor = new SnapshotDescriptor(
            uploadedArchive.FileId,
            snapshot.ArchiveSha256,
            snapshot.ContentSha256,
            snapshot.ArchiveSize,
            timeProvider.GetUtcNow().ToUniversalTime(),
            sourceMachineId);
        var nextGames = expected.Games
            .Select(item => item.GameId == gameId
                ? item with { Snapshot = descriptor }
                : item)
            .ToArray();
        var next = expected with
        {
            Generation = expected.Generation + 1,
            Games = nextGames,
        };

        return await UploadAndVerifyCatalogAsync(
            next,
            secondPreflight.CurrentFileIds,
            game.Snapshot?.ArchiveFileId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task CleanupOrphansAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.Kind is not CatalogLoadKind.Loaded and not CatalogLoadKind.Empty
            || loaded.Catalog is null)
        {
            return;
        }

        var referenced = loaded.Catalog.Games
            .Select(game => game.Snapshot?.ArchiveFileId)
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);
        var archives = await cloud.ListAsync("archive-", cancellationToken)
            .ConfigureAwait(false);
        foreach (var archive in archives.Where(item => !referenced.Contains(item.FileId)))
        {
            await TryDeleteAsync(archive.FileId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Preflight> VerifyExpectedAsync(
        Catalog expected,
        CancellationToken cancellationToken)
    {
        var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (current.Kind == CatalogLoadKind.Conflict)
        {
            return new Preflight(
                new CatalogCommitResult(
                    CatalogCommitKind.Conflict,
                    null,
                    "Cloud catalog has conflicting generations."),
                current.CatalogFileIds);
        }

        if (current.Kind == CatalogLoadKind.Corrupt || current.Catalog is null)
        {
            return new Preflight(
                new CatalogCommitResult(
                    CatalogCommitKind.Failed,
                    null,
                    "Cloud catalog is corrupt."),
                current.CatalogFileIds);
        }

        var expectedHash = await codec.ComputeCanonicalSha256Async(
            expected,
            cancellationToken).ConfigureAwait(false);
        var currentHash = await codec.ComputeCanonicalSha256Async(
            current.Catalog,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(expectedHash, currentHash, StringComparison.Ordinal))
        {
            return new Preflight(
                new CatalogCommitResult(
                    CatalogCommitKind.Conflict,
                    current.Catalog,
                    "Cloud catalog changed during this session."),
                current.CatalogFileIds);
        }

        return new Preflight(null, current.CatalogFileIds);
    }

    private async Task<Preflight> VerifyCatalogFilesUnchangedAsync(
        Catalog expected,
        IReadOnlyList<string> expectedFileIds,
        CancellationToken cancellationToken)
    {
        var currentObjects = await cloud.ListAsync("catalog-", cancellationToken)
            .ConfigureAwait(false);
        var currentFileIds = currentObjects.Select(item => item.FileId).ToArray();
        if (currentFileIds.Length == expectedFileIds.Count
            && currentFileIds.ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedFileIds))
        {
            return new Preflight(null, currentFileIds);
        }

        return await VerifyExpectedAsync(expected, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CatalogCommitResult> UploadAndVerifyCatalogAsync(
        Catalog next,
        IReadOnlyList<string> previousCatalogFileIds,
        string? oldArchiveFileId,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await codec.ToCanonicalBytesAsync(next, cancellationToken)
                .ConfigureAwait(false);
            await using var upload = new MemoryStream(bytes, writable: false);
            var uploaded = await cloud.UploadAsync(
                $"catalog-{next.Generation:00000000}-{Guid.NewGuid():N}.json",
                upload,
                "application/json",
                cancellationToken).ConfigureAwait(false);
            if (uploaded.Size != bytes.LongLength)
            {
                return new CatalogCommitResult(
                    CatalogCommitKind.Failed,
                    null,
                    "Uploaded catalog size does not match local metadata.");
            }

            var expectedHash = await codec.ComputeCanonicalSha256Async(
                next,
                cancellationToken).ConfigureAwait(false);
            Catalog verified;
            if (!string.IsNullOrWhiteSpace(uploaded.Sha256Checksum))
            {
                if (!string.Equals(
                    expectedHash,
                    uploaded.Sha256Checksum,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return new CatalogCommitResult(
                        CatalogCommitKind.Failed,
                        null,
                        "Uploaded catalog could not be verified.");
                }

                verified = next;
            }
            else
            {
                await using var verification = new MemoryStream();
                await cloud.DownloadAsync(
                    uploaded.FileId,
                    verification,
                    cancellationToken).ConfigureAwait(false);
                verification.Position = 0;
                verified = await codec.ReadAsync(verification, cancellationToken)
                    .ConfigureAwait(false);
                var verifiedHash = await codec.ComputeCanonicalSha256Async(
                    verified,
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(expectedHash, verifiedHash, StringComparison.Ordinal))
                {
                    return new CatalogCommitResult(
                        CatalogCommitKind.Failed,
                        null,
                        "Uploaded catalog could not be verified.");
                }
            }

            foreach (var fileId in previousCatalogFileIds)
            {
                await TryDeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(oldArchiveFileId))
            {
                await TryDeleteAsync(oldArchiveFileId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new CatalogCommitResult(CatalogCommitKind.Success, verified);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            return new CatalogCommitResult(
                CatalogCommitKind.Failed,
                null,
                exception.Message);
        }
    }

    private async Task<Stream> DownloadToMemoryAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        var output = new MemoryStream();
        await cloud.DownloadAsync(fileId, output, cancellationToken)
            .ConfigureAwait(false);
        output.Position = 0;
        return output;
    }

    private async Task TryDeleteAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        try
        {
            await cloud.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            // Cleanup is best effort after a new catalog has been verified.
        }
    }

    private sealed record Preflight(
        CatalogCommitResult? Result,
        IReadOnlyList<string> CurrentFileIds);
}
