# AutoSaveGame Public Release and Installation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a public AutoSaveGame release that installs per user without administrator rights, contains usable Google Desktop OAuth configuration, passes automated packaging gates, and can be installed from PowerShell or a normal Windows setup executable.

**Architecture:** Keep the existing WPF and transactional backup engine. Add environment-first plus embedded-release OAuth resolution, user-facing authentication failures, disk-backed restore downloads, per-user Inno Setup packaging, checksum-verifying PowerShell bootstrap, and separate CI/release workflows. Publish through a new public `SeikoP/AutoSaveGame` repository after local verification.

**Tech Stack:** C# 14, .NET SDK 10.0.302, WPF, xUnit v3, Google.Apis.Drive.v3, PowerShell 5.1-compatible scripts, Inno Setup 6, GitHub Actions, GitHub CLI.

## Global Constraints

- Install under `%LOCALAPPDATA%\Programs\AutoSaveGame` without administrator rights.
- Do not add a Windows service, scheduled task, startup item, machine-wide PATH entry, or browser extension.
- Never commit or print production OAuth values or user tokens.
- Keep `AUTOSAVEGAME_GOOGLE_CLIENT_ID` and `AUTOSAVEGAME_GOOGLE_CLIENT_SECRET` as local-development overrides.
- Official release builds embed OAuth configuration from protected GitHub environment secrets.
- Request only Google Drive `drive.appdata`; keep user access and refresh tokens in memory.
- Do not add rclone, another cloud provider, a TUI, or a stack rewrite.
- Preserve catalog generations, verified snapshot commits, staging restore, and rollback guarantees.
- Fake-cloud smoke proof must never be reported as live Google Drive proof.
- Do not stage or commit the unrelated `.codebase-memory/` directory.

---

## File Map

- `global.json`: pins the SDK used locally and in Actions.
- `src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleOAuthOptions.cs`: resolves environment and embedded OAuth inputs.
- `src/AutoSaveGame.Infrastructure/GoogleDrive/UserAuthenticationException.cs`: stable authentication failure categories.
- `src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleUserSession.cs`: browser OAuth, timeout, token lifecycle, and error translation.
- `src/AutoSaveGame.App/Services/ApplicationRuntimeFactory.cs`: opens the build-time OAuth resource and composes runtime services.
- `src/AutoSaveGame.App/Services/SessionDiagnosticLog.cs`: redacted session diagnostics under `%TEMP%`.
- `src/AutoSaveGame.App/Views/ErrorDialog.xaml`: actionable error text and copyable diagnostic details.
- `src/AutoSaveGame.App/Services/RestoreArchiveStore.cs`: temporary disk stream for cloud restore downloads.
- `src/AutoSaveGame.App/Services/ApplicationRuntime.cs`: initial backup, restore streaming, and runtime state.
- `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`: sign-out, empty state, actionable messages, and commands.
- `src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs`: user-facing status labels and restore availability.
- `src/AutoSaveGame.App/MainWindow.xaml`: public sign-in, empty state, game recovery, and sign-out UI.
- `installer/AutoSaveGame.iss`: per-user Inno Setup definition.
- `scripts/Build-Installer.ps1`: deterministic installer build wrapper.
- `scripts/Install.ps1`: latest-release download, checksum verification, quiet install, and launch.
- `scripts/Test-Install.ps1`: checksum success/mismatch and installer smoke checks.
- `.github/workflows/ci.yml`: secret-free test/build/publish/smoke workflow.
- `.github/workflows/release.yml`: credential-bearing package and GitHub Release workflow.
- `docs/public-release-runbook.md`: Google Cloud, GitHub environment, and live acceptance instructions.

---

### Task 1: Pin the SDK and Resolve Embedded Release OAuth

