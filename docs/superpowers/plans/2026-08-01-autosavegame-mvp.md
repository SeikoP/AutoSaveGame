# AutoSaveGame MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Build a portable Windows application that restores and continuously backs up one safe Google Drive snapshot per configured game.

**Architecture:** A WPF composition root calls application services in AutoSaveGame.Core through explicit cloud, archive, restore, watch, and authentication interfaces. AutoSaveGame.Infrastructure implements those ports with the local filesystem and Google Drive appDataFolder; cloud commits use immutable catalog generations so an interrupted upload never destroys the last confirmed snapshot.

**Tech Stack:** C# 14, .NET 10, WPF, NuGet, Google.Apis.Drive.v3 1.75.0.4218, xUnit v3 3.2.2, Microsoft.NET.Test.Sdk 18.8.1

## Global Constraints

- Target Windows 10/11 x64.
- Publish a self-contained portable executable; target machines must not need a .NET installation.
- Do not require administrator rights, a Windows Service, Steam Cloud, Supabase, or Google Drive Desktop.
- Store OAuth tokens in process memory only; never persist refresh tokens.
- Request only the Google Drive appDataFolder scope.
- Never overwrite the active archive or catalog in place.
- Keep one committed snapshot per game; temporary predecessor and orphan files are allowed only during safe commit and cleanup.
- Never back up a partially written or locked save set.
- Treat only a cloud-confirmed backup timestamp as safe.
- Do not commit OAuth credentials, .env files, tokens, archives, publish output, or temporary save data.

## File Map

- AutoSaveGame.sln: solution entry point.
- Directory.Build.props: common nullable, warnings, language, and deterministic-build settings.
- src/AutoSaveGame.Core/Models/: game, catalog, snapshot, status, and result records.
- src/AutoSaveGame.Core/Abstractions/: ports used by application services.
- src/AutoSaveGame.Core/Services/: path, catalog, backup, restore orchestration, and state transitions.
- src/AutoSaveGame.Infrastructure/GoogleDrive/: OAuth session and appDataFolder object store.
- src/AutoSaveGame.Infrastructure/Snapshots/: deterministic hashing, ZIP creation, and safe extraction.
- src/AutoSaveGame.Infrastructure/Restore/: staged filesystem replacement and rollback.
- src/AutoSaveGame.Infrastructure/Watching/: FileSystemWatcher plus periodic reconciliation.
- src/AutoSaveGame.App/: WPF UI, tray icon, dialogs, view models, and manual composition.
- tests/AutoSaveGame.Core.Tests/: domain and orchestration tests with fakes.
- tests/AutoSaveGame.Infrastructure.Tests/: real temporary-directory integration tests.
- tests/AutoSaveGame.App.Tests/: view-model behavior tests without opening windows.

---

### Task 1: Bootstrap the solution and portable path handling

**Files:**
- Create: AutoSaveGame.sln
- Create: Directory.Build.props
- Create: .gitignore
- Create: src/AutoSaveGame.Core/AutoSaveGame.Core.csproj
- Create: src/AutoSaveGame.Core/Services/PathTemplateService.cs
- Create: tests/AutoSaveGame.Core.Tests/AutoSaveGame.Core.Tests.csproj
- Create: tests/AutoSaveGame.Core.Tests/Services/PathTemplateServiceTests.cs

**Interfaces:**
- Produces: PathTemplateService.Collapse(string absolutePath) returning a portable string.
- Produces: PathTemplateService.Expand(string pathTemplate) returning an absolute string.

- [ ] **Step 1: Install and verify the .NET 10 SDK**

Run:

    winget install --id Microsoft.DotNet.SDK.10 --exact --source winget
    dotnet --list-sdks

Expected: one 10.0.x SDK appears. Do not install Visual Studio.

- [ ] **Step 2: Scaffold the solution and projects**

Run:

    dotnet new sln --format sln -n AutoSaveGame
    dotnet new classlib -n AutoSaveGame.Core -o src/AutoSaveGame.Core -f net10.0
    dotnet new xunit -n AutoSaveGame.Core.Tests -o tests/AutoSaveGame.Core.Tests -f net10.0
    dotnet sln AutoSaveGame.sln add src/AutoSaveGame.Core/AutoSaveGame.Core.csproj
    dotnet sln AutoSaveGame.sln add tests/AutoSaveGame.Core.Tests/AutoSaveGame.Core.Tests.csproj
    dotnet add tests/AutoSaveGame.Core.Tests/AutoSaveGame.Core.Tests.csproj reference src/AutoSaveGame.Core/AutoSaveGame.Core.csproj

