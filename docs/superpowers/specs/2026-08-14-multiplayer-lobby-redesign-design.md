# Multiplayer Lobby Redesign — Design

Date: 2026-08-14
Branch: `Feat/robotics-and-minigame`

## Problem

Opening the Multiplayer page shows two unrelated popups: a self-installing IMGUI box
with the local IP, and the authored lobby canvas. Beyond the cosmetics, the session
cannot outlive the menu, so "start a game and let friends join later" is impossible.

Three concrete defects:

1. **Two popups.** `DirectConnectPanel` installs itself into any scene containing a
   `LobbySystem` via `RuntimeInitializeOnLoadMethod`, so it appears beside the authored
   `LobbyMenu.unity` canvas with no scene wiring and no way for a designer to see it
   coming.
2. **Late join is impossible by construction.** `LobbySystem.StartLobbyGame` sets
   `IsLocked = true` before handing off, and `LobbySystem` lives only in
   `LobbyMenu.unity`, so `LoadScene(Single)` destroys it and the 15s heartbeat with it.
   The Lobby service delists an un-heartbeated lobby after 30s.
3. **The menu is a pile of independently toggled panels.** `LobbyListPanel`,
   `CreateLobbyPanel`, `JoinPrivateLobbyPanel`, `JoinLobbyByPasswordPanel`, `LobbyPanel`,
   `LobbyScreen` and `WarningPanel` are shown and hidden by button arrays on
   `OpenCloseUIElement`, with no single owner of "which screen am I on".

## Preservation rule

Every multiplayer file authored before July 2026 is kept and built on, never replaced.
Dating each file against its introducing commit:

**Pre-July — kept** (commits `19ec27ed` 2026-01-26 … `934e21c0` 2026-02-22)

| File | Added |
|---|---|
| `LobbySystem.cs` | 2026-01-26 |
| `LobbyListSystem.cs` | 2026-01-26 |
| `LobbyElementController.cs` | 2026-01-26 |
| `OpenCloseUIElement.cs` | 2026-01-26 |
| `JoinLobbyByPasswordButton.cs` | 2026-01-26 |
| `LobbyMenu.unity` | 2026-01-26 |
| `NetworkGameManager.cs` | 2026-02-18 |
| `JoinLobbyByCodeController.cs` | 2026-02-22 |
| `LobbyUIManager.cs` | 2026-02-22 |
| `LobbyWarningSystem.cs` | 2026-02-22 |

`LobbySystem.cs` originates 2026-01-26 but was rewritten in August (280 → 667 lines).
By the rule it is kept; its August documentation is accurate and is retained.

**Post-July — free to change or delete** (all `77bc1a33`, 2026-08-14)

`SessionLauncher.cs`, `NetworkBootstrap.cs`, `DirectConnectPanel.cs`, `PlayerIdentity.cs`

**Deleted** (explicit ruling, overriding the pre-July rule because both are dead):

- `StartingGameManager.cs` — a public static that hardcodes
  `LoadScene("Tommy test scene")` and duplicates `LobbySystem.StartLobbyGame`. Zero call
  sites. An active hazard sitting beside the real start path.
- `Entity.cs` — a health stub with empty `Start`/`Update`/`die()`, unrelated to lobbies,
  filed under `LobbyMenu/Core/`. Zero call sites.

## Architecture

| Layer | File | Status |
|---|---|---|
| Transport | `Core/Multiplayer/SessionLauncher.cs` | post-July, kept & verified |
| Session state | `Core/Multiplayer/LobbySession.cs` | **new**, persistent |
| Menu controller | `UI/LobbyMenu/Core/LobbySystem.cs` | pre-July, kept, narrowed |
| View | `UI/LobbyMenu/**` (5 files) | pre-July, kept, hardened |
| Direct connect view | `UI/LobbyMenu/Join/DirectConnectController.cs` | **new** |
| World | `Core/Multiplayer/NetworkGameManager.cs` | pre-July, kept |

