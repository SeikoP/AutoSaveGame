using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;
using AutoSaveGame.Core.Tests.TestSupport;

namespace AutoSaveGame.Core.Tests.Services;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task BackupAsync_DoesNotUploadWhenContentHashIsUnchanged()
    {
        var contentHash = new string('b', 64);
        var game = new GameConfig(
            Guid.Parse("8edcd84d-8294-4c1e-81c5-569991c58499"),
            "Hades",
            @"%USERPROFILE%\Documents\Hades",
            new SnapshotDescriptor(
                "old",
                new string('a', 64),
                contentHash,
                8,
                DateTimeOffset.UnixEpoch,
                Guid.Parse("0de891ef-1e21-4d51-bacd-a5f1120437bb")),
            true);
        var catalog = new Catalog(1, 1, [game]);
        var cloud = new InMemoryCloudObjectStore();
        var repository = new CatalogRepository(
            cloud,
            new CatalogCodec(),
            new CatalogSelector(new CatalogCodec()),
            TimeProvider.System);
        var paths = new PathTemplateService(
            new Dictionary<string, string>
            {
                ["USERPROFILE"] = @"C:\Users\Cafe",
            });
        var archive = new StubSnapshotArchive(
            new SnapshotBuildResult(
                SnapshotBuildKind.Success,
                contentHash,
                new string('c', 64),
                8,
                "unused.zip",
                null));
        var reported = new List<OperationProgress>();
        var sut = new BackupService(
            archive,
            repository,
            paths,
            Guid.Parse("0de891ef-1e21-4d51-bacd-a5f1120437bb"),
            Path.GetTempPath(),
            new InlineProgress<OperationProgress>(reported.Add));

        var result = await sut.BackupAsync(
            game,
            catalog,
            TestContext.Current.CancellationToken);

        Assert.Equal(BackupKind.Unchanged, result.Kind);
        Assert.Equal(0, cloud.UploadCalls);
        Assert.Equal(OperationStage.BuildingArchive, reported[0].Stage);
        Assert.Equal(OperationStage.Completed, reported[^1].Stage);
        Assert.Equal(OperationOutcome.Succeeded, reported[^1].Outcome);
    }

    private sealed class StubSnapshotArchive(SnapshotBuildResult result)
        : ISnapshotArchive
    {
        public Task<SnapshotBuildResult> BuildAsync(
            string sourceDirectory,
            string archivePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(result with { ArchivePath = archivePath });

        public Task<SnapshotExtractResult> ExtractAsync(
            string archivePath,
            string stagingDirectory,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
