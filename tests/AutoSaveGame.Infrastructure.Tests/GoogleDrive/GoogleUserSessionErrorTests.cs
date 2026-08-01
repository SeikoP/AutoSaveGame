using AutoSaveGame.Infrastructure.GoogleDrive;

namespace AutoSaveGame.Infrastructure.Tests.GoogleDrive;

public sealed class GoogleUserSessionErrorTests
{
    [Fact]
    public void ClassifyFailure_RecognizesTimeout()
    {
        var result = GoogleUserSession.ClassifyFailure(
            new OperationCanceledException(),
            timedOut: true);

        Assert.Equal(AuthenticationFailureKind.TimedOut, result);
    }

    [Fact]
    public void ClassifyFailure_RecognizesNetworkFailure()
    {
        var result = GoogleUserSession.ClassifyFailure(
            new HttpRequestException(),
            timedOut: false);

        Assert.Equal(AuthenticationFailureKind.Network, result);
    }

    [Fact]
    public void ClassifyFailure_TreatsOtherFailuresAsBrowserCallbackFailure()
    {
        var result = GoogleUserSession.ClassifyFailure(
            new IOException(),
            timedOut: false);

        Assert.Equal(AuthenticationFailureKind.BrowserCallback, result);
    }
}
