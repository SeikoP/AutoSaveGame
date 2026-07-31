namespace AutoSaveGame.Core.Models;

public sealed record Catalog(
    int SchemaVersion,
    long Generation,
    IReadOnlyList<GameConfig> Games)
{
    public static Catalog Empty { get; } = new(1, 0, []);
}

