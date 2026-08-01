# Glass Game Cards and Drive Delete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a glass-card game overview, in-popup game detail view, and confirmed deletion of all Google Drive `appDataFolder` data for one selected game.

**Architecture:** Put destructive cloud deletion in Core through `ICatalogRepository.DeleteGameCloudDataAsync`, then expose it through `IApplicationRuntime.DeleteGameCloudDataAsync`. Keep WPF focused on navigation, confirmation, and Vietnamese presentation. Preserve the immutable catalog model by clearing the selected game's snapshot before deleting its Drive archive objects.

**Tech Stack:** .NET 10, WPF, C# 14, Google.Apis.Drive.v3 1.75.0.4218, xUnit, GitHub Actions, Inno Setup.

## Global Constraints

- The popup remains borderless and fixed at `360 x 480` device-independent pixels.
- Vietnamese (`vi-VN`) is the only application-owned user-facing language.
- Google Drive remains the storage provider and uses the private `appDataFolder` space with the minimal `drive.appdata` scope.
- Deleting cloud data for a game must not delete local save files.
- Deleting cloud data for a game must not delete other games or their archive objects.
- Do not expose a raw Drive file manager or arbitrary Drive object deletion.
- Preserve immutable catalog commits and rollback-safe restore behavior.
- Display progress from measured bytes/stages, never from an artificial timer.
- Never expose OAuth secrets, tokens, save-file content, or raw stack traces in the UI or diagnostics.
- Preserve unrelated working-tree changes.

---

## File Structure

- `src/AutoSaveGame.Core/Models/GameCloudDeleteResult.cs`: result model for per-game Drive deletion, including partial cleanup state.
- `src/AutoSaveGame.Core/Abstractions/ICatalogRepository.cs`: adds the Core deletion entry point.
- `src/AutoSaveGame.Core/Services/CatalogRepository.cs`: clears the selected game's snapshot through a verified catalog commit, then deletes only archive objects associated with that game.
- `tests/AutoSaveGame.Core.Tests/Services/CatalogRepositoryTests.cs`: verifies selected-game-only deletion, commit-before-delete ordering, and partial cleanup reporting.
- `tests/AutoSaveGame.Core.Tests/TestSupport/InMemoryCloudObjectStore.cs`: records delete calls and can inject delete failures.
- `src/AutoSaveGame.App/Services/IApplicationRuntime.cs`: exposes `DeleteGameCloudDataAsync` to the UI.
- `src/AutoSaveGame.App/Services/ApplicationRuntime.cs`: calls Core deletion, refreshes runtime games, and restarts watchers from the updated catalog.
- `tests/AutoSaveGame.App.Tests/Services/ApplicationRuntimeTests.cs`: verifies runtime deletion refresh and selected game behavior.
- `src/AutoSaveGame.App/Services/IUserPromptService.cs`: adds a destructive cloud-data confirmation method.
- `src/AutoSaveGame.App/Services/UserPromptService.cs`: implements Vietnamese confirmation copy.
- `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`: adds overview/detail navigation, selected game state, and a confirmed Drive deletion command.
- `src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs`: adds display helpers for card/detail metadata.
- `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs`: verifies navigation and confirmation gating.
- `src/AutoSaveGame.App/App.xaml`: adds glass card and danger button resources.
- `src/AutoSaveGame.App/MainWindow.xaml`: changes signed-in UI to glass cards and game detail view.
- `src/AutoSaveGame.App/MainWindow.xaml.cs`: routes card clicks to view-model navigation and keeps existing add/edit behavior.
- `tests/AutoSaveGame.App.Tests/Views/VietnameseCompactUiTests.cs`: asserts compact glass-card Vietnamese UI copy.

---

### Task 1: Core Per-Game Cloud Deletion

**Files:**
- Create: `src/AutoSaveGame.Core/Models/GameCloudDeleteResult.cs`
- Modify: `src/AutoSaveGame.Core/Abstractions/ICatalogRepository.cs`
- Modify: `src/AutoSaveGame.Core/Services/CatalogRepository.cs`
- Modify: `tests/AutoSaveGame.Core.Tests/Services/CatalogRepositoryTests.cs`
- Modify: `tests/AutoSaveGame.Core.Tests/TestSupport/InMemoryCloudObjectStore.cs`

**Interfaces:**
- Consumes: `CatalogRepository.LoadAsync(CancellationToken)`, `CatalogRepository.SaveCatalogAsync(Catalog expected, Catalog next, CancellationToken cancellationToken)`, and `ICloudObjectStore.DeleteAsync(string fileId, CancellationToken cancellationToken)`.
- Produces: `Task<GameCloudDeleteResult> ICatalogRepository.DeleteGameCloudDataAsync(Guid gameId, CancellationToken cancellationToken)`.
- Produces: `GameCloudDeleteResult(GameCloudDeleteKind Kind, Catalog? Catalog, IReadOnlyList<string> DeletedFileIds, IReadOnlyList<string> FailedFileIds, string? Message = null)`.

- [ ] **Step 1: Add failing selected-game deletion tests**

Append these tests to `tests/AutoSaveGame.Core.Tests/Services/CatalogRepositoryTests.cs` before the helper methods:

