# World Selection — New World / Load World, Singleplayer and Multiplayer

Date: 2026-08-15
Status: approved, ready for planning

## Problem

The save system already supports many named slots — `SaveSlots` enumerates arbitrary
`*.json` files newest-first, `SaveSlots.Sanitize` exists specifically to accept
player-typed names, `SaveManager.Save(slotId, label)` takes any id, and
`StageLoad(slotId)` hands a document across the scene load via a static.

None of it is reachable. Every caller passes one of two hardcoded ids, `"autosave"` or
`"quicksave"`. There is no world name, no world identity, no world-select screen, and no
way to start a second world without editing a scene.

The goal is the Minecraft model: the player picks **New World** or an existing world from
a list, and that choice applies whether they then play alone or open the world to others.
The person creating the lobby picks the world; joining clients bring nothing.

## What this is not

The world is authored terrain sliced into 48 chunk scenes by `WorldChunkerEditor`, and a
save is only the *delta* over those baked scenes. **"New World" does not generate terrain.**
There is no seed. A new world is a fresh empty delta over the one shipping world.

## Decisions

| # | Decision | Chosen |
|---|---|---|
| 1 | World identity | **Slot id is the world.** One flat file per world under `Saves/<name>.json`. Reuses `SaveSlots` unchanged. |
| 2 | When the host picks | **Before the lobby opens.** One world-select screen serves both Singleplayer and Multiplayer. |
| 3 | How the choice travels | **A single `WorldSession` static**, replacing `PendingLoad`/`StageLoad`/`ClearStagedLoad`. |
| 4 | What New World offers | **One world type, with a `WorldConfigId` guard** recorded in the header and validated on load. |

### Why 1 (flat file per world)

Per-world folders (`Saves/<id>/world.json`) would be more Minecraft-like and leave room
for screenshots and region files, but `SaveSlots` would be rewritten to buy structure
nothing uses yet. The flat layout costs one field and no rewrite.

Consequence worth naming: quicksave today writes a **global** `quicksave.json`. With two
worlds that silently crosses them — F5 in world B then F9 in world A loads B's state into
A. Decision 3 fixes this by making the active world the default target for every save
path, so F5/F9 become per-world automatically.

### Why 2 (pick before the lobby)

`LobbyMenu.unity` binds its buttons to methods **by string name** (`LobbySystem.StartLobbyGame`,
`createLobbyWithGivenOptions`, and six others), pinned by `LobbyMenuWiringTests`. Embedding a
world picker there means editing that fragile surface. Picking first means `BeginGameAsync`
needs no new argument and the lobby scene is untouched.

Matches Minecraft: you pick the world, *then* open it to LAN.

### Why 3 (one `WorldSession` static)

Today "new game" is expressed as the *absence* of a staged document — `StartSinglePlayer()`
calls `ClearStagedLoad()`, and every entry point must independently know that convention.
That is a two-valued flag pretending to be a null check.

`WorldSession` makes new-vs-saved an explicit value and gives the world a name and a config
id to travel with. It **replaces** the old statics rather than sitting beside them, so net
line count is roughly flat. Only 5 call sites outside `SaveManager` touch the old API, so
the migration is cheap and total.

### Why 4 (one world type + a guard)

There are two `WorldStreamingConfig` assets: the shipping 8×6 grid and Ferdinand's 4×2 test
grid, each wired into its own persistent scene. Offering both as world types would require
`MainMenuUI.gameScene` to become per-config, and `MapService`/`MapHologramTerrain` hardcode
the main config's GUID — the hologram map would lie on the second world. That cleanup is
separate work.

Recording `WorldConfigId` anyway costs one field and is the thing that would be regretted
later: without it, a save from world 2 hydrates chunk deltas onto world 1's grid and
scatters objects into wrong scenes. The guard refuses with a readable error instead.

## Architecture

### 1. `WorldSession` — new, `Core/Persistence/Runtime/WorldSession.cs`

The single answer to "which world are we in".

```csharp
public static class WorldSession
{
    public static string WorldId       { get; }  // sanitized slot id == file name
    public static string DisplayName   { get; }  // what the player typed
    public static string WorldConfigId { get; }  // which WorldStreamingConfig this world belongs to
    public static bool   IsNew         { get; }
    public static bool   IsActive      { get; }  // false before any world is chosen

    public static void StageNew(string displayName, WorldStreamingConfig config);
    public static bool StageExisting(string worldId, WorldStreamingConfig config, out string error);
    public static SaveDocument Consume();        // SaveManager.Awake() calls this; null when IsNew
    public static void Clear();
}
```

`StageExisting` takes the config it is being loaded *into* so it can run the guard; the caller
supplies it because `WorldSession` must not depend on how a config is found (the menu has a
serialized reference, `SaveHotkeys` asks the live `WorldStreamer`).

**Assembly split.** `SpaceGame.Persistence` declares `"references": []` and cannot see
`Assembly-CSharp` or `SpaceGame.World.Streaming`. So the rules that must be unit-testable live
in a pure `WorldIdentity` class inside the Format assembly, and `WorldSession` — which needs
`SaveDocument` staging and a `WorldStreamingConfig` — sits in `Assembly-CSharp` and wraps it.

`StageExisting` reads the file through `SaveFileStore`, validates `header.WorldConfigId`,
and fails with a readable error on mismatch rather than hydrating the wrong grid.

**A config's id is its asset GUID**, read via `AssetDatabase.AssetPathToGUID` at author time
and stored on the config asset itself as a serialized string field, so the runtime never
needs `AssetDatabase`. GUIDs are stable across renames, which asset names are not.