Pin xunit.v3 to 3.2.2 and Microsoft.NET.Test.Sdk to 18.8.1 in the test project. Set LangVersion to 14.0, Nullable to enable, TreatWarningsAsErrors to true, and Deterministic to true in Directory.Build.props.

- [ ] **Step 3: Write failing portable-path tests**

Add tests for all supported roots and the no-match case:

    [Fact]
    public void Collapse_ReplacesTheLongestKnownRoot()
    {
        var env = new Dictionary<string, string>
        {
            ["USERPROFILE"] = @"C:\Users\Cafe",
            ["APPDATA"] = @"C:\Users\Cafe\AppData\Roaming",
            ["LOCALAPPDATA"] = @"C:\Users\Cafe\AppData\Local",
            ["PROGRAMDATA"] = @"C:\ProgramData"
        };
        var sut = new PathTemplateService(env);

        Assert.Equal(
            @"%APPDATA%\Game\save",
            sut.Collapse(@"C:\Users\Cafe\AppData\Roaming\Game\save"));
    }

    [Fact]
    public void Expand_RejectsUnknownVariables()
    {
        var sut = new PathTemplateService(new Dictionary<string, string>());
        Assert.Throws<InvalidOperationException>(
            () => sut.Expand(@"%SYSTEMROOT%\Game"));
    }

- [ ] **Step 4: Run the tests and verify RED**

Run:

    dotnet test tests/AutoSaveGame.Core.Tests/AutoSaveGame.Core.Tests.csproj --filter PathTemplateServiceTests

Expected: compilation fails because PathTemplateService does not exist.

- [ ] **Step 5: Implement the minimal path service**

The constructor accepts IReadOnlyDictionary<string, string>. Collapse compares normalized paths case-insensitively, checks APPDATA and LOCALAPPDATA before USERPROFILE, and replaces only a complete directory prefix. Expand accepts only USERPROFILE, APPDATA, LOCALAPPDATA, and PROGRAMDATA, then returns Path.GetFullPath.

- [ ] **Step 6: Verify GREEN and commit**

Run:

    dotnet test AutoSaveGame.sln
    git add .gitignore Directory.Build.props AutoSaveGame.sln src/AutoSaveGame.Core tests/AutoSaveGame.Core.Tests
    git commit -m "Bootstrap solution and portable paths"

Expected: all tests pass.

---

### Task 2: Model and select immutable catalog generations

**Files:**
- Create: src/AutoSaveGame.Core/Models/GameConfig.cs
- Create: src/AutoSaveGame.Core/Models/SnapshotDescriptor.cs
- Create: src/AutoSaveGame.Core/Models/Catalog.cs
- Create: src/AutoSaveGame.Core/Models/GameSyncStatus.cs
- Create: src/AutoSaveGame.Core/Models/CloudObject.cs
- Create: src/AutoSaveGame.Core/Models/CatalogLoadResult.cs
- Create: src/AutoSaveGame.Core/Services/CatalogCodec.cs
- Create: src/AutoSaveGame.Core/Services/CatalogSelector.cs
- Create: tests/AutoSaveGame.Core.Tests/Services/CatalogSelectorTests.cs
- Create: tests/AutoSaveGame.Core.Tests/Services/CatalogCodecTests.cs
- Create: tests/AutoSaveGame.Core.Tests/TestData/CatalogTestData.cs

**Interfaces:**
- Produces: Catalog(int SchemaVersion, long Generation, IReadOnlyList<GameConfig> Games).
- Produces: GameConfig(Guid GameId, string DisplayName, string PathTemplate, SnapshotDescriptor? Snapshot, bool WatchEnabled).
- Produces: SnapshotDescriptor(string ArchiveFileId, string ArchiveSha256, string ContentSha256, long ArchiveSize, DateTimeOffset LastBackupUtc, Guid SourceMachineId).
- Produces: CloudObject(string FileId, string Name, long Size, DateTimeOffset CreatedUtc, DateTimeOffset ModifiedUtc).
- Produces: CatalogSelector.SelectAsync(IEnumerable<CloudObject>, Func<string, CancellationToken, Task<Stream>>, CancellationToken).
- Produces: CatalogCodec.ReadAsync(Stream, CancellationToken) and WriteAsync(Catalog, Stream, CancellationToken).

- [ ] **Step 1: Write failing generation and fork tests**