```csharp
[Fact]
public async Task DeleteGameCloudDataAsync_ClearsOnlySelectedGameSnapshotAndDeletesItsArchive()
{
    var cloud = new InMemoryCloudObjectStore { ReturnChecksums = true };
    var selectedArchiveId = cloud.Seed("archive-8edcd84d82944c1e81c5569991c58499-selected.zip", "selected-save");
    var otherGameId = Guid.Parse("b8d2d4ab-d02b-43a1-8f69-b2f8663fd48e");
    var otherArchiveId = cloud.Seed("archive-b8d2d4abd02b43a18f69b2f8663fd48e-other.zip", "other-save");
    var expected = await SeedCatalogAsync(cloud, 1, selectedArchiveId, otherGameId, otherArchiveId);
    var sut = CreateRepository(cloud);

    var result = await sut.DeleteGameCloudDataAsync(
        GameId,
        TestContext.Current.CancellationToken);

    var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);
    Assert.Equal(GameCloudDeleteKind.Deleted, result.Kind);
    Assert.Null(loaded.Catalog?.Games.Single(game => game.GameId == GameId).Snapshot);
    Assert.NotNull(loaded.Catalog?.Games.Single(game => game.GameId == otherGameId).Snapshot);
    Assert.False(cloud.ContainsId(selectedArchiveId));
    Assert.True(cloud.ContainsId(otherArchiveId));
    Assert.Equal([selectedArchiveId], cloud.DeleteCalls);
}

[Fact]
public async Task DeleteGameCloudDataAsync_DoesNotDeleteArchiveWhenCatalogCommitFails()
{
    var cloud = new InMemoryCloudObjectStore { ReturnChecksums = true };
    var archiveId = cloud.Seed("archive-selected.zip", "selected-save");
    var expected = await SeedCatalogAsync(cloud, 1, archiveId);
    cloud.FailUploadCall = 1;
    var sut = CreateRepository(cloud);

    var result = await sut.DeleteGameCloudDataAsync(
        GameId,
        TestContext.Current.CancellationToken);

    Assert.Equal(GameCloudDeleteKind.Failed, result.Kind);
    Assert.True(cloud.ContainsId(archiveId));
    Assert.Empty(cloud.DeleteCalls);
    Assert.NotNull((await sut.LoadAsync(TestContext.Current.CancellationToken)).Catalog?.Games.Single().Snapshot);
}

[Fact]
public async Task DeleteGameCloudDataAsync_ReportsCleanupIncompleteAfterCatalogCommit()
{
    var cloud = new InMemoryCloudObjectStore { ReturnChecksums = true };
    var archiveId = cloud.Seed("archive-selected.zip", "selected-save");
    var expected = await SeedCatalogAsync(cloud, 1, archiveId);
    cloud.FailDeleteIds.Add(archiveId);
    var sut = CreateRepository(cloud);

    var result = await sut.DeleteGameCloudDataAsync(
        GameId,
        TestContext.Current.CancellationToken);

    Assert.Equal(GameCloudDeleteKind.CleanupIncomplete, result.Kind);
    Assert.Equal([archiveId], result.FailedFileIds);
    Assert.Null((await sut.LoadAsync(TestContext.Current.CancellationToken)).Catalog?.Games.Single().Snapshot);
}
```

Replace the existing `SeedCatalogAsync` helper with this overload-compatible version:

```csharp
private static async Task<Catalog> SeedCatalogAsync(
    InMemoryCloudObjectStore cloud,
    long generation,
    string? archiveFileId = null,
    Guid? otherGameId = null,
    string? otherArchiveFileId = null)
{
    var snapshot = archiveFileId is null
        ? null
        : new SnapshotDescriptor(
            archiveFileId,
            new string('a', 64),
            new string('b', 64),
            8,
            DateTimeOffset.UnixEpoch,
            MachineId);
    var games = new List<GameConfig>
    {
        new(
            GameId,
            "Hades",
            @"%USERPROFILE%\Documents\Hades",
            snapshot,
            true),
    };
    if (otherGameId is not null)
    {
        games.Add(new GameConfig(
            otherGameId.Value,
            "Celeste",
            @"%USERPROFILE%\Documents\Celeste",
            new SnapshotDescriptor(
                otherArchiveFileId ?? throw new ArgumentNullException(nameof(otherArchiveFileId)),
                new string('c', 64),
                new string('d', 64),
                10,
                DateTimeOffset.UnixEpoch,
                MachineId),
            true));
    }

    var catalog = new Catalog(1, generation, games);
    await using var output = new MemoryStream();
    await new CatalogCodec().WriteAsync(
        catalog,
        output,
        TestContext.Current.CancellationToken);
    cloud.Seed(
        $"catalog-{generation:00000000}-{Guid.NewGuid():N}.json",
        output.ToArray());
    return catalog;
}
```

- [ ] **Step 2: Run failing Core tests**

Run: `rtk dotnet test tests/AutoSaveGame.Core.Tests --filter DeleteGameCloudDataAsync`

Expected: FAIL with missing `DeleteGameCloudDataAsync`, `GameCloudDeleteKind`, `DeleteCalls`, or `FailDeleteIds`.

- [ ] **Step 3: Add the result model**

Create `src/AutoSaveGame.Core/Models/GameCloudDeleteResult.cs`:

```csharp
namespace AutoSaveGame.Core.Models;

public enum GameCloudDeleteKind
{
    Deleted,
    AlreadyEmpty,
    NotFound,
    Conflict,
    Failed,
    CleanupIncomplete,
}

public sealed record GameCloudDeleteResult(
    GameCloudDeleteKind Kind,
    Catalog? Catalog,
    IReadOnlyList<string> DeletedFileIds,
    IReadOnlyList<string> FailedFileIds,
    string? Message = null);
```

