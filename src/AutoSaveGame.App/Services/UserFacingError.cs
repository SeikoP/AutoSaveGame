using AutoSaveGame.Infrastructure.GoogleDrive;

namespace AutoSaveGame.App.Services;

public sealed record UserFacingError(string Title, string Message)
{
    public static UserFacingError From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is not UserAuthenticationException authentication)
        {
            return new UserFacingError(
                "AutoSaveGame couldn't complete the operation",
                "Try again. If the problem continues, copy the diagnostic details.");
        }

        var message = authentication.Kind switch
        {
            AuthenticationFailureKind.Canceled =>
                "Google sign-in was canceled. You can try again when ready.",
            AuthenticationFailureKind.TimedOut =>
                "Google sign-in did not return to AutoSaveGame in time. Try again.",
            AuthenticationFailureKind.Network =>
                "Cannot reach Google. Check this computer's network and try again.",
            AuthenticationFailureKind.Rejected =>
                "Google rejected this sign-in request. Check the account and try again.",
            AuthenticationFailureKind.InvalidBuild =>
                "This is not a usable official build. Download the latest GitHub Release.",
            _ =>
                "The browser could not return sign-in to AutoSaveGame. Try again.",
        };
        return new UserFacingError("Google sign-in failed", message);
    }
}
