using AutoSaveGame.App.Services;
using AutoSaveGame.Infrastructure.GoogleDrive;

namespace AutoSaveGame.App.Tests.Services;

public sealed class UserFacingErrorTests
{
    [Fact]
    public void From_DescribesGoogleNetworkFailureWithoutRawExceptionText()
    {
        var error = UserFacingError.From(
            new UserAuthenticationException(
                AuthenticationFailureKind.Network,
                "raw transport details"));

        Assert.Equal("Google sign-in failed", error.Title);
        Assert.Contains("Cannot reach Google", error.Message);
        Assert.DoesNotContain("raw transport details", error.Message);
    }

    [Fact]
    public void From_DescribesInvalidReleaseBuild()
    {
        var error = UserFacingError.From(
            new UserAuthenticationException(
                AuthenticationFailureKind.InvalidBuild,
                "raw build details"));

        Assert.Contains("official build", error.Message);
    }
}
