# Game deletion and glass UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete a game’s Google Drive appDataFolder backup and catalog entry together, removing its card immediately, while restyling the WPF window as a coherent dark glass interface.

**Architecture:** Add `DeleteGameAndCloudDataAsync` to the application runtime. It owns Drive cleanup and catalog removal under the existing operation gate; the ViewModel confirms, invokes it and deselects the game only after success. The XAML keeps business logic in bindings and applies reusable dark-glass resources.

**Tech Stack:** .NET 10, C#, WPF/XAML, xUnit.

## Global Constraints

- Google Drive `appDataFolder` is cloud storage; never delete the configured local save directory.
- Preserve existing sign-in, add/edit, backup, restore, watch-toggle, progress, error and accessibility bindings.
- The destructive button is labelled `Xóa game và dữ liệu Drive` and requires confirmation.
- On Drive conflict/failure retain the card and selection, using the existing safe error path.
- Follow red-green TDD for production behavior changes.

---

## File structure

- `src/AutoSaveGame.App/Services/IApplicationRuntime.cs`: unified deletion API.
- `src/AutoSaveGame.App/Services/ApplicationRuntime.cs`: gated Drive/catalog/runtime cleanup.
- `src/AutoSaveGame.App/Services/UnavailableApplicationRuntime.cs`: contract implementation.
- `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`: command and selection behavior.
- `src/AutoSaveGame.App/Services/IUserPromptService.cs`, `UserPromptService.cs`: irreversible-operation confirmation text.
- `src/AutoSaveGame.App/App.xaml`, `MainWindow.xaml`: dark-glass resource system and layouts.
- `tests/AutoSaveGame.App.Tests/Services/ApplicationRuntimeTests.cs`, `ViewModels/MainViewModelTests.cs`: runtime and UI-state coverage.

### Task 1: Add the unified runtime deletion operation

**Files:**
- Modify: `tests/AutoSaveGame.App.Tests/Services/ApplicationRuntimeTests.cs`
- Modify: `src/AutoSaveGame.App/Services/IApplicationRuntime.cs`
- Modify: `src/AutoSaveGame.App/Services/ApplicationRuntime.cs`
- Modify: `src/AutoSaveGame.App/Services/UnavailableApplicationRuntime.cs`

**Interfaces:**
- Produces: `Task<GameCloudDeleteResult> DeleteGameAndCloudDataAsync(Guid gameId, CancellationToken cancellationToken)`.
- Consumes: `ICatalogRepository.DeleteGameCloudDataAsync`, catalog save/refresh, watcher and scheduler cleanup.

- [ ] **Step 1: Write failing runtime tests**

```csharp
[Fact]
public async Task DeleteGameAndCloudDataAsync_RemovesArchiveCatalogEntryAndRuntimeCard()
{
    var (runtime, game, catalogs, watcher, scheduler) = CreateSignedInRuntimeWithGame();
    await runtime.DeleteGameAndCloudDataAsync(game.GameId, TestContext.Current.CancellationToken);
    Assert.Equal(game.GameId, catalogs.DeletedGameId);
    Assert.Empty(runtime.Games);
    Assert.Contains(game.GameId, watcher.StoppedGameIds);
    Assert.Contains(game.GameId, scheduler.UnregisteredGameIds);
}

[Fact]
public async Task DeleteGameAndCloudDataAsync_WhenCloudCleanupFails_KeepsRuntimeGame()
{
    var (runtime, game, catalogs, _, _) = CreateSignedInRuntimeWithGame();
    catalogs.DeleteResult = new GameCloudDeleteResult(GameCloudDeleteKind.Failed, null, [], [], "failed");
    await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.DeleteGameAndCloudDataAsync(game.GameId, TestContext.Current.CancellationToken));
    Assert.Single(runtime.Games);
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/AutoSaveGame.App.Tests --filter "FullyQualifiedName~ApplicationRuntimeTests.DeleteGameAndCloudDataAsync"`

Expected: compilation failure because `DeleteGameAndCloudDataAsync` is absent.

- [ ] **Step 3: Implement minimally**

Add the interface method and an unavailable-runtime implementation matching its existing failure behavior. In `ApplicationRuntime`, hold `operationGate`, verify the game exists, call `catalogs.DeleteGameCloudDataAsync`, throw on `Conflict`/`Failed`, then persist a next catalog excluding `gameId` with generation incremented and refresh runtime games through existing cleanup logic. Do not call another method that attempts to reacquire the gate.

- [ ] **Step 4: Verify green**

Run: `dotnet test tests/AutoSaveGame.App.Tests`

Expected: exit code 0.

- [ ] **Step 5: Commit**

Run: `git add src/AutoSaveGame.App/Services/IApplicationRuntime.cs src/AutoSaveGame.App/Services/ApplicationRuntime.cs src/AutoSaveGame.App/Services/UnavailableApplicationRuntime.cs tests/AutoSaveGame.App.Tests/Services/ApplicationRuntimeTests.cs; git commit -m "feat: remove game after Drive data deletion"`

### Task 2: Remove the selected card through the ViewModel command

**Files:**
- Modify: `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`
- Modify: `src/AutoSaveGame.App/Services/IUserPromptService.cs`
- Modify: `src/AutoSaveGame.App/Services/UserPromptService.cs`

**Interfaces:**
- Consumes Task 1’s `DeleteGameAndCloudDataAsync` method.
- Produces existing `DeleteCloudDataCommand`; after a success it returns to the overview with no card.