Cover an empty Drive, the highest valid generation, an invalid JSON object, and a fork:

    [Fact]
    public async Task SelectAsync_ReturnsConflictForDifferentCatalogsAtHighestGeneration()
    {
        var objects = new[]
        {
            CloudObject.Catalog("a", "catalog-00000007-a.json"),
            CloudObject.Catalog("b", "catalog-00000007-b.json")
        };

        var result = await CatalogTestData.Select(objects, new()
        {
            ["a"] = CatalogTestData.Json(7, "Game A"),
            ["b"] = CatalogTestData.Json(7, "Game B")
        });

        Assert.Equal(CatalogLoadKind.Conflict, result.Kind);
    }

- [ ] **Step 2: Verify RED**

Run:

    dotnet test AutoSaveGame.sln --filter "CatalogSelectorTests|CatalogCodecTests"

Expected: compilation fails for missing catalog types.

- [ ] **Step 3: Implement strict catalog serialization and selection**

Use System.Text.Json with camelCase, case-sensitive property names, ISO-8601 UTC timestamps, and an explicit schemaVersion value of 1. Reject negative generations, duplicate game IDs, blank names, unsupported variables, and snapshot hashes that are not 64 lowercase hexadecimal characters. Catalog filenames must match:

    ^catalog-(?<generation>[0-9]{8})-(?<id>[0-9a-f]{32})\.json$

Two byte-identical valid catalogs at the highest generation are duplicates, not a fork. Different valid catalogs at that generation produce Conflict.

- [ ] **Step 4: Verify GREEN and commit**

Run:

    dotnet test AutoSaveGame.sln
    git add src/AutoSaveGame.Core tests/AutoSaveGame.Core.Tests
    git commit -m "Model immutable catalog generations"

Expected: all tests pass.

---

### Task 3: Build stable snapshots and extract archives safely

**Files:**
- Create: src/AutoSaveGame.Core/Abstractions/ISnapshotArchive.cs
- Create: src/AutoSaveGame.Core/Models/SnapshotBuildResult.cs
- Create: src/AutoSaveGame.Infrastructure/AutoSaveGame.Infrastructure.csproj
- Create: src/AutoSaveGame.Infrastructure/Snapshots/ZipSnapshotArchive.cs
- Create: src/AutoSaveGame.Infrastructure/Snapshots/StableDirectoryReader.cs
- Create: src/AutoSaveGame.Infrastructure/Snapshots/ContentHasher.cs
- Create: tests/AutoSaveGame.Infrastructure.Tests/AutoSaveGame.Infrastructure.Tests.csproj
- Create: tests/AutoSaveGame.Infrastructure.Tests/Snapshots/ZipSnapshotArchiveTests.cs

**Interfaces:**
- Consumes: GameConfig and SnapshotBuildResult.
- Produces: ISnapshotArchive.BuildAsync(string sourceDirectory, string archivePath, CancellationToken).
- Produces: ISnapshotArchive.ExtractAsync(string archivePath, string stagingDirectory, CancellationToken).

- [ ] **Step 1: Write failing archive tests**

Tests must create real temporary directories and verify:

    [Fact]
    public async Task BuildAsync_ContentHashIsIndependentOfCreationOrder()
    {
        using var left = TempDirectory.Create();
        using var right = TempDirectory.Create();
        left.Write("a/1.dat", "one");
        left.Write("b/2.dat", "two");
        right.Write("b/2.dat", "two");
        right.Write("a/1.dat", "one");

        Assert.Equal(
            (await Build(left)).ContentSha256,
            (await Build(right)).ContentSha256);
    }

Also test empty directories, locked files, a changing file, reparse points, deletes, Unicode filenames, and ZIP entries containing ../outside.dat or absolute paths.

- [ ] **Step 2: Verify RED**

Run:

    dotnet test tests/AutoSaveGame.Infrastructure.Tests/AutoSaveGame.Infrastructure.Tests.csproj --filter ZipSnapshotArchiveTests

Expected: compilation fails because ZipSnapshotArchive is missing.

- [ ] **Step 3: Implement stable reads, hashes, ZIP, and Zip Slip protection**

Enumerate regular files recursively, reject every reparse point, normalize relative paths to forward slashes, and sort with StringComparer.Ordinal. Define contentSha256 as SHA-256 over this repeated binary sequence:

    Int32 little-endian UTF-8 path byte length
    UTF-8 normalized relative path bytes
    Int64 little-endian file byte length
    raw file bytes

