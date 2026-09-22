---
system: GameModes
layer: presentation
summary: Versus team PvP in the streamed world and the plain story run
paths:
  - Assets/Game/Scripts/Gameplay/Versus/
  - Assets/Game/Scripts/Gameplay/Game/
  - Assets/Game/Scripts/Gameplay/Arrival/
  - Assets/Game/ScriptableObjects/Versus/VersusShipSpawnConfig.asset
symptoms:
  - "the team ship spawns for the host and nobody else can see it"
  - "players land inside the wrong team's ship or on top of each other"
  - "the host can pick 8 teams of 12 in a 24-seat lobby"
  - "the second match starts on the previous match's spawn ring"
  - "I died in versus and respawned in the enemy team's ship"
  - "respawning put me on open sand at the world's starting coordinates instead of back in my ship"
  - "I respawned still roped, still under the net, or still on fire"
reads_with: [Multiplayer, Lobby, PlayerShip, Persistence]
updated: 2026-09-22
---

# Game Modes

**Versus** — team PvP in the streamed world, everyone starts in a team ship — plus the plain story run.

A third family, the **Minigame arena** (bot deathmatch, three gamemodes off one `MatchManager`), was deleted on 2026-09-22. Its arena scene had been empty since commit `7cbccf9f`, no `MatchManager` existed in any scene or prefab, and no menu button reached it, so the whole path was unreachable. If a bot arena is wanted again it is a new build, not a restore.

**Scope:** [Assets/Game/Scripts/Gameplay/Versus/](Assets/Game/Scripts/Gameplay/Versus), [Gameplay/Game/](Assets/Game/Scripts/Gameplay/Game), [Gameplay/Arrival/](Assets/Game/Scripts/Gameplay/Arrival)
**Related:** [Multiplayer.md](Multiplayer.md) · [Lobby.md](Lobby.md) · [Persistence.md](Persistence.md) · [PlayerShip.md](PlayerShip.md) · [AgentSystem.md](AgentSystem.md) · [NavMeshSystem.md](NavMeshSystem.md)

## Model