**The invariant:** `LobbySession` is the only owner of lobby state. Everything above it
is disposable view that may be destroyed by a scene load at any time.

### `LobbySession` — new, persistent

`SpaceGame.Core`, `DontDestroyOnLoad` singleton, no `UnityEngine.UI` dependency.

```csharp
public enum LobbyState { Idle, InLobby, InGame }

public class LobbySession : MonoBehaviour
{
    public static LobbySession Instance { get; }
    public LobbyState State { get; }
    public Lobby Current { get; }
    public bool IsHost { get; }
    public event Action Changed;          // roster / state / code moved
    public event Action<string> Failed;   // message fit to show a player

    public Task<bool> CreateAsync(string name, bool isPrivate, string password);
    public Task<List<Lobby>> QueryAsync();
    public Task<bool> JoinByIdAsync(string id);
    public Task<bool> JoinByCodeAsync(string code, string password = null);
    public Task LeaveAsync();
    public Task<bool> BeginGameAsync(string sceneName);
}
```

The heartbeat (15s, host only), poll (2s), per-call in-flight guards and the `busy`
one-operation-at-a-time guard move here **verbatim** from `LobbySystem`. That logic is
already correct and carries the reasoning for each interval; it is relocated, not
rewritten.

Two behavioural changes:

- `BeginGameAsync` does **not** set `IsLocked = true`. It writes a public lobby data key
  `State = "in-game"` instead, so the browser can label the row while joins still succeed.
- Heartbeat and poll keep running after the world loads, because the object persists.

### `LobbySystem` — kept, narrowed from 667 to ~150 lines

Every public member the scene binds **by string** through `UnityEvent` keeps its exact
name. UnityEvent resolves targets by name at runtime and silently drops any it cannot
find, so a rename here is a silent no-op button:

`createLobbyWithGivenOptions`, `listLobbies`, `JoinLobbyById`, `JoinLobbyByCode`,
`JoinLobbyByPassword`, `LeaveLobby`, `StartLobbyGame`, `GameSceneName`

Each becomes a thin call into `LobbySession` plus a view update. No UGS logic is lost.

One fix: the player name is read from `GameSettings.PlayerName` instead of
`"Player" + UnityEngine.Random.Range(10, 99)`, so the lobby roster and the in-game
`PlayerIdentity` roster show the same names.

### Late-join flow

```
HOST                                    LATE JOINER
Create ──► lobby created, UNLOCKED
Start  ──► State = "in-game"            Browse ──► "Ferdinand's game  1/4 · in game"
       ──► LoadScene(world)             Join   ──► SessionLauncher.JoinRelayAsync
       ──► heartbeat KEEPS RUNNING             ──► Netcode syncs into host's scenes
       ──► plays alone                         ──► server OnClientConnectedCallback
                                               ──► NetworkGameManager spawns player
```

This rests on `NetworkManager.prefab` carrying `EnableSceneManagement: 1` (verified), so
Netcode synchronizes a late client into whatever scenes the host has loaded.
`ConnectionApproval: 0` and `PlayerPrefab: 0` mean spawning is manual, which is why
`NetworkGameManager` iterates `ConnectedClientsIds` itself — already late-join capable.

On the joining client, `LobbySystem` reads the lobby's `State` key:
`in-game` → `LoadingScreenUI.ShowUntilReady(...)`; `lobby` → the lobby screen as today.

### Direct connect

`DirectConnectPanel.cs` (IMGUI, self-installing, post-July) is deleted. A new
`DirectConnectController` (~50 lines) is wired to a `Direct` tab in the rebuilt scene and
calls the existing `SessionLauncher.HostDirect` / `JoinDirectAsync`. The capability is
kept because it is the only path that works when Relay or Lobby is down, blocked, or
unconfigured — and a Relay misconfiguration hangs rather than errors, so without it there
is no way to tell the two apart.

