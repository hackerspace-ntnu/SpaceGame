---
system: GameModes
layer: presentation
summary: Versus team PvP in the streamed world, the three-gamemode bot arena, and the plain story run
paths:
  - Assets/Game/Scripts/Gameplay/Versus/
  - Assets/Game/Scripts/Gameplay/Minigame/
  - Assets/Game/Scripts/Gameplay/Game/
  - Assets/Game/Scripts/Gameplay/Arrival/
  - Assets/Game/ScriptableObjects/Versus/VersusShipSpawnConfig.asset
symptoms:
  - "starting a deathmatch drops me into an empty arena with no bots and no spawns"
  - "the team ship spawns for the host and nobody else can see it"
  - "a last-standing match never ends even though everyone is dead"
  - "players land inside the wrong team's ship or on top of each other"
  - "the host can pick 8 teams of 12 in a 24-seat lobby"
  - "the leaderboard counts a kill twice on the host"
  - "the second match starts on the previous match's spawn ring"
  - "bots on opposite teams refuse to fight each other"
reads_with: [Multiplayer, Lobby, PlayerShip, Persistence]
updated: 2026-09-01
---

# Game Modes

Two unrelated match families — **Versus** (team PvP in the streamed world, everyone starts in a team ship) and the **Minigame arena** (bot deathmatch with three gamemodes off one `MatchManager`) — plus the plain story run.

**Scope:** [Assets/Game/Scripts/Gameplay/Versus/](Assets/Game/Scripts/Gameplay/Versus), [Gameplay/Minigame/](Assets/Game/Scripts/Gameplay/Minigame), [Gameplay/Game/](Assets/Game/Scripts/Gameplay/Game), [Gameplay/Arrival/](Assets/Game/Scripts/Gameplay/Arrival)
**Related:** [Multiplayer.md](Multiplayer.md) · [Lobby.md](Lobby.md) · [Persistence.md](Persistence.md) · [PlayerShip.md](PlayerShip.md) · [AgentSystem.md](AgentSystem.md) · [NavMeshSystem.md](NavMeshSystem.md)

## Model

