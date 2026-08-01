using System.Text;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;
using AutoSaveGame.Core.Tests.TestSupport;

namespace AutoSaveGame.Core.Tests.Services;

public sealed class CatalogRepositoryTests
{
    private static readonly Guid GameId =
        Guid.Parse("8edcd84d-8294-4c1e-81c5-569991c58499");
    private static readonly Guid MachineId =
        Guid.Parse("0de891ef-1e21-4d51-bacd-a5f1120437bb");

    [Fact]
    public async Task SaveCatalogAsync_CreatesOneNewGenerationWithoutUploadingArchive()
    {
        var cloud = new InMemoryCloudObjectStore();
        var expected = await SeedCatalogAsync(cloud, generation: 1);
        var next = expected with
        {
            Generation = 2,
            Games =
            [
                expected.Games.Single() with { DisplayName = "Hades II" },
            ],
        };
        var sut = CreateRepository(cloud);

        var result = await sut.SaveCatalogAsync(
            expected,
            next,
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogCommitKind.Success, result.Kind);
        Assert.Equal(1, cloud.UploadCalls);
        Assert.DoesNotContain(cloud.Names, name => name.StartsWith("archive-", StringComparison.Ordinal));
        Assert.Equal(2, (await sut.LoadAsync(TestContext.Current.CancellationToken)).Catalog?.Generation);
    }

    [Fact]
    public async Task SaveCatalogAsync_DetectsChangedExpectedGenerationBeforeUpload()
    {
        var cloud = new InMemoryCloudObjectStore();
        var stale = await SeedCatalogAsync(cloud, generation: 1);
        await SeedCatalogAsync(cloud, generation: 2);
        var next = stale with { Generation = 2 };
        var sut = CreateRepository(cloud);

        var result = await sut.SaveCatalogAsync(
            stale,
            next,
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogCommitKind.Conflict, result.Kind);
        Assert.Equal(0, cloud.UploadCalls);
    }

    [Fact]
    public async Task CommitSnapshotAsync_KeepsOldSnapshotWhenCatalogUploadFails()
    {
        var cloud = new InMemoryCloudObjectStore();
        var oldArchiveId = cloud.Seed("archive-old.zip", "old-save");
        var expected = await SeedCatalogAsync(cloud, 1, oldArchiveId);
        cloud.FailUploadCall = 2;
        var sut = CreateRepository(cloud);
        await using var archive = new MemoryStream("new-save"u8.ToArray());

        var result = await sut.CommitSnapshotAsync(
            expected,
            GameId,
            archive,
            Snapshot("new-content"),
            MachineId,
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogCommitKind.Failed, result.Kind);
        Assert.True(cloud.ContainsId(oldArchiveId));
        Assert.Equal(1, (await sut.LoadAsync(TestContext.Current.CancellationToken)).Catalog?.Generation);
    }