**Files:**
- Create: `global.json`
- Modify: `src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleOAuthOptions.cs`
- Modify: `src/AutoSaveGame.App/AutoSaveGame.App.csproj`
- Modify: `src/AutoSaveGame.App/Services/ApplicationRuntimeFactory.cs`
- Modify: `tests/AutoSaveGame.Infrastructure.Tests/GoogleDrive/GoogleOAuthOptionsTests.cs`

**Interfaces:**
- Produces: `GoogleOAuthOptions.Resolve(Func<string,string?>, Func<Stream?>)`.
- Produces: embedded resource logical name `AutoSaveGame.GoogleOAuthClient.json`.
- Consumes: optional MSBuild property `AutoSaveGameOAuthConfig` containing an external JSON path.

- [ ] **Step 1: Add failing source-precedence tests**

Add tests which prove a complete environment pair wins, partial environment configuration fails, embedded JSON is used when the environment is empty, malformed JSON fails, and no source returns the packaging error:

```csharp
[Fact]
public void Resolve_UsesEmbeddedReleaseConfigWhenEnvironmentIsEmpty()
{
    using var json = new MemoryStream(
        """{"clientId":"release-id","clientSecret":"release-secret"}"""u8.ToArray());

    var result = GoogleOAuthOptions.Resolve(_ => null, () => json);

    Assert.Equal("release-id", result.ClientId);
    Assert.Equal("release-secret", result.ClientSecret);
}
```

- [ ] **Step 2: Run the focused tests and confirm red**

Run:

```powershell
rtk dotnet test tests\AutoSaveGame.Infrastructure.Tests\AutoSaveGame.Infrastructure.Tests.csproj -c Release --filter GoogleOAuthOptionsTests
```

Expected: compilation fails because `Resolve` does not exist.

- [ ] **Step 3: Implement environment-first and embedded JSON resolution**

Implement this public contract in `GoogleOAuthOptions`:

```csharp
public static GoogleOAuthOptions Resolve(
    Func<string, string?> readEnvironmentVariable,
    Func<Stream?> openEmbeddedConfig)
```

Use `System.Text.Json` with an internal DTO containing `ClientId` and `ClientSecret`. Reject partial environment values. Reject blank embedded values with `InvalidOperationException("This build does not contain Google OAuth configuration.")`.

- [ ] **Step 4: Add the conditional embedded resource**

Add to `AutoSaveGame.App.csproj`:

```xml
<ItemGroup Condition="'$(AutoSaveGameOAuthConfig)' != ''">
  <EmbeddedResource Include="$(AutoSaveGameOAuthConfig)"
                    LogicalName="AutoSaveGame.GoogleOAuthClient.json" />
</ItemGroup>
```

Update `ApplicationRuntimeFactory.Create` to call `Resolve` and open the logical resource through `Assembly.GetExecutingAssembly().GetManifestResourceStream(...)`.

- [ ] **Step 5: Pin .NET SDK 10.0.302**

Create:

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestPatch"
  }
}
```

- [ ] **Step 6: Run tests and build**

Run:

```powershell
rtk dotnet test AutoSaveGame.sln -c Release
rtk dotnet build AutoSaveGame.sln -c Release
```

Expected: all tests and build pass with zero errors.

- [ ] **Step 7: Commit**

```powershell
rtk git add global.json src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleOAuthOptions.cs src/AutoSaveGame.App/AutoSaveGame.App.csproj src/AutoSaveGame.App/Services/ApplicationRuntimeFactory.cs tests/AutoSaveGame.Infrastructure.Tests/GoogleDrive/GoogleOAuthOptionsTests.cs
rtk git commit -m "Embed OAuth configuration in release builds"
```

---

### Task 2: Translate Authentication Failures and Write Safe Diagnostics

**Files:**
- Create: `src/AutoSaveGame.Infrastructure/GoogleDrive/UserAuthenticationException.cs`
- Create: `src/AutoSaveGame.App/Services/SessionDiagnosticLog.cs`
- Modify: `src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleUserSession.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`
- Modify: `src/AutoSaveGame.App/Services/IUserPromptService.cs`
- Modify: `src/AutoSaveGame.App/Services/UserPromptService.cs`
- Create: `src/AutoSaveGame.App/Views/ErrorDialog.xaml`
- Create: `src/AutoSaveGame.App/Views/ErrorDialog.xaml.cs`
- Create: `tests/AutoSaveGame.App.Tests/Services/SessionDiagnosticLogTests.cs`
- Modify: `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Produces: `AuthenticationFailureKind` values `Canceled`, `TimedOut`, `Network`, `Rejected`, `BrowserCallback`, `InvalidBuild`.
- Produces: `UserAuthenticationException(AuthenticationFailureKind kind, string message, Exception? innerException = null)`.
- Produces: `SessionDiagnosticLog.Write(Exception exception, string operation) -> string correlationId`.
- Changes: `IUserPromptService.ShowError(string title, string message, string? correlationId)`.
- Produces: `ErrorDialog` with `Copy diagnostic details` copying only operation, category, app version, and correlation ID.