Read file metadata twice one second apart before archiving. Retry sharing violations with delays 1, 2, 4, 8, 15, and 30 seconds; after 60 seconds return SnapshotBuildKind.Pending. Create ZIP output outside the watched tree and compute archiveSha256 after closing it.

Extraction resolves each entry with Path.GetFullPath and requires the result to begin with the staging root plus DirectorySeparatorChar. Reject symlink metadata and duplicate normalized paths.

- [ ] **Step 4: Verify GREEN and commit**

Run:

    dotnet test AutoSaveGame.sln
    git add AutoSaveGame.sln src/AutoSaveGame.Core src/AutoSaveGame.Infrastructure tests/AutoSaveGame.Infrastructure.Tests
    git commit -m "Create safe save snapshots"

Expected: all tests pass, including traversal rejection.

---

### Task 4: Restore through staging with rollback

**Files:**
- Create: src/AutoSaveGame.Core/Abstractions/IRestoreService.cs
- Create: src/AutoSaveGame.Core/Models/RestoreResult.cs
- Create: src/AutoSaveGame.Infrastructure/Restore/RestoreService.cs
- Create: src/AutoSaveGame.Infrastructure/Restore/IRestoreFileOperations.cs
- Create: src/AutoSaveGame.Infrastructure/Restore/RestoreFileOperations.cs
- Create: tests/AutoSaveGame.Infrastructure.Tests/Restore/RestoreServiceTests.cs

**Interfaces:**
- Consumes: ISnapshotArchive.ExtractAsync and expected archive SHA-256.
- Produces: IRestoreService.RestoreAsync(Stream cloudArchive, string expectedArchiveSha256, string expectedContentSha256, string targetDirectory, CancellationToken).
- Produces: RestoreResult with Success, RolledBack, and Message properties.

- [ ] **Step 1: Write failing restore transaction tests**

Cover successful replacement, archive hash mismatch before local mutation, extraction failure, target move failure, staging move failure, and successful rollback after the old target has moved.

    [Fact]
    public async Task RestoreAsync_RollsBackWhenStagingCannotReplaceTarget()
    {
        var files = new FaultingRestoreFileOperations(failOnMoveNumber: 2);
        var result = await CreateService(files).RestoreAsync(
            ValidArchive(), ValidArchiveSha256, ValidContentSha256,
            files.TargetPath, default);

        Assert.False(result.Success);
        Assert.True(result.RolledBack);
        Assert.Equal("old-save", files.ReadTarget("slot.dat"));
    }

- [ ] **Step 2: Verify RED**

Run:

    dotnet test AutoSaveGame.sln --filter RestoreServiceTests

Expected: compilation fails because RestoreService is missing.

- [ ] **Step 3: Implement the staged transaction**

Copy the incoming stream to a session temp file while hashing it. Abort on hash mismatch. Extract to a sibling staging directory, verify the staged content hash, move an existing target to a sibling rollback directory, move staging to target, verify target, then delete rollback. If any step after moving target fails, restore rollback before returning. Never put staging or rollback beneath the watched save directory.

- [ ] **Step 4: Verify GREEN and commit**

Run:

    dotnet test AutoSaveGame.sln
    git add src/AutoSaveGame.Core src/AutoSaveGame.Infrastructure tests/AutoSaveGame.Infrastructure.Tests
    git commit -m "Add transactional save restore"

Expected: all restore fault-injection tests pass.

---

### Task 5: Authenticate in memory and access Google Drive appDataFolder

**Files:**
- Create: src/AutoSaveGame.Core/Abstractions/ICloudObjectStore.cs
- Create: src/AutoSaveGame.Core/Abstractions/IUserSession.cs
- Create: src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleOAuthOptions.cs
- Create: src/AutoSaveGame.Infrastructure/GoogleDrive/MemoryDataStore.cs
- Create: src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleUserSession.cs
- Create: src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleDriveObjectStore.cs
- Create: tests/AutoSaveGame.Infrastructure.Tests/GoogleDrive/MemoryDataStoreTests.cs
- Create: tests/AutoSaveGame.Infrastructure.Tests/GoogleDrive/GoogleDriveObjectStoreContractTests.cs

**Interfaces:**
- Produces: IUserSession.SignInAsync(CancellationToken), SignOutAsync(CancellationToken), and IsSignedIn.
- Produces: ICloudObjectStore.ListAsync(string prefix, CancellationToken), UploadAsync(string name, Stream content, string contentType, CancellationToken), DownloadAsync(string fileId, Stream destination, CancellationToken), and DeleteAsync(string fileId, CancellationToken).
- Produces: GoogleOAuthOptions(string ClientId, string ClientSecret), loaded from AUTOSAVEGAME_GOOGLE_CLIENT_ID and AUTOSAVEGAME_GOOGLE_CLIENT_SECRET; an empty value is a startup validation error.

