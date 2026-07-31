namespace AutoSaveGame.Core.Models;

public enum CatalogCommitKind
{
    Success,
    Unchanged,
    Conflict,
    Failed,
}

public sealed record CatalogCommitResult(
    CatalogCommitKind Kind,
    Catalog? Catalog,
    string? Message = null);