- A mode is carried across the scene load by **statics**, because the lobby/menu that chose it is destroyed by that very load: [`VersusSession`](Assets/Game/Scripts/Gameplay/Versus/Core/VersusSession.cs), [`VersusShipSpawns`](Assets/Game/Scripts/Gameplay/Versus/Core/VersusShipSpawns.cs), [`VersusTeamRoster`](Assets/Game/Scripts/Gameplay/Versus/Core/VersusTeamRoster.cs), [`MatchSettings`](Assets/Game/Scripts/Gameplay/Minigame/Runtime/MatchSettings.cs). All of them have a `Clear`/`ResetToDefaults` that every exit route must hit.
- `Core/` files are Unity-free and live in their own asmdefs so EditMode tests reach them; `Runtime/` siblings hold the MonoBehaviours/NetworkBehaviours.
- Everything decisive is **server-side**. The only replicated per-player mode state is `PlayerIdentity.Team` (server-write); the leaderboard is pushed wholesale by RPC.
- Two spawn paths: VS resolves a seat inside its team's ship via [`VersusShipSpawner`](Assets/Game/Scripts/Gameplay/Versus/Runtime/VersusShipSpawner.cs); everything else goes through [`SpawnManager`](Assets/Game/Scripts/Gameplay/Game/Spawning/SpawnManager.cs) + [`SpawnPoint`](Assets/Game/Scripts/Gameplay/Game/Spawning/SpawnPoint.cs). `MatchManager` collects its own spawn points, scene-scoped, and does not use `SpawnManager`.
- [`Game.Mode`](Assets/Game/Scripts/Gameplay/Game/State/Game.cs) (`Singleplayer`/`Multiplayer`) and [`GameManager`](Assets/Game/Scripts/Gameplay/Game/State/GameManager.cs) belong to the **story run** (timer + `WinGame` → win scene), not to VS or the arena.
- Team identity is one integer everywhere: index into `VersusRules.Names`, into the team colour array, and into the ship layout.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `MatchManager` | [Minigame/Runtime/MatchManager.cs](Assets/Game/Scripts/Gameplay/Minigame/Runtime/MatchManager.cs) | Server orchestrator for all 3 arena gamemodes: bot spawn, factions, kills/lives, win check, respawn, leaderboard |
| `MatchSettings` | [Minigame/Runtime/MatchSettings.cs](Assets/Game/Scripts/Gameplay/Minigame/Runtime/MatchSettings.cs) | Host-side static config written by `MinigameConfigUI`, read once in `OnNetworkSpawn` |
| `MatchRules` | [Minigame/Core/MatchRules.cs](Assets/Game/Scripts/Gameplay/Minigame/Core/MatchRules.cs) | Caps + `ResolveCondition`/`ResolveLives`/`RespawnsEnabled` |
| `MatchWinEvaluator` | [Minigame/Core/MatchWinEvaluator.cs](Assets/Game/Scripts/Gameplay/Minigame/Core/MatchWinEvaluator.cs) | Pure win eval: kill target, lives exhausted, last standing → team index or null |
| `SpawnReachability` | [Minigame/Core/SpawnReachability.cs](Assets/Game/Scripts/Gameplay/Minigame/Core/SpawnReachability.cs) | Union-find over NavMesh pathability; keeps largest connected spawn group |
| `TeamAssignment` | [Minigame/Core/TeamAssignment.cs](Assets/Game/Scripts/Gameplay/Minigame/Core/TeamAssignment.cs) | Splits shuffled spawn list into per-team blocks |
| `MatchScoreEntry` | [Minigame/Runtime/MatchScoreEntry.cs](Assets/Game/Scripts/Gameplay/Minigame/Runtime/MatchScoreEntry.cs) | `INetworkSerializable` leaderboard row (name/kills/deaths/team/clientId) |
| `VersusRules` | [Versus/Core/VersusRules.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusRules.cs) | Seat arithmetic: 2–8 teams, 1–12 size, `MaxSeats = 24`, team names, coupled clamps |
| `VersusSession` | [Versus/Core/VersusSession.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusSession.cs) | Local peer's match: `IsActive`, `TeamCount`, `TeamSize`, `LocalTeam`, `ColorOf` |
| `VersusTeamRoster` | [Versus/Core/VersusTeamRoster.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusTeamRoster.cs) | Server map clientId→team; `Claim` (lobby choice) outranks `Assign` (fill emptiest) |
| `TeamColorRules` | [Versus/Core/TeamColorRules.cs](Assets/Game/Scripts/Gameplay/Versus/Core/TeamColorRules.cs) | Swatch stepping that skips colours other teams wear; `DefaultColors` spread |
| `VersusShipSpawnConfig` | [Versus/Core/VersusShipSpawnConfig.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusShipSpawnConfig.cs) | Per-arena asset: Ring (centre+radius) or Explicit points, probe height, seat ring |
| `VersusShipSpawns` | [Versus/Core/VersusShipSpawns.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusShipSpawns.cs) | Runtime override static that wins over the asset |
| `ShipSpawnLayout` | [Versus/Core/ShipSpawnLayout.cs](Assets/Game/Scripts/Gameplay/Versus/Core/ShipSpawnLayout.cs) | `Ring`, `SeatRing`, `TryPointForTeam`, `TryValidateExplicit` |
| `VersusShipSpawner` | [Versus/Runtime/VersusShipSpawner.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/VersusShipSpawner.cs) + [.Seats.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/VersusShipSpawner.Seats.cs) | One ship per team via `GameServices.World.Spawn`, team livery, `TryClaimSeat` |
| `ShipGrounding` / `ShipSeat` | [Versus/Runtime/](Assets/Game/Scripts/Gameplay/Versus/Runtime) | Heightmap-first ground probe; seat markers (ordered, component not name) |
| `RankLayout` | [Versus/Core/RankLayout.cs](Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs) | Lobby rank geometry: seat spacing, 4-wide wrap, team gap, camera pull-back |
| `PlayerIdentity` | [Core/Multiplayer/Players/PlayerIdentity.cs](Assets/Game/Scripts/Core/Multiplayer/Players/PlayerIdentity.cs) | `Team` NetworkVariable (server-write, `-1` = no team); name/suit are owner-write |
| `NetworkGameManager.Versus` | [Joining/NetworkGameManager.Versus.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.Versus.cs) | Adopts the session from the lobby, `SpawnIntoTeamShip`, `PublishTeam` |

## Modes

| Mode | Files | Rules |
| --- | --- | --- |
| Versus (PvP world) | `Versus/**`, `Arrival/**`, `NetworkGameManager.Versus.cs` | 2–8 teams × 1–12, product ≤ 24 seats. One identical ship per team on a ring. No scoring, no win condition, no end — the mode ends when people leave. |
| Team Deathmatch | `MatchManager`, `MatchRules` | 2 teams sharing the first two `teamFactions`; ≤4 bots per side. Host picks `KillTarget` / `LivesPerPlayer` / `LastStanding`. Host is always ally team; later joiners fill the thinner side. |
| Free-For-All | same | Every entity its own team index + its own solo faction (16 in the pool, ≤15 bots). Lives 1–10; collapses to `LastStanding` when lives == 1, else `LivesPerPlayer`. |
| Battle Royale | same | FFA with lives forced to 1, condition forced to `LastStanding`, no respawns; the lives control is hidden. |
| Story / singleplayer | `Gameplay/Game/State/**` | Host of one; `GameManager.GameTimer` + `WinGame()` → `onWinScene` through Netcode's scene manager. |