- [ ] **Step 1: Write failing error-presentation and redaction tests**

Cover a rejected OAuth error, a timeout, a normal network exception, and diagnostics containing `access_token`, `refresh_token`, `client_secret`, and an OAuth `code` query parameter. Assert the log contains the exception type and correlation ID but none of those values.

```csharp
[Fact]
public void Write_RedactsOAuthValues()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        $"AutoSaveGame-LogTest-{Guid.NewGuid():N}");
    try
    {
        var log = new SessionDiagnosticLog(root);
        var id = log.Write(
            new InvalidOperationException("access_token=abc&code=xyz"),
            "Google sign-in");

        var text = File.ReadAllText(Directory.GetFiles(root).Single());
        Assert.Contains(id, text);
        Assert.DoesNotContain("abc", text);
        Assert.DoesNotContain("xyz", text);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
rtk dotnet test tests\AutoSaveGame.App.Tests\AutoSaveGame.App.Tests.csproj -c Release --filter "SessionDiagnosticLogTests|MainViewModelTests"
```

Expected: compilation fails for the new types and prompt signature.

- [ ] **Step 3: Add stable authentication categories**

Create the enum and exception. In `GoogleUserSession.SignInAsync`, use a linked cancellation source with a five-minute timeout. Preserve caller cancellation, map timeout separately, map `HttpRequestException` to `Network`, `Google.Apis.Auth.OAuth2.Responses.TokenResponseException` to `Rejected`, and other installed-app callback failures to `BrowserCallback`.

- [ ] **Step 4: Add redacted session diagnostics**

Write logs under `Path.Combine(Path.GetTempPath(), "AutoSaveGame", "logs")`. Redact case-insensitive key/value occurrences for `access_token`, `refresh_token`, `client_secret`, `authorization`, and `code` before writing. Never log `GoogleOAuthOptions`.

- [ ] **Step 5: Present actionable UI errors**

Map failure kinds to concise messages such as:

```csharp
AuthenticationFailureKind.Network =>
    "Cannot reach Google. Check this computer's network and try again.",
AuthenticationFailureKind.TimedOut =>
    "Google sign-in did not return to AutoSaveGame in time. Try again.",
AuthenticationFailureKind.InvalidBuild =>
    "This is not a usable official build. Download the latest GitHub Release."
```

Store technical details through `SessionDiagnosticLog` and show the correlation ID in `ErrorDialog`. Its copy button places only redacted operation, category, app version, and correlation ID on the clipboard; it never copies the raw exception or OAuth URL.

- [ ] **Step 6: Run focused and full tests**

