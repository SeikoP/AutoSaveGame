using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoSaveGame.Infrastructure.GoogleDrive;

public sealed record GoogleOAuthOptions(string ClientId, string ClientSecret)
{
    private const string ClientIdVariable = "AUTOSAVEGAME_GOOGLE_CLIENT_ID";
    private const string ClientSecretVariable = "AUTOSAVEGAME_GOOGLE_CLIENT_SECRET";

    public static GoogleOAuthOptions Resolve(
        Func<string, string?> readEnvironmentVariable,
        Func<Stream?> openEmbeddedConfig)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(openEmbeddedConfig);

        var clientId = readEnvironmentVariable(ClientIdVariable);
        var clientSecret = readEnvironmentVariable(ClientSecretVariable);
        var hasClientId = !string.IsNullOrWhiteSpace(clientId);
        var hasClientSecret = !string.IsNullOrWhiteSpace(clientSecret);

        if (hasClientId || hasClientSecret)
        {
            return CreateValidated(clientId, clientSecret);
        }

        using var embeddedConfig = openEmbeddedConfig();
        if (embeddedConfig is null)
        {
            throw new InvalidOperationException(
                "This build does not contain Google OAuth configuration.");
        }

        try
        {
            var values = JsonSerializer.Deserialize<EmbeddedOAuthConfig>(embeddedConfig);
            return CreateValidated(values?.ClientId, values?.ClientSecret);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The embedded Google OAuth configuration is invalid.",
                exception);
        }
    }

    public static GoogleOAuthOptions FromEnvironment(
        Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
        return CreateValidated(
            readEnvironmentVariable(ClientIdVariable),
            readEnvironmentVariable(ClientSecretVariable));
    }

    private static GoogleOAuthOptions CreateValidated(
        string? clientId,
        string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "AUTOSAVEGAME_GOOGLE_CLIENT_ID and AUTOSAVEGAME_GOOGLE_CLIENT_SECRET are required.");
        }

        return new GoogleOAuthOptions(clientId.Trim(), clientSecret.Trim());
    }

    private sealed record EmbeddedOAuthConfig(
        [property: JsonPropertyName("clientId")] string? ClientId,
        [property: JsonPropertyName("clientSecret")] string? ClientSecret);
}
