# Vietnamese Tray and Drive Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a Vietnamese-only 360 x 480 tray application with visible operation progress, manageable Google Drive app storage, faster verified transfers, and lower idle watcher cost.

**Architecture:** Add framework-neutral operation/transfer telemetry to Core, adapt the Google Drive gateway to return checksums and real byte progress, and preserve immutable catalog commits with metadata-first verification plus strict download fallback. Keep one WPF runtime and present it through a compact navigation shell with Vietnamese formatting; make filesystem reconciliation adaptive and cancelable.

**Tech Stack:** .NET 10, WPF, C# 14, Google.Apis.Drive.v3 1.75.0.4218, xUnit, GitHub Actions, Inno Setup.

## Global Constraints

- The popup is borderless and fixed at `360 x 480` device-independent pixels.
- Vietnamese (`vi-VN`) is the only application-owned user-facing language.
- Google Drive remains the storage provider and uses the private `appDataFolder` space.
- Preserve the immutable one-snapshot catalog commit and rollback-safe restore behavior.
- Display progress from measured bytes/stages, never from an artificial timer.
- Never expose OAuth secrets, tokens, save-file content, or raw stack traces in the UI or diagnostics.
- Preserve unrelated working-tree changes, including the current deleted `README.md` and untracked `.codebase-memory/`.

---

## File structure

- `src/AutoSaveGame.Core/Models/OperationProgress.cs`: operation kind/stage and immutable progress snapshot.
- `src/AutoSaveGame.Core/Models/CloudObject.cs`: cloud checksum metadata.
- `src/AutoSaveGame.Core/Abstractions/ICloudObjectStore.cs`: progress-aware upload/download and metadata listing contract.
- `src/AutoSaveGame.Core/Services/BackupService.cs`: measured backup stages.
- `src/AutoSaveGame.Core/Services/CatalogRepository.cs`: metadata-first conflict and checksum verification.
- `src/AutoSaveGame.Infrastructure/GoogleDrive/DriveGatewayContracts.cs`: Drive checksum/progress DTOs.
- `src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleDriveGateway.cs`: Drive fields, resumable progress, bounded retry.
- `src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleDriveObjectStore.cs`: mapping between Drive and Core contracts.
- `src/AutoSaveGame.Infrastructure/Watching/GameDirectoryWatcher.cs`: adaptive reconciliation and cancellation.
- `src/AutoSaveGame.App/Services/OperationMonitor.cs`: current operation plus bounded local timing history.
- `src/AutoSaveGame.App/Services/ApplicationRuntime.cs`: route service progress to UI state and storage inspection.
- `src/AutoSaveGame.App/Services/IApplicationRuntime.cs`: UI-facing runtime state/actions.
- `src/AutoSaveGame.App/Services/VietnameseText.cs`: application copy and `vi-VN` formatting.
- `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`: popup navigation and overview state.
- `src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs`: localized per-game status and detail state.
- `src/AutoSaveGame.App/ViewModels/StorageDetailsViewModel.cs`: safe appDataFolder inventory/cleanup.
- `src/AutoSaveGame.App/MainWindow.xaml`: 360 x 480 compact shell and views.
- `src/AutoSaveGame.App/MainWindow.xaml.cs`: tray anchoring, toggle, and deactivate-to-hide.
- `src/AutoSaveGame.App/Services/TrayIconService.cs`: one icon/menu lifetime with Vietnamese labels.
- `src/AutoSaveGame.App/Assets/*`: generated PNG/ICO assets.
- Existing matching test projects receive focused tests alongside each task.

---

### Task 1: Operation telemetry contract

**Files:**
- Create: `src/AutoSaveGame.Core/Models/OperationProgress.cs`
- Create: `src/AutoSaveGame.App/Services/OperationMonitor.cs`
- Test: `tests/AutoSaveGame.App.Tests/Services/OperationMonitorTests.cs`

**Interfaces:**
- Produces: `OperationProgress(Guid OperationId, Guid? GameId, OperationKind Kind, OperationStage Stage, long BytesCompleted, long? TotalBytes, TimeSpan Elapsed, string? Detail, OperationOutcome Outcome)`.
- Produces: `OperationMonitor.Report(OperationProgress progress)`, `Current`, `History`, and `Changed`.

- [ ] **Step 1: Write failing monitor tests**

