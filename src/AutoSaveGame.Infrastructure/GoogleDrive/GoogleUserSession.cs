using AutoSaveGame.Core.Abstractions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
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
        var app = new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver());
        credential = await app.AuthorizeAsync(
            SessionUserKey,
            cancellationToken).ConfigureAwait(false);
        driveService = new DriveService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AutoSaveGame",
            });
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
