using AutoSaveGame.Infrastructure.GoogleDrive;
using Google.Apis.Drive.v3;

namespace AutoSaveGame.Infrastructure.Tests.GoogleDrive;

public sealed class GoogleOAuthOptionsTests
{
    [Fact]
    public void Resolve_PrefersCompleteEnvironmentConfiguration()
    {
        var values = new Dictionary<string, string?>
        {
            ["AUTOSAVEGAME_GOOGLE_CLIENT_ID"] = "environment-id",
            ["AUTOSAVEGAME_GOOGLE_CLIENT_SECRET"] = "environment-secret",
        };

        var result = GoogleOAuthOptions.Resolve(
            name => values.GetValueOrDefault(name),
            () => throw new InvalidOperationException("Embedded config should not be opened."));

        Assert.Equal("environment-id", result.ClientId);
        Assert.Equal("environment-secret", result.ClientSecret);
    }

    [Fact]
    public void Resolve_RejectsPartialEnvironmentConfiguration()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            GoogleOAuthOptions.Resolve(
                name => name == "AUTOSAVEGAME_GOOGLE_CLIENT_ID" ? "environment-id" : null,
                () => null));

        Assert.Contains("AUTOSAVEGAME_GOOGLE_CLIENT_SECRET", error.Message);
    }

    [Fact]
    public void Resolve_UsesEmbeddedReleaseConfigWhenEnvironmentIsEmpty()
    {
        using var json = new MemoryStream(
            """{"clientId":"release-id","clientSecret":"release-secret"}"""u8.ToArray());

        var result = GoogleOAuthOptions.Resolve(_ => null, () => json);

        Assert.Equal("release-id", result.ClientId);
        Assert.Equal("release-secret", result.ClientSecret);
    }

    [Fact]
    public void Resolve_RejectsMalformedEmbeddedReleaseConfig()
    {
        using var json = new MemoryStream("not-json"u8.ToArray());

        var error = Assert.Throws<InvalidOperationException>(
            () => GoogleOAuthOptions.Resolve(_ => null, () => json));

        Assert.Contains("Google OAuth configuration", error.Message);
    }

    [Fact]
    public void Resolve_RejectsBuildWithoutAnyConfiguration()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => GoogleOAuthOptions.Resolve(_ => null, () => null));

        Assert.Equal(
            "This build does not contain Google OAuth configuration.",
            error.Message);
    }

    [Fact]
    public void FromEnvironment_RejectsMissingDesktopClientValues()
    {
        var values = new Dictionary<string, string?>();

        var error = Assert.Throws<InvalidOperationException>(
            () => GoogleOAuthOptions.FromEnvironment(
                name => values.GetValueOrDefault(name)));

        Assert.Contains("AUTOSAVEGAME_GOOGLE_CLIENT_ID", error.Message);
        Assert.Contains("AUTOSAVEGAME_GOOGLE_CLIENT_SECRET", error.Message);
    }

    [Fact]
    public void FromEnvironment_LoadsBothDesktopClientValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["AUTOSAVEGAME_GOOGLE_CLIENT_ID"] = "client.apps.googleusercontent.com",
            ["AUTOSAVEGAME_GOOGLE_CLIENT_SECRET"] = "public-client-value",
        };

        var result = GoogleOAuthOptions.FromEnvironment(
            name => values.GetValueOrDefault(name));

        Assert.Equal("client.apps.googleusercontent.com", result.ClientId);
        Assert.Equal("public-client-value", result.ClientSecret);
    }

    [Fact]
    public void UserSession_RequestsOnlyAppDataFolderScope()
    {
        Assert.Equal(
            [DriveService.Scope.DriveAppdata],
            GoogleUserSession.RequiredScopes);
    }
}