```csharp
[Fact]
public void Report_ReplacesCurrentAndKeepsOnlyTwentyTerminalEntries()
{
    var monitor = new OperationMonitor();
    for (var index = 0; index < 25; index++)
        monitor.Report(Finished(index));
    Assert.Equal(20, monitor.History.Count);
    Assert.Equal(Finished(24).OperationId, monitor.Current?.OperationId);
}
```

- [ ] **Step 2: Run `rtk dotnet test tests/AutoSaveGame.App.Tests --filter OperationMonitorTests` and confirm the missing-type failure.**
- [ ] **Step 3: Implement immutable enums/record and a thread-safe monitor that raises `Changed` after releasing its lock.**
- [ ] **Step 4: Re-run the focused tests and `rtk dotnet build AutoSaveGame.slnx`.**
- [ ] **Step 5: Commit with `Add operation progress telemetry`.**

### Task 2: Drive checksums, byte progress, and retry classification

**Files:**
- Modify: `src/AutoSaveGame.Core/Models/CloudObject.cs`
- Modify: `src/AutoSaveGame.Core/Abstractions/ICloudObjectStore.cs`
- Modify: `src/AutoSaveGame.Infrastructure/GoogleDrive/DriveGatewayContracts.cs`
- Modify: `src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleDriveGateway.cs`
- Modify: `src/AutoSaveGame.Infrastructure/GoogleDrive/GoogleDriveObjectStore.cs`
- Modify: `tests/AutoSaveGame.Infrastructure.Tests/GoogleDrive/GoogleDriveObjectStoreContractTests.cs`
- Create: `tests/AutoSaveGame.Infrastructure.Tests/GoogleDrive/GoogleDriveGatewayPolicyTests.cs`

**Interfaces:**
- Consumes: `IProgress<OperationProgress>?`.
- Produces: `CloudObject` fields `Sha256Checksum` and `Md5Checksum`.
- Produces: upload/download overloads accepting byte progress without buffering the ZIP.

- [ ] **Step 1: Add failing contract tests that map `sha256Checksum`, map `md5Checksum`, and forward byte counts monotonically.**

```csharp
Assert.Equal("archive-sha", result.Sha256Checksum);
Assert.Equal("archive-md5", result.Md5Checksum);
Assert.Equal([262144L, 524288L], reportedBytes);
```

- [ ] **Step 2: Run `rtk dotnet test tests/AutoSaveGame.Infrastructure.Tests --filter GoogleDrive` and confirm failures.**
- [ ] **Step 3: Request `sha256Checksum,md5Checksum` in list/create fields, map them through `DriveItem`, and subscribe to the Google upload/download progress events.**
- [ ] **Step 4: Add a pure retry classifier for HTTP 429, rate-limit 403, and 5xx; test that authentication, quota, checksum, conflict, and cancellation are not retried.**
- [ ] **Step 5: Implement at most three attempts with cancellation-aware exponential delays of 250 ms, 1 s, and 4 s; keep upload stream position correct before a full retry.**
- [ ] **Step 6: Benchmark the SDK default against an 8 MiB aligned chunk and retain 8 MiB only when the public API supports it and the focused transfer test passes.**
- [ ] **Step 7: Run the infrastructure tests and commit with `Report and verify Google Drive transfers`.**

### Task 3: Metadata-first immutable catalog commit

**Files:**
- Modify: `src/AutoSaveGame.Core/Services/CatalogRepository.cs`
- Modify: `tests/AutoSaveGame.Core.Tests/Services/CatalogRepositoryTests.cs`
- Modify: `tests/AutoSaveGame.Core.Tests/TestSupport/InMemoryCloudObjectStore.cs`

**Interfaces:**
- Consumes: checksum-enabled `CloudObject` and progress-aware cloud methods.
- Produces: catalog verification that uses returned SHA-256 and downloads only when SHA-256 is absent.

- [ ] **Step 1: Add failing tests for checksum match, checksum mismatch, missing-checksum fallback, and a metadata-only unchanged second preflight.**

```csharp
[Fact]
public async Task CommitSnapshotAsync_SkipsCatalogDownloadWhenDriveSha256Matches()
{
    var result = await fixture.CommitAsync(returnSha256: true);
    Assert.Equal(CatalogCommitKind.Success, result.Kind);
    Assert.Equal(0, fixture.Cloud.CatalogVerificationDownloads);
}
```