A save written before this change has an empty config id and is accepted as belonging to
the main world.

Removed in the same commit: `SaveManager.PendingLoad`, `StageLoad`, `ClearStagedLoad`.

### 2. `SaveHeader` — two new fields

```csharp
[JsonProperty("worldName")]     public string WorldName = string.Empty;
[JsonProperty("worldConfigId")] public string WorldConfigId = string.Empty;
```

Format stays at **v1**. Both fields read as empty on existing files, absorbed by the same
tolerance that lets savers come and go, so `SaveMigrator`'s ladder stays empty and the
existing format tests keep passing.

### 3. `SaveManager` — default to the active world

`Save()`'s slot id defaults to `WorldSession.WorldId` instead of `SaveSlots.AutoSaveSlotId`.
Autosave, quit-save and quicksave then all land in the active world's file with no
per-caller change. `BuildDocument` stamps `WorldName` and `WorldConfigId` from `WorldSession`.

**Quicksave becomes per-world and stops being a separate file.** F5 writes the active
world's own file; F9 restages that same world. There is no global `quicksave.json` once a
world is active — that file is exactly the cross-world hazard decision 1 identified.

`SaveSlots.AutoSaveSlotId` / `QuickSaveSlotId` survive only as the fallback for
**no world active** — a world scene opened directly in the editor, with no menu run and
therefore no `WorldSession`. In that case saves land in `autosave.json` as they do today,
so existing editor workflows keep working. Once a world is active the constants are never
used.

### 4. `WorldSelectUI` — new

Reached from both **Singleplayer** and **Multiplayer** on the main menu.

- Lists `SaveSlots.List()` (already returns newest-first with parsed headers), showing world
  name, last-played time, and an unreadable marker for corrupt files.
- **New World**: name entry → `WorldSession.StageNew` → enter.
- **Load**: row select → `WorldSession.StageExisting` → enter; stays on screen and shows the
  error when the slot cannot be read.
- **Delete**: wires `SaveSlots.Delete`, which currently has no runtime caller, behind a
  confirm.
- On confirm, singleplayer enters the world directly; multiplayer loads `LobbyMenu` with the
  choice already staged.

### 5. `MainMenuUI` — routed through the world select

`StartSinglePlayer()` and `StartMultiPlayer()` both open `WorldSelectUI` with the
destination as their argument. `ContinueGame()` keeps working as a shortcut: most-recent
slot → `StageExisting` → enter.

Folded-in fix: `EnterWorld()` calls a bare `NetworkManager.Singleton.StartHost()` without
touching the transport, while `SessionLauncher` exists precisely because UnityTransport
retains its last settings. Starting singleplayer after a failed Relay attempt in the same
process hosts on stale Relay data. Routed through `SessionLauncher.HostDirect()`.

## Deliberately unchanged

`LobbyMenu.unity` and its string-bound UnityEvents, `LobbySystem`, `LobbySession`,
`SessionLauncher`'s Relay/join flow, `WorldSaveStore`, all six `ISaveable` adapters, and
the format layer beyond the two header fields.

Joining clients stage nothing. The host is the only machine that saves — `Save()` already
refuses when `Network.IsNetworked && !Network.Server` — and Netcode's scene sync pulls
clients into whatever the host loaded. A guest's own local worlds are irrelevant while
they are a guest.

## Verification (hard requirement)

Saving, loading and new-world must be **demonstrated working**, not merely compiled.

### Tier 1 — headless, via `HeadlessTestRunner` (EditMode, `Temp/headless_tests.txt`)

New fixture `WorldSessionTests`, in the style of the existing format tests (temp directory,
real `SaveFileStore`, no Unity runtime):

1. **Round trip** — stage new world "Alpha" → save → read back → header carries name and
   config id → `StageExisting("Alpha")` returns the same world state.
2. **Per-world isolation** — save worlds "Alpha" and "Beta" with different state; loading
   Alpha never yields Beta's data; two files exist on disk.
3. **New world is empty** — `StageNew` over an existing name does not inherit that world's
   delta.
4. **Config guard** — a document whose `WorldConfigId` names an absent config fails
   `StageExisting` with a non-empty error and stages nothing.
5. **Legacy tolerance** — a v1 file with no `worldName`/`worldConfigId` still loads.
6. **Name sanitisation** — a typed name containing path separators cannot escape the save
   root.
7. **Quicksave is per-world** — F5 in world B then F9 in world A does not load B's state.

Plus `WorldSelectWiringTests` in the `LobbyMenuWiringTests` style, asserting the new screen's
buttons resolve to the methods they name.

### Tier 2 — needs a live editor, reported honestly

Clicking through the menu into a world, and the host-with-a-saved-world multiplayer path,
need a focused editor and a Play session. The EditMode runner cannot be launched over the
MCP bridge while the editor is unfocused. These will be written up as a manual test plan
and run if the editor can be driven; if a run cannot be observed, that will be stated
plainly rather than reported as green.

## Out of scope

- Multiple world *types* / selectable `WorldStreamingConfig` (needs the hardcoded
  `MapService` / `MapHologramTerrain` config GUIDs fixed first).
- World seeds and runtime world generation (the world is authored, not generated).
- Per-world folders, screenshots, world thumbnails.
- In-game pause-menu Save button and a save-slot browser during play.
- Rebuilding `LobbyMenu.unity` to the tabbed `LobbyMenuBuilder` layout (which would also
  restore the unreachable Direct Connect tab).