```powershell
rtk dotnet test tests\AutoSaveGame.App.Tests\AutoSaveGame.App.Tests.csproj -c Release
rtk dotnet test AutoSaveGame.sln -c Release
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```powershell
rtk git add src/AutoSaveGame.Infrastructure/GoogleDrive/UserAuthenticationException.cs src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleUserSession.cs src/AutoSaveGame.App/Services/SessionDiagnosticLog.cs src/AutoSaveGame.App/Services/IUserPromptService.cs src/AutoSaveGame.App/Services/UserPromptService.cs src/AutoSaveGame.App/Views/ErrorDialog.xaml src/AutoSaveGame.App/Views/ErrorDialog.xaml.cs src/AutoSaveGame.App/ViewModels/MainViewModel.cs tests/AutoSaveGame.App.Tests/Services/SessionDiagnosticLogTests.cs tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs
rtk git commit -m "Report actionable Google sign-in failures"
```

---

### Task 3: Back Up New Games Immediately and Stream Restore Downloads

**Files:**
- Create: `src/AutoSaveGame.App/Services/IRestoreArchiveStore.cs`
- Create: `src/AutoSaveGame.App/Services/SessionRestoreArchiveStore.cs`
- Modify: `src/AutoSaveGame.App/Services/ApplicationRuntime.cs`
- Modify: `src/AutoSaveGame.App/Services/ApplicationRuntimeFactory.cs`
- Modify: `tests/AutoSaveGame.App.Tests/Services/ApplicationRuntimeTests.cs`

**Interfaces:**
- Produces: `IRestoreArchiveStore.CreateAsync(CancellationToken) -> ValueTask<IRestoreArchiveHandle>`.
- Produces: `IRestoreArchiveHandle.Stream` and asynchronous cleanup through `IAsyncDisposable`.
- Consumes: existing `ICloudObjectStore.DownloadAsync` and `IRestoreService.RestoreAsync` stream contracts.

- [ ] **Step 1: Write failing initial-backup and disk-restore tests**

Add a scheduler fake that records `BackupNowAsync` calls. Assert a newly added valid game requests exactly one immediate backup after catalog update. Add a restore archive fake and assert `ApplicationRuntime.RestoreAsync` downloads into its stream and disposes the handle after both success and failure.

```csharp
[Fact]
public async Task AddOrUpdateGameAsync_RequestsInitialBackupAfterSavingCatalog()
{
    var fixture = RuntimeFixture.CreateSignedIn();

    await fixture.Runtime.AddOrUpdateGameAsync(
        null, "Game", fixture.SavePath, CancellationToken.None);

    Assert.Single(fixture.Scheduler.BackupNowGameIds);
}
```

- [ ] **Step 2: Run the focused tests and confirm red**

```powershell
rtk dotnet test tests\AutoSaveGame.App.Tests\AutoSaveGame.App.Tests.csproj -c Release --filter ApplicationRuntimeTests
```

Expected: new assertions fail because no initial backup is requested and restore uses `MemoryStream`.

- [ ] **Step 3: Implement session restore archive storage**

Create unique files beneath `%TEMP%\AutoSaveGame\session-<pid>\restore`. Open with asynchronous and sequential-scan options. `DisposeAsync` closes the stream and deletes the exact file; directory cleanup must stay beneath the fixed session root.

- [ ] **Step 4: Replace whole-archive restore buffering**

Inject `IRestoreArchiveStore` into `ApplicationRuntime`. Download to the handle stream, flush, seek to zero, then call the existing restore service. Always dispose the handle.

- [ ] **Step 5: Trigger first backup outside the runtime operation gate**

Save the catalog while holding `operationGate`, release it, then call `scheduler.BackupNowAsync(gameId, cancellationToken)`. Do not invoke the scheduler while holding the gate because its callback re-enters `ApplicationRuntime`.

- [ ] **Step 6: Run focused tests, full tests, and smoke**

```powershell
rtk dotnet test AutoSaveGame.sln -c Release
rtk dotnet publish src\AutoSaveGame.App\AutoSaveGame.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\win-x64
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\SmokeTest.ps1 -Executable artifacts\win-x64\AutoSaveGame.exe
```

Expected: all tests pass and smoke prints `PASS`.

- [ ] **Step 7: Commit**

```powershell
rtk git add src/AutoSaveGame.App/Services/IRestoreArchiveStore.cs src/AutoSaveGame.App/Services/SessionRestoreArchiveStore.cs src/AutoSaveGame.App/Services/ApplicationRuntime.cs src/AutoSaveGame.App/Services/ApplicationRuntimeFactory.cs tests/AutoSaveGame.App.Tests/Services/ApplicationRuntimeTests.cs
rtk git commit -m "Stream restores and protect new games"
```

---

### Task 4: Make the Main Window Recovery-First

**Files:**
- Modify: `src/AutoSaveGame.App/MainWindow.xaml`
- Modify: `src/AutoSaveGame.App/MainWindow.xaml.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs`
- Modify: `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Produces: `MainViewModel.HasGames`, `MainViewModel.IsEmpty`, and `MainViewModel.SignOutCommand`.
- Produces: `GameItemViewModel.StatusText`, `CanRestore`, and `NeedsSaveFolder`.