- [ ] **Step 1: Write failing ViewModel tests**

```csharp
[Fact]
public async Task DeleteCloudDataCommand_AfterConfirmation_RemovesCardAndReturnsToOverview()
{
    var runtime = new FakeRuntime([]) { RuntimeGames = [CreateRuntimeGame()] };
    var sut = new MainViewModel(runtime, new FakePrompts([]) { ConfirmCloudDelete = true });
    await sut.SignInCommand.ExecuteAsync();
    sut.SelectGameCommand.Execute(sut.Games.Single());
    await sut.DeleteCloudDataCommand.ExecuteAsync();
    Assert.Empty(sut.Games);
    Assert.Null(sut.SelectedGame);
    Assert.True(sut.IsOverviewVisible);
}
```

Add a second test configuring `FakeRuntime` to throw: it must assert the original card and selection remain.

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/AutoSaveGame.App.Tests --filter "FullyQualifiedName~MainViewModelTests.DeleteCloudDataCommand"`

Expected: existing behavior leaves the card and the new test fails.

- [ ] **Step 3: Implement minimally**

Call `runtime.DeleteGameAndCloudDataAsync(game.GameId, CancellationToken.None)` in `DeleteCloudDataAsync`; only then set `SelectedGame = null`. Make `FakeRuntime` remove the matching runtime game and raise `GamesChanged`. Change the confirmation title/copy and status text to state that Drive backup plus card/catalog will be deleted but the local save folder is unaffected.

- [ ] **Step 4: Verify green**

Run: `dotnet test AutoSaveGame.sln`

Expected: exit code 0.

- [ ] **Step 5: Commit**

Run: `git add src/AutoSaveGame.App/ViewModels/MainViewModel.cs src/AutoSaveGame.App/Services/IUserPromptService.cs src/AutoSaveGame.App/Services/UserPromptService.cs tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs; git commit -m "feat: remove selected game card with Drive data"`

### Task 3: Establish dark glass tokens and controls

**Files:**
- Modify: `src/AutoSaveGame.App/App.xaml`

**Interfaces:**
- Produces existing resource keys `WindowBackground`, `Card`, `GlassGameCard`, `PrimaryButton`, `DangerButton` and `CompactButton` with new visuals; no ViewModel API changes.

- [ ] **Step 1: Record the XAML build baseline**

Run: `dotnet build src/AutoSaveGame.App/AutoSaveGame.App.csproj --no-restore`

Expected: exit code 0.

- [ ] **Step 2: Implement the token system**

Replace the light palette with navy/purple gradient window background, translucent white-violet surfaces, readable white/lavender text, cyan/purple primary accents and red destructive accents. Retain all resource keys. Give buttons/cards visible hover, keyboard-focus and disabled states, and use restrained shadows/highlight borders on glass surfaces.

- [ ] **Step 3: Verify resource compilation**

Run: `dotnet build src/AutoSaveGame.App/AutoSaveGame.App.csproj --no-restore`

Expected: exit code 0, no markup/resource errors.

- [ ] **Step 4: Commit**

Run: `git add src/AutoSaveGame.App/App.xaml; git commit -m "style: add dark glass design tokens"`

### Task 4: Recompose all window states with the new system

**Files:**
- Modify: `src/AutoSaveGame.App/MainWindow.xaml`

**Interfaces:**
- Consumes Task 3 resource keys and all existing ViewModel bindings.
- Produces consistent sign-in, overview/card, detail, progress, empty/error and footer states.

- [ ] **Step 1: Run the XAML build before editing**

Run: `dotnet build src/AutoSaveGame.App/AutoSaveGame.App.csproj --no-restore`

Expected: exit code 0.

- [ ] **Step 2: Implement the layout**

Replace hard-coded white panels with layered transparent glass borders. Give the header a compact logo/connection treatment; arrange overview cards with status pills, path and backup metadata; group detail information into local-save and Drive sections; retain commands and automation names. The danger panel must use the task-2 text `Xóa game và dữ liệu Drive`. Preserve current keyboard tab flow, live status text, progress binding, visibility bindings and all action command parameters.

- [ ] **Step 3: Verify build and regression tests**

Run: `dotnet build AutoSaveGame.sln --no-restore; dotnet test AutoSaveGame.sln --no-build`

Expected: both commands exit 0.

- [ ] **Step 4: Commit**

Run: `git add src/AutoSaveGame.App/MainWindow.xaml; git commit -m "style: refresh game dashboard with glass layout"`

### Task 5: Final integration verification

**Files:**
- Modify only if verification identifies a defect in one of the files above.

- [ ] **Step 1: Run complete verification**

Run: `git diff --check; dotnet test AutoSaveGame.sln; dotnet build AutoSaveGame.sln --no-restore`

Expected: no whitespace output and both .NET commands exit 0.

- [ ] **Step 2: Perform the manual WPF smoke test**

Start the application with the existing development script. With a disposable game, confirm deletion and verify its card disappears while the local save folder still exists. Cancel once and simulate a Drive failure once; verify the card remains in both cases. Check readable contrast and keyboard focus in sign-in, overview, detail, progress, empty and error states.

- [ ] **Step 3: Commit any scoped verification-only correction**

Run: `git status --short`, then stage only corrected task files and commit with `fix: polish game deletion flow`.
