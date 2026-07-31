using AutoSaveGame.Infrastructure.GoogleDrive;

namespace AutoSaveGame.Infrastructure.Tests.GoogleDrive;

public sealed class GoogleDriveObjectStoreContractTests
{
    [Fact]
    public async Task ListAsync_UsesAppDataSpaceAndExcludesTrashedFiles()
    {
        var gateway = new RecordingDriveGateway
        {
            ListedItems =
            [
                new DriveItem(
                    "file-1",
                    "catalog-00000001-a.json",
                    123,
                    new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 1, 0, 1, 0, TimeSpan.Zero)),
            ],
        };
        var sut = new GoogleDriveObjectStore(gateway);

        var result = await sut.ListAsync(
            "catalog-'",
            TestContext.Current.CancellationToken);

        Assert.Equal("appDataFolder", gateway.LastListSpec?.Spaces);
        Assert.Equal(
            "trashed = false and name contains 'catalog-\\''",
            gateway.LastListSpec?.Query);
        Assert.Equal(
            "files(id,name,size,createdTime,modifiedTime)",
            gateway.LastListSpec?.Fields);
        Assert.Equal("file-1", result.Single().FileId);
        Assert.Equal(123, result.Single().Size);
    }

    [Fact]
    public async Task UploadAsync_UsesAppDataFolderAsTheOnlyParent()
    {
        var gateway = new RecordingDriveGateway
        {
            UploadedItem = new DriveItem(
                "new-file",
                "archive.zip",
                8,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch),
        };
        var sut = new GoogleDriveObjectStore(gateway);
        await using var content = new MemoryStream("zip-data"u8.ToArray());

        var result = await sut.UploadAsync(
            "archive.zip",
            content,
            "application/zip",
            TestContext.Current.CancellationToken);

        Assert.Equal(["appDataFolder"], gateway.LastUploadSpec?.Parents);
        Assert.Equal("archive.zip", gateway.LastUploadSpec?.Name);
        Assert.Equal("application/zip", gateway.LastUploadSpec?.ContentType);
        Assert.Equal("new-file", result.FileId);
    }

    [Fact]
    public async Task DownloadAsync_WritesGatewayContentToCallerStream()
    {
        var gateway = new RecordingDriveGateway
        {
            DownloadContent = "save-data"u8.ToArray(),
        };
        var sut = new GoogleDriveObjectStore(gateway);
        await using var output = new MemoryStream();

        await sut.DownloadAsync(
            "file-1",
            output,
            TestContext.Current.CancellationToken);

        Assert.Equal("save-data", System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public async Task DeleteAsync_TreatsMissingObjectAsAlreadyDeleted()
    {
        var gateway = new RecordingDriveGateway
        {
            DeleteException = new CloudObjectNotFoundException("file-1"),
        };
        var sut = new GoogleDriveObjectStore(gateway);

        await sut.DeleteAsync("file-1", TestContext.Current.CancellationToken);
    }

    private sealed class RecordingDriveGateway : IDriveGateway
    {
        public IReadOnlyList<DriveItem> ListedItems { get; init; } = [];

        public DriveItem? UploadedItem { get; init; }

        public byte[] DownloadContent { get; init; } = [];

        public Exception? DeleteException { get; init; }

        public DriveListSpec? LastListSpec { get; private set; }

        public DriveUploadSpec? LastUploadSpec { get; private set; }

        public Task<IReadOnlyList<DriveItem>> ListAsync(
            DriveListSpec spec,
            CancellationToken cancellationToken)
        {
            LastListSpec = spec;
            return Task.FromResult(ListedItems);
        }

        public Task<DriveItem> UploadAsync(
            DriveUploadSpec spec,
            Stream content,
            CancellationToken cancellationToken)
        {
            LastUploadSpec = spec;
            return Task.FromResult(
                UploadedItem ?? throw new InvalidOperationException("No upload response."));
        }

        public async Task DownloadAsync(
            string fileId,
            Stream destination,
            CancellationToken cancellationToken) =>
            await destination.WriteAsync(DownloadContent, cancellationToken);

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken) =>
            DeleteException is null
                ? Task.CompletedTask
                : Task.FromException(DeleteException);
    }
}