- [ ] **Step 1: Write failing view-model tests**

Test empty catalog state, game state labels, restore availability without a snapshot, and sign-out clearing the game list:

```csharp
[Fact]
public async Task SignOutCommand_ClearsSignedInDashboard()
{
    var runtime = new FakeRuntime { SignedIn = true };
    var viewModel = new MainViewModel(runtime, new FakePrompts());

    await viewModel.SignOutCommand.ExecuteAsync();

    Assert.False(viewModel.IsSignedIn);
    Assert.True(viewModel.IsEmpty);
}
```

- [ ] **Step 2: Run tests and confirm red**

```powershell
rtk dotnet test tests\AutoSaveGame.App.Tests\AutoSaveGame.App.Tests.csproj -c Release --filter MainViewModelTests
```

Expected: compilation fails for new properties and command.

- [ ] **Step 3: Implement recovery-oriented view-model state**

Expose derived empty-state and user-facing labels. Raise property changes whenever games or sign-in state changes. Disable restore when the snapshot is absent. Add sign-out without closing the app.

- [ ] **Step 4: Update WPF layout**

Keep one primary `Sign in with Google` action before authentication. After authentication, show an empty-state card when there are no games. For existing games, make `Restore` the first action when a snapshot exists, keep `Backup now`, and place edit/remove as secondary actions. Add `Sign out` in the header.

- [ ] **Step 5: Build and perform a local UI launch check**

```powershell
rtk dotnet test tests\AutoSaveGame.App.Tests\AutoSaveGame.App.Tests.csproj -c Release
rtk dotnet build AutoSaveGame.sln -c Release
```

Launch the published app without env credentials and confirm it shows the official-build error only after sign-in is selected, without overlapping footer text or raw exception content.

- [ ] **Step 6: Commit**

```powershell
rtk git add src/AutoSaveGame.App/MainWindow.xaml src/AutoSaveGame.App/MainWindow.xaml.cs src/AutoSaveGame.App/ViewModels/MainViewModel.cs src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs
rtk git commit -m "Make save recovery easier to navigate"
```

---

### Task 5: Add Secret-Free Continuous Integration

**Files:**
- Create: `.github/workflows/ci.yml`
- Modify: `README.md`

**Interfaces:**
- Produces: GitHub Actions workflow `CI` on pull requests and pushes to `main`.
- Consumes: `.NET SDK 10.0.302`, `AutoSaveGame.sln`, and `scripts/SmokeTest.ps1`.

- [ ] **Step 1: Add the CI workflow**

Use official current action majors and minimum permissions:

```yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
permissions:
  contents: read
jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v6
      - uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json
      - run: dotnet restore AutoSaveGame.sln
      - run: dotnet test AutoSaveGame.sln -c Release --no-restore
      - run: dotnet build AutoSaveGame.sln -c Release --no-restore
      - run: dotnet publish src/AutoSaveGame.App/AutoSaveGame.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/win-x64
      - shell: powershell
        run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/SmokeTest.ps1 -Executable artifacts/win-x64/AutoSaveGame.exe
```

