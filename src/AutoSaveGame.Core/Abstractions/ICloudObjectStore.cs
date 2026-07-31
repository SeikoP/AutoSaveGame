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

    Task DownloadAsync(
        string fileId,
        Stream destination,
        CancellationToken cancellationToken);

    Task DeleteAsync(string fileId, CancellationToken cancellationToken);
}

