using System.IO;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;
using AutoSaveGame.Infrastructure.Restore;
using AutoSaveGame.Infrastructure.Snapshots;

namespace AutoSaveGame.App.Smoke;

public static class SmokeTestRunner
{
    public static async Task<int> RunAsync(
        string rootDirectory,
        CancellationToken cancellationToken)
    {
        var root = ValidateRoot(rootDirectory);
        Directory.CreateDirectory(root);
        var saveDirectory = Path.Combine(root, "save");
        var tempDirectory = Path.Combine(root, "temp");
        Directory.CreateDirectory(saveDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(saveDirectory, "slot.dat"),
            "smoke-save-v1",
            cancellationToken);
        var expectedSaveHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(
                    Path.Combine(saveDirectory, "slot.dat"),
                    cancellationToken)));
        await File.WriteAllTextAsync(
            Path.Combine(root, "expected-save.sha256"),
            expectedSaveHash,
            cancellationToken);

        var cloud = new InMemorySmokeCloudStore();
        var codec = new CatalogCodec();
        var repository = new CatalogRepository(
            cloud,
            codec,
            new CatalogSelector(codec),
            TimeProvider.System);
        var game = new GameConfig(
            Guid.Parse("8edcd84d-8294-4c1e-81c5-569991c58499"),
            "Smoke Game",
            saveDirectory,
            null,
            false);
        var initial = new Catalog(1, 1, [game]);
        var initialCommit = await repository.SaveCatalogAsync(
            Catalog.Empty,
            initial,
            cancellationToken);
        Require(
            initialCommit.Kind == CatalogCommitKind.Success
            && initialCommit.Catalog is not null,
            initialCommit.Message ?? "Initial catalog commit failed.");
        var committedCatalog = initialCommit.Catalog
            ?? throw new InvalidOperationException("Initial catalog is missing.");

        var archive = new ZipSnapshotArchive(TimeSpan.Zero);
        var backup = new BackupService(
            archive,
            repository,
            new PathTemplateService(new Dictionary<string, string>()),
            Guid.Parse("0de891ef-1e21-4d51-bacd-a5f1120437bb"),
            tempDirectory);
        var backupResult = await backup.BackupAsync(
            game,
            committedCatalog,
            cancellationToken);
        Require(
            backupResult.Kind == BackupKind.Success,
            backupResult.Message ?? "Smoke backup failed.");

        var loaded = await repository.LoadAsync(cancellationToken);
        var committedGame = loaded.Catalog?.Games.Single()
            ?? throw new InvalidOperationException("Committed smoke game is missing.");
        var snapshot = committedGame.Snapshot
            ?? throw new InvalidOperationException("Committed smoke snapshot is missing.");

        Directory.Delete(saveDirectory, recursive: true);
        await using var download = new MemoryStream();
        await cloud.DownloadAsync(
            snapshot.ArchiveFileId,
            download,
            cancellationToken);
        download.Position = 0;
        var restore = new RestoreService(archive, new RestoreFileOperations());
        var restoreResult = await restore.RestoreAsync(
            download,
            snapshot.ArchiveSha256,
            snapshot.ContentSha256,
            saveDirectory,
            cancellationToken);
        Require(restoreResult.Success, restoreResult.Message);

        var restored = await File.ReadAllTextAsync(
            Path.Combine(saveDirectory, "slot.dat"),
            cancellationToken);
        Require(
            string.Equals(restored, "smoke-save-v1", StringComparison.Ordinal),
            "Restored smoke save content does not match.");
        await File.WriteAllTextAsync(
            Path.Combine(root, "smoke-result.txt"),
            "PASS",
            cancellationToken);
        return 0;
    }

    private static string ValidateRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith(
                "AutoSaveGame-Smoke-",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Smoke root must be a task-specific AutoSaveGame-Smoke-* directory under TEMP.");
        }

        return root;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