- [ ] **Step 2: Validate YAML and reproduce commands locally**

Parse the YAML with the available repository tooling or PowerShell YAML parser if installed, then run every command body locally. Expected: no production OAuth value is required and smoke prints `PASS`.

- [ ] **Step 3: Document CI evidence boundary**

Add a concise README section stating that CI validates build, tests, and fake-cloud backup/restore, while live Google OAuth remains a release acceptance gate.

- [ ] **Step 4: Commit**

```powershell
rtk git add .github/workflows/ci.yml README.md
rtk git commit -m "Run Windows CI and packaged smoke tests"
```

---

### Task 6: Build a Normal Per-User Windows Installer

**Files:**
- Create: `installer/AutoSaveGame.iss`
- Create: `scripts/Build-Installer.ps1`
- Create: `scripts/Test-Installer.ps1`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `artifacts/release/AutoSaveGame-Setup.exe`.
- Consumes: published app directory, semantic version, and Inno Setup `ISCC.exe`.

- [ ] **Step 1: Write installer smoke assertions**

Create `Test-Installer.ps1` which accepts `-Installer` and `-ExpectedVersion`, installs quietly for the current user, checks `%LOCALAPPDATA%\Programs\AutoSaveGame\AutoSaveGame.exe`, runs the installed executable's `--smoke-test`, invokes the uninstall entry quietly, and asserts the executable is removed.

Use a task-specific smoke root and existing .NET SHA-256 pattern; never delete game save paths.

- [ ] **Step 2: Add the Inno Setup definition**

Use these required directives:

```ini
[Setup]
AppId={{F532D19B-8DB1-44A8-9E03-96C4FE725F10}
AppName=AutoSaveGame
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\AutoSaveGame
DefaultGroupName=AutoSaveGame
UninstallDisplayName=AutoSaveGame
OutputBaseFilename=AutoSaveGame-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
```

Copy the complete published directory, create a Start Menu shortcut, and offer a post-install launch for interactive installations.

- [ ] **Step 3: Add deterministic build wrapper**

`Build-Installer.ps1` accepts `-PublishDirectory`, `-Version`, `-OutputDirectory`, and optional `-IsccPath`. It validates all inputs, resolves the known Inno Setup 6 path when omitted, and checks the compiler exit code.

- [ ] **Step 4: Build and smoke the installer locally**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1 -PublishDirectory artifacts\win-x64 -Version 0.1.0 -OutputDirectory artifacts\release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Installer.ps1 -Installer artifacts\release\AutoSaveGame-Setup.exe -ExpectedVersion 0.1.0
```

Expected: install, installed smoke, and uninstall all pass. If Inno Setup is absent locally, verify the script fails with a single actionable dependency message and perform the successful proof on the GitHub Windows runner in Task 8.

- [ ] **Step 5: Commit**

```powershell
rtk git add installer/AutoSaveGame.iss scripts/Build-Installer.ps1 scripts/Test-Installer.ps1 .gitignore
rtk git commit -m "Package a per-user Windows installer"
```

---

### Task 7: Add the Checksum-Verifying PowerShell Bootstrap

**Files:**
- Create: `scripts/Install.ps1`
- Create: `scripts/Test-Install.ps1`
- Modify: `README.md`

**Interfaces:**
- Produces: `Install.ps1 [-Repository <owner/name>] [-Version <tag>] [-NoLaunch]`, with repository default `SeikoP/AutoSaveGame`.
- Produces: `Install.ps1 -VerifyOnly -InstallerPath <path> -ChecksumPath <path>` for deterministic tests.
- Consumes: GitHub Release assets `AutoSaveGame-Setup.exe` and `SHA256SUMS.txt`.

- [ ] **Step 1: Write checksum success and mismatch tests**

`Test-Install.ps1` creates a task-specific `%TEMP%\AutoSaveGame-InstallTest-*` directory, writes a fake installer, calculates its SHA-256 through `System.Security.Cryptography.SHA256`, and invokes `Install.ps1 -VerifyOnly`. It then changes one byte and asserts verification exits nonzero.

- [ ] **Step 2: Run test and confirm red**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Install.ps1
```