- [ ] **Step 1: Add and pin the Google Drive SDK**

Run:

    dotnet add src/AutoSaveGame.Infrastructure/AutoSaveGame.Infrastructure.csproj package Google.Apis.Drive.v3 --version 1.75.0.4218

Expected: restore succeeds and the exact version is written to the project file.

- [ ] **Step 2: Write failing memory-store and Drive request tests**

    [Fact]
    public async Task ClearAsync_RemovesRefreshTokenFromMemory()
    {
        var store = new MemoryDataStore();
        await store.StoreAsync("user", new TokenResponse { RefreshToken = "sensitive" });
        await store.ClearAsync();

        Assert.Null(await store.GetAsync<TokenResponse>("user"));
    }

Use a fake HttpMessageHandler to assert every list query includes:

    spaces=appDataFolder
    trashed=false

and every create request sets appDataFolder as the parent. Assert the requested scope is DriveService.Scope.DriveAppdata only.

- [ ] **Step 3: Verify RED**

Run:

    dotnet test AutoSaveGame.sln --filter "MemoryDataStoreTests|GoogleDriveObjectStoreContractTests"

Expected: compilation fails for missing adapters.

- [ ] **Step 4: Implement OAuth and the object store**

Use GoogleAuthorizationCodeFlow with a loopback receiver and MemoryDataStore. Open the system browser, request account selection, and never write TokenResponse to disk. SignOutAsync revokes the token when possible, clears MemoryDataStore in all cases, and disposes DriveService.

The object store requests id, name, size, createdTime, modifiedTime, and md5Checksum fields only. Uploads use unique names and appDataFolder parent. Download writes to the caller-owned stream. Delete treats HTTP 404 as already deleted and propagates quota/auth/network failures as typed CloudStoreException values without logging tokens or raw Authorization headers.

- [ ] **Step 5: Verify GREEN and commit**

Run:

    dotnet test AutoSaveGame.sln
    git add src/AutoSaveGame.Core src/AutoSaveGame.Infrastructure tests/AutoSaveGame.Infrastructure.Tests
    git commit -m "Connect Google Drive app data"

Expected: all fake-HTTP contract tests pass. Live OAuth remains disabled until AUTOSAVEGAME_GOOGLE_CLIENT_ID and AUTOSAVEGAME_GOOGLE_CLIENT_SECRET are supplied outside Git.

---

### Task 6: Commit catalog and archive generations safely

**Files:**
- Create: src/AutoSaveGame.Core/Abstractions/ICatalogRepository.cs
- Create: src/AutoSaveGame.Core/Models/CatalogCommitResult.cs
- Create: src/AutoSaveGame.Core/Services/CatalogRepository.cs
- Create: src/AutoSaveGame.Core/Services/BackupService.cs
- Create: tests/AutoSaveGame.Core.Tests/Services/CatalogRepositoryTests.cs
- Create: tests/AutoSaveGame.Core.Tests/Services/BackupServiceTests.cs

**Interfaces:**
- Consumes: ICloudObjectStore, CatalogCodec, CatalogSelector, and ISnapshotArchive.
- Produces: ICatalogRepository.LoadAsync(CancellationToken).
- Produces: ICatalogRepository.SaveCatalogAsync(Catalog expected, Catalog next, CancellationToken) for add, edit, delete, and watch-setting changes without an archive upload.
- Produces: ICatalogRepository.CommitAsync(Catalog expected, Catalog next, Stream archive, SnapshotBuildResult snapshot, CancellationToken).
- Produces: BackupService.BackupAsync(GameConfig game, Catalog loadedCatalog, CancellationToken).

- [ ] **Step 1: Write failing failure-boundary tests**

Parameterize failures after archive upload, before catalog upload, after catalog upload, during catalog read-back, and during old-object deletion. For every pre-commit failure, assert the old catalog and archive remain. For post-commit cleanup failure, assert the new highest generation restores correctly.

