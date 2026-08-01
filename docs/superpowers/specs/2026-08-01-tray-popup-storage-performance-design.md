# AutoSaveGame compact tray, storage, and performance design

Date: 2026-08-01  
Status: Awaiting user review

## Goal

Turn AutoSaveGame into a compact, dependable Windows tray application that is easy to understand on a public computer. The app must show what is happening, expose the hidden Google Drive `appDataFolder` safely, and shorten local and cloud operations without weakening snapshot integrity.

## Current findings

- The current backup path is `build ZIP -> compute content/archive hashes -> verify current catalog -> upload archive -> verify current catalog again -> upload catalog -> download and verify catalog -> delete old objects`.
- The archive is not downloaded again after upload. The extra read-back is only the small catalog JSON.
- Each commit performs two catalog preflights. Each preflight can list and download catalog generations, so network round trips can dominate even when the ZIP is small.
- Google Drive upload currently returns only ID, name, size, and timestamps. Drive can also return checksums for binary files, including `sha256Checksum` when available.
- `Files.Create(...).UploadAsync(...)` uses the Google .NET media-upload mechanism. Large files should remain resumable; progress must come from its byte progress callbacks rather than a simulated timer.
- The watcher reconciliation interval is currently short enough to create periodic full-directory fingerprint bursts. `FileSystemWatcher` should remain the primary signal.

Because no stage timing is currently recorded, the first implementation step must add instrumentation. Optimization results will be judged from measured stage durations, not total-operation guesses.

## Product shape

### Window behavior

- A borderless fixed-size `360 x 480` WPF popup opens from the tray icon and anchors to the notification area.
- Opening the tray icon toggles the popup. Losing focus hides it; it does not terminate the process.
- Only one AutoSaveGame process may run per Windows session.
- Exit, sign-out, and application shutdown cancel watchers, reconciliation, and active operations cleanly.

### Visual direction

- Compact Tailscale-like layout: restrained navy/sky palette, high contrast, generous whitespace, no decorative gradients, and no dense dashboard chrome.
- Header contains icon, product name, Drive connection state, and an overflow menu.
- Signed-out view contains one primary Google sign-in action and a short privacy explanation.
- Signed-in overview contains overall health, compact game rows, Add game, last activity, and cloud usage.
- Errors appear inline with a Retry action. Modal dialogs are reserved for restore and destructive confirmations.

### Game details

Selecting a game navigates inside the same popup and shows:

- local save path and watch toggle;
- current snapshot time, archive size, and verification state;
- Backup now, Restore, Edit, and Remove actions;
- the current operation stage and determinate progress when byte totals are known.

## Operation state and progress

Core/application services publish a single immutable operation snapshot with:

- operation ID, game ID, operation type, and stage;
- bytes completed and total bytes when available;
- percentage derived from bytes, never from an artificial timer;
- elapsed time, user-facing message, and terminal result;
- cancellation and retry capability where safe.

Backup stages are `Scanning`, `BuildingArchive`, `Hashing`, `CheckingCloud`, `UploadingArchive`, `CommittingCatalog`, `CleaningUp`, and `Completed`. Restore uses `CheckingCloud`, `DownloadingArchive`, `VerifyingArchive`, `RestoringFiles`, and `Completed`.

Only one mutating operation runs for a game at a time. Duplicate watcher events coalesce into one pending backup. A change arriving during backup schedules one follow-up run, not another concurrent process.

## Google Drive transfer optimization

### Instrument before changing behavior

Record elapsed time and bytes for archive build, local hash, catalog list/download, archive upload, catalog upload, catalog verification, and cleanup. Diagnostics stay local, omit tokens and file content, and retain only a small rolling history for the advanced view.

### Upload and verification

- Request `size`, `sha256Checksum`, and `md5Checksum` in Drive upload/list responses.
- Carry the local archive SHA-256 through upload and compare it with Drive's returned SHA-256 when present. A size-only match is no longer considered fully verified.
- For catalog JSON, compare the canonical local SHA-256 with Drive's returned SHA-256. Skip the immediate catalog download when they match.
- If Drive omits the SHA-256 field, fall back to the existing download-and-canonical-hash verification. A checksum mismatch fails the commit and preserves the previous catalog.
- Expose real upload/download bytes through the Google client progress events. Do not buffer the entire ZIP in memory.
- Use a large, 256 KiB-aligned resumable chunk size selected by benchmark. Start with 8 MiB; retain the SDK default if tests show it performs better or the installed SDK does not support safe customization.
- Retry transient `5xx`, `429`, and rate-limit failures with bounded exponential backoff and cancellation. Do not retry authentication, quota exhaustion, checksum mismatch, or catalog conflict as if they were network failures.