## Scene rebuild

`LobbyMenu.unity` is rebuilt through the Unity MCP bridge in the `UITheme` visual
language (procedurally generated rounded panels, accent colours, no new art assets). One
screen with four tabs replaces seven independently toggled panels.

```
┌──────────────────────────────────────────────────────────┐
│ MULTIPLAYER                                     [ Back ] │
│ ┌ Browse ┬ Create ┬ Join by code ┬ Direct ┐              │
│ └────────┴────────┴──────────────┴────────┘              │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Ferdinand's game        2/4   in game        [Join]  │ │
│ │ Emil's lobby            1/4   waiting        [Join]  │ │
│ └──────────────────────────────────────────────────────┘ │
│                                            [ Refresh ]   │
└──────────────────────────────────────────────────────────┘

in-lobby:
│ FERDINAND'S GAME                                         │
│ Code  ABC123  [copy]                            2 / 4    │
│ ● Ferdinand   host                                       │
│ ● Emil                                                   │
│ [ Start Game ]  (host only)                   [ Leave ]  │
```

Errors render in an inline status strip rather than a modal panel.
`LobbyWarningSystem.warn(string)` keeps its signature and points at that strip, so the
pre-July error path is preserved.

Tab switching keeps using the pre-July `OpenCloseUIElement` + `LobbyUIManager` pair: each
tab body is an `OpenCloseUIElement` whose open button is its tab and whose close buttons
are the other three tabs.

## Hardening the pre-July view (verified, not replaced)

- `LobbyListSystem.openLobbyScreen` reaches the lobby title and code via
  `lobbyScreen.GetChild(0)` / `GetChild(1)`. Replaced with serialized fields. The same
  file's `FindPlayerListContainer` already documents why index-walking is a bug —
  reordering children in the inspector turns it into an exception twice a second.
- The scene guard `if (SceneManager.GetActiveScene().name != "LobbyMenu") return;`
  already present on `showPlayerElements` and `setStartGameButtonState` is extended to
  `openLobbyScreen`, `hideLobbyScreen`, `clearPrevList` and `listNewLobby`, so a
  persisted session cannot NRE on a destroyed canvas.
- `LobbyElementController.setMaxPlayers` hardcodes `"0/"` and its `lobbyCodeUI` field is
  unused. Wire real occupancy and the waiting/in-game state.

## Error handling

Every failure path already returns a player-facing string rather than throwing —
`SessionResult` carries `Success` + `Error`, and `LobbySystem` wraps every `async void`
in a catch because an exception escaping one is swallowed with no stack trace. That
contract is preserved and extended to `LobbySession`, which raises `Failed(string)`
rather than throwing across the session/view boundary.

Disconnect handling stays where it is: `OnClientDisconnectCallback` on any disconnect —
not only a clean host shutdown — reports the reason and forgets the lobby locally.

## Testing

Existing: `SessionLauncherTests.cs`, `NetworkPrefabRegistrationTests.cs`.

Added:
- EditMode tests for `LobbySession` state transitions (`Idle → InLobby → InGame`, and
  every failure returning to a consistent state).
- A test asserting `BeginGameAsync` never sets `IsLocked`, since that single line is what
  made late join impossible and it would be easy to reintroduce.
- A test asserting `LobbySystem`'s string-bound public method names still exist, because
  UnityEvent cannot catch their removal at compile time.

Run headless via `HeadlessTestRunner` over the MCP bridge. Per the repo's headless
verification notes: delete the results file and run the suite twice, or stale results
from the previous assembly are read back.

Play verification: host in the editor, join from a build, confirm the late client spawns
a player in the running world.

## Out of scope

- Networking the systems that are currently unnetworked (AI, vehicles, creatures,
  backpack, most weapons). The lobby cannot fix those and they are tracked separately.
- Steam or platform-native invites.
- Persisting a session across an application restart.