Add catalog-only tests proving add/edit/delete creates exactly one new catalog generation, uploads no archive, detects a changed expected generation, and leaves the previous catalog valid when the new catalog cannot be verified.

    [Theory]
    [InlineData(CommitFault.AfterArchiveUpload)]
    [InlineData(CommitFault.BeforeCatalogUpload)]
    [InlineData(CommitFault.DuringCatalogVerification)]
    public async Task CommitAsync_NeverDeletesLastConfirmedSnapshot(CommitFault fault)
    {
        var cloud = SeededCloudStore.WithFault(fault);
        var result = await CreateRepository(cloud).CommitAsync(
            cloud.CurrentCatalog, cloud.NextCatalog, NewArchive(), NewSnapshot(), default);

        Assert.True(cloud.Contains(cloud.OriginalArchiveId));
        Assert.True(cloud.CanLoadAValidCatalog());
    }

- [ ] **Step 2: Verify RED**

Run:

    dotnet test AutoSaveGame.sln --filter "CatalogRepositoryTests|BackupServiceTests"

Expected: compilation fails for missing services.

- [ ] **Step 3: Implement the commit protocol**

Before upload, reload the highest generation and compare its canonical catalog SHA-256 with expected. On mismatch return Conflict. Upload archive-<gameId>-<uuid>.zip, verify Drive size, create generation + 1 catalog with the new descriptor, upload its canonical JSON, download and parse it, and confirm its canonical hash. Only then delete the predecessor archive and older catalog objects. Cleanup lists all referenced archive IDs before deleting an orphan.

If contentSha256 matches the current descriptor, return Unchanged without uploading. A failed operation preserves local dirty state and exposes a retryable/non-retryable error category.

- [ ] **Step 4: Verify GREEN and commit**

Run:

    dotnet test AutoSaveGame.sln
    git add src/AutoSaveGame.Core tests/AutoSaveGame.Core.Tests
    git commit -m "Commit cloud snapshots atomically"

Expected: all fault-boundary and no-change tests pass.

---

### Task 7: Watch saves with debounce and periodic reconciliation

**Files:**
- Create: src/AutoSaveGame.Core/Abstractions/IBackupScheduler.cs
- Create: src/AutoSaveGame.Core/Services/GameSyncStateMachine.cs
- Create: src/AutoSaveGame.Core/Services/DebouncedBackupScheduler.cs
- Create: src/AutoSaveGame.Infrastructure/Watching/GameDirectoryWatcher.cs
- Create: src/AutoSaveGame.Infrastructure/Watching/DirectoryFingerprint.cs
- Create: tests/AutoSaveGame.Core.Tests/Services/GameSyncStateMachineTests.cs
- Create: tests/AutoSaveGame.Core.Tests/Services/DebouncedBackupSchedulerTests.cs
- Create: tests/AutoSaveGame.Core.Tests/Fakes/ManualTimeProvider.cs
- Create: tests/AutoSaveGame.Infrastructure.Tests/Watching/GameDirectoryWatcherTests.cs

**Interfaces:**
- Consumes: BackupService.BackupAsync.
- Produces: GameDirectoryWatcher.Start(GameConfig), Stop(Guid), and DisposeAsync().
- Produces: DebouncedBackupScheduler.MarkDirty(Guid), BackupNowAsync(Guid, CancellationToken), and observable GameSyncStatus changes.

- [ ] **Step 1: Write failing state, debounce, and missed-event tests**

Use an injected TimeProvider. Assert a burst at seconds 0, 1, and 2 invokes one backup at second 5. Assert Backup now cancels the pending debounce but still calls the stable snapshot builder. Assert watcher overflow marks dirty. In a temporary-directory integration test, suppress the watcher callback, mutate a file, advance the 30-second reconciliation tick, and assert dirty is emitted.

- [ ] **Step 2: Verify RED**

Run:

    dotnet test AutoSaveGame.sln --filter "GameSyncStateMachineTests|DebouncedBackupSchedulerTests|GameDirectoryWatcherTests"

Expected: compilation fails for missing watcher and scheduler.

- [ ] **Step 3: Implement watcher and scheduler**

Watch IncludeSubdirectories with NotifyFilter.FileName, DirectoryName, LastWrite, Size, and CreationTime. Callback code only calls MarkDirty. A per-game SemaphoreSlim serializes backup and restore. Use a three-second debounce, a 30-second fingerprint reconciliation interval, and exponential retry for retryable cloud failures. Buffer overflow immediately schedules a full fingerprint.

State transitions allowed are:

    NotConfigured -> Watching
    Watching -> Dirty
    Dirty -> BackingUp
    BackingUp -> Watching | Pending | Conflict | Error
    Pending -> BackingUp | Error
    Watching | Dirty | Pending -> Restoring
    Restoring -> Watching | Error