- [ ] **Step 4: Add the repository interface method**

Modify `src/AutoSaveGame.Core/Abstractions/ICatalogRepository.cs`:

```csharp
Task<GameCloudDeleteResult> DeleteGameCloudDataAsync(
    Guid gameId,
    CancellationToken cancellationToken);
```

Place it after `CommitSnapshotAsync` and before `CleanupOrphansAsync`.

- [ ] **Step 5: Record delete calls and inject delete failure in the in-memory cloud store**

Modify `tests/AutoSaveGame.Core.Tests/TestSupport/InMemoryCloudObjectStore.cs`:

```csharp
public List<string> DeleteCalls { get; } = [];

public HashSet<string> FailDeleteIds { get; } = new(StringComparer.Ordinal);

public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
{
    DeleteCalls.Add(fileId);
    if (FailDeleteIds.Contains(fileId))
    {
        throw new CloudStoreException(
            CloudStoreErrorKind.Network,
            $"Injected delete failure for {fileId}.");
    }

    objects.Remove(fileId);
    return Task.CompletedTask;
}
```

Replace the existing `DeleteAsync` method with this version.

- [ ] **Step 6: Implement deletion in CatalogRepository**

Add this public method to `src/AutoSaveGame.Core/Services/CatalogRepository.cs` after `CommitSnapshotAsync`:

```csharp
public async Task<GameCloudDeleteResult> DeleteGameCloudDataAsync(
    Guid gameId,
    CancellationToken cancellationToken)
{
    var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
    if (loaded.Kind == CatalogLoadKind.Conflict)
    {
        return new GameCloudDeleteResult(
            GameCloudDeleteKind.Conflict,
            null,
            [],
            [],
            "Cloud catalog has conflicting generations.");
    }

    if (loaded.Kind == CatalogLoadKind.Corrupt || loaded.Catalog is null)
    {
        return new GameCloudDeleteResult(
            GameCloudDeleteKind.Failed,
            null,
            [],
            [],
            "Cloud catalog is corrupt.");
    }

    var current = loaded.Catalog;
    var game = current.Games.SingleOrDefault(item => item.GameId == gameId);
    if (game is null)
    {
        return new GameCloudDeleteResult(
            GameCloudDeleteKind.NotFound,
            current,
            [],
            [],
            "Game is not in catalog.");
    }

    var archiveFileIds = game.Snapshot?.ArchiveFileId is null
        ? []
        : new[] { game.Snapshot.ArchiveFileId };
    if (archiveFileIds.Length == 0)
    {
        return new GameCloudDeleteResult(
            GameCloudDeleteKind.AlreadyEmpty,
            current,
            [],
            []);
    }

    var next = current with
    {
        Generation = current.Generation + 1,
        Games = current.Games
            .Select(item => item.GameId == gameId
                ? item with { Snapshot = null }
                : item)
            .ToArray(),
    };
    var commit = await SaveCatalogAsync(
        current,
        next,
        cancellationToken).ConfigureAwait(false);
    if (commit.Kind != CatalogCommitKind.Success || commit.Catalog is null)
    {
        return new GameCloudDeleteResult(
            commit.Kind == CatalogCommitKind.Conflict
                ? GameCloudDeleteKind.Conflict
                : GameCloudDeleteKind.Failed,
            commit.Catalog,
            [],
            [],
            commit.Message ?? "Catalog update failed.");
    }

    var deleted = new List<string>();
    var failed = new List<string>();
    foreach (var fileId in archiveFileIds)
    {
        try
        {
            await cloud.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
            deleted.Add(fileId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failed.Add(fileId);
        }
    }

    return new GameCloudDeleteResult(
        failed.Count == 0
            ? GameCloudDeleteKind.Deleted
            : GameCloudDeleteKind.CleanupIncomplete,
        commit.Catalog,
        deleted,
        failed,
        failed.Count == 0
            ? null
            : "Cloud catalog was updated, but some archive files could not be deleted.");
}
```

- [ ] **Step 7: Run focused Core tests**

Run: `rtk dotnet test tests/AutoSaveGame.Core.Tests --filter DeleteGameCloudDataAsync`

Expected: PASS.

- [ ] **Step 8: Run all Core tests**

Run: `rtk dotnet test tests/AutoSaveGame.Core.Tests`

Expected: PASS.

- [ ] **Step 9: Commit Task 1**

Run:

```bash
git add src/AutoSaveGame.Core tests/AutoSaveGame.Core.Tests
git commit -m "feat: delete per-game Drive data safely"
```

---

### Task 2: Runtime API and Confirmation Contract

