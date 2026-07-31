using AutoSaveGame.Core.Models;
using AutoSaveGame.Core.Services;
using AutoSaveGame.Core.Tests.TestData;

namespace AutoSaveGame.Core.Tests.Services;

public sealed class CatalogSelectorTests
{
    [Fact]
    public async Task SelectAsync_ReturnsEmptyWhenDriveHasNoCatalog()
    {
        var sut = new CatalogSelector(new CatalogCodec());

        var result = await sut.SelectAsync(
            [],
            CatalogTestData.Downloader(new Dictionary<string, string>()),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogLoadKind.Empty, result.Kind);
        Assert.Equal(0, result.Catalog?.Generation);
    }

    [Fact]
    public async Task SelectAsync_ReturnsTheHighestValidGeneration()
    {
        var objects = new[]
        {
            CatalogTestData.Object("invalid", "catalog-00000009-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json"),
            CatalogTestData.Object("older", "catalog-00000003-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json"),
            CatalogTestData.Object("newer", "catalog-00000008-cccccccccccccccccccccccccccccccc.json"),
        };
        var content = new Dictionary<string, string>
        {
            ["invalid"] = "{broken",
            ["older"] = CatalogTestData.Json(3, "Old"),
            ["newer"] = CatalogTestData.Json(8, "Current"),
        };
        var sut = new CatalogSelector(new CatalogCodec());

        var result = await sut.SelectAsync(
            objects,
            CatalogTestData.Downloader(content),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogLoadKind.Loaded, result.Kind);
        Assert.Equal(8, result.Catalog?.Generation);
        Assert.Equal("Current", result.Catalog?.Games.Single().DisplayName);
        Assert.Equal("newer", result.CatalogFileIds.Single());
    }

    [Fact]
    public async Task SelectAsync_ReturnsConflictForDifferentCatalogsAtHighestGeneration()
    {
        var objects = new[]
        {
            CatalogTestData.Object("a", "catalog-00000007-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json"),
            CatalogTestData.Object("b", "catalog-00000007-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json"),
        };
        var sut = new CatalogSelector(new CatalogCodec());

        var result = await sut.SelectAsync(
            objects,
            CatalogTestData.Downloader(new Dictionary<string, string>
            {
                ["a"] = CatalogTestData.Json(7, "Game A"),
                ["b"] = CatalogTestData.Json(7, "Game B"),
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogLoadKind.Conflict, result.Kind);
        Assert.Null(result.Catalog);
        Assert.Equal(2, result.CatalogFileIds.Count);
    }

    [Fact]
    public async Task SelectAsync_TreatsByteIdenticalCatalogsAsDuplicates()
    {
        var json = CatalogTestData.Json(7, "Same");
        var objects = new[]
        {
            CatalogTestData.Object("a", "catalog-00000007-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json"),
            CatalogTestData.Object("b", "catalog-00000007-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json"),
        };
        var sut = new CatalogSelector(new CatalogCodec());

        var result = await sut.SelectAsync(
            objects,
            CatalogTestData.Downloader(new Dictionary<string, string>
            {
                ["a"] = json,
                ["b"] = json,
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogLoadKind.Loaded, result.Kind);
        Assert.Equal(7, result.Catalog?.Generation);
        Assert.Equal(2, result.CatalogFileIds.Count);
    }

    [Fact]
    public async Task SelectAsync_ReturnsCorruptWhenEveryCatalogIsInvalid()
    {
        var objects = new[]
        {
            CatalogTestData.Object("bad", "catalog-00000002-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json"),
        };
        var sut = new CatalogSelector(new CatalogCodec());

        var result = await sut.SelectAsync(
            objects,
            CatalogTestData.Downloader(new Dictionary<string, string>
            {
                ["bad"] = "{broken",
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogLoadKind.Corrupt, result.Kind);
        Assert.Null(result.Catalog);
    }
}
