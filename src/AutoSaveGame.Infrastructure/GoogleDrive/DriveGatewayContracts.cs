namespace AutoSaveGame.Infrastructure.GoogleDrive;

public sealed record DriveListSpec(string Spaces, string Query, string Fields);

public sealed record DriveUploadSpec(
    string Name,
    IReadOnlyList<string> Parents,
    string ContentType,
    string Fields);

public sealed record DriveTransferProgress(
    long BytesTransferred,
    long? TotalBytes);

public sealed record DriveItem(
    string FileId,
    string Name,
    long Size,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc,
    string? Sha256Checksum = null,
    string? Md5Checksum = null);

public interface IDriveGateway
{
    Task<IReadOnlyList<DriveItem>> ListAsync(
        DriveListSpec spec,
        CancellationToken cancellationToken);

    Task<DriveItem> UploadAsync(
        DriveUploadSpec spec,
        Stream content,
        CancellationToken cancellationToken);

    Task<DriveItem> UploadAsync(
        DriveUploadSpec spec,
        Stream content,
        IProgress<DriveTransferProgress>? progress,
        CancellationToken cancellationToken) =>
        UploadAsync(spec, content, cancellationToken);

    Task DownloadAsync(
        string fileId,
        Stream destination,
        CancellationToken cancellationToken);

    Task DownloadAsync(
        string fileId,
        Stream destination,
        IProgress<DriveTransferProgress>? progress,
        CancellationToken cancellationToken) =>
        DownloadAsync(fileId, destination, cancellationToken);

    Task DeleteAsync(string fileId, CancellationToken cancellationToken);
}

public sealed class CloudObjectNotFoundException(string fileId)
    : Exception($"Cloud object was not found: {fileId}");