**Files:**
- Modify: `src/AutoSaveGame.App/Services/IApplicationRuntime.cs`
- Modify: `src/AutoSaveGame.App/Services/ApplicationRuntime.cs`
- Modify: `src/AutoSaveGame.App/Services/IUserPromptService.cs`
- Modify: `src/AutoSaveGame.App/Services/UserPromptService.cs`
- Modify: `tests/AutoSaveGame.App.Tests/Services/ApplicationRuntimeTests.cs`
- Modify: `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `ICatalogRepository.DeleteGameCloudDataAsync(Guid gameId, CancellationToken cancellationToken)` from Task 1.
- Produces: `Task<GameCloudDeleteResult> IApplicationRuntime.DeleteGameCloudDataAsync(Guid gameId, CancellationToken cancellationToken)`.
- Produces: `Task<bool> IUserPromptService.ConfirmDeleteCloudDataAsync(string displayName)`.

- [ ] **Step 1: Add failing ApplicationRuntime deletion test**

Append this test to `tests/AutoSaveGame.App.Tests/Services/ApplicationRuntimeTests.cs` before fake classes:

```csharp
[Fact]
public async Task DeleteGameCloudDataAsync_CallsCatalogDeletionAndRefreshesGames()
{
    var game = new GameConfig(
        Guid.Parse("8edcd84d-8294-4c1e-81c5-569991c58499"),
        "Hades",
        Path.Combine(Path.GetTempPath(), "AutoSaveGame-Runtime-Delete"),
        new SnapshotDescriptor(
            "archive-file",
            new string('a', 64),
            new string('b', 64),
            8,
            DateTimeOffset.UnixEpoch,
            Guid.Parse("0de891ef-1e21-4d51-bacd-a5f1120437bb")),
        true);
    var cleared = game with { Snapshot = null };
    var catalogs = new FakeCatalogRepository(new Catalog(1, 1, [game]))
    {
        DeleteResult = new GameCloudDeleteResult(
            GameCloudDeleteKind.Deleted,
            new Catalog(1, 2, [cleared]),
            ["archive-file"],
            []),
    };
    var runtime = new ApplicationRuntime(
        new FakeSession(),
        catalogs,
        new RecordingCloudStore([]),
        new RecordingRestoreService([]),
        new RecordingScheduler(),
        new RecordingWatcher([]),
        new PathTemplateService(new Dictionary<string, string>()),
        new RecordingRestoreArchiveStore([]),
        (_, _, _) => Task.FromResult(new BackupResult(BackupKind.Success)));
    await runtime.SignInAsync(TestContext.Current.CancellationToken);

    var result = await runtime.DeleteGameCloudDataAsync(
        game.GameId,
        TestContext.Current.CancellationToken);

    Assert.Equal(GameCloudDeleteKind.Deleted, result.Kind);
    Assert.Equal(game.GameId, catalogs.DeletedGameId);
    Assert.Null(runtime.Games.Single().Config.Snapshot);
}
```

Add these members to `FakeCatalogRepository` in the same test file:

```csharp
public Guid? DeletedGameId { get; private set; }

public GameCloudDeleteResult? DeleteResult { get; init; }

public Task<GameCloudDeleteResult> DeleteGameCloudDataAsync(
    Guid gameId,
    CancellationToken cancellationToken)
{
    DeletedGameId = gameId;
    return Task.FromResult(DeleteResult ?? new GameCloudDeleteResult(
        GameCloudDeleteKind.AlreadyEmpty,
        catalog,
        [],
        []));
}
```

- [ ] **Step 2: Add failing view-model confirmation tests**

Append these tests to `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs` before `CreateRuntimeGame`:

```csharp
[Fact]
public async Task DeleteCloudDataCommand_DoesNothingWhenConfirmationIsDeclined()
{
    var events = new List<string>();
    var runtime = new FakeRuntime(events) { RuntimeGames = [CreateRuntimeGame()] };
    var prompts = new FakePrompts(events) { ConfirmCloudDelete = false };
    var sut = new MainViewModel(runtime, prompts);
    await sut.SignInCommand.ExecuteAsync();
    sut.SelectGameCommand.Execute(sut.Games.Single());
    events.Clear();

    await sut.DeleteCloudDataCommand.ExecuteAsync();

    Assert.Equal(["confirm-cloud-delete"], events);
}

[Fact]
public async Task DeleteCloudDataCommand_DeletesSelectedGameAfterConfirmation()
{
    var events = new List<string>();
    var runtime = new FakeRuntime(events) { RuntimeGames = [CreateRuntimeGame()] };
    var prompts = new FakePrompts(events) { ConfirmCloudDelete = true };
    var sut = new MainViewModel(runtime, prompts);
    await sut.SignInCommand.ExecuteAsync();
    sut.SelectGameCommand.Execute(sut.Games.Single());
    events.Clear();

    await sut.DeleteCloudDataCommand.ExecuteAsync();

    Assert.Equal(["confirm-cloud-delete", "delete-cloud"], events);
    Assert.Equal("Đã xóa dữ liệu Drive của game.", sut.StatusMessage);
}
```

Add this property and method to `FakePrompts`:

```csharp
public bool ConfirmCloudDelete { get; init; }

public Task<bool> ConfirmDeleteCloudDataAsync(string displayName)
{
    events.Add("confirm-cloud-delete");
    return Task.FromResult(ConfirmCloudDelete);
}
```

Add this method to `FakeRuntime`:

```csharp
public Task<GameCloudDeleteResult> DeleteGameCloudDataAsync(
    Guid gameId,
    CancellationToken cancellationToken)
{
    events.Add("delete-cloud");
    return Task.FromResult(new GameCloudDeleteResult(
        GameCloudDeleteKind.Deleted,
        null,
        ["archive-file"],
        []));
}
```

- [ ] **Step 3: Run failing App tests**

Run: `rtk dotnet test tests/AutoSaveGame.App.Tests --filter "DeleteGameCloudDataAsync|DeleteCloudDataCommand"`

Expected: FAIL with missing runtime, prompt, and view-model members.

- [ ] **Step 4: Add runtime and prompt interfaces**

Modify `src/AutoSaveGame.App/Services/IApplicationRuntime.cs`:

```csharp
Task<GameCloudDeleteResult> DeleteGameCloudDataAsync(
    Guid gameId,
    CancellationToken cancellationToken);