## Flows

**Start a VS match** — 1. `MainMenuUI.HostVersus` → `VersusRulesUI` stages teams/size (statics) → lobby. 2. Lobby writes team count/size/colours/per-player team into Unity Lobby data ([`LobbyTeams`](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/LobbyTeams.cs), [`VersusSetup`](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/VersusSetup.cs)). 3. On load, **every** peer runs `AdoptVersusSessionFromLobby()` in `NetworkGameManager.OnNetworkSpawn` → `VersusSession.Begin` (or `Clear`). 4. Client sends `ReportVersusTeamServerRpc`; server `VersusTeamRoster.Claim`s it (index validated against `TeamCount`). 5. `SpawnWhenReady` sees `VersusSession.IsActive` + a `VersusShipSpawner.Instance` → `SpawnIntoTeamShip`: wait for team → preload chunks around **every** team anchor → `ArrivalDirector.SpawnIntoVersusArrival` (whole formation or nothing) or fall back to `TryClaimSeat` → `SpawnManager.SpawnPlayerForClient(pos, rot)` → `PublishTeam` writes `PlayerIdentity.SetTeam`.

**Start an arena match** — 1. `MainMenuUI.StartMinigame` → `MinigameConfigUI` (resets `MatchSettings`, writes mode/counts/condition, `ClampToLimits`). 2. `MainMenuUI.LaunchMinigame`: sets `NetworkGameManager.PendingSceneNameToWaitFor = minigameScene`, `StartHost()`, loads `gameScene` Single then the arena Additive. 3. `MatchManager.OnNetworkSpawn` (server) reads `MatchSettings` **once**, collects scene-scoped `SpawnPoint`s, snaps to NavMesh, keeps the largest mutually reachable group, shuffles, splits into 2 blocks, spawns bots, sweeps already-spawned players, publishes scores once.

