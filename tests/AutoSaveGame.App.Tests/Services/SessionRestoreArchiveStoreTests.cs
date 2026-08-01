using AutoSaveGame.App.Services;

namespace AutoSaveGame.App.Tests.Services;

public sealed class SessionRestoreArchiveStoreTests
{
    [Fact]
    public async Task CreateAsync_UsesDiskAndDeletesArchiveOnDispose()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"AutoSaveGame-RestoreStoreTest-{Guid.NewGuid():N}");
        try
        {
            var store = new SessionRestoreArchiveStore(root);
            var handle = await store.CreateAsync(TestContext.Current.CancellationToken);
            Assert.IsType<FileStream>(handle.Stream);
            var archivePath = Directory.GetFiles(root).Single();

            await handle.DisposeAsync();

            Assert.False(File.Exists(archivePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
