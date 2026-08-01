using AutoSaveGame.App.Services;
using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.App.Tests.Services;

public sealed class ApplicationRuntimeTests
{
    [Fact]
    public async Task RestoreAsync_StopsWatcherRestoresAndRestartsWatcher()
    {
        var events = new List<string>();
        var game = new GameConfig(
            Guid.Parse("8edcd84d-8294-4c1e-81c5-569991c58499"),
            "Hades",
            Path.Combine(Path.GetTempPath(), "AutoSaveGame-Runtime-Test"),
            new SnapshotDescriptor(
                "archive-file",
                new string('a', 64),
                new string('b', 64),
                8,
                DateTimeOffset.UnixEpoch,
                Guid.Parse("0de891ef-1e21-4d51-bacd-a5f1120437bb")),
            true);
        var catalog = new Catalog(1, 1, [game]);
        var watcher = new RecordingWatcher(events);
        var archiveStore = new RecordingRestoreArchiveStore(events);
        var runtime = new ApplicationRuntime(
            new FakeSession(),
            new FakeCatalogRepository(catalog),
            new RecordingCloudStore(events),
            new RecordingRestoreService(events),
            new RecordingScheduler(),
            watcher,
            new PathTemplateService(new Dictionary<string, string>()),
            archiveStore,
            (_, _, _) => Task.FromResult(new BackupResult(BackupKind.Success)));
        await runtime.SignInAsync(TestContext.Current.CancellationToken);
        events.Clear();

        await runtime.RestoreAsync(
            game.GameId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["stop", "create-archive", "download", "restore", "dispose-archive", "start"],
            events);
        Assert.Equal(GameSyncStatus.Watching, runtime.Games.Single().StateMachine.Status);
    }

    [Fact]
    public async Task AddOrUpdateGameAsync_RequestsInitialBackupAfterSavingCatalog()
    {
        var savePath = Path.Combine(
            Path.GetTempPath(),
            $"AutoSaveGame-Runtime-Add-{Guid.NewGuid():N}");
        Directory.CreateDirectory(savePath);
        try
        {
            var scheduler = new RecordingScheduler();
            var runtime = new ApplicationRuntime(
                new FakeSession(),
                new FakeCatalogRepository(Catalog.Empty),
                new RecordingCloudStore([]),
                new RecordingRestoreService([]),
                scheduler,
                new RecordingWatcher([]),
                new PathTemplateService(new Dictionary<string, string>()),
                new RecordingRestoreArchiveStore([]),
                (_, _, _) => Task.FromResult(new BackupResult(BackupKind.Success)));
            await runtime.SignInAsync(TestContext.Current.CancellationToken);

            await runtime.AddOrUpdateGameAsync(
                null,
                "Hades",
                savePath,
                TestContext.Current.CancellationToken);

            Assert.Single(scheduler.BackupNowGameIds);
            Assert.Equal(runtime.Games.Single().Config.GameId, scheduler.BackupNowGameIds[0]);
        }
        finally
        {
            Directory.Delete(savePath, recursive: true);
        }
    }

    private sealed class FakeSession : IUserSession
    {
        public bool IsSignedIn { get; private set; }

        public Task SignInAsync(CancellationToken cancellationToken)
        {
            IsSignedIn = true;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            IsSignedIn = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCatalogRepository(Catalog catalog) : ICatalogRepository
    {
        public Task<CatalogLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CatalogLoadResult.Loaded(catalog, ["catalog-file"]));

        public Task<CatalogCommitResult> SaveCatalogAsync(
            Catalog expected,
            Catalog next,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CatalogCommitResult(CatalogCommitKind.Success, next));

        public Task<CatalogCommitResult> CommitSnapshotAsync(
            Catalog expected,
            Guid gameId,
            Stream archive,
            SnapshotBuildResult snapshot,
            Guid sourceMachineId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CleanupOrphansAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingCloudStore(List<string> events) : ICloudObjectStore
    {
        public Task<IReadOnlyList<CloudObject>> ListAsync(
            string prefix,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CloudObject> UploadAsync(
            string name,
            Stream content,
            string contentType,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task DownloadAsync(
            string fileId,
            Stream destination,
            CancellationToken cancellationToken)
        {
            events.Add("download");
            await destination.WriteAsync("zip-data"u8.ToArray(), cancellationToken);
        }

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingRestoreService(List<string> events) : IRestoreService
    {
        public Task<RestoreResult> RestoreAsync(
            Stream cloudArchive,
            string expectedArchiveSha256,
            string expectedContentSha256,
            string targetDirectory,
            CancellationToken cancellationToken)
        {
            events.Add("restore");
            return Task.FromResult(
                new RestoreResult(true, false, "Save restored successfully."));
        }
    }

    private sealed class RecordingScheduler : IBackupScheduler
    {
        public List<Guid> BackupNowGameIds { get; } = [];

        public void RegisterGame(Guid gameId, GameSyncStateMachine stateMachine)
        {
        }

        public void UnregisterGame(Guid gameId)
        {
        }

        public void MarkDirty(Guid gameId)
        {
        }

        public Task BackupNowAsync(Guid gameId, CancellationToken cancellationToken)
        {
            BackupNowGameIds.Add(gameId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRestoreArchiveStore(List<string> events)
        : IRestoreArchiveStore
    {
        public ValueTask<IRestoreArchiveHandle> CreateAsync(
            CancellationToken cancellationToken)
        {
            events.Add("create-archive");
            return ValueTask.FromResult<IRestoreArchiveHandle>(
                new RecordingRestoreArchiveHandle(events));
        }
    }

    private sealed class RecordingRestoreArchiveHandle(List<string> events)
        : IRestoreArchiveHandle
    {
        public Stream Stream { get; } = new MemoryStream();

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
            events.Add("dispose-archive");
        }
    }

    private sealed class RecordingWatcher(List<string> events) : IGameDirectoryWatcher
    {
        public Task StartAsync(GameConfig game, CancellationToken cancellationToken)
        {
            events.Add("start");
            return Task.CompletedTask;
        }

        public Task StopAsync(Guid gameId)
        {
            events.Add("stop");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