Reject all other transitions with InvalidOperationException so UI cannot report false safety.

- [ ] **Step 4: Verify GREEN and commit**

Run:

    dotnet test AutoSaveGame.sln
    git add src/AutoSaveGame.Core src/AutoSaveGame.Infrastructure tests
    git commit -m "Watch and schedule save backups"

Expected: all tests pass without timing sleeps longer than one second.

---

### Task 8: Build the WPF workflow and system tray

**Files:**
- Create: src/AutoSaveGame.App/AutoSaveGame.App.csproj
- Create: src/AutoSaveGame.App/App.xaml
- Create: src/AutoSaveGame.App/App.xaml.cs
- Create: src/AutoSaveGame.App/MainWindow.xaml
- Create: src/AutoSaveGame.App/MainWindow.xaml.cs
- Create: src/AutoSaveGame.App/ViewModels/MainViewModel.cs
- Create: src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs
- Create: src/AutoSaveGame.App/ViewModels/AsyncCommand.cs
- Create: src/AutoSaveGame.App/Views/GameEditorDialog.xaml
- Create: src/AutoSaveGame.App/Views/GameEditorDialog.xaml.cs
- Create: src/AutoSaveGame.App/Services/TrayIconService.cs
- Create: src/AutoSaveGame.App/Services/UserPromptService.cs
- Create: tests/AutoSaveGame.App.Tests/AutoSaveGame.App.Tests.csproj
- Create: tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs

**Interfaces:**
- Consumes: IUserSession, ICatalogRepository, BackupService, IRestoreService, GameDirectoryWatcher, and PathTemplateService.
- Produces: SignInCommand, AddGameCommand, EditGameCommand, DeleteGameCommand, RestoreCommand, BackupNowCommand, ToggleWatchCommand, and ExitCommand.

- [ ] **Step 1: Scaffold the Windows projects**

Run:

    dotnet new wpf -n AutoSaveGame.App -o src/AutoSaveGame.App -f net10.0
    dotnet new xunit -n AutoSaveGame.App.Tests -o tests/AutoSaveGame.App.Tests -f net10.0
    dotnet sln AutoSaveGame.sln add src/AutoSaveGame.App/AutoSaveGame.App.csproj
    dotnet sln AutoSaveGame.sln add tests/AutoSaveGame.App.Tests/AutoSaveGame.App.Tests.csproj
    dotnet add src/AutoSaveGame.App/AutoSaveGame.App.csproj reference src/AutoSaveGame.Core/AutoSaveGame.Core.csproj src/AutoSaveGame.Infrastructure/AutoSaveGame.Infrastructure.csproj
    dotnet add tests/AutoSaveGame.App.Tests/AutoSaveGame.App.Tests.csproj reference src/AutoSaveGame.App/AutoSaveGame.App.csproj

Set UseWPF and UseWindowsForms true in the app project for NotifyIcon.
Change the app test project's TargetFramework to net10.0-windows and set EnableWindowsTargeting to true before adding the app project reference.

- [ ] **Step 2: Write failing view-model tests**

    [Fact]
    public async Task RestoreCommand_StopsWatcherRestoresAndRestartsWatcher()
    {
        var harness = MainViewModelHarness.WithOneGame();
        await harness.ViewModel.RestoreCommand.ExecuteAsync(harness.Game);

        Assert.Equal(
            new[] { "stop", "confirm-closed", "restore", "start" },
            harness.Events);
    }

Also assert sign-in loads the cloud catalog, add/edit stores collapsed path templates, delete requires confirmation, dirty Exit offers Backup/Exit anyway/Cancel, missing OAuth client ID shows a configuration message, and Conflict never enables silent overwrite.

- [ ] **Step 3: Verify RED**

Run:

    dotnet test AutoSaveGame.sln --filter MainViewModelTests

Expected: compilation fails because MainViewModel is missing.

- [ ] **Step 4: Implement the view models and composition root**

MainWindow shows a sign-in panel before authentication and a game list afterward. Each row shows DisplayName, expanded path, sync status, last confirmed backup UTC converted to local time, Restore, Backup now, and Watch toggle. The editor uses FolderBrowserDialog and validates a non-empty name plus a directory path outside the app directory.

Before OAuth, show a public-computer warning instructing the user to use Guest/Private mode and close that browser window. Before restore, require confirmation that the game is closed. Disable destructive commands during BackingUp or Restoring. Closing the window minimizes to tray; explicit Exit runs the dirty-state prompt and clears the in-memory session.