- [ ] **Step 2: Run the four focused tests and verify they fail for the expected call counts/commit result.**
- [ ] **Step 3: Compare uploaded archive size and SHA-256; reject mismatches before constructing the next descriptor.**
- [ ] **Step 4: Cache the first validated catalog identity and make the second preflight list/compare immutable IDs first, falling back to full load on ambiguity.**
- [ ] **Step 5: Verify uploaded catalog SHA-256 from metadata, retain download/canonical-hash fallback, and keep cleanup after successful verification only.**
- [ ] **Step 6: Run all Core tests and commit with `Reduce verified catalog round trips`.**

### Task 4: Adaptive watcher and operation coalescing

**Files:**
- Modify: `src/AutoSaveGame.Infrastructure/Watching/GameDirectoryWatcher.cs`
- Modify: `tests/AutoSaveGame.Infrastructure.Tests/Watching/GameDirectoryWatcherTests.cs`
- Modify: `src/AutoSaveGame.Core/Services/DebouncedBackupScheduler.cs`
- Modify: `tests/AutoSaveGame.Core.Tests/Services/DebouncedBackupSchedulerTests.cs`

**Interfaces:**
- Produces: default reconciliation interval `TimeSpan.FromMinutes(5)` and one pending follow-up backup per game.

- [ ] **Step 1: Add fake-time tests proving no reconciliation before five minutes and no scan when disabled/path-missing/disposed.**
- [ ] **Step 2: Add scheduler tests that a burst coalesces into one backup and a change during backup creates exactly one follow-up.**
- [ ] **Step 3: Run both focused suites and confirm current 30-second/burst behavior fails.**
- [ ] **Step 4: Make native events primary, gate reconciliation by watcher state, and replace overlapping work with a single-flight semaphore plus dirty-during-run flag.**
- [ ] **Step 5: Run Core and Infrastructure tests and commit with `Reduce idle watcher work`.**

### Task 5: Vietnamese runtime copy and formatting

**Files:**
- Create: `src/AutoSaveGame.App/Services/VietnameseText.cs`
- Modify: `src/AutoSaveGame.App/Services/UserFacingError.cs`
- Modify: `src/AutoSaveGame.App/Services/UserPromptService.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs`
- Modify: `src/AutoSaveGame.App/Views/ErrorDialog.xaml`
- Modify: `src/AutoSaveGame.App/Views/GameEditorDialog.xaml`
- Modify: matching tests under `tests/AutoSaveGame.App.Tests/`

**Interfaces:**
- Produces: `VietnameseText.FormatBytes(long)`, `FormatDateTime(DateTimeOffset)`, `FormatRelativeTime(DateTimeOffset, DateTimeOffset)`, and mappings for operation/status/error enums.

- [ ] **Step 1: Add tests for Vietnamese status/error strings, decimal byte formatting, and `vi-VN` date/relative-time output.**

```csharp
Assert.Equal("Đang tải lên Google Drive", VietnameseText.OperationStage(OperationStage.UploadingArchive));
Assert.Equal("1,5 MB", VietnameseText.FormatBytes(1_572_864));
```

- [ ] **Step 2: Run App tests and confirm the missing formatter/copy failures.**
- [ ] **Step 3: Centralize application-owned Vietnamese copy and replace every reachable English primary label/message in view models, prompts, and dialogs.**
- [ ] **Step 4: Add a source-level regression test that scans application XAML and declared user-facing constants for the known legacy English labels.**
- [ ] **Step 5: Run all App tests and commit with `Localize the application in Vietnamese`.**

### Task 6: Runtime storage details and progress wiring

**Files:**
- Modify: `src/AutoSaveGame.App/Services/IApplicationRuntime.cs`
- Modify: `src/AutoSaveGame.App/Services/ApplicationRuntime.cs`
- Modify: `src/AutoSaveGame.App/Services/ApplicationRuntimeFactory.cs`
- Modify: `src/AutoSaveGame.App/Services/UnavailableApplicationRuntime.cs`
- Create: `src/AutoSaveGame.App/ViewModels/StorageDetailsViewModel.cs`
- Modify: `tests/AutoSaveGame.App.Tests/Services/ApplicationRuntimeTests.cs`
- Create: `tests/AutoSaveGame.App.Tests/ViewModels/StorageDetailsViewModelTests.cs`

**Interfaces:**
- Produces: `OperationChanged`, `CurrentOperation`, `GetStorageDetailsAsync`, and `CleanSafeOrphansAsync` on `IApplicationRuntime`.
- Produces: per-game current archive, catalog generation, checksum, size, modified time, verification state, and safe orphan count.

