# AutoSaveGame Public Release and Installation Design

## 1. Outcome

AutoSaveGame must be usable by an ordinary Windows user without installing an SDK, creating Google OAuth credentials, setting environment variables, or understanding the backup implementation. A user downloads or installs the app, signs in with Google, restores an existing save or protects a new game, and leaves the app running while playing.

The first public release keeps the existing C#/.NET 10, WPF, Google Drive `appDataFolder`, transactional catalog, verified ZIP snapshot, watcher, and rollback implementation. Rewriting the stack, adding rclone, adding a TUI, and supporting other cloud providers are outside this release.

## 2. Product Decisions

- Install per user under `%LOCALAPPDATA%\Programs\AutoSaveGame`.
- Do not require administrator rights or a machine-wide installation.
- Provide a normal Windows installer with Start Menu and Uninstall entries.
- Provide a PowerShell installation command for quickly preparing a public computer.
- Ship production Google Desktop OAuth application credentials in official release binaries.
- Keep user access and refresh tokens only in process memory.
- Use only the non-sensitive Google Drive `drive.appdata` scope.
- Keep the native Google Drive adapter. Rclone is not part of the first public release.
- Treat an automated fake-cloud smoke test and a live Google Drive acceptance test as different evidence.

## 3. User Journey

### 3.1 Installation

The release offers two entry points to the same signed or checksummed payload:

1. A user downloads `AutoSaveGame-Setup.exe`, runs it, and follows a short installer.
2. A public-computer user runs the documented PowerShell command, which downloads the latest official release, verifies its SHA-256, performs a quiet per-user installation, and starts the app.

The installer creates Start Menu and uninstall entries. It must not add a Windows service, scheduled task, startup item, machine-wide PATH entry, or browser extension.

Running the installer again upgrades the existing per-user installation. An uninstall removes application binaries and shortcuts. It must not delete local game saves.

### 3.2 First Launch and Sign-In

The first screen contains one primary action: `Sign in with Google`. OAuth application credentials are already present in an official release. The UI never asks the user for a client ID, client secret, environment variable, or Google Cloud configuration.

Selecting sign-in opens the system browser with the Google Desktop OAuth loopback flow. After consent, the browser shows a completion message and the app loads the user's cloud catalog. The app requests only `drive.appdata`.

User credentials remain in the existing in-memory data store. Signing out revokes the current token and clears the in-memory store. The app warns users on public computers to use a Guest or Private browser session because it cannot clear browser cookies.

### 3.3 Existing Cloud Data

When the catalog contains games, the dashboard leads with recovery. Each game shows its latest confirmed backup time and one of these actions:

- `Restore` when its portable save path resolves on the current machine.
- `Choose folder` when the save path cannot be resolved or needs relocation.
- `Backup now` when a valid local save exists.

Restore retains the existing close-game confirmation, archive hash validation, staging extraction, transactional replacement, and rollback behavior.

### 3.4 New User

When the catalog is empty, the dashboard presents `Add a game`. The initial wizard asks only for a display name and save directory. Saving the game starts the first backup immediately. The UI reports success only after the archive and catalog have been verified in Google Drive.

### 3.5 Background Backup and Exit

The app remains in the system tray while the game is running. User-facing states are `Watching`, `Changes detected`, `Backing up`, `Safe in Google Drive`, and `Action required`. Internal terms such as catalog generation, SHA-256, and `FileSystemWatcher` do not appear in the primary UI.

On exit, a clean session can sign out immediately. A dirty or pending session offers `Backup and exit`, `Keep app open`, and `Exit anyway`. `Backup and exit` waits for a cloud-confirmed result before signing out.

## 4. OAuth Configuration

### 4.1 Runtime Resolution

OAuth settings are resolved in this order:

1. `AUTOSAVEGAME_GOOGLE_CLIENT_ID` and `AUTOSAVEGAME_GOOGLE_CLIENT_SECRET` environment variables for local development and explicit overrides.
2. Build-generated credentials embedded in an official release.
3. A packaging error runtime that clearly states the build is not an official usable release.

The repository must not contain production OAuth values. The release workflow reads them from GitHub environment secrets and writes a generated source or resource only inside the runner's intermediate build directory. Secret values must not be printed in workflow logs or uploaded as standalone files.

Desktop OAuth application credentials are identifiers distributed with the client, not a secure store for user credentials. GitHub Secrets prevent accidental source and log disclosure; they do not make values embedded in a desktop binary unrecoverable.

### 4.2 Google Cloud Prerequisites

The owner configures a separate production Google Cloud project with:

- Google Drive API enabled.
- An External production audience.
- A Desktop OAuth client.
- AutoSaveGame branding and support contact.
- Only the `drive.appdata` scope.
- A public home page and privacy policy when required by Google's production policy.

