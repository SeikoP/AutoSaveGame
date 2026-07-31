using System.Security.Cryptography;
using AutoSaveGame.Infrastructure.Restore;
using AutoSaveGame.Infrastructure.Snapshots;
using AutoSaveGame.Infrastructure.Tests.TestSupport;

namespace AutoSaveGame.Infrastructure.Tests.Restore;

public sealed class RestoreServiceTests
{
    [Fact]
    public async Task RestoreAsync_ReplacesExistingSaveAfterBothHashesMatch()
    {
        using var root = TempDirectory.Create();
        var target = root.FilePath("game-save");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "slot.dat"), "old-save");
        var archive = await BuildArchiveAsync(root, "new-save");
        var sut = new RestoreService(
            new ZipSnapshotArchive(TimeSpan.Zero),
            new RestoreFileOperations());

        await using var input = File.OpenRead(archive.Path);
        var result = await sut.RestoreAsync(
            input,
            archive.ArchiveSha256,
            archive.ContentSha256,
            target,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("new-save", File.ReadAllText(Path.Combine(target, "slot.dat")));
        Assert.Empty(Directory.EnumerateDirectories(root.Path, ".autosavegame-restore-*"));
    }

    [Fact]
    public async Task RestoreAsync_DoesNotMutateLocalSaveWhenArchiveHashMismatches()
    {
        using var root = TempDirectory.Create();
        var target = root.FilePath("game-save");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "slot.dat"), "old-save");
        var archive = await BuildArchiveAsync(root, "new-save");
        var sut = new RestoreService(
            new ZipSnapshotArchive(TimeSpan.Zero),
            new RestoreFileOperations());

        await using var input = File.OpenRead(archive.Path);
        var result = await sut.RestoreAsync(
            input,
            new string('0', 64),
            archive.ContentSha256,
            target,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(result.RolledBack);
        Assert.Equal("old-save", File.ReadAllText(Path.Combine(target, "slot.dat")));
    }

    [Fact]
    public async Task RestoreAsync_RollsBackWhenStagingCannotReplaceTarget()
    {
        using var root = TempDirectory.Create();
        var target = root.FilePath("game-save");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "slot.dat"), "old-save");
        var archive = await BuildArchiveAsync(root, "new-save");
        var files = new FaultingRestoreFileOperations(
            new RestoreFileOperations(),
            failOnMoveNumber: 2);
        var sut = new RestoreService(new ZipSnapshotArchive(TimeSpan.Zero), files);

        await using var input = File.OpenRead(archive.Path);
        var result = await sut.RestoreAsync(
            input,
            archive.ArchiveSha256,
            archive.ContentSha256,
            target,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.True(result.RolledBack);
        Assert.Equal("old-save", File.ReadAllText(Path.Combine(target, "slot.dat")));
    }

    [Fact]
    public async Task RestoreAsync_DoesNotCreateTargetWhenContentHashMismatches()
    {
        using var root = TempDirectory.Create();
        var target = root.FilePath("game-save");
        var archive = await BuildArchiveAsync(root, "new-save");
        var sut = new RestoreService(
            new ZipSnapshotArchive(TimeSpan.Zero),
            new RestoreFileOperations());

        await using var input = File.OpenRead(archive.Path);
        var result = await sut.RestoreAsync(
            input,
            archive.ArchiveSha256,
            new string('f', 64),
            target,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task RestoreAsync_RollsBackBeforePropagatingCancellation()
    {
        using var root = TempDirectory.Create();
        var target = root.FilePath("game-save");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "slot.dat"), "old-save");
        var archive = await BuildArchiveAsync(root, "new-save");
        var files = new CancelingRestoreFileOperations(new RestoreFileOperations());
        var sut = new RestoreService(new ZipSnapshotArchive(TimeSpan.Zero), files);

        await using var input = File.OpenRead(archive.Path);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.RestoreAsync(
                input,
                archive.ArchiveSha256,
                archive.ContentSha256,
                target,
                TestContext.Current.CancellationToken));

        Assert.Equal("old-save", File.ReadAllText(Path.Combine(target, "slot.dat")));
    }

    private static async Task<ArchiveFixture> BuildArchiveAsync(
        TempDirectory root,
        string saveContent)
    {
        var source = root.FilePath($"source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "slot.dat"), saveContent);
        var path = root.FilePath($"archive-{Guid.NewGuid():N}.zip");
        var result = await new ZipSnapshotArchive(TimeSpan.Zero).BuildAsync(
            source,
            path,
            TestContext.Current.CancellationToken);
        Assert.NotNull(result.ArchiveSha256);
        Assert.NotNull(result.ContentSha256);
        await using var hashInput = File.OpenRead(path);
        Assert.Equal(
            result.ArchiveSha256,
            Convert.ToHexString(await SHA256.HashDataAsync(
                hashInput,
                TestContext.Current.CancellationToken)).ToLowerInvariant());
        return new ArchiveFixture(path, result.ArchiveSha256, result.ContentSha256);
    }

    private sealed record ArchiveFixture(
        string Path,
        string ArchiveSha256,
        string ContentSha256);
}
