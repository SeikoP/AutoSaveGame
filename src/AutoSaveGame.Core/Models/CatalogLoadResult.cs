namespace AutoSaveGame.Core.Models;

public enum CatalogLoadKind
{
    Empty,
    Loaded,
    Conflict,
    Corrupt,
}

public sealed record CatalogLoadResult(
    CatalogLoadKind Kind,
    Catalog? Catalog,
    IReadOnlyList<string> CatalogFileIds)
{
    public static CatalogLoadResult Empty() =>
        new(CatalogLoadKind.Empty, Models.Catalog.Empty, []);

    public static CatalogLoadResult Corrupt(IReadOnlyList<string> fileIds) =>
        new(CatalogLoadKind.Corrupt, null, fileIds);

    public static CatalogLoadResult Conflict(IReadOnlyList<string> fileIds) =>
        new(CatalogLoadKind.Conflict, null, fileIds);

    public static CatalogLoadResult Loaded(
        Models.Catalog catalog,
        IReadOnlyList<string> fileIds) =>
        new(CatalogLoadKind.Loaded, catalog, fileIds);
}

