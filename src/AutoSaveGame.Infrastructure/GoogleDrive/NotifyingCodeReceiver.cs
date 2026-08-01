using Google.Apis.Auth.OAuth2;

namespace AutoSaveGame.Infrastructure.GoogleDrive;

/// <summary>
/// A <see cref="LocalServerCodeReceiver"/> that reports the authorization URL
/// right before the default browser is launched, so callers can copy it to the
/// clipboard and complete sign-in in another browser profile.
/// </summary>
internal sealed class NotifyingCodeReceiver(Action<string> onUrlGenerated)
    : LocalServerCodeReceiver
{
    private readonly Action<string> onUrlGenerated = onUrlGenerated
        ?? throw new ArgumentNullException(nameof(onUrlGenerated));

    protected override bool OpenBrowser(string url)
    {
        onUrlGenerated(url);
        return base.OpenBrowser(url);
    }
}
