using System.Text;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Tests.TestData;

internal static class CatalogTestData
{
    public static CloudObject Object(string fileId, string name, long size = 10) =>
        new(
            fileId,
            name,
            size,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 1, 0, TimeSpan.Zero));

    public static string Json(long generation, string gameName = "Hades") =>
        $$"""
          {"schemaVersion":1,"generation":{{generation}},"games":[{"gameId":"8edcd84d-8294-4c1e-81c5-569991c58499","displayName":"{{gameName}}","pathTemplate":"%USERPROFILE%\\Documents\\Saved Games\\Hades","snapshot":null,"watchEnabled":true}]}
          """;

    public static Func<string, CancellationToken, Task<Stream>> Downloader(
        IReadOnlyDictionary<string, string> contentById) =>
        (fileId, _) =>
        {
            Stream stream = new MemoryStream(
                Encoding.UTF8.GetBytes(contentById[fileId]),
                writable: false);
            return Task.FromResult(stream);
        };
}

