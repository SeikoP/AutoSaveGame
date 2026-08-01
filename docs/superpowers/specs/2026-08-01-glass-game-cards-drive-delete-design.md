# Glass game cards and per-game Drive deletion design

Date: 2026-08-01
Status: Approved for implementation planning

## Goal

Redesign AutoSaveGame around one visual card per game. Each card should feel modern and lightweight with a glass effect, open into a detailed game view, and let the user delete all Google Drive `appDataFolder` data for that specific game after an explicit confirmation.

## Current context

AutoSaveGame is a .NET 10 WPF tray popup using Google Drive `appDataFolder` through the minimal `drive.appdata` scope. The current popup is already fixed at `360 x 480`, Vietnamese-only, and has basic signed-in/signed-out states, game rows, progress, and an appDataFolder expander. Core storage uses an immutable catalog plus archive objects. Catalog commits are already designed to avoid corrupting the last known-good snapshot.

Google Drive `appDataFolder` is hidden app-specific storage. Users cannot manage these files through the normal Drive UI, so deletion must be exposed through the app and must remain scoped to objects AutoSaveGame can prove belong to the selected game.

## Product shape

### Main overview

The signed-in overview shows one glass card per game. The card is the primary navigation surface and replaces the dense row/action cluster.

Each card shows:

- game display name;
- compact local path;
- watch state badge;
- sync status badge;
- last snapshot time;
- active archive size;
- inline operation progress when that game is backing up or restoring.

Clicking the body of the card opens the game detail view. Small direct actions may remain on the card only if they do not make the layout crowded; destructive Drive deletion is not exposed on the overview card.

### Glass visual style

The visual direction is a restrained glass effect that still fits a compact utility app:

- translucent white or navy-tinted card background;
- thin semi-transparent border;
- soft shadow;
- subtle highlight line near the top edge;
- rounded corners;
- high-contrast text over the glass surface;
- no heavy gradients or animated decoration.

WPF blur/acrylic support can be inconsistent for a tray popup, so the implementation should prefer a performant acrylic-like composition using opacity, layered borders, and shadow. True blur is optional only if it does not harm startup, popup placement, or text legibility.

### Game detail view

The game detail view opens inside the same `360 x 480` popup and includes:

- Back navigation;
- game name and current sync status;
- local save path;
- watch toggle;
- current snapshot time;
- archive size;
- archive file ID;
- archive SHA-256 when available;
- catalog generation;
- last operation status/progress;
- Backup now, Restore, Edit, and Remove local config actions;
- Drive data section for this game.

The Drive data section explains that the data is stored in Google's hidden appDataFolder and is visible only to AutoSaveGame. It shows the active archive and any detected stale/orphan objects that are confidently associated with the game.

## Per-game appDataFolder deletion

The user explicitly wants deletion of all cloud data for a selected game, not only safe orphan cleanup. The app must therefore provide a destructive action named in Vietnamese along the lines of `Xóa dữ liệu Drive của game này`.

The operation deletes Drive data for the selected game only. It does not delete local save files. It does not delete other games. It does not expose arbitrary raw object deletion.

### Required confirmation

Deletion requires a confirmation dialog before any mutation. The dialog must clearly state:

- the game name;
- local saves are not deleted;
- Google Drive cloud backup data for this game will be removed;
- restore from Drive for this game will no longer be possible until a new backup is created;
- this action affects hidden `appDataFolder` storage.

The confirmation should require an explicit destructive confirmation, preferably typing the game display name or pressing a second clearly marked destructive button. A normal single OK dialog is not enough.

### Deletion flow

The safe flow is:

1. Load and verify the current catalog.
2. Find the selected game in the catalog.
3. Build the set of Drive object IDs that belong to that game, including the active archive and any confidently matched stale archive names for the same game ID.
4. If the game has a snapshot in the catalog, commit a new catalog generation with that game's `Snapshot` set to null.
5. Only after the catalog commit succeeds, delete the selected game's archive objects from `appDataFolder`.
6. Treat missing objects as already deleted.
7. If archive deletion partially fails after the catalog update, keep the catalog result and show a retryable cleanup warning for the game.
8. Refresh the game card and detail view.

This ordering prevents a failed catalog commit from removing the user's only catalog reference to a cloud snapshot.

### Catalog and storage rules

The deletion feature belongs in Core/Application services, not directly in the WPF view. The UI calls a runtime method such as `DeleteGameCloudDataAsync(gameId, cancellationToken)`. Core owns catalog mutation and cloud-object deletion semantics.

The resulting catalog should keep the game configuration but remove the cloud snapshot. That means the card remains visible, but its Drive state becomes `Chưa có bản sao trên Drive` until the next backup succeeds.

If the user wants to remove the game from AutoSaveGame entirely, the existing remove-local-config action remains separate.

## Error handling

- Signed out: disable the Drive deletion action and ask the user to sign in.
- Catalog conflict: do not delete archives; ask the user to refresh/sign in again.
- Catalog corrupt: do not delete archives; show a safe error.
- Network failure before catalog commit: no archive deletion happens.
- Network failure after catalog commit: show partial cleanup warning and allow retry.
- Auth failure: mark the session disconnected and offer sign-in again.
- Missing archive: treat as success for that object and continue.

## Testing

Add focused tests for:

- card view-model state for glass-card overview;
- card click/navigation to game detail;
- detail view exposes per-game Drive metadata;
- deletion confirmation is required before runtime mutation;
- canceling confirmation does not call deletion;
- confirmed deletion calls the runtime for the selected game only;
- Core deletion clears only the selected game's snapshot;
- Core deletion does not delete archive objects until after catalog commit succeeds;
- partial post-commit cleanup failure leaves the snapshot cleared and reports retryable cleanup;
- other games and their archives are preserved.

Existing app tests should continue to assert Vietnamese user-facing copy and fixed `360 x 480` popup behavior.

## Acceptance criteria

- The signed-in overview uses one glass card per game.
- Clicking a card opens a full detail screen in the same popup.
- The detail screen shows the selected game's local and Drive information.
- The user can delete all Google Drive data for that specific game only after explicit confirmation.
- Deleting cloud data clears that game's snapshot from the catalog and removes its Drive archive objects without touching local saves or other games.
- Failed or canceled deletion never leaves other games corrupted.
- The UI remains Vietnamese-only and usable within `360 x 480`.
- Unit tests cover view-model navigation, confirmation gating, and catalog/cloud deletion semantics.

## Non-goals

- Deleting local save files.
- Deleting all AutoSaveGame appDataFolder data across every game.
- Exposing a raw Drive file manager.
- Supporting non-Google cloud providers.
- Adding multiple historical snapshots per game.
