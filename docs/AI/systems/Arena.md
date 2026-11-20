---
system: Arena
layer: presentation
summary: Two-team elimination in a walled settlement: best-of rounds, weapon pads, scaled damage, no friendly fire
paths:
  - Assets/Game/Scripts/Gameplay/Arena/
  - Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.Arena.cs
  - Assets/Game/Scripts/Presentation/UI/HUD/ArenaHudUI.cs
  - Assets/Game/Scenes/Arena/NomadArena.unity
  - Assets/Game/ScriptableObjects/Arena/NomadArenaWeapons.asset
  - Assets/Game/ScriptableObjects/Settlements/NomadArenaSettlement.asset
  - Assets/Game/Resources/Worlds/Arena.asset
symptoms:
  - "the arena never starts a round and just says waiting for both teams"
  - "I can't test the arena alone"
  - "two players in the arena are stuck in practice / both on the same team"
  - "I died in the arena and there is no respawn button"
  - "a player who joined mid-round is stuck on the loading screen"
  - "my teammate's shots do nothing to me in the arena"
  - "nobody dies in fewer than three hits in the arena"
  - "the weapon pads are empty"
  - "I picked a weapon up in the arena with a full hotbar and nothing happened"
  - "the arena logs no ObjectiveDirector / no BountyBoard"
reads_with: [GameModes, Combat, Lobby, Multiplayer, Artifacts]
updated: 2026-11-20
---

# Arena

A two-team elimination match fought inside a settlement walled in with invisible walls. Last team standing takes the round, first to three rounds takes the match, and the weapons are artifacts lying on pads around the map.

**Scope:** [Assets/Game/Scripts/Gameplay/Arena/](Assets/Game/Scripts/Gameplay/Arena), [NetworkGameManager.Arena.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.Arena.cs), [ArenaHudUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/ArenaHudUI.cs), [NomadArena.unity](Assets/Game/Scenes/Arena/NomadArena.unity)
**Related:** [GameModes.md](GameModes.md) · [Combat.md](Combat.md) · [Lobby.md](Lobby.md) · [Multiplayer.md](Multiplayer.md) · [Artifacts.md](Artifacts.md)

## Model

