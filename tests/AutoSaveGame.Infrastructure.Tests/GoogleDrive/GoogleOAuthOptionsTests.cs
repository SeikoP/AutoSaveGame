using AutoSaveGame.Infrastructure.GoogleDrive;
using Google.Apis.Drive.v3;

namespace AutoSaveGame.Infrastructure.Tests.GoogleDrive;

public sealed class GoogleOAuthOptionsTests
{
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
