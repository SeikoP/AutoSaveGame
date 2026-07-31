using System.Collections.Concurrent;
using System.Text.Json;
using Google.Apis.Util.Store;

namespace AutoSaveGame.Infrastructure.GoogleDrive;

public sealed class MemoryDataStore : IDataStore
{
    private readonly ConcurrentDictionary<string, byte[]> values =
        new(StringComparer.Ordinal);

    public Task StoreAsync<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        values[TypedKey<T>(key)] = JsonSerializer.SerializeToUtf8Bytes(value);
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        values.TryRemove(TypedKey<T>(key), out _);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!values.TryGetValue(TypedKey<T>(key), out var bytes))
        {
            return Task.FromResult(default(T)!);
        }

        return Task.FromResult(JsonSerializer.Deserialize<T>(bytes)!);
    }

    public Task ClearAsync()
    {
        values.Clear();
        return Task.CompletedTask;
    }

    private static string TypedKey<T>(string key) =>
        $"{typeof(T).AssemblyQualifiedName}:{key}";
}

