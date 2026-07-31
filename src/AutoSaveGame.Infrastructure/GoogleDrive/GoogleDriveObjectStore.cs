using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Infrastructure.GoogleDrive;

public sealed class GoogleDriveObjectStore(IDriveGateway gateway) : ICloudObjectStore
{
    private const string AppDataFolder = "appDataFolder";
    private const string FileFields = "files(id,name,size,createdTime,modifiedTime)";
    private const string SingleFileFields = "id,name,size,createdTime,modifiedTime";

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
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        var item = await gateway.UploadAsync(
            new DriveUploadSpec(
                name,
                [AppDataFolder],
                contentType,
                SingleFileFields),
            content,
            cancellationToken).ConfigureAwait(false);
        return Map(item);
    }

    public Task DownloadAsync(
        string fileId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(destination);
        return gateway.DownloadAsync(fileId, destination, cancellationToken);
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
            item.ModifiedUtc);
}

