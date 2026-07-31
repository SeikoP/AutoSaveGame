using System.Text;
using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Tests.TestSupport;

internal sealed class InMemoryCloudObjectStore : ICloudObjectStore
{
    private readonly Dictionary<string, StoredObject> objects = new(StringComparer.Ordinal);
    private int nextId;
    private int uploadCalls;

    public int? FailUploadCall { get; set; }

    public int UploadCalls => uploadCalls;

    public IReadOnlyCollection<string> Names =>
        objects.Values.Select(item => item.Name).ToArray();

    public string Seed(string name, string content) =>
        Seed(name, Encoding.UTF8.GetBytes(content));

    public string Seed(string name, byte[] content)
    {
        var id = $"seed-{++nextId}";
        objects[id] = new StoredObject(id, name, content.ToArray());
        return id;
    }

    public bool ContainsId(string fileId) => objects.ContainsKey(fileId);

    public Task<IReadOnlyList<CloudObject>> ListAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CloudObject> result = objects.Values
            .Where(item => item.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(Map)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<CloudObject> UploadAsync(
        string name,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        uploadCalls++;
        if (FailUploadCall == uploadCalls)
        {
            throw new CloudStoreException(
                CloudStoreErrorKind.Network,
                $"Injected upload failure {uploadCalls}.");
        }

        await using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        var id = $"upload-{++nextId}";
        var stored = new StoredObject(id, name, copy.ToArray());
        objects[id] = stored;
        return Map(stored);
    }

    public async Task DownloadAsync(
        string fileId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var stored = objects[fileId];
        await destination.WriteAsync(stored.Content, cancellationToken);
    }

    public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        objects.Remove(fileId);
        return Task.CompletedTask;
    }

    private static CloudObject Map(StoredObject item) =>
        new(
            item.Id,
            item.Name,
            item.Content.LongLength,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private sealed record StoredObject(string Id, string Name, byte[] Content);
}