Expected: failure because `Install.ps1` does not exist.

- [ ] **Step 3: Implement release resolution and checksum verification**

Use GitHub's public API with `User-Agent: AutoSaveGame-Installer`. Select exact asset names, reject prereleases when `-Version` is absent, download to a unique temp directory, and compare uppercase invariant SHA-256 strings in constant time where practical. Never continue on a missing or mismatched checksum.

- [ ] **Step 4: Implement quiet installation and launch**

Run the verified setup with:

```powershell
$process = Start-Process -FilePath $installerPath `
    -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
    -Wait -PassThru
```

Check `ExitCode`, resolve `%LOCALAPPDATA%\Programs\AutoSaveGame\AutoSaveGame.exe`, launch it unless `-NoLaunch`, and clean only the script's own validated temp directory.

- [ ] **Step 5: Run deterministic tests**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Install.ps1
```

Expected: matching checksum passes and mismatch is refused.

- [ ] **Step 6: Document direct and PowerShell installation**

Make the direct setup download the primary README route. Add this quick route after the direct route:

```powershell
irm https://raw.githubusercontent.com/SeikoP/AutoSaveGame/main/scripts/Install.ps1 | iex
```

State that a venue may block PowerShell or GitHub and that the setup executable is the fallback.

- [ ] **Step 7: Commit**

```powershell
rtk git add scripts/Install.ps1 scripts/Test-Install.ps1 README.md
rtk git commit -m "Install the latest release from PowerShell"
```

---

### Task 8: Publish Tested Release Assets in GitHub Actions

**Files:**
- Create: `.github/workflows/release.yml`
- Create: `docs/public-release-runbook.md`

**Interfaces:**
- Consumes GitHub environment secrets: `AUTOSAVEGAME_GOOGLE_CLIENT_ID`, `AUTOSAVEGAME_GOOGLE_CLIENT_SECRET`.
- Produces release assets: `AutoSaveGame-win-x64.zip`, `AutoSaveGame-Setup.exe`, `SHA256SUMS.txt`.
- Uses protected GitHub environment: `release`.

- [ ] **Step 1: Add the release workflow**

Trigger on `v*` tags and manual dispatch with a required existing `tag`. Resolve both triggers to `RELEASE_TAG`, verify that exact tag exists, and derive the numeric assembly version by removing the leading `v`. Grant `contents: write` only to the release job. Use `actions/checkout@v6` and `actions/setup-dotnet@v5`.

- [ ] **Step 2: Generate OAuth input without logging values**

Pass secrets through step environment variables, validate nonblank, create `$env:RUNNER_TEMP\autosavegame-oauth.json` using `ConvertTo-Json`, and publish with:

```powershell
dotnet publish src/AutoSaveGame.App/AutoSaveGame.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:AutoSaveGameOAuthConfig="$env:RUNNER_TEMP\autosavegame-oauth.json" `
  -p:Version="$env:RELEASE_VERSION" `
  -o artifacts/win-x64
```

Do not echo the JSON or enable diagnostic MSBuild logging.

- [ ] **Step 3: Gate packaging**

Run full tests, build, published executable smoke, install Inno Setup 6 through the runner's package manager if `ISCC.exe` is absent, build the installer, run installer smoke, build the portable ZIP, and generate SHA-256 lines for ZIP and setup.

- [ ] **Step 4: Create an immutable GitHub Release**

Use the preinstalled GitHub CLI and repository token:

