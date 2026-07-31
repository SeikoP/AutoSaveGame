using System.Text.RegularExpressions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Services;

public sealed partial class CatalogSelector(CatalogCodec codec)
{
    private readonly CatalogCodec codec = codec
        ?? throw new ArgumentNullException(nameof(codec));

    public async Task<CatalogLoadResult> SelectAsync(
        IEnumerable<CloudObject> cloudObjects,
        Func<string, CancellationToken, Task<Stream>> download,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cloudObjects);
        ArgumentNullException.ThrowIfNull(download);

        var candidates = cloudObjects
            .Select(item => (Item: item, Match: CatalogNamePattern().Match(item.Name)))
            .Where(candidate => candidate.Match.Success)
            .ToArray();

        if (candidates.Length == 0)
        {
            return CatalogLoadResult.Empty();
        }

        var valid = new List<ValidCatalog>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var input = await download(
                candidate.Item.FileId,
                cancellationToken).ConfigureAwait(false);
            await using var copy = new MemoryStream();
            await input.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            var bytes = copy.ToArray();

            try
            {
                await using var parseStream = new MemoryStream(bytes, writable: false);
                var catalog = await codec.ReadAsync(
                    parseStream,
                    cancellationToken).ConfigureAwait(false);
                var filenameGeneration = long.Parse(
                    candidate.Match.Groups["generation"].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                if (catalog.Generation == filenameGeneration)
                {
                    valid.Add(new ValidCatalog(candidate.Item.FileId, catalog, bytes));
                }
            }
            catch (InvalidDataException)
            {
                // A corrupt candidate is ignored while older valid generations remain usable.
            }
        }

        if (valid.Count == 0)
        {
            return CatalogLoadResult.Corrupt(
                candidates.Select(candidate => candidate.Item.FileId).ToArray());
        }

        var highestGeneration = valid.Max(candidate => candidate.Catalog.Generation);
        var highest = valid
            .Where(candidate => candidate.Catalog.Generation == highestGeneration)
            .ToArray();
        var fileIds = highest.Select(candidate => candidate.FileId).ToArray();
        var referenceBytes = highest[0].Bytes;

        if (highest.Skip(1).Any(candidate => !candidate.Bytes.AsSpan().SequenceEqual(referenceBytes)))
        {
            return CatalogLoadResult.Conflict(fileIds);
        }

        return CatalogLoadResult.Loaded(highest[0].Catalog, fileIds);
    }

    [GeneratedRegex(
        "^catalog-(?<generation>[0-9]{8})-(?<id>[0-9a-f]{32})\\.json$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CatalogNamePattern();

    private sealed record ValidCatalog(
        string FileId,
        Catalog Catalog,
        byte[] Bytes);
}
