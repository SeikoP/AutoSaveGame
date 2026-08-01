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

        Assert.Equal("Không thể đăng nhập Google", error.Title);
        Assert.Contains("Không thể kết nối tới Google", error.Message);
        Assert.DoesNotContain("raw transport details", error.Message);
    }

    [Fact]
    public void From_DescribesInvalidReleaseBuild()
    {
        var error = UserFacingError.From(
            new UserAuthenticationException(
                AuthenticationFailureKind.InvalidBuild,
                "raw build details"));

        Assert.Contains("bản phát hành chính thức", error.Message);
    }
}
