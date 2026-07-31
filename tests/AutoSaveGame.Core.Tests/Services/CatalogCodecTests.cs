using System.Text;
using System.Text.Json;
using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;

namespace AutoSaveGame.Core.Tests.Services;

public sealed class CatalogCodecTests
{
    [Fact]
    public async Task WriteAsync_UsesCamelCaseAndUtcTimestamps()
    {
        var catalog = new Catalog(
            1,
            4,
            [
                new GameConfig(
                    Guid.Parse("8edcd84d-8294-4c1e-81c5-569991c58499"),
                    "Hades",
                    @"%USERPROFILE%\Documents\Saved Games\Hades",
                    new SnapshotDescriptor(
                        "drive-file",
                        new string('a', 64),
                        new string('b', 64),
                        123,
                        new DateTimeOffset(2026, 8, 1, 7, 30, 0, TimeSpan.FromHours(7)),
                        Guid.Parse("0de891ef-1e21-4d51-bacd-a5f1120437bb")),
                    true),
            ]);
        var output = new MemoryStream();

        await new CatalogCodec().WriteAsync(
            catalog,
            output,
            TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(output.ToArray());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(4, root.GetProperty("generation").GetInt64());
        var snapshot = root.GetProperty("games")[0].GetProperty("snapshot");
        Assert.Equal("2026-08-01T00:30:00+00:00", snapshot.GetProperty("lastBackupUtc").GetString());
        Assert.False(root.TryGetProperty("SchemaVersion", out _));
    }

    [Theory]
    [InlineData("""{"schemaVersion":2,"generation":1,"games":[]}""")]
    [InlineData("""{"schemaVersion":1,"generation":-1,"games":[]}""")]
    [InlineData("""{"schemaVersion":1,"generation":1,"games":[{"gameId":"00000000-0000-0000-0000-000000000000","displayName":"","pathTemplate":"%SYSTEMROOT%\\x","snapshot":null,"watchEnabled":true}]}""")]
    public async Task ReadAsync_RejectsInvalidCatalogs(string json)
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new CatalogCodec().ReadAsync(
                input,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_RejectsMalformedSnapshotHashes()
    {
        const string json =
            """
            {"schemaVersion":1,"generation":1,"games":[{"gameId":"8edcd84d-8294-4c1e-81c5-569991c58499","displayName":"Hades","pathTemplate":"%USERPROFILE%\\Hades","snapshot":{"archiveFileId":"file","archiveSha256":"XYZ","contentSha256":"abc","archiveSize":1,"lastBackupUtc":"2026-08-01T00:00:00+00:00","sourceMachineId":"0de891ef-1e21-4d51-bacd-a5f1120437bb"},"watchEnabled":true}]}
            """;
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new CatalogCodec().ReadAsync(
                input,
                TestContext.Current.CancellationToken));
    }
}
