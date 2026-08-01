using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Abstractions;

public interface ICloudObjectStore
{
    Task<IReadOnlyList<CloudObject>> ListAsync(
        string prefix,
        CancellationToken cancellationToken);

    Task<CloudObject> UploadAsync(
        string name,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    Task<CloudObject> UploadAsync(
        string name,
        Stream content,
        string contentType,
        IProgress<CloudTransferProgress>? progress,
        CancellationToken cancellationToken) =>
        UploadAsync(name, content, contentType, cancellationToken);

    Task DownloadAsync(
        string fileId,
        Stream destination,
        CancellationToken cancellationToken);

    Task DownloadAsync(
        string fileId,
        Stream destination,
        IProgress<CloudTransferProgress>? progress,
        CancellationToken cancellationToken) =>
        DownloadAsync(fileId, destination, cancellationToken);

    Task DeleteAsync(string fileId, CancellationToken cancellationToken);
}