```powershell
gh release create $env:RELEASE_TAG `
  artifacts/release/AutoSaveGame-win-x64.zip `
  artifacts/release/AutoSaveGame-Setup.exe `
  artifacts/release/SHA256SUMS.txt `
  --verify-tag --generate-notes --title "AutoSaveGame $env:RELEASE_TAG"
```

Fail if the release already exists. Do not overwrite release assets.

- [ ] **Step 5: Write the public release runbook**

Document exact Google Cloud production project steps, Desktop OAuth creation, `drive.appdata`, GitHub `release` environment secrets, first tag creation, workflow evidence, live acceptance, rollback by issuing a new version, and SmartScreen/code-signing caveat.

- [ ] **Step 6: Validate workflows and run all local gates**

```powershell
rtk dotnet test AutoSaveGame.sln -c Release
rtk dotnet build AutoSaveGame.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\SmokeTest.ps1 -Executable artifacts\win-x64\AutoSaveGame.exe
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Install.ps1
```

Expected: all available local gates pass; installer compilation may remain runner-only when Inno Setup is absent locally.

- [ ] **Step 7: Commit**

```powershell
rtk git add .github/workflows/release.yml docs/public-release-runbook.md
rtk git commit -m "Publish verified AutoSaveGame releases"
```

---

### Task 9: Create the Public Repository and Prove GitHub Automation

**Files:**
- No source files created solely for this task.

**Interfaces:**
- Produces: public repository `SeikoP/AutoSaveGame` with `main` as default branch.
- Produces: passing `CI` workflow run on the pushed commit.

- [ ] **Step 1: Run final local verification**

```powershell
rtk git branch --show-current
rtk git status --short
rtk dotnet test AutoSaveGame.sln -c Release
rtk dotnet build AutoSaveGame.sln -c Release
```

Expected: branch is `main`; only intentionally ignored/generated files remain; tests and build pass.

- [ ] **Step 2: Create and connect the public GitHub repository**

After confirming `SeikoP/AutoSaveGame` still does not exist:

```powershell
rtk gh repo create SeikoP/AutoSaveGame --public --source . --remote origin --push
```

Do not include `.codebase-memory/` or any OAuth value in the push.

- [ ] **Step 3: Watch CI to completion**

```powershell
$runId = rtk gh run list --workflow CI --limit 1 --json databaseId --jq '.[0].databaseId'
rtk gh run watch $runId --exit-status
```

Expected: the remote Windows workflow passes test, build, publish, and smoke.

- [ ] **Step 4: Configure the release environment**

Create the GitHub environment named `release`. Set OAuth secrets from secure local environment input without printing them:

```powershell
gh secret set AUTOSAVEGAME_GOOGLE_CLIENT_ID --env release
gh secret set AUTOSAVEGAME_GOOGLE_CLIENT_SECRET --env release
```

If production values do not yet exist, stop before tagging and report release publication as blocked by the external Google Cloud prerequisite.

- [ ] **Step 5: Publish and verify the first release when credentials exist**

Create and push an annotated version tag, watch `Release` to completion, inspect release asset names and checksums, run the documented PowerShell install against the real release, and uninstall it.

- [ ] **Step 6: Perform live Google acceptance**

Follow `docs/public-release-runbook.md`: clean-user install, production Google sign-in, add temporary save, cloud-confirmed backup, delete local sample, clean-context restore, hash comparison, watcher backup, sign-out, and on-disk token search. Record actual evidence and do not mark live integration complete unless every step passes.

---

## Final Verification

Run after all code tasks:

```powershell
rtk git status --short
rtk dotnet test AutoSaveGame.sln -c Release
rtk dotnet build AutoSaveGame.sln -c Release
rtk dotnet publish src\AutoSaveGame.App\AutoSaveGame.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\win-x64
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\SmokeTest.ps1 -Executable artifacts\win-x64\AutoSaveGame.exe
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Install.ps1
```

Inspect Git history to ensure each commit contains only task files. Inspect the public repository's CI run. Report live OAuth/Drive and Release status separately from local and fake-cloud results.