    [Fact]
    public async Task CommitSnapshotAsync_DeletesOldSnapshotOnlyAfterNewCatalogVerifies()
    {
        var cloud = new InMemoryCloudObjectStore();
        var oldArchiveId = cloud.Seed("archive-old.zip", "old-save");
        var expected = await SeedCatalogAsync(cloud, 1, oldArchiveId);
        var sut = CreateRepository(cloud);
        await using var archive = new MemoryStream("new-save"u8.ToArray());

        var result = await sut.CommitSnapshotAsync(
            expected,
            GameId,
            archive,
            Snapshot("new-content"),
            MachineId,
            TestContext.Current.CancellationToken);

        var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogCommitKind.Success, result.Kind);
        Assert.Equal(2, loaded.Catalog?.Generation);
        Assert.False(cloud.ContainsId(oldArchiveId));
        Assert.NotEqual(oldArchiveId, loaded.Catalog?.Games.Single().Snapshot?.ArchiveFileId);
        Assert.Equal(2, cloud.Names.Count);
    }

    [Fact]
    public async Task SaveCatalogAsync_SkipsVerificationDownloadWhenDriveSha256Matches()
    {
        var cloud = new InMemoryCloudObjectStore { ReturnChecksums = true };
        var expected = await SeedCatalogAsync(cloud, generation: 1);
        var next = expected with { Generation = 2 };
        var sut = CreateRepository(cloud);

        var result = await sut.SaveCatalogAsync(
            expected,
            next,
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogCommitKind.Success, result.Kind);
        Assert.Equal(1, cloud.DownloadCalls);
    }

    [Fact]
    public async Task CommitSnapshotAsync_RejectsArchiveWhenDriveSha256DoesNotMatch()
    {
        var cloud = new InMemoryCloudObjectStore
        {
            ReturnChecksums = true,
            UploadChecksumOverride = new string('f', 64),
        };
        var expected = await SeedCatalogAsync(cloud, generation: 1);
        var sut = CreateRepository(cloud);
        await using var archive = new MemoryStream("new-save"u8.ToArray());

        var result = await sut.CommitSnapshotAsync(
            expected,
            GameId,
            archive,
            Snapshot("new-content"),
            MachineId,
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogCommitKind.Failed, result.Kind);
        Assert.Equal(1, cloud.UploadCalls);
    }

    [Fact]
    public async Task CommitSnapshotAsync_SkipsSecondCatalogDownloadWhenIdsAreUnchanged()
    {
        var cloud = new InMemoryCloudObjectStore { ReturnChecksums = true };
        var expected = await SeedCatalogAsync(cloud, generation: 1);
        var sut = CreateRepository(cloud);
        await using var archive = new MemoryStream("new-save"u8.ToArray());

        var result = await sut.CommitSnapshotAsync(
            expected,
            GameId,
            archive,
            Snapshot("new-content"),
            MachineId,
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogCommitKind.Success, result.Kind);
        Assert.Equal(1, cloud.DownloadCalls);
    }

    [Fact]
    public async Task SaveCatalogAsync_ForwardsCloudUploadByteProgress()
    {
        var cloud = new InMemoryCloudObjectStore { ReturnChecksums = true };
        var expected = await SeedCatalogAsync(cloud, generation: 1);
        var reported = new List<CloudTransferProgress>();
        var progress = new InlineProgress<CloudTransferProgress>(reported.Add);
        var codec = new CatalogCodec();
        var sut = new CatalogRepository(
            cloud,
            codec,
            new CatalogSelector(codec),
            TimeProvider.System,
            progress);

        await sut.SaveCatalogAsync(
            expected,
            expected with { Generation = 2 },
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(reported);
        Assert.Equal(reported[^1].TotalBytes, reported[^1].BytesTransferred);
    }

    private static CatalogRepository CreateRepository(InMemoryCloudObjectStore cloud) =>
        new(
            cloud,
            new CatalogCodec(),
            new CatalogSelector(new CatalogCodec()),
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));

    private static async Task<Catalog> SeedCatalogAsync(
        InMemoryCloudObjectStore cloud,
        long generation,
        string? archiveFileId = null)
    {
        var snapshot = archiveFileId is null
            ? null
            : new SnapshotDescriptor(
                archiveFileId,
                new string('a', 64),
                new string('b', 64),
                8,
                DateTimeOffset.UnixEpoch,
                MachineId);
        var catalog = new Catalog(
            1,
            generation,
            [
                new GameConfig(
                    GameId,
                    "Hades",
                    @"%USERPROFILE%\Documents\Hades",
                    snapshot,
                    true),
            ]);
        await using var output = new MemoryStream();
        await new CatalogCodec().WriteAsync(
            catalog,
            output,
            TestContext.Current.CancellationToken);
        cloud.Seed(
            $"catalog-{generation:00000000}-{Guid.NewGuid():N}.json",
            output.ToArray());
        return catalog;
    }

    private static SnapshotBuildResult Snapshot(string content) =>
        new(
            SnapshotBuildKind.Success,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(content))).ToLowerInvariant(),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                "new-save"u8.ToArray())).ToLowerInvariant(),
            "new-save"u8.Length,
            "snapshot.zip",
            null);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