- [ ] **Step 1: Add failing runtime tests for progress forwarding, storage grouping, active-object protection, and idempotent orphan cleanup.**
- [ ] **Step 2: Run focused App tests and confirm the interface/behavior failures.**
- [ ] **Step 3: Wire `OperationMonitor` through backup, restore, and Drive calls, dispatching UI notifications through `IUiDispatcher`.**
- [ ] **Step 4: Implement storage inventory from `appDataFolder`, derive the active ID set from the verified catalog, and permit deletion only for objects outside that set.**
- [ ] **Step 5: Run App tests and commit with `Expose safe Drive storage details`.**

### Task 7: Compact 360 x 480 tray popup

**Files:**
- Modify: `src/AutoSaveGame.App/App.xaml`
- Modify: `src/AutoSaveGame.App/MainWindow.xaml`
- Modify: `src/AutoSaveGame.App/MainWindow.xaml.cs`
- Modify: `src/AutoSaveGame.App/Services/TrayIconService.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs`
- Modify: `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs`
- Modify: `tests/AutoSaveGame.App.Tests/ViewModels/GameItemViewModelTests.cs`

**Interfaces:**
- Consumes: localized operation/storage state from Tasks 5 and 6.
- Produces: overview, game detail, and advanced storage navigation inside one popup.

- [ ] **Step 1: Add view-model tests for signed-out, overview, selected game, storage details, progress, error/retry, and back navigation states.**
- [ ] **Step 2: Run App tests and verify missing navigation/progress state failures.**
- [ ] **Step 3: Implement a borderless `360 x 480`, non-resizable shell with keyboard focus, high-contrast navy/sky resources, and accessible hit targets.**
- [ ] **Step 4: Implement overview rows, detail actions, determinate/indeterminate progress, inline errors, and the advanced storage screen with Vietnamese copy.**
- [ ] **Step 5: Anchor to the taskbar work area, toggle from the existing tray icon, hide on deactivation, and ensure popup reopening reuses the same window/runtime.**
- [ ] **Step 6: Run App tests, build the WPF project, and commit with `Build compact Vietnamese tray popup`.**

### Task 8: Icon generation and Windows packaging

**Files:**
- Create: `src/AutoSaveGame.App/Assets/AutoSaveGame.png`
- Create: `src/AutoSaveGame.App/Assets/AutoSaveGame.ico`
- Modify: `src/AutoSaveGame.App/AutoSaveGame.App.csproj`
- Modify: `installer/AutoSaveGame.iss`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Produces: transparent cloud/checkpoint icon embedded in EXE, tray, taskbar, and installer.

- [ ] **Step 1: Generate a navy/sky cloud plus checkpoint mark with transparent background and inspect it at full size.**
- [ ] **Step 2: Create ICO entries at 16, 20, 24, 32, 48, 64, 128, and 256 pixels; inspect the 16-pixel raster for recognition.**
- [ ] **Step 3: Reference the ICO from WPF project metadata, tray creation, and Inno Setup; ensure release workflow includes both assets.**
- [ ] **Step 4: Run a release publish locally and inspect EXE/installer icon metadata.**
- [ ] **Step 5: Commit with `Add AutoSaveGame application icon`.**

### Task 9: End-to-end verification and release

**Files:**
- Modify: `scripts/SmokeTest.ps1` only if new progress/storage assertions require it.
- Modify: `.github/workflows/ci.yml` only if test discovery or artifact checks require it.
- Modify: `.github/workflows/release.yml` only if packaging validation requires it.

**Interfaces:**
- Consumes: all prior tasks.
- Produces: tested installer and GitHub release artifact.

- [ ] **Step 1: Run `rtk dotnet test AutoSaveGame.slnx` and require all tests to pass.**
- [ ] **Step 2: Run `rtk dotnet build AutoSaveGame.slnx -c Release` and the repository smoke test.**
- [ ] **Step 3: Benchmark the same save fixture before/after and record stage durations, Drive call count, transferred bytes, and checksum mode without recording content or credentials.**
- [ ] **Step 4: Observe 60 seconds signed-in idle; verify average CPU below 1%, no upward handle/thread trend, one process, and one tray icon.**
- [ ] **Step 5: Manually verify Vietnamese sign-in/error states, 360 x 480 placement, backup progress, verified restore, storage details, safe cleanup, and exit.**
- [ ] **Step 6: Push the implementation branch, run GitHub Actions, publish a versioned release, install it to the normal per-user program folder, and repeat the one-process smoke check.**
- [ ] **Step 7: Commit any narrowly required verification-script adjustment with `Verify compact tray release`.**
