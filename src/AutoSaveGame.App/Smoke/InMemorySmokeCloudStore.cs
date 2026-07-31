using System.IO;
using AutoSaveGame.Core.Abstractions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.App.Smoke;

internal sealed class InMemorySmokeCloudStore : ICloudObjectStore
{
    private readonly Dictionary<string, Item> items = new(StringComparer.Ordinal);
    private int nextId;

    public Task<IReadOnlyList<CloudObject>> ListAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CloudObject> result = items.Values
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
        await using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        var item = new Item($"smoke-{++nextId}", name, copy.ToArray());
        items[item.Id] = item;
        return Map(item);
    }

    public async Task DownloadAsync(
        string fileId,
        Stream destination,
        CancellationToken cancellationToken) =>
        await destination.WriteAsync(items[fileId].Content, cancellationToken);

    public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        items.Remove(fileId);
        return Task.CompletedTask;
    }

    private static CloudObject Map(Item item) =>
        new(
            item.Id,
            item.Name,
            item.Content.LongLength,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private sealed record Item(string Id, string Name, byte[] Content);
}