### Reduce catalog round trips without losing conflict detection

- The first preflight establishes the expected catalog generation and immutable file-ID set.
- After archive upload, the second preflight uses a lightweight list/metadata comparison first. If the expected catalog IDs and generation are unchanged, it reuses the already validated catalog instead of downloading it again.
- If metadata changed or is ambiguous, fall back to the full existing load-and-hash path and report a conflict when appropriate.
- Upload the next immutable catalog only after the second preflight succeeds.
- Verify the new catalog by Drive SHA-256 with download fallback, then delete superseded catalog/archive objects.
- Cleanup failures do not invalidate an already verified committed generation; they become visible orphan-cleanup work.

This keeps the existing one-snapshot transactional model while removing avoidable downloads and making interrupted large uploads resumable.

## Watcher and local-process optimization

- Keep `FileSystemWatcher` as the primary change source.
- Change full reconciliation from a fixed 30-second scan to an adaptive default of five minutes while idle.
- Do not reconcile while signed out, watch is disabled, the path is missing, or the same game already has an active scan/backup.
- Coalesce noisy filesystem events and perform at most one scan/backup per game at a time.
- Avoid full content hashing during ordinary reconciliation. Build and hash only when the debounced change signal requires a backup.
- Cancel timers and watchers on sign-out/exit and ensure repeated popup opens never create new runtimes or tray icons.

## `appDataFolder` management

The overview links to an Advanced storage details view. It lists total usage and, per game:

- current snapshot object, size, modified time, and verification state;
- active catalog generation;
- Drive object ID and SHA-256 when available;
- orphan count and last cleanup result.

Actions are Refresh and Clean safe orphans. Arbitrary raw-object deletion is not exposed because deleting an active catalog or archive can corrupt restore history. The screen explains that `appDataFolder` is private app storage and is not shown in the normal Google Drive file browser.

## Icon

Generate a simple cloud plus checkpoint/save-slot mark with no text, transparent background, and the same navy/sky palette. Export a master PNG and a multi-size ICO containing 16, 20, 24, 32, 48, 64, 128, and 256 pixel variants. Use it for the executable, taskbar, tray, installer, and popup header; verify legibility at 16 pixels.

## Error and recovery rules

- Authentication expiry changes the connection state and offers Sign in again; it does not spawn another process or leave a modal loop.
- Network interruption leaves the previous catalog authoritative and allows safe retry/resume.
- Catalog conflict stops mutation, preserves both immutable candidates, and asks the user to refresh.
- Checksum mismatch marks the new object untrusted and never points the active catalog at it.
- Restore always downloads to a temporary file, verifies the stored SHA-256, and only then replaces local saves through the existing rollback-safe flow.

## Verification and acceptance criteria

- Unit/contract tests cover progress events, SHA-256 response mapping, checksum fallback, mismatch rejection, lightweight second preflight, transient retry, cancellation, and cleanup semantics.
- Fake-time tests prove watcher debounce, five-minute idle reconciliation, event coalescing, and no work while signed out/disabled.
- UI tests/view-model tests cover signed-out, idle, active progress, success, conflict, retryable failure, storage details, and popup hide/show states.
- Existing build, full test suite, packaging, and smoke test remain green.
- A local benchmark report shows per-stage timings before and after on the same fixture. The optimized path must make fewer Drive round trips and transfer no extra full ZIP copies.
- On a 60-second signed-in idle observation with no file changes, average process CPU is below 1% on the test machine and handle/thread counts do not trend upward.
- Manual release verification confirms one installed process, one tray icon, correct 360 x 480 popup behavior, accurate byte progress, successful backup/restore, and visible `appDataFolder` details.

## Delivery order

1. Add stage timing/progress contracts and tests.
2. Add checksum metadata and optimized Drive verification with fallbacks.
3. Optimize preflights, retries, and watcher scheduling with regression tests.
4. Build the compact popup and advanced storage view.
5. Generate and integrate the icon assets.
6. Run full verification, package, publish through GitHub Actions, install the release, and perform live manual checks.

## Non-goals

- Replacing Google Drive with R2 or rclone in this release.
- Multiple historical snapshots per game.
- Exposing arbitrary deletion of active cloud objects.
- Running simultaneous backups for the same game.
- Guaranteeing a fixed upload duration independent of the public computer's disk and network.
