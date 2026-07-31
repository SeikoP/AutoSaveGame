namespace AutoSaveGame.Infrastructure.GoogleDrive;

public sealed record GoogleOAuthOptions(string ClientId, string ClientSecret)
{
    public static GoogleOAuthOptions FromEnvironment(
        Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
        var clientId = readEnvironmentVariable("AUTOSAVEGAME_GOOGLE_CLIENT_ID");
        var clientSecret = readEnvironmentVariable("AUTOSAVEGAME_GOOGLE_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "AUTOSAVEGAME_GOOGLE_CLIENT_ID and AUTOSAVEGAME_GOOGLE_CLIENT_SECRET are required.");
        }

        return new GoogleOAuthOptions(clientId.Trim(), clientSecret.Trim());
    }
}