```

Place it after `DeleteGameAsync`.

Modify `src/AutoSaveGame.App/Services/IUserPromptService.cs`:

```csharp
Task<bool> ConfirmDeleteCloudDataAsync(string displayName);
```

Place it after `ConfirmDeleteAsync`.

- [ ] **Step 5: Implement runtime deletion**

Add this method to `src/AutoSaveGame.App/Services/ApplicationRuntime.cs` after `DeleteGameAsync`:

```csharp
public async Task<GameCloudDeleteResult> DeleteGameCloudDataAsync(
    Guid gameId,
    CancellationToken cancellationToken)
{
    await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        _ = FindGame(gameId);
        var result = await catalogs.DeleteGameCloudDataAsync(
            gameId,
            cancellationToken).ConfigureAwait(false);
        if (result.Catalog is not null)
        {
            currentCatalog = result.Catalog;
            await ReplaceRuntimeGamesAsync(
                currentCatalog,
                cancellationToken).ConfigureAwait(false);
        }

        if (result.Kind is GameCloudDeleteKind.Conflict or GameCloudDeleteKind.Failed)
        {
            throw new InvalidOperationException(
                result.Message ?? "Không thể xóa dữ liệu Drive của game.");
        }

        return result;
    }
    finally
    {
        operationGate.Release();
    }
}
```

- [ ] **Step 6: Implement destructive confirmation copy**

Add this method to `src/AutoSaveGame.App/Services/UserPromptService.cs` after `ConfirmDeleteAsync`:

```csharp
public Task<bool> ConfirmDeleteCloudDataAsync(string displayName) =>
    Task.FromResult(
        WpfMessageBox.Show(
            $"Xóa toàn bộ dữ liệu Google Drive của {displayName}?\n\n" +
            "File save trên máy không bị xóa. Bản sao lưu trên Drive của game này sẽ bị xóa khỏi appDataFolder ẩn và không thể khôi phục từ Drive cho tới khi sao lưu lại.",
            "Xóa dữ liệu Drive của game",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes);
```

- [ ] **Step 7: Run focused App service tests**

Run: `rtk dotnet test tests/AutoSaveGame.App.Tests --filter DeleteGameCloudDataAsync`

Expected: PASS after Task 3 view-model implementation is complete for the view-model tests; service test should pass now.

- [ ] **Step 8: Commit Task 2**

Run:

```bash
git add src/AutoSaveGame.App/Services tests/AutoSaveGame.App.Tests/Services
git commit -m "feat: expose per-game Drive deletion"
```

---

### Task 3: View-Model Navigation and Delete Command

**Files:**
- Modify: `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs`
- Modify: `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs`
- Modify: `tests/AutoSaveGame.App.Tests/ViewModels/GameItemViewModelTests.cs`

**Interfaces:**
- Consumes: `IApplicationRuntime.DeleteGameCloudDataAsync(Guid gameId, CancellationToken cancellationToken)` and `IUserPromptService.ConfirmDeleteCloudDataAsync(string displayName)` from Task 2.
- Produces: `MainViewModel.SelectedGame`, `IsGameDetailVisible`, `IsOverviewVisible`, `SelectGameCommand`, `BackToOverviewCommand`, and `DeleteCloudDataCommand`.
- Produces: `GameItemViewModel.ArchiveSizeText`, `ArchiveFileIdDisplay`, and `ArchiveSha256Display`.

- [ ] **Step 1: Add failing navigation tests**

Append this test to `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs` before `CreateRuntimeGame`:

```csharp
[Fact]
public async Task SelectGameCommand_OpensDetailAndBackCommandReturnsToOverview()
{
    var runtime = new FakeRuntime([]) { RuntimeGames = [CreateRuntimeGame()] };
    var sut = new MainViewModel(runtime, new FakePrompts([]));
    await sut.SignInCommand.ExecuteAsync();

    sut.SelectGameCommand.Execute(sut.Games.Single());

    Assert.True(sut.IsGameDetailVisible);
    Assert.False(sut.IsOverviewVisible);
    Assert.Equal("Hades", sut.SelectedGame?.DisplayName);

    sut.BackToOverviewCommand.Execute(null);

    Assert.False(sut.IsGameDetailVisible);
    Assert.True(sut.IsOverviewVisible);
    Assert.Null(sut.SelectedGame);
}
```

Append this test to `tests/AutoSaveGame.App.Tests/ViewModels/GameItemViewModelTests.cs`:

```csharp
[Fact]
public void DriveMetadataDisplay_FormatsArchiveFieldsForDetailView()
{
    var viewModel = CreateViewModel(GameSyncStatus.Watching);

    Assert.Equal("8 B", viewModel.ArchiveSizeText);
    Assert.Equal("file", viewModel.ArchiveFileIdDisplay);
    Assert.StartsWith(new string('a', 12), viewModel.ArchiveSha256Display, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run failing view-model tests**

Run: `rtk dotnet test tests/AutoSaveGame.App.Tests --filter "SelectGameCommand|DriveMetadataDisplay|DeleteCloudDataCommand"`

Expected: FAIL with missing command/properties.

- [ ] **Step 3: Add selection fields and commands to MainViewModel**

Add fields near the existing `statusMessage` field:

```csharp
private GameItemViewModel? selectedGame;
```

Add commands in the constructor after `DeleteGameCommand`:

```csharp
SelectGameCommand = new AsyncCommand<GameItemViewModel>(
    game =>
    {
        SelectedGame = game;
        return Task.CompletedTask;
    },
    game => game is not null);
BackToOverviewCommand = new AsyncCommand(
    () =>
    {
        SelectedGame = null;
        return Task.CompletedTask;
    });
DeleteCloudDataCommand = new AsyncCommand(
    DeleteCloudDataAsync,
    () => !IsBusy && SelectedGame is not null && SelectedGame.CanRestore);
```

Add public members after `DeleteGameCommand`:

```csharp
public AsyncCommand<GameItemViewModel> SelectGameCommand { get; }

public AsyncCommand BackToOverviewCommand { get; }

public AsyncCommand DeleteCloudDataCommand { get; }

public GameItemViewModel? SelectedGame
{
    get => selectedGame;
    private set
    {
        if (ReferenceEquals(selectedGame, value))
        {
            return;
        }

        selectedGame = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(IsOverviewVisible));
        OnPropertyChanged(nameof(IsGameDetailVisible));
        DeleteCloudDataCommand.RaiseCanExecuteChanged();
    }
}

public bool IsOverviewVisible => IsSignedIn && SelectedGame is null;

public bool IsGameDetailVisible => SelectedGame is not null;
```

Add this private method after `DeleteGameAsync`:

```csharp
private async Task DeleteCloudDataAsync()
{
    if (SelectedGame is null)
    {
        return;
    }

    var game = SelectedGame;
    if (!await prompts.ConfirmDeleteCloudDataAsync(game.DisplayName))
    {
        return;
    }

    await RunBusyAsync(
        async () =>
        {
            var result = await runtime.DeleteGameCloudDataAsync(
                game.GameId,
                CancellationToken.None);
            if (result.Kind == Core.Models.GameCloudDeleteKind.CleanupIncomplete)
            {
                StatusMessage = "Đã bỏ liên kết Drive, còn file cần dọn lại.";
            }
        },
        "Đã xóa dữ liệu Drive của game.",
        "Xóa dữ liệu Drive",
        $"Đang xóa dữ liệu Drive của {game.DisplayName}...");
}
```

Update `RefreshGames()` to keep or clear selection:

```csharp
var selectedGameId = SelectedGame?.GameId;
Games.Clear();
foreach (var game in runtime.Games.OrderBy(
             item => item.Config.DisplayName,
             StringComparer.CurrentCultureIgnoreCase))
{
    Games.Add(new GameItemViewModel(game, uiDispatcher));
}

SelectedGame = selectedGameId is null
    ? null
    : Games.SingleOrDefault(game => game.GameId == selectedGameId.Value);
OnPropertyChanged(nameof(IsSignedIn));
OnPropertyChanged(nameof(HasGames));
OnPropertyChanged(nameof(IsEmpty));
OnPropertyChanged(nameof(CloudUsageText));
OnPropertyChanged(nameof(IsOverviewVisible));
OnPropertyChanged(nameof(IsGameDetailVisible));
```

Update `RaiseCommandStates()`:

```csharp
DeleteCloudDataCommand.RaiseCanExecuteChanged();
```

- [ ] **Step 4: Add Drive metadata display helpers**

Modify `src/AutoSaveGame.App/ViewModels/GameItemViewModel.cs` by adding these properties after `ArchiveSha256`:

```csharp
public string ArchiveSizeText => VietnameseText.FormatBytes(ArchiveSize);

public string ArchiveFileIdDisplay => ArchiveFileId ?? "Chưa có file Drive";

public string ArchiveSha256Display => ArchiveSha256 is null
    ? "Chưa có checksum"
    : ArchiveSha256.Length <= 16
        ? ArchiveSha256
        : $"{ArchiveSha256[..16]}...";
```

- [ ] **Step 5: Run focused view-model tests**

Run: `rtk dotnet test tests/AutoSaveGame.App.Tests --filter "SelectGameCommand|DriveMetadataDisplay|DeleteCloudDataCommand"`

Expected: PASS.

- [ ] **Step 6: Run all App view-model tests**

Run: `rtk dotnet test tests/AutoSaveGame.App.Tests --filter ViewModels`

Expected: PASS.

- [ ] **Step 7: Commit Task 3**

Run:

```bash
git add src/AutoSaveGame.App/ViewModels tests/AutoSaveGame.App.Tests/ViewModels
git commit -m "feat: navigate game cards to detail"
```

---

### Task 4: Glass Card UI and Detail Screen

**Files:**
- Modify: `src/AutoSaveGame.App/App.xaml`
- Modify: `src/AutoSaveGame.App/MainWindow.xaml`
- Modify: `src/AutoSaveGame.App/MainWindow.xaml.cs`
- Modify: `tests/AutoSaveGame.App.Tests/Views/VietnameseCompactUiTests.cs`

**Interfaces:**
- Consumes: `MainViewModel.IsOverviewVisible`, `SelectedGame`, `SelectGameCommand`, `BackToOverviewCommand`, and `DeleteCloudDataCommand` from Task 3.
- Produces: A `360 x 480` Vietnamese popup where every game is displayed as a glass card and selecting a card opens an in-popup detail screen.

- [ ] **Step 1: Add failing UI markup test**

Modify `MainWindow_IsCompactTrayPopupWithVietnamesePrimaryCopy` in `tests/AutoSaveGame.App.Tests/Views/VietnameseCompactUiTests.cs` by adding these assertions:

```csharp
Assert.Contains("GlassGameCard", markup);
Assert.Contains("Command=\"{Binding SelectGameCommand}\"", markup);
Assert.Contains("Xóa dữ liệu Drive của game này", markup);
Assert.Contains("appDataFolder ẩn của Google Drive", markup);
```

- [ ] **Step 2: Run failing UI test**

Run: `rtk dotnet test tests/AutoSaveGame.App.Tests --filter VietnameseCompactUiTests`

Expected: FAIL because the glass card style and detail copy do not exist yet.

- [ ] **Step 3: Add glass card resources**

Add these resources to `src/AutoSaveGame.App/App.xaml` after `DangerBrush`:

```xml
<SolidColorBrush x:Key="GlassCardBackground" Color="#DFFFFFFF" Opacity="0.88" />
<SolidColorBrush x:Key="GlassCardBorderBrush" Color="#BFD8EAFE" Opacity="0.75" />
<SolidColorBrush x:Key="GlassHighlightBrush" Color="#FFFFFFFF" Opacity="0.70" />
<SolidColorBrush x:Key="DangerSurfaceBrush" Color="#FEF2F2" />
```

Add this style after the existing `Card` style:

```xml
<Style x:Key="GlassGameCard" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="{StaticResource GlassCardBackground}" />
    <Setter Property="BorderBrush" Value="{StaticResource GlassCardBorderBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="0" />
    <Setter Property="Margin" Value="0,0,0,10" />
    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        CornerRadius="16"
                        Padding="12">
                    <Border.Effect>
                        <DropShadowEffect BlurRadius="18"
                                          ShadowDepth="3"
                                          Opacity="0.16"
                                          Color="#0F172A" />
                    </Border.Effect>
                    <Grid>
                        <Border Height="1"
                                VerticalAlignment="Top"
                                Background="{StaticResource GlassHighlightBrush}" />
                        <ContentPresenter />
                    </Grid>
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 4: Replace signed-in overview rows with glass cards**

In `src/AutoSaveGame.App/MainWindow.xaml`, replace the signed-in `ScrollViewer` `ItemsControl.ItemTemplate` card `Border` with this button template:

```xml
<Button Style="{StaticResource GlassGameCard}"
        Command="{Binding DataContext.SelectGameCommand, RelativeSource={RelativeSource AncestorType=Window}}"
        CommandParameter="{Binding}">
    <StackPanel>
        <Grid>
            <TextBlock Text="{Binding DisplayName}"
                       FontWeight="SemiBold"
                       FontSize="15"
                       Foreground="{StaticResource TextStrongBrush}" />
            <Border HorizontalAlignment="Right"
                    Padding="7,2"
                    Background="{StaticResource SoftBlueBrush}"
                    CornerRadius="8">
                <TextBlock Text="{Binding StatusText}"
                           FontSize="10"
                           Foreground="{StaticResource PrimaryBrush}" />
            </Border>
        </Grid>
        <TextBlock Margin="0,6,0,0"
                   Text="{Binding LocalPath}"
                   FontSize="10"
                   Foreground="{StaticResource TextMutedBrush}"
                   TextTrimming="CharacterEllipsis"
                   ToolTip="{Binding LocalPath}" />
        <Grid Margin="0,8,0,0">
            <TextBlock Text="{Binding LastBackupDisplayText}"
                       FontSize="10"
                       Foreground="{StaticResource TextMutedBrush}" />
            <TextBlock HorizontalAlignment="Right"
                       Text="{Binding ArchiveSizeText}"
                       FontSize="10"
                       FontWeight="SemiBold"
                       Foreground="{StaticResource TextStrongBrush}" />
        </Grid>
    </StackPanel>
</Button>
```

- [ ] **Step 5: Add detail screen markup**

Still in `src/AutoSaveGame.App/MainWindow.xaml`, add a second signed-in `Grid` next to the overview `Grid` inside row 2. Bind its visibility to `IsGameDetailVisible` with a style trigger and bind all fields through `SelectedGame`:

```xml
<Grid DataContext="{Binding SelectedGame}">
    <Grid.Style>
        <Style TargetType="Grid">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding DataContext.IsGameDetailVisible, RelativeSource={RelativeSource AncestorType=Window}}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Grid.Style>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>
    <Grid>
        <Button Content="← Quay lại"
                Style="{StaticResource CompactButton}"
                Command="{Binding DataContext.BackToOverviewCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
        <TextBlock HorizontalAlignment="Right"
                   VerticalAlignment="Center"
                   Text="Chi tiết game"
                   FontSize="12"
                   Foreground="{StaticResource TextMutedBrush}" />
    </Grid>
    <ScrollViewer Grid.Row="1" Margin="0,10,0,0" VerticalScrollBarVisibility="Auto">
        <StackPanel>
            <TextBlock Text="{Binding DisplayName}"
                       FontSize="20"
                       FontWeight="Bold"
                       Foreground="{StaticResource TextStrongBrush}" />
            <TextBlock Margin="0,4,0,10"
                       Text="{Binding StatusText}"
                       Foreground="{StaticResource PrimaryBrush}" />
            <Border Style="{StaticResource Card}" Padding="10" Margin="0,0,0,10">
                <StackPanel>
                    <TextBlock Text="Thư mục save" FontWeight="SemiBold" />
                    <TextBlock Text="{Binding LocalPath}" TextWrapping="Wrap" FontSize="11" />
                    <TextBlock Margin="0,8,0,0" Text="Bản sao Drive" FontWeight="SemiBold" />
                    <TextBlock Text="{Binding LastBackupDisplayText}" FontSize="11" />
                    <TextBlock Text="{Binding ArchiveSizeText}" FontSize="11" />
                    <TextBlock Text="{Binding ArchiveFileIdDisplay}" TextWrapping="Wrap" FontSize="10" />
                    <TextBlock Text="{Binding ArchiveSha256Display}" TextWrapping="Wrap" FontSize="10" />
                </StackPanel>
            </Border>
            <WrapPanel Margin="0,0,0,10">
                <Button Margin="0,0,5,5" Style="{StaticResource CompactButton}"
                        Content="Sao lưu"
                        Command="{Binding DataContext.BackupNowCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                        CommandParameter="{Binding}" />
                <Button Margin="0,0,5,5" Style="{StaticResource CompactButton}"
                        Content="Khôi phục"
                        Command="{Binding DataContext.RestoreCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                        CommandParameter="{Binding}" />
            </WrapPanel>
            <Border Padding="10"
                    Background="{StaticResource DangerSurfaceBrush}"
                    BorderBrush="{StaticResource DangerBrush}"
                    BorderThickness="1"
                    CornerRadius="10">
                <StackPanel>
                    <TextBlock Text="Dữ liệu appDataFolder ẩn của Google Drive"
                               FontWeight="SemiBold"
                               Foreground="{StaticResource DangerBrush}" />
                    <TextBlock Margin="0,4,0,8"
                               Text="Xóa bản sao lưu Drive của game này. File save trên máy không bị xóa."
                               FontSize="11"
                               TextWrapping="Wrap" />
                    <Button Content="Xóa dữ liệu Drive của game này"
                            Style="{StaticResource DangerButton}"
                            Command="{Binding DataContext.DeleteCloudDataCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</Grid>
```

- [ ] **Step 6: Keep add/edit click handlers compiling**

If overview card actions no longer call `EditGame_Click`, keep the existing code-behind methods unchanged. If the XAML compiler reports an unused handler, no change is required because unused handlers do not fail compilation.

- [ ] **Step 7: Run UI markup tests and build App project**

Run: `rtk dotnet test tests/AutoSaveGame.App.Tests --filter VietnameseCompactUiTests`

Expected: PASS.

Run: `rtk dotnet build src/AutoSaveGame.App/AutoSaveGame.App.csproj`

Expected: PASS.

- [ ] **Step 8: Commit Task 4**

Run:

```bash
git add src/AutoSaveGame.App/App.xaml src/AutoSaveGame.App/MainWindow.xaml src/AutoSaveGame.App/MainWindow.xaml.cs tests/AutoSaveGame.App.Tests/Views
git commit -m "feat: build glass game cards"
```

---

### Task 5: End-to-End Verification and Polish

**Files:**
- Modify only files from Tasks 1-4 if verification finds a concrete defect.

**Interfaces:**
- Consumes: all previous task outputs.
- Produces: verified feature with passing tests and a working WPF build.

- [ ] **Step 1: Run full test suite**

Run: `rtk dotnet test AutoSaveGame.sln`

Expected: PASS.

- [ ] **Step 2: Run release build**

Run: `rtk dotnet build AutoSaveGame.sln -c Release`

Expected: PASS.

- [ ] **Step 3: Verify no application-owned English copy was introduced**

Run: `rtk dotnet test tests/AutoSaveGame.App.Tests --filter VietnameseCompactUiTests`

Expected: PASS and no new primary English labels in `MainWindow.xaml`, `GameEditorDialog.xaml`, or `ErrorDialog.xaml`.

- [ ] **Step 4: Manually verify popup behavior**

Run the app from the build output, sign in with a test Google account, and verify these states:

- overview remains `360 x 480`;
- each game appears as one glass card;
- clicking a card opens detail inside the same popup;
- the detail screen shows local path, archive size, archive file ID, and SHA-256 preview;
- canceling the Drive deletion prompt performs no deletion;
- confirming deletion removes only that game's Drive backup and keeps local save files;
- the card remains visible and shows no cloud backup until a new backup succeeds.

- [ ] **Step 5: Inspect git diff for unrelated changes**

Run: `git diff --stat`

Expected: only files listed in this plan changed, plus any pre-existing unrelated working-tree changes left untouched.

- [ ] **Step 6: Commit verification fixes if any were required**

Run only if Step 1-4 required a code fix:

```bash
git add <fixed-files>
git commit -m "fix: polish per-game Drive deletion UI"
```

---

## Self-Review

- Spec coverage: Tasks 1 and 2 implement selected-game-only `appDataFolder` deletion with commit-before-delete ordering. Task 3 implements card navigation and confirmation gating. Task 4 implements glass cards and the detail screen. Task 5 verifies fixed popup size, Vietnamese copy, and manual deletion behavior.
- Placeholder scan: The plan contains concrete file paths, method signatures, code snippets, commands, and expected results. No `TBD`, `TODO`, or deferred implementation placeholders remain.
- Type consistency: `GameCloudDeleteResult`, `GameCloudDeleteKind`, `DeleteGameCloudDataAsync`, `ConfirmDeleteCloudDataAsync`, `SelectedGame`, `SelectGameCommand`, `BackToOverviewCommand`, and `DeleteCloudDataCommand` are introduced before later tasks consume them.
