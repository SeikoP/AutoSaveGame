using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Infrastructure.Restore;

public sealed class RestoreService(
    ISnapshotArchive snapshotArchive,
    IRestoreFileOperations files) : IRestoreService
{
    private readonly ISnapshotArchive snapshotArchive = snapshotArchive
        ?? throw new ArgumentNullException(nameof(snapshotArchive));
    private readonly IRestoreFileOperations files = files
        ?? throw new ArgumentNullException(nameof(files));

    public async Task<RestoreResult> RestoreAsync(
        Stream cloudArchive,
        string expectedArchiveSha256,
        string expectedContentSha256,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cloudArchive);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArchiveSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedContentSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        var target = Path.GetFullPath(targetDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var workspace = files.CreateWorkspace(target);
        var archivePath = Path.Combine(workspace, "snapshot.zip");
        var stagingPath = Path.Combine(workspace, "staging");
        var rollbackPath = Path.Combine(workspace, "rollback");
        var targetMoved = false;
        var replacementMoved = false;

        try
        {
            var actualArchiveHash = await files.CopyAndHashAsync(
                cloudArchive,
                archivePath,
                cancellationToken).ConfigureAwait(false);
            if (!HashesEqual(actualArchiveHash, expectedArchiveSha256))
            {
                return FailureAndCleanup(
                    workspace,
                    "Downloaded archive SHA-256 does not match cloud metadata.");
            }

            files.CreateDirectory(stagingPath);
            var extraction = await snapshotArchive.ExtractAsync(
                archivePath,
                stagingPath,
                cancellationToken).ConfigureAwait(false);
            if (!HashesEqual(extraction.ContentSha256, expectedContentSha256))
            {
                return FailureAndCleanup(
                    workspace,
                    "Extracted save content SHA-256 does not match cloud metadata.");
            }

            if (files.DirectoryExists(target))
            {
                files.MoveDirectory(target, rollbackPath);
                targetMoved = true;
            }

            files.MoveDirectory(stagingPath, target);
            replacementMoved = true;

            var restoredHash = await files.ComputeDirectoryHashAsync(
                target,
                cancellationToken).ConfigureAwait(false);
            if (!HashesEqual(restoredHash, expectedContentSha256))
            {
                throw new InvalidDataException(
                    "Restored target SHA-256 does not match cloud metadata.");
            }

            SafeCleanup(workspace);
            return new RestoreResult(true, false, "Save restored successfully.");
        }
        catch (Exception exception)
        {
            if (replacementMoved && files.DirectoryExists(target))
            {
                TryDeleteDirectory(target);
            }

            if (targetMoved && files.DirectoryExists(rollbackPath))
            {
                Exception? rollbackFailure = null;
                try
                {
                    files.MoveDirectory(rollbackPath, target);
                    SafeCleanup(workspace);
                }
                catch (Exception rollbackException)
                {
                    rollbackFailure = rollbackException;
                }

                if (rollbackFailure is not null)
                {
                    if (exception is OperationCanceledException)
                    {
                        throw new IOException(
                            $"Restore was canceled and rollback failed. Recovery data remains at {rollbackPath}.",
                            new AggregateException(exception, rollbackFailure));
                    }

                    return new RestoreResult(
                        false,
                        false,
                        $"Restore and rollback failed: {rollbackFailure.Message}",
                        rollbackPath);
                }

                if (exception is OperationCanceledException)
                {
                    throw;
                }

                return new RestoreResult(
                    false,
                    true,
                    $"Restore failed and the previous save was restored: {exception.Message}");
            }

            SafeCleanup(workspace);
            if (exception is OperationCanceledException)
            {
                throw;
            }

            return new RestoreResult(false, false, $"Restore failed: {exception.Message}");
        }
    }

    private RestoreResult FailureAndCleanup(string workspace, string message)
    {
        SafeCleanup(workspace);
        return new RestoreResult(false, false, message);
    }

    private void SafeCleanup(string workspace)
    {
        try
        {
            files.DeleteDirectory(workspace);
        }
        catch (IOException)
        {
            // A stale workspace can be removed on the next application start.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve successful restore outcome even if temporary cleanup fails.
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            files.DeleteDirectory(path);
        }
        catch (IOException)
        {
            // Rollback below reports failure if the target cannot be replaced.
        }
        catch (UnauthorizedAccessException)
        {
            // Rollback below reports failure if the target cannot be replaced.
        }
    }

    private static bool HashesEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
