using System.IO.Compression;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Infrastructure.Snapshots;
using AutoSaveGame.Infrastructure.Tests.TestSupport;

namespace AutoSaveGame.Infrastructure.Tests.Snapshots;

public sealed class ZipSnapshotArchiveTests
{
    [Fact]
    public async Task BuildAsync_ContentHashIsIndependentOfCreationOrder()
    {
        using var left = TempDirectory.Create();
        using var right = TempDirectory.Create();
        using var output = TempDirectory.Create();
        left.Write("a/1.dat", "one");
        left.Write("b/2.dat", "two");
        right.Write("b/2.dat", "two");
        right.Write("a/1.dat", "one");
        var sut = new ZipSnapshotArchive(TimeSpan.Zero);

        var leftResult = await sut.BuildAsync(
            left.Path,
            output.FilePath("left.zip"),
            TestContext.Current.CancellationToken);
        var rightResult = await sut.BuildAsync(
            right.Path,
            output.FilePath("right.zip"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SnapshotBuildKind.Success, leftResult.Kind);
        Assert.Equal(SnapshotBuildKind.Success, rightResult.Kind);
        Assert.Equal(leftResult.ContentSha256, rightResult.ContentSha256);
        Assert.Equal(64, leftResult.ArchiveSha256?.Length);
    }

    [Fact]
    public async Task ExtractAsync_RoundTripsUnicodeFilesAndContentHash()
    {
        using var source = TempDirectory.Create();
        using var output = TempDirectory.Create();
        using var staging = TempDirectory.Create();
        source.Write("profile/slot-01.dat", "save-one");
        source.Write("hồ-sơ/保存.dat", "save-two");
        var sut = new ZipSnapshotArchive(TimeSpan.Zero);
        var build = await sut.BuildAsync(
            source.Path,
            output.FilePath("save.zip"),
            TestContext.Current.CancellationToken);

        var extracted = await sut.ExtractAsync(
            output.FilePath("save.zip"),
            staging.Path,
            TestContext.Current.CancellationToken);

        Assert.Equal("save-one", staging.Read("profile/slot-01.dat"));
        Assert.Equal("save-two", staging.Read("hồ-sơ/保存.dat"));
        Assert.Equal(build.ContentSha256, extracted.ContentSha256);
    }

    [Fact]
    public async Task ExtractAsync_RejectsPathTraversalWithoutWritingOutsideStaging()
    {
        using var root = TempDirectory.Create();
        var archivePath = root.FilePath("malicious.zip");
        await CreateArchiveAsync(archivePath, ("../outside.dat", "stolen"));
        var stagingPath = root.FilePath("staging");
        var outsidePath = root.FilePath("outside.dat");
        var sut = new ZipSnapshotArchive(TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => sut.ExtractAsync(
                archivePath,
                stagingPath,
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task ExtractAsync_RejectsDuplicateNormalizedPaths()
    {
        using var root = TempDirectory.Create();
        var archivePath = root.FilePath("duplicate.zip");
        await CreateArchiveAsync(
            archivePath,
            ("profile/slot.dat", "first"),
            (@"profile\slot.dat", "second"));
        var sut = new ZipSnapshotArchive(TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => sut.ExtractAsync(
                archivePath,
                root.FilePath("staging"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExtractAsync_RejectsUnixSymlinkEntries()
    {
        using var root = TempDirectory.Create();
        var archivePath = root.FilePath("symlink.zip");
        await using (var output = File.Create(archivePath))
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var entry = archive.CreateEntry("link.dat");
            entry.ExternalAttributes = unchecked((int)0xA0000000);
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("target");
        }

        var sut = new ZipSnapshotArchive(TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => sut.ExtractAsync(
                archivePath,
                root.FilePath("staging"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildAsync_RejectsArchiveInsideWatchedDirectory()
    {
        using var source = TempDirectory.Create();
        source.Write("slot.dat", "save");
        var sut = new ZipSnapshotArchive(TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.BuildAsync(
                source.Path,
                source.FilePath("snapshot.zip"),
                TestContext.Current.CancellationToken));
    }

    private static async Task CreateArchiveAsync(
        string archivePath,
        params (string Name, string Content)[] entries)
    {
        await using var output = File.Create(archivePath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Name);
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync(item.Content);
        }
    }
}