**Spawn / respawn** — `SpawnManager.SpawnPlayerForClient` ensures the default faction, `SpawnAsPlayerObject`, then hands the body to any `MatchManager` in the scene (`RegisterPlayerEntity` → team, faction, arena `TargetingProfile`, per-team herd id, `HealthComponent.OnDeath` hook, teleport to that side's spawn). Respawn is a **state change on the living object** (`SetActive`, `ResetToFull`, re-enable `EntityFaction` + `AgentController`), never despawn/respawn. Movement is routed by `TeleportRpc` to the **owner** because the player's `NetworkTransform` is owner-authoritative.

**Score** — `HandleDeath` bumps deaths, credits the kill from `HealthComponent.LastDamageSource` walked up to a registered entity (friendly fire and suicides score nothing; a 2-team match attributes unattributed deaths to the other side), decrements lives, then `PublishScores()` rebuilds the whole table and `BroadcastScoresRpc(SendTo.NotServer)`.

**End** — `CheckWinCondition` runs the matching `MatchWinEvaluator`; a draw (`-1`) when nobody is left. `EndMatch` raises `OnMatchEnded` locally and `BroadcastMatchEndedClientRpc(SendTo.NotServer)`; `MatchResultUI` compares the winner to `MatchManager.LocalTeamIndex`. Eliminated humans get `EnterSpectatorRpc` → `PlayerController.EnterSpectatorMode`.

## Multiplayer

| Concern | Authority |
| --- | --- |
| Team assignment | Server (`VersusTeamRoster`); client only *claims* a validated index |
| `PlayerIdentity.Team` | Server-write NetworkVariable, `-1` outside a match (name/suit colour are owner-write) |
| Team ships | Server, via `GameServices.World.Spawn`; livery replicated by `ShipTeamAccent` |
| Bots | Server: raw `Instantiate` + `NetworkObject.Spawn()` (not `GameServices.World`) |
| Leaderboard | Server rebuilds and pushes `MatchScoreEntry[]` on death/join only; `SendTo.NotServer` so a host does not double-apply |
| Match end | Server-only decision, broadcast `SendTo.NotServer` |
| Respawn teleport | Server asks, **owner** performs (`TeleportRpc`) |
| `MatchManager.LocalTeamIndex` / `Scores` | Static per-peer overlays, cleared in `OnNetworkSpawn` before the `IsServer` gate |

## Persistence

N/A for match state, deliberately: a VS match and an arena match are single-session and nothing in `Versus/` or `Minigame/` implements `ISaveable`. The statics are session-scoped and explicitly cleared. Only the **story run**'s session state persists, via [`GameStateSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/GameStateSaveable.cs) (key `gameState`: `GameManager.GameTimer` + `GameState`, restored through `RestoreTimer`/`RestoreState`, which never re-trigger `WinGame`). Runtime faction/targeting swaps made by `MatchManager` are excluded from saves — see the notes in [`SaveablePolicy`](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs) and [`AgentStateSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/AgentStateSaveable.cs).

## Gotchas

- **[`MinigameArena.unity`](Assets/Game/Scenes/Minigames/MinigameArena.unity) is empty** — `SceneRoots: []`, `m_NavMeshData: {fileID: 0}`. No `MatchManager`, no `SpawnPoint`s, no baked NavMesh anywhere in the project. The whole deathmatch code path is orphaned until that scene is authored; nothing warns you.
- **`VersusShipSpawns.UseRing`/`UseExplicit` are called only from EditMode tests.** Shipping code always falls through to the asset ([VersusShipSpawnConfig.asset](Assets/Game/ScriptableObjects/Versus/VersusShipSpawnConfig.asset): Ring, centre `(2500, 500)`, radius 120). Do not assume the override is live.
- `VersusSession.Clear()` also clears `VersusShipSpawns` — that coupling is intentional and is the only thing stopping match N+1 starting on match N's ring. `VersusTeamRoster.Clear()` happens in `NetworkGameManager.OnNetworkDespawn`, *before* the null guard.
- **Network prefab lists store GUIDs, so name greps lie.** The live list is [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset) (referenced by GUID from NetworkManager.prefab); `PlayerShip` and `DeathmatchBot` are both registered there today. An unregistered ship spawns for the host and nobody else.
- **`VersusShipSpawner` is a prefab instance in [persistentScene](Assets/Game/Scenes/world/persistentScene.unity)** — grepping scenes for the *script* GUID finds nothing. It is a plain `MonoBehaviour` with a `static Instance`, on purpose (no scene-placed `NetworkObject` id to keep alive).
- `MatchManager` reads `MatchSettings` **once** in `OnNetworkSpawn`; changing a static mid-match does nothing. Statics survive returning to the menu, which is why `MinigameConfigUI.Awake` calls `ResetToDefaults()`.
- Faction pools are hard ceilings from authored assets: 4 team factions (2 used), 16 solo. Overflow reuses the last solo faction and silently allies two entities.
- `VersusRules.ClampTeams`/`ClampTeamSize` are **coupled** — each takes the other axis. Clamping them independently is how a host gets 8×12 in a 24-seat lobby. `VersusRules.MaxTeams` is derived from the `Names` array and must stay declared *after* it or static init throws.
- `-1` means two things in the arena: `DrawTeam` (match ended in a draw) and "no team yet" for `LocalTeamIndex`. `PlayerIdentity.Team` uses `-1` for "not in a versus match".
- A dead player object stays active forever, so `MatchManager` disables `EntityFaction` to pull corpses out of `EntityTargetRegistry`; without it every survivor aims at the body and a last-standing match never ends.
- Arena NavMesh islands silently hang a match — `SpawnReachability` drops minority-island spawn points and logs a warning telling you to rebake.
- `HerdModule` ids are baked into the bot prefab; `RegisterEntity` overwrites them per team, or a FFA puts 16 mutual enemies in one herd.
- Ground is probed **heightmap first, raycast second** (`ShipGrounding`, `SpawnManager.TryFindOpenGround`). A `false` means "not yet, the chunk hasn't loaded" — retry, never substitute a guessed height.
- `GameManager.WinGame` deliberately does *not* use `Network.Simulates` (a plain MonoBehaviour reads as its own authority on every client); it checks `Network.IsNetworked && !Network.Server` by hand.

## Extending: add a new arena gamemode

1. Add the case to `MatchGameMode` in [MatchEnums.cs](Assets/Game/Scripts/Gameplay/Minigame/Core/MatchEnums.cs) (Core assembly — keep it Unity-free).
2. Teach [`MatchRules`](Assets/Game/Scripts/Gameplay/Minigame/Core/MatchRules.cs): `UsesTeams`, `ResolveCondition`, `ResolveLives`, and any new clamp. Add a `WinCondition` + a pure evaluator in `MatchWinEvaluator` if the ending is new.
3. Add the tunables to `MatchSettings` and cover them in `ResetToDefaults`, `ClampToLimits`, `Describe`.
4. Give it a row in [`MinigameConfigUI`](Assets/Game/Scripts/Presentation/UI/Pages/MinigameConfigUI.cs) — mode button, the controls it needs, visibility in `Refresh`.
5. In `MatchManager`: branch `SpawnBots` (team groups vs solo slots), `FactionFor`, and `NextSpawnPosition`. Assign every entity a faction from the authored pool — do not create factions at runtime.
6. Wire the new condition into `CheckWinCondition` and `NobodyLeft`; confirm `RespawnsEnabled` gives the behaviour you want.
7. Unit-test the Core pieces (see [VersusShipSpawnTests](Assets/Game/Editor/Tests/VersusShipSpawnTests.cs) for the pattern), then verify **on a real client**: bots spawn, the leaderboard matches, and the result screen shows Victory on the winning side and Defeat on the other.