- **An arena is a versus session with its own scene.** The lobby mode is `"arena"` ([`LobbyKeys.ModeArena`](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/LobbyKeys.cs)). `LobbyTeams.IsVersus` answers true for it, so the team picker, team colours, `VersusSession` and `VersusTeamRoster` all work unchanged. `LobbyTeams.IsArena` tells the two apart, and `LobbyTeams.ReadRules` forces the team count to `ArenaRules.TeamCount` (2) whatever the key says.
- **"Is this an arena" means `ArenaMatch.Instance != null`.** It is set in `Awake`, before the network spawn, so it is answered the same way on every machine from the moment the scene loads. The spawn flow, the respawn gate, the death screen, the pickup overflow and the objective/bounty networks all ask this.
- **One state machine, server-side, replicated whole.** [`ArenaMatch`](Assets/Game/Scripts/Gameplay/Arena/Runtime/ArenaMatch.cs) holds a `NetworkVariable<ArenaMatchState>` with the phase, round, score per team, alive count per team, round winner and phase end time. Every peer's HUD reads that and nothing else.
- **Phases** ([`ArenaPhase`](Assets/Game/Scripts/Gameplay/Arena/Core/ArenaRules.cs)): `Waiting` (both sides need a player) → `Countdown` → `Fighting` → `RoundOver` → the next `Countdown`, or `MatchOver`. Damage only lands in `Fighting`.
- **Practice rounds** (`ArenaMatchState.Practice`) let the arena be tried alone. While waiting with a side empty, the host presses **P** (`ArenaHudUI`) → `ArenaMatch.TogglePractice`. A practice round is reset like any round, does not advance the round count, scores nothing, ends only when everyone in it is down (`EvaluateRound(..., practice: true)` → `Draw`) or the host presses P again, and lets joiners spawn straight in. When both sides have a player it is replaced by a real round on the next tick.
- **An empty side is filled by the server.** The lobby puts anyone who never clicked a team on team 0, so a host and friends who just pressed Ready arrive on one side. While waiting or practising, once every connected client has a body (`inMatch == ConnectedClientsIds.Count`, so a 2v2 isn't broken by whoever loads first), `ArenaRules.SideToFill` → `ArenaMatch.FillEmptySide` moves the newest non-host player across (`VersusTeamRoster.Claim` + `PlayerIdentity.SetTeam`), and the round that follows stands them at their new spawn. Never during a real round. A single player is never moved — that is practice.
- **Balance is the match's, not the weapons'.** The weapons are tuned for the story world. Inside an arena every hit on a contestant is multiplied by `damageScale` (0.6), then capped at `maxHitDamage` (34): against 100 health nobody dies in fewer than three hits. [`ArenaDamageRules`](Assets/Game/Scripts/Gameplay/Arena/Core/ArenaDamageRules.cs) is the pure rule. Reference TTKs: pellet gun ~2.7 s sustained, laser staff ~1.7 s of beam on target, bazooka two good rockets.
- `Core/` is Unity-free in its own asmdef (`SpaceGame.Arena.Core`, `noEngineReferences`) so [ArenaRulesTests](Assets/Game/Tests/EditMode/ArenaRulesTests.cs) reach it.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `ArenaRules` / `ArenaPhase` | [Core/ArenaRules.cs](Assets/Game/Scripts/Gameplay/Arena/Core/ArenaRules.cs) | `EvaluateRound(alive0, alive1, practice)` → team, `Draw` or `Undecided`; `MatchWinner`; `CanStartRound`; `CanStartPractice`; `SideToFill` |
| `ArenaDamageRules` | [Core/ArenaDamageRules.cs](Assets/Game/Scripts/Gameplay/Arena/Core/ArenaDamageRules.cs) | 0 outside `Fighting`, 0 between teammates (self-damage still lands), else scale then cap; a non-zero hit is never rounded to 0 |
| `ArenaMatch` | [Runtime/ArenaMatch.cs](Assets/Game/Scripts/Gameplay/Arena/Runtime/ArenaMatch.cs) | Server state machine, `IDamageFilter` on every contestant, round reset, death drop, wall safety net; on every peer, spectator mode on the local player's death |
| `ArenaMatchState` | [Runtime/ArenaMatchState.cs](Assets/Game/Scripts/Gameplay/Arena/Runtime/ArenaMatchState.cs) | The replicated scoreboard |
| `ArenaTeamSpawn` | [Runtime/ArenaTeamSpawn.cs](Assets/Game/Scripts/Gameplay/Arena/Runtime/ArenaTeamSpawn.cs) | A team tag on a `SpawnPoint`; the spawn point's own scatter and clearance pick the position |
| `ArenaWeaponPad` / `ArenaWeaponTable` | [Runtime/](Assets/Game/Scripts/Gameplay/Arena/Runtime) | Server-only pad: weighted pick from the table, `World.Spawn`, respawn `respawnSeconds` (20) after its item is despawned (picked up) |
| `ArenaBounds` | [Runtime/ArenaBounds.cs](Assets/Game/Scripts/Gameplay/Arena/Runtime/ArenaBounds.cs) | Ring of invisible `BoxCollider` panels built in `Awake` on every machine; `Contains` for the server's safety net |
| `ArenaHudUI` | [ArenaHudUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/ArenaHudUI.cs) | `DontDestroyOnLoad` overlay (sort order 700), dormant until an `ArenaMatch` spawns; score, round, alive counts, banner, Back to menu |
| `NetworkGameManager.SpawnIntoArena` | [NetworkGameManager.Arena.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.Arena.cs) | Team (shared versus wait/roster) → hold while `Fighting` → team spawn pose → `SpawnPlayerForClient` → `PublishTeam` |

## Flows

**Start** — `MainMenuUI.StartArena` → host: `VersusRulesUI.Open(menu, arena: true)` (team size only) → `EnterArenaLobby` → `LobbyRoute.ArenaHost` → `VersusSetup.Arena(size)` writes mode `"arena"`. Join: `JoinArena` → `LobbyRoute.ArenaJoin`, whose browser lists only arena lobbies. `MainMenuUI.GameSceneName` resolves to `arenaWorld` (`NomadArena`) on both routes, which the lobby loads and the joiner's loading screen waits for. Pressing Play in `NomadArena.unity` also works: it is a registered `GameWorld`, so `Bootstrapper` hosts it, and with no lobby the roster fills the emptier side.

**Spawn** — `SpawnWhenReady` checks `ArenaMatch.Instance` **before** the versus ship branch (an arena is also a versus session), then `SpawnIntoArena`.

**Round** (server, `ArenaMatch.Update`):
1. `Waiting` → once `CanStartRound` → `BeginRound`: disarm the pads, despawn every runtime-spawned `PickupableItem` (scene objects are skipped), empty every hotbar, `PlayerRespawn.ReviveAt` each contestant at their team's spawn.
2. `Countdown` ends → `Fighting`, and the pads are armed. Each pad puts a weapon down straight away.
3. Every frame in `Fighting`, the round is evaluated from the alive counts, which covers deaths and disconnects alike. Any living contestant found outside `ArenaBounds.Contains` is teleported back to their team's spawn.
4. When the round is decided → `RoundOver` (draw scores nothing) or `MatchOver` at `roundsToWin`. The pads are disarmed.

**Death** — server: `HealthComponent.OnDeath` → `PlayerInventoryNetwork.DropSlot` for every slot (the drop key's server half). Owner: `PlayerController.OnPlayerDeath` → `EnterSpectatorMode` (free-fly `SpectatorCamera`); the next round's revive exits it through `OnRevive`. `PlayerRespawn.OnRespawnRequested` refuses while a match exists, and `DeathScreenUI` hides its overlay.

**End** — `MatchOver` shows the winner and a Back to menu button that takes the cursor through `GameplayMenuScope` and calls `SessionExit.ToMainMenu(saveWorld: false)`.

## Multiplayer

| Concern | Authority |
| --- | --- |
| Phase, score, alive counts | Server writes `ArenaMatchState`; every peer reads |
| Damage rules | Server, as an `IDamageFilter` the match adds to each contestant's `HealthComponent` |
| Team | Server (`VersusTeamRoster` → `PlayerIdentity.SetTeam`) |
| Pads, floor clear, death drop | Server: `GameServices.World.Spawn/Despawn`, `DropSlot` |
| Revive/teleport | Server asks, owner moves (`NetworkedTeleport` inside `ReviveAt`) |
| Walls | Built locally on every machine — movement is owner-authoritative |
| Spectating | Owner only, off its own replicated death |

The pad items are ordinary `InventoryItem.itemPrefab`s, which are already registered network prefabs, so nothing new needs registering. `ArenaMatch` is a scene-placed `NetworkObject` in `NomadArena.unity`.

## Persistence

N/A — a match is single-session by design. The arena scene has no `[SaveSystem]`, the arena routes call `WorldSession.Clear()`, and nothing in `Arena/` implements `ISaveable`. A direct Play in the editor stages a throwaway world, but with no `SaveManager` in the scene nothing is written.

## Gotchas

- **Solo, a real round never starts.** `CanStartRound` needs both sides populated; use a practice round (P, host only) to test alone. P is read straight off `Keyboard.current` and gated on `GameplayMenuScope.AcceptsGameplayInput`, so a dead host cannot end a practice that a teammate is still alive in — it ends when they go down.
- **A moved player's `VersusSession.LocalTeam` is stale.** `FillEmptySide` changes `PlayerIdentity.Team`, which everything in the arena reads, but the client's own session still names its lobby team; only the voice list's team grouping (`SpeakerTeams`) reads that.
- **Only damage is team-filtered.** Knockdowns, freezes, nets and lassos bypass `HealthComponent`, so a teammate can still shove or freeze you (see [Combat.md](Combat.md) — the filter cannot see them).
- **The backpack is no overflow in an arena.** `PickupableItem` refuses a pickup with a full hotbar while a match exists. A stowed weapon would survive both the round reset and the death drop, which only touch hotbars.
- **Gauntlets are left out of the pad table on purpose.** A gauntlet picked up from the ground sits in the hotbar and only works once moved to a forearm on the body screen. The gravel blaster is out too: `refireSeconds` 0 kills in about a second even capped.
- **A late joiner waits behind the loading screen while a round is live** (`AcceptsSpawns`), then spawns at the next countdown.
- **The settlement was generated on terrain flattened first.** On the bowl's natural slope the terrace sculptor cut the buildings into pits. The terrain is an arena-only `TerrainData` (`Assets/Game/Terrain/Arena/`, seed 80459). Never point the arena at Spillexo's `NoiseTerrain_TerrainData`.
- **`ObjectiveNetwork` / `BountyNetwork` stay quiet in an arena.** They ride on the `NetworkGameManager` prefab and only log a missing director or board when no `ArenaMatch` exists.

## Extending

- **Tune balance** on the `ArenaMatch` component (`damageScale`, `maxHitDamage`, `roundsToWin`, timers) and the pool in `NomadArenaWeapons.asset`. Keep `3 × maxHitDamage ≥ 100 > 2 × maxHitDamage` if the three-hit promise should hold. `ArenaRulesTests.ThreeCappedHitsAreNeededToKill` pins it for the default numbers.
- **A new arena map** — a scene with an `ArenaMatch` (on a `NetworkObject`), an `ArenaBounds`, one `ArenaTeamSpawn` per team, pads with a table, a `BreathableVolume` trigger (Ignore Raycast layer) over the playable area, and the `NetworkGameManager` + `SpawnManager` prefabs. Then a `GameWorld` in `Resources/Worlds`, and an entry at the END of Build Settings. A map choice in the lobby would replace `MainMenuUI.arenaWorld`.
- **A new win condition** belongs in `ArenaRules`, as pure arithmetic with a test, and is read from `ArenaMatch.Update`.
