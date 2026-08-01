using AutoSaveGame.Core.Abstractions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace AutoSaveGame.Infrastructure.GoogleDrive;

public sealed class GoogleUserSession : IUserSession, IDisposable
{
    private const string SessionUserKey = "autosavegame-session";

    private readonly GoogleOAuthOptions options;
    private readonly MemoryDataStore dataStore;
    private UserCredential? credential;
    private DriveService? driveService;

    public static IReadOnlyList<string> RequiredScopes { get; } =
        Array.AsReadOnly([DriveService.Scope.DriveAppdata]);

    public event EventHandler<string>? AuthUrlGenerated;

    public GoogleUserSession(
        GoogleOAuthOptions options,
        MemoryDataStore? dataStore = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.dataStore = dataStore ?? new MemoryDataStore();
    }

    public bool IsSignedIn => credential is not null && driveService is not null;

    public DriveService CurrentDriveService =>
        driveService
        ?? throw new InvalidOperationException("Sign in before accessing Google Drive.");

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        if (IsSignedIn)
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            var flow = new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = options.ClientId,
                        ClientSecret = options.ClientSecret,
                    },
                    DataStore = dataStore,
                    Scopes = RequiredScopes,
                    Prompt = "select_account",
                });
            var app = new AuthorizationCodeInstalledApp(
                flow,
                new NotifyingCodeReceiver(
                    url => AuthUrlGenerated?.Invoke(this, url)));
            credential = await app.AuthorizeAsync(
                SessionUserKey,
                timeout.Token).ConfigureAwait(false);
            driveService = new DriveService(
                new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "AutoSaveGame",
                });
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested
                  && timeout.IsCancellationRequested)
        {
            throw new UserAuthenticationException(
                AuthenticationFailureKind.TimedOut,
                "Google sign-in timed out.",
                exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TokenResponseException exception)
        {
            var kind = string.Equals(
                exception.Error?.Error,
                "access_denied",
                StringComparison.OrdinalIgnoreCase)
                ? AuthenticationFailureKind.Canceled
                : AuthenticationFailureKind.Rejected;
            throw new UserAuthenticationException(
                kind,
                "Google rejected the authorization request.",
                exception);
        }
        catch (Exception exception)
        {
            throw new UserAuthenticationException(
                ClassifyFailure(exception, timedOut: false),
                "Google sign-in could not return to AutoSaveGame.",
                exception);
        }
    }

    public static AuthenticationFailureKind ClassifyFailure(
        Exception exception,
        bool timedOut)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (timedOut)
        {
            return AuthenticationFailureKind.TimedOut;
        }

        return exception is HttpRequestException
            ? AuthenticationFailureKind.Network
            : AuthenticationFailureKind.BrowserCallback;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var currentCredential = credential;
        credential = null;
        driveService?.Dispose();
        driveService = null;

        try
        {
            if (currentCredential is not null)
            {
                await currentCredential.RevokeTokenAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await dataStore.ClearAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        driveService?.Dispose();
        driveService = null;
        credential = null;
        dataStore.ClearAsync().GetAwaiter().GetResult();
    }
}