Use manual constructor composition in App.xaml.cs; do not add a dependency-injection package.

- [ ] **Step 5: Verify GREEN, build, and commit**

Run:

    dotnet test AutoSaveGame.sln
    dotnet build AutoSaveGame.sln -c Release
    git add AutoSaveGame.sln src/AutoSaveGame.App tests/AutoSaveGame.App.Tests
    git commit -m "Add WPF backup workflow"

Expected: tests and Release build pass with zero warnings.

---

### Task 9: Publish and run end-to-end smoke tests

**Files:**
- Create: README.md
- Create: scripts/SmokeTest.ps1
- Create: docs/google-oauth-setup.md
- Create: src/AutoSaveGame.App/Smoke/SmokeTestRunner.cs
- Modify: src/AutoSaveGame.App/App.xaml.cs
- Modify: src/AutoSaveGame.App/AutoSaveGame.App.csproj
- Modify: .gitignore

**Interfaces:**
- Consumes: the completed WPF application.
- Produces: artifacts/win-x64/AutoSaveGame.exe.

- [ ] **Step 1: Add a deterministic local smoke script**

SmokeTestRunner handles --smoke-test without opening MainWindow and composes the real snapshot, catalog, backup, and restore services with an in-process fake cloud object store. SmokeTest.ps1 creates a task-specific temp directory, launches that mode, creates a sample save, waits for a confirmed backup marker, deletes local content, restores it, compares SHA-256, exits the app, and removes only its own verified temp directory.

The script exits non-zero for timeout, hash mismatch, unhandled process exit, or leftover locked files.

- [ ] **Step 2: Document real Google OAuth configuration**

docs/google-oauth-setup.md must state the exact Google Cloud Console steps: enable Drive API, configure OAuth consent, create a Desktop app OAuth client, set AUTOSAVEGAME_GOOGLE_CLIENT_ID and AUTOSAVEGAME_GOOGLE_CLIENT_SECRET only in the launch environment, add the test account while consent is in testing, and never commit either value. The document must explain that the installed-app client secret is a public-client protocol value rather than a confidential server secret, but is still kept out of Git. README must explain Restore before playing, green confirmed-backup status before leaving, public-browser precautions, the unavoidable power-loss window, and Drive quota errors.

- [ ] **Step 3: Configure self-contained publication**

Set RuntimeIdentifier win-x64, SelfContained true, PublishSingleFile true, PublishReadyToRun true, IncludeNativeLibrariesForSelfExtract true, DebugType embedded, and PublishTrimmed false. Do not enable trimming because WPF and Google client libraries use reflection.

- [ ] **Step 4: Run the complete verification**

Run:

    dotnet test AutoSaveGame.sln -c Release
    dotnet build AutoSaveGame.sln -c Release
    dotnet publish src/AutoSaveGame.App/AutoSaveGame.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/SmokeTest.ps1 -Executable artifacts/win-x64/AutoSaveGame.exe

Expected: all tests pass, build has zero warnings, publish creates AutoSaveGame.exe, and smoke output ends with PASS.

- [ ] **Step 5: Run the real OAuth smoke test when credentials are available**

Set AUTOSAVEGAME_GOOGLE_CLIENT_ID and AUTOSAVEGAME_GOOGLE_CLIENT_SECRET only in the current PowerShell process, open AutoSaveGame.exe, sign in through a Guest/Private browser window, add a temporary save folder, run Backup now, delete the local sample, restore it, and compare its SHA-256.

If no real desktop OAuth client values are available, report this check as blocked by missing external credentials; do not claim live Google Drive verification.

- [ ] **Step 6: Commit the release-ready MVP**

Run:

    git add .gitignore README.md docs/google-oauth-setup.md scripts/SmokeTest.ps1 src/AutoSaveGame.App/AutoSaveGame.App.csproj
    git commit -m "Document and package portable MVP"
    git status --short

Expected: the commit succeeds and the working tree is clean; artifacts remain ignored.

---

## Final Acceptance Checklist

- All unit and integration tests pass in Release configuration.
- Release build completes with zero warnings.
- The self-contained win-x64 executable launches without an installed SDK.
- Fake-cloud smoke test proves backup, deletion, restore, and matching SHA-256.
- No token, client ID, archive, publish artifact, or user save appears in Git.
- Failure injection proves the last confirmed cloud snapshot survives every interrupted commit boundary.
- The final report distinguishes local/fake-cloud verification from live Google OAuth and Drive verification.
