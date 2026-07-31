using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Services;

public sealed partial class CatalogCodec
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        Converters = { new UtcDateTimeOffsetConverter() },
    };

    public async Task<Catalog> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            var catalog = await JsonSerializer.DeserializeAsync<Catalog>(
                input,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return Validate(catalog);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The catalog JSON is invalid.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException("The catalog contains unsupported data.", exception);
        }
    }

    public async Task WriteAsync(
        Catalog catalog,
        Stream output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        var validated = Validate(catalog);
        await JsonSerializer.SerializeAsync(
            output,
            validated,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }

    private static Catalog Validate(Catalog? catalog)
    {
        if (catalog is null)
        {
            throw new InvalidDataException("The catalog is empty.");
        }

        if (catalog.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported catalog schema version: {catalog.SchemaVersion}.");
        }

        if (catalog.Generation < 0)
        {
            throw new InvalidDataException("Catalog generation cannot be negative.");
        }

        if (catalog.Games is null)
        {
            throw new InvalidDataException("Catalog games are required.");
        }

        var gameIds = new HashSet<Guid>();
        foreach (var game in catalog.Games)
        {
            ValidateGame(game);
            if (!gameIds.Add(game.GameId))
            {
                throw new InvalidDataException($"Duplicate game ID: {game.GameId}.");
            }
        }

        return catalog;
    }

    private static void ValidateGame(GameConfig? game)
    {
        if (game is null)
        {
            throw new InvalidDataException("Catalog games cannot contain null.");
        }

        if (game.GameId == Guid.Empty)
        {
            throw new InvalidDataException("Game ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(game.DisplayName))
        {
            throw new InvalidDataException("Game display name is required.");
        }

        if (!IsSupportedPathTemplate(game.PathTemplate))
        {
            throw new InvalidDataException(
                $"Game path template is invalid: {game.PathTemplate}.");
        }

        if (game.Snapshot is not null)
        {
            ValidateSnapshot(game.Snapshot);
        }
    }

    private static bool IsSupportedPathTemplate(string? pathTemplate)
    {
        if (string.IsNullOrWhiteSpace(pathTemplate))
        {
            return false;
        }

        if (Path.IsPathFullyQualified(pathTemplate))
        {
            return !pathTemplate.Contains('%', StringComparison.Ordinal);
        }

        var match = PathVariablePattern().Match(pathTemplate);
        return match.Success
            && match.Index == 0
            && match.Length > 0
            && !pathTemplate[match.Length..].Contains('%', StringComparison.Ordinal);
    }

    private static void ValidateSnapshot(SnapshotDescriptor snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.ArchiveFileId))
        {
            throw new InvalidDataException("Snapshot archive file ID is required.");
        }

        if (!Sha256Pattern().IsMatch(snapshot.ArchiveSha256)
            || !Sha256Pattern().IsMatch(snapshot.ContentSha256))
        {
            throw new InvalidDataException("Snapshot hashes must be lowercase SHA-256.");
        }

        if (snapshot.ArchiveSize < 0)
        {
            throw new InvalidDataException("Snapshot archive size cannot be negative.");
        }

        if (snapshot.SourceMachineId == Guid.Empty)
        {
            throw new InvalidDataException("Snapshot source machine ID cannot be empty.");
        }
    }

    [GeneratedRegex(
        "^%(USERPROFILE|APPDATA|LOCALAPPDATA|PROGRAMDATA)%",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PathVariablePattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            reader.GetDateTimeOffset().ToUniversalTime();

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime());
    }
}

