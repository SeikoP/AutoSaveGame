using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Infrastructure.GoogleDrive;

public sealed class GoogleDriveObjectStore(IDriveGateway gateway) : ICloudObjectStore
{
    private const string AppDataFolder = "appDataFolder";
    private const string FileFields =
        "files(id,name,size,createdTime,modifiedTime,sha256Checksum,md5Checksum)";
    private const string SingleFileFields =
        "id,name,size,createdTime,modifiedTime,sha256Checksum,md5Checksum";

    private readonly IDriveGateway gateway = gateway
        ?? throw new ArgumentNullException(nameof(gateway));

    public async Task<IReadOnlyList<CloudObject>> ListAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        var escapedPrefix = prefix
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
        var query = string.IsNullOrEmpty(escapedPrefix)
            ? "trashed = false"
            : $"trashed = false and name contains '{escapedPrefix}'";
        var items = await gateway.ListAsync(
            new DriveListSpec(AppDataFolder, query, FileFields),
            cancellationToken).ConfigureAwait(false);
        return items.Select(Map).ToArray();
    }

    public async Task<CloudObject> UploadAsync(
        string name,
        Stream content,
        string contentType,
        CancellationToken cancellationToken) =>
        await UploadAsync(name, content, contentType, null, cancellationToken)
            .ConfigureAwait(false);

    public async Task<CloudObject> UploadAsync(
        string name,
        Stream content,
        string contentType,
        IProgress<CloudTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        var driveProgress = progress is null
            ? null
            : new DriveProgressAdapter(progress);
        var item = await gateway.UploadAsync(
            new DriveUploadSpec(
                name,
                [AppDataFolder],
                contentType,
                SingleFileFields),
            content,
            driveProgress,
            cancellationToken).ConfigureAwait(false);
        return Map(item);
    }

    public Task DownloadAsync(
        string fileId,
        Stream destination,
        CancellationToken cancellationToken)
        => DownloadAsync(fileId, destination, null, cancellationToken);

    public Task DownloadAsync(
        string fileId,
        Stream destination,
        IProgress<CloudTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(destination);
        var driveProgress = progress is null
            ? null
            : new DriveProgressAdapter(progress);
        return gateway.DownloadAsync(
            fileId,
            destination,
            driveProgress,
            cancellationToken);
    }

    public async Task DeleteAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        try
        {
            await gateway.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
        }
        catch (CloudObjectNotFoundException)
        {
            // Deleting an already absent orphan is idempotent.
        }
    }

    private static CloudObject Map(DriveItem item) =>
        new(
            item.FileId,
            item.Name,
            item.Size,
            item.CreatedUtc,
            item.ModifiedUtc,
            item.Sha256Checksum,
            item.Md5Checksum);

    private sealed class DriveProgressAdapter(
        IProgress<CloudTransferProgress> target) : IProgress<DriveTransferProgress>
    {
        public void Report(DriveTransferProgress value) =>
            target.Report(new CloudTransferProgress(
                value.BytesTransferred,
                value.TotalBytes));
    }
}