- A mode is carried across the scene load by **statics**, because the lobby/menu that chose it is destroyed by that very load: [`VersusSession`](Assets/Game/Scripts/Gameplay/Versus/Core/VersusSession.cs), [`VersusShipSpawns`](Assets/Game/Scripts/Gameplay/Versus/Core/VersusShipSpawns.cs), [`VersusTeamRoster`](Assets/Game/Scripts/Gameplay/Versus/Core/VersusTeamRoster.cs). All of them have a `Clear`/`ResetToDefaults` that every exit route must hit.
- `Core/` files are Unity-free and live in their own asmdefs so EditMode tests reach them; `Runtime/` siblings hold the MonoBehaviours/NetworkBehaviours.
- Everything decisive is **server-side**. The only replicated per-player mode state is `PlayerIdentity.Team` (server-write); the leaderboard is pushed wholesale by RPC.
- Two spawn paths: VS resolves a seat inside its team's ship via [`VersusShipSpawner`](Assets/Game/Scripts/Gameplay/Versus/Runtime/VersusShipSpawner.cs); everything else goes through [`SpawnManager`](Assets/Game/Scripts/Gameplay/Game/Spawning/SpawnManager.cs) + [`SpawnPoint`](Assets/Game/Scripts/Gameplay/Game/Spawning/SpawnPoint.cs).
- **The rule of respawn: you come back inside your ship** — in VS, your TEAM's ship, never any other hull. [`ShipRespawn`](Assets/Game/Scripts/Gameplay/Game/Spawning/ShipRespawn.cs) resolves the pose (VS: `VersusShipSpawner.TryClaimRespawnPose`; story: the crew hull's `ShipSeat` dismount points); `SpawnManager`'s spawn-point/open-ground path is the fallback for a world with no ship in it.
- **A respawn lets go of everything holding the body, and death does not.** [`RespawnRelease.Everything`](Assets/Game/Scripts/Gameplay/Game/Spawning/RespawnRelease.cs) cuts every rope on the player (leash, lasso, grapple), takes them out of the net they are under, unties a hogtie and clears every status condition — run by both respawn paths, on the deciding machine, immediately **before** the move. A corpse stays roped and netted on purpose: dragging a body somewhere is a thing players do.
- [`Game.Mode`](Assets/Game/Scripts/Gameplay/Game/State/Game.cs) (`Singleplayer`/`Multiplayer`) and [`GameManager`](Assets/Game/Scripts/Gameplay/Game/State/GameManager.cs) belong to the **story run** (timer + `WinGame` → win scene), not to VS.
- Team identity is one integer everywhere: index into `VersusRules.Names`, into the team colour array, and into the ship layout.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `VersusRules` | [Versus/Core/VersusRules.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusRules.cs) | Seat arithmetic: 2–8 teams, 1–12 size, `MaxSeats = 24`, team names, coupled clamps |
| `VersusSession` | [Versus/Core/VersusSession.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusSession.cs) | Local peer's match: `IsActive`, `TeamCount`, `TeamSize`, `LocalTeam`, `ColorOf` |
| `VersusTeamRoster` | [Versus/Core/VersusTeamRoster.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusTeamRoster.cs) | Server map clientId→team; `Claim` (lobby choice) outranks `Assign` (fill emptiest) |
| `TeamColorRules` | [Versus/Core/TeamColorRules.cs](Assets/Game/Scripts/Gameplay/Versus/Core/TeamColorRules.cs) | Swatch stepping that skips colours other teams wear; `DefaultColors` spread |
| `VersusShipSpawnConfig` | [Versus/Core/VersusShipSpawnConfig.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusShipSpawnConfig.cs) | Per-arena asset: Ring (centre+radius) or Explicit points, probe height, seat ring |
| `VersusShipSpawns` | [Versus/Core/VersusShipSpawns.cs](Assets/Game/Scripts/Gameplay/Versus/Core/VersusShipSpawns.cs) | Runtime override static that wins over the asset |
| `ShipSpawnLayout` | [Versus/Core/ShipSpawnLayout.cs](Assets/Game/Scripts/Gameplay/Versus/Core/ShipSpawnLayout.cs) | `Ring`, `SeatRing`, `TryPointForTeam`, `TryValidateExplicit` |
| `VersusShipSpawner` | [Versus/Runtime/VersusShipSpawner.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/VersusShipSpawner.cs) + [.Seats.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/VersusShipSpawner.Seats.cs) | One ship per team via `GameServices.World.Spawn`, team livery, `TryClaimSeat` (match start, seat marker pose) / `TryClaimRespawnPose` (respawn, the seat's standing `DismountPoint`) |
| `ShipRespawn` | [Game/Spawning/ShipRespawn.cs](Assets/Game/Scripts/Gameplay/Game/Spawning/ShipRespawn.cs) | Static resolver: a dead player comes back inside their own ship — team ship in VS, the crew hull otherwise; refuses rather than pick a wrong hull |
| `RespawnRelease` | [Game/Spawning/RespawnRelease.cs](Assets/Game/Scripts/Gameplay/Game/Spawning/RespawnRelease.cs) | `Everything(body)`: `CuttableRopes.CutEveryRopeOn` + `SnareCatch.Holding(...).FreeEverywhere` + `Hogtie.Untie` + `StatusReceiver.ClearAll`. Called by every respawn path, before the teleport |
| `ShipGrounding` / `ShipSeat` | [Versus/Runtime/](Assets/Game/Scripts/Gameplay/Versus/Runtime) | Heightmap-first ground probe, raised onto anything standing on the terrain when it is a HULL being landed (`TryResolveLandingSurface` — see [PlayerShip](PlayerShip.md)); seat markers (ordered, component not name) |
| `RankLayout` | [Versus/Core/RankLayout.cs](Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs) | Lobby rank geometry: seat spacing, 4-wide seat wrap, **4-wide team wrap on a shared half-pitch lattice**, team gap, two-axis camera fit, eye lift |
| `RankGrounding` | [Versus/Core/RankGrounding.cs](Assets/Game/Scripts/Gameplay/Versus/Core/RankGrounding.cs) | Drops the flat seats onto the ground through an injected probe; reports the height spread the camera frames |
| `RankOverlayScale` | [Versus/Core/RankOverlayScale.cs](Assets/Game/Scripts/Gameplay/Versus/Core/RankOverlayScale.cs) | Projected spacing to font size + label rung; the floor rung is still a word, never a bare colour |
| `PlayerIdentity` | [Core/Multiplayer/Players/PlayerIdentity.cs](Assets/Game/Scripts/Core/Multiplayer/Players/PlayerIdentity.cs) | `Team` NetworkVariable (server-write, `-1` = no team); name/suit are owner-write |
| `NetworkGameManager.Versus` | [Joining/NetworkGameManager.Versus.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.Versus.cs) | Adopts the session from the lobby, `SpawnIntoTeamShip`, `PublishTeam` |

## Modes

| Mode | Files | Rules |
| --- | --- | --- |
| Versus (PvP world) | `Versus/**`, `Arrival/**`, `NetworkGameManager.Versus.cs` | 2–8 teams × 1–12, product ≤ 24 seats. One identical ship per team on a ring. No scoring, no win condition, no end — the mode ends when people leave. |
| Story / singleplayer | `Gameplay/Game/State/**` | Host of one; `GameManager.GameTimer` + `WinGame()` → `onWinScene` through Netcode's scene manager. |

## Flows

**Start a VS match** — 1. `MainMenuUI.HostVersus` → `VersusRulesUI` stages teams/size (statics) → lobby. 2. Lobby writes team count/size/colours/per-player team into Unity Lobby data ([`LobbyTeams`](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/LobbyTeams.cs), [`VersusSetup`](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/VersusSetup.cs)). 3. On load, **every** peer runs `AdoptVersusSessionFromLobby()` in `NetworkGameManager.OnNetworkSpawn` → `VersusSession.Begin` (or `Clear`). 4. Client sends `ReportVersusTeamServerRpc`; server `VersusTeamRoster.Claim`s it (index validated against `TeamCount`). 5. `SpawnWhenReady` sees `VersusSession.IsActive` + a `VersusShipSpawner.Instance` → `SpawnIntoTeamShip`: wait for team → preload chunks around **every** team anchor → `ArrivalDirector.SpawnIntoVersusArrival` (whole formation or nothing) or fall back to `TryClaimSeat` → `SpawnManager.SpawnPlayerForClient(pos, rot)` → `PublishTeam` writes `PlayerIdentity.SetTeam`.

**Spawn / respawn** — `SpawnManager.SpawnPlayerForClient` ensures the default faction, then `SpawnAsPlayerObject`. Respawn is a **state change on the living object** (`SetActive`, `ResetToFull`, re-enable `EntityFaction` + `AgentController`), never despawn/respawn. Movement is routed by `TeleportRpc` to the **owner** because the player's `NetworkTransform` is owner-authoritative.

## Multiplayer

| Concern | Authority |
| --- | --- |
| Team assignment | Server (`VersusTeamRoster`); client only *claims* a validated index |
| `PlayerIdentity.Team` | Server-write NetworkVariable, `-1` outside a match (name/suit colour are owner-write) |
| Team ships | Server, via `GameServices.World.Spawn`; livery replicated by `ShipTeamAccent` |
| Respawn teleport | Server asks, **owner** performs (`TeleportRpc`) |

## Persistence

N/A for match state, deliberately: a VS match is single-session and nothing in `Versus/` implements `ISaveable`. The statics are session-scoped and explicitly cleared. Only the **story run**'s session state persists, via [`GameStateSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/GameStateSaveable.cs) (key `gameState`: `GameManager.GameTimer` + `GameState`, restored through `RestoreTimer`/`RestoreState`, which never re-trigger `WinGame`). Runtime faction/targeting swaps are excluded from saves — see the notes in [`SaveablePolicy`](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs) and [`AgentStateSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/AgentStateSaveable.cs).

## Gotchas

- **`VersusShipSpawns.UseRing`/`UseExplicit` are called only from EditMode tests.** Shipping code always falls through to the asset ([VersusShipSpawnConfig.asset](Assets/Game/ScriptableObjects/Versus/VersusShipSpawnConfig.asset): Ring, centre `(2500, 500)`, radius 120). Do not assume the override is live.
- `VersusSession.Clear()` also clears `VersusShipSpawns` — that coupling is intentional and is the only thing stopping match N+1 starting on match N's ring. `VersusTeamRoster.Clear()` happens in `NetworkGameManager.OnNetworkDespawn`, *before* the null guard.
- **Network prefab lists store GUIDs, so name greps lie.** The live list is [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset) (referenced by GUID from NetworkManager.prefab); `PlayerShip` is registered there today. An unregistered ship spawns for the host and nobody else.
- **`VersusShipSpawner` is a prefab instance in [persistentScene](Assets/Game/Scenes/world/persistentScene.unity)** — grepping scenes for the *script* GUID finds nothing. It is a plain `MonoBehaviour` with a `static Instance`, on purpose (no scene-placed `NetworkObject` id to keep alive).
- **A static survives returning to the menu.** That is why every screen that stages one resets it on the way in, the way `VersusRulesUI` does.
- `VersusRules.ClampTeams`/`ClampTeamSize` are **coupled** — each takes the other axis. Clamping them independently is how a host gets 8×12 in a 24-seat lobby. `VersusRules.MaxTeams` is derived from the `Names` array and must stay declared *after* it or static init throws.
- `PlayerIdentity.Team` uses `-1` for "not in a versus match".
- **A dead player object stays active forever.** Anything that wants corpses out of `EntityTargetRegistry` has to disable their `EntityFaction`; without it every survivor keeps aiming at the body.
- Ground is probed **heightmap first, raycast second** (`ShipGrounding`, `SpawnManager.TryFindOpenGround`). A `false` means "not yet, the chunk hasn't loaded" — retry, never substitute a guessed height. A **ship** landing adds a third pass on top of that: the heightmap is blind to what is built on it, and a hull rests on the highest thing it spans, so `TryResolveLandingSurface` raises the answer onto any structure standing there — the reason a team ship no longer plans its descent into a building.
- `GameManager.WinGame` deliberately does *not* use `Network.Simulates` (a plain MonoBehaviour reads as its own authority on every client); it checks `Network.IsNetworked && !Network.Server` by hand.

## Extending: add a new versus rule

1. Put the pure arithmetic in [`VersusRules`](Assets/Game/Scripts/Gameplay/Versus/Core/VersusRules.cs) — it is Unity-free and its own asmdef so EditMode tests reach it. Remember the clamps are **coupled**.
2. Stage the host's choice on a session static and clear it on every exit route, the way `VersusSession` does.
3. Carry it to the peers through lobby data ([`VersusSetup`](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/VersusSetup.cs)), not through an RPC at load — every peer adopts the session in `NetworkGameManager.OnNetworkSpawn`.
4. Unit-test the Core pieces (see [VersusShipSpawnTests](Assets/Game/Editor/Tests/VersusShipSpawnTests.cs) for the pattern), then verify **on a real client**.