Publishing and branding approval are external prerequisites. The app must remain testable with the development project while production approval is pending.

## 5. GitHub Actions

### 5.1 Continuous Integration

`.github/workflows/ci.yml` runs on pull requests and pushes to `main`:

1. Check out the repository.
2. Install the pinned .NET 10 SDK.
3. Restore dependencies.
4. Run all Release tests.
5. Build the Release solution.
6. Publish the self-contained `win-x64` app without production OAuth secrets.
7. Run `scripts/SmokeTest.ps1` against the published executable.

CI must not depend on GitHub Secrets so pull requests, including forks, can be validated safely.

### 5.2 Release

`.github/workflows/release.yml` runs for a version tag matching `v*` or a manual dispatch with an explicit version. It uses a protected GitHub environment named `release` and fails before publication if either OAuth secret is absent.

The workflow:

1. Runs the same test, build, and smoke gates as CI.
2. Generates the OAuth build input without logging its contents.
3. Publishes the self-contained `win-x64` application.
4. Runs the smoke test on the credential-bearing published executable.
5. Builds the per-user installer.
6. Performs a quiet install-launch-uninstall check on the disposable Windows runner.
7. Produces the portable ZIP, setup executable, and `SHA256SUMS.txt`.
8. Creates a GitHub Release for the tag and uploads only the intended artifacts.

The release must be immutable. Updating a release requires a new version tag rather than replacing binaries under an existing version.

## 6. Installer and PowerShell Bootstrap

The Windows installer targets `%LOCALAPPDATA%\Programs\AutoSaveGame`, registers a per-user uninstall entry, and creates a Start Menu shortcut. It supports interactive use and quiet current-user installation. It does not request elevation.

The repository contains a Windows PowerShell 5.1-compatible bootstrap script. It:

1. Resolves the latest non-prerelease GitHub Release.
2. Downloads the installer and `SHA256SUMS.txt` to a task-specific directory under `%TEMP%`.
3. Calculates SHA-256 through .NET APIs for Windows PowerShell 5.1 compatibility.
4. Refuses to run the installer when the checksum is absent or mismatched.
5. Starts the installer in quiet per-user mode and checks its exit code.
6. Launches the installed app.
7. Removes only its own verified temporary download directory.

The direct installer remains the fallback when a venue blocks PowerShell scripts or GitHub downloads.

## 7. Runtime Reliability and Supportability

Authentication errors are mapped to actionable user messages: browser launch failure, user cancellation, loopback callback timeout, Google rejection, blocked network, and invalid release configuration. Raw exception text is written to a diagnostic log instead of being the primary dialog content.

Diagnostic logs live under `%TEMP%\AutoSaveGame\logs` for the current session and must not contain OAuth tokens, client secrets, save contents, or full cloud responses. The UI offers `Copy diagnostic details` with redacted version, operation, error category, and correlation ID.

Backup and restore continue to use the existing transactional safeguards. This release also removes whole-archive buffering from the normal restore path: downloads use a session-scoped temporary file so large saves do not require an equivalent in-memory copy. Temporary paths are uniquely scoped and safely cleaned after success or recoverable failure.

## 8. Verification

Automated verification includes:

- OAuth option precedence and missing-release-credential tests.
- Authentication error mapping tests.
- Existing Core, infrastructure, application, watcher, backup, and rollback tests.
- Release build and published-executable smoke test.
- Installer quiet install, launch, upgrade, and uninstall checks.
- PowerShell bootstrap checksum success and mismatch tests.
- A scan proving generated credentials and tokens are absent from repository files and workflow logs.

Before the first public release, a manual live acceptance run must prove:

1. Install on a clean Windows user without the .NET SDK.
2. Sign in through Google using the production OAuth client.
3. Add a temporary game and complete a cloud-confirmed backup.
4. Remove the local sample.
5. Install or launch from another clean Windows user context.
6. Sign in, restore, and compare the restored content hash.
7. Modify the save, observe automatic backup, and wait for `Safe in Google Drive`.
8. Sign out and confirm that the app retains no reusable token on disk.

The release is not called operationally complete until this live path passes. Automated fake-cloud proof alone is insufficient.

## 9. Success Criteria

- A public user never supplies OAuth application credentials.
- A clean Windows user installs and uninstalls without administrator rights.
- Both direct installer and PowerShell bootstrap use the same versioned release.
- Every published artifact passed tests, build, executable smoke, checksum generation, and installer smoke.
- Google sign-in, real Drive backup, cross-context restore, watcher backup, and sign-out pass the live acceptance run.
- Failed or interrupted backup never replaces the last confirmed snapshot.
- Failed restore leaves the previous local save intact or provides a recovery path.
- No OAuth token, production credential source file, save archive, or user save is committed to Git.

