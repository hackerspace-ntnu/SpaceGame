# Minigame Deathmatch Framework — Design

## Goal

Turn the existing minigame arena (`MinigameArena.unity`, see
`2026-08-03-minigame-scene-design.md`) into a playable combat minigame with
three configurable modes — **Team Deathmatch**, **Free-For-All**, and
**Battle Royale** — driven by `PatrolRobot`-derived bots and the player(s),
using the existing faction/combat AI system. A new pre-match config screen
lets the host choose gamemode, bot counts, and win condition before
launching.

## Background / prior art

- The minigame scene and its menu → host → additive-load flow already exist
  (`MainMenuUI.StartMinigame()`, `NetworkGameManager`, `SpawnManager`) — see
  the prior spec for full detail. This design builds on top of that flow
  without changing it, except where noted.
- The entity AI system (`Assets/Scripts/agents/`) is a modular
  `IBehaviourModule` architecture. `PatrolRobot.prefab`
  (`Assets/Prefabs/agents/prefab/RobotPatrol/`) already has ranged combat
  (`AgentRangedCombatModule`, currently disabled by default), melee
  (`CloseCombatModule`), `ChaseModule`, health (`HealthComponent`), and
  faction membership (`EntityFaction`).
- Targeting is **faction-based only** — no tags. `EntityFaction` components
  self-register into a static `EntityTargetRegistry`; combat/chase modules
  call `EntityTargetRegistry.ResolveNearest(selfFaction, requiredRelationship,
  position)`. Relationships between factions (`Neutral`/`Allied`/`Hostile`)
  live in one shared `FactionRelationshipTable` asset
  (`GlobalRelationships`). Same faction is always Allied automatically.
- Factions (`FactionDefinition`) are plain edit-time `ScriptableObject`
  assets in this codebase (existing: `PlayerFaction`, `NPCFaction`,
  `RobotFaction`) — no faction is currently created at runtime anywhere, and
  `EntityFaction.faction` has no public setter today.
- The player prefab (`PlayerCharacterNetworked.prefab`) currently has **no
  `EntityFaction` and no weapon/shoot component** — it is invisible to
  `EntityTargetRegistry` and cannot fire projectiles. Loose weapon scripts
  exist (`EnergyRifle.cs`, `BasicGun.cs`, `BallLightningWeapon.cs`) but are
  not wired to the player prefab and are not reused here — the design reuses
  the robots' `AgentWeaponDefinition`-based projectile weapon instead, for
  consistency and minimal new code.
- There is **no respawn system anywhere** in the codebase — `HealthComponent
  → HealthReactionModule` death handling only despawns
  (`gameObject.SetActive(false)` after a delay). Two of the three win
  conditions below need respawn behavior; this design adds it.
- The UI has no page-router/framework — `MainMenuUI.cs` is a flat
  `MonoBehaviour` with public methods wired directly to uGUI `Button
  OnClick` events, navigating via scene loads. New UI in this design follows
  the same pattern; no framework is introduced.
- `SpawnPoint.cs` (`Assets/Scripts/Game/SpawnPoint.cs`) is a plain
  radius-based random-point spawner with no team/faction tagging.
  `SpawnManager.GetSpawnPoint()` currently returns a single position
  (`spawnPoints[0]`) with no team awareness — not sufficient for
  multi-team/multi-entity placement, addressed below.

## Design

### 1. Config screen (`MinigameConfigUI.cs`, new file in `Assets/Scripts/UI/Pages/`)

A new scene/panel between the main menu and the arena, following
`MainMenuUI`'s existing flat-MonoBehaviour-with-Inspector-wired-buttons
style (no new page framework). Controls:

- **Gamemode dropdown**: Team Deathmatch / Free-For-All / Battle Royale.
- **Team Deathmatch only**: ally count slider, enemy count slider
  (independent sliders, each capped at 4 — one per non-player slot in a
  4-team pool of up to 4 players/bots per side using up to 2 of the 4 team
  factions at a time). Win-condition selector: **Kill Target** (+ target
  number field), **Lives Per Player** (+ lives number field), or **Last
  Team Standing**.
- **Free-For-All / Battle Royale**: single total bot-count slider (capped
  at 15, since the human player occupies one of 16 available solo-faction
  slots). Battle Royale is Free-For-All with lives forced to 1 — its UI
  hides/grays the lives concept entirely; Free-For-All's own lives are
  fixed at "unlimited until match end" (last-standing is the only
  meaningful win condition when everyone is mutually hostile with no team
  kill-counter to target, so FFA does not expose the TDM win-condition
  selector).
- **Start button**: writes all choices into a new static `MatchSettings`
  class (gamemode, ally/enemy or bot count, win condition + target/lives
  numbers), then performs the same host-start + additive-arena-load flow
  as today's `MainMenuUI.StartMinigame()`. `MatchSettings` is read once by
  `MatchManager` after the arena scene loads — this mirrors the existing
  `NetworkGameManager.PendingSceneNameToWaitFor` static-field-set-before-
  `StartHost()` pattern already used by the minigame flow.

`MatchSettings` is plain host-side config, not networked — consistent with
the host being the one configuring and starting the match.

### 2. Factions (edit-time assets, no runtime `ScriptableObject` creation)

- **4 team factions**: `TeamRed`, `TeamBlue`, `TeamGreen`, `TeamYellow`,
  used by Team Deathmatch (a given match uses 2 of the 4 — allies get one,
  enemies get another). All 6 pairwise relationships across the 4 factions
  are set `Hostile` in the shared table; same-faction is `Allied`
  automatically.
- **16 solo factions**: `Solo1`..`Solo16`, used by Free-For-All / Battle
  Royale. All 120 pairs set `Hostile` in the shared table, giving true
  everyone-vs-everyone with zero new relationship-resolution code.
- Both sets live in the same existing `GlobalRelationships`
  `FactionRelationshipTable` asset — no new table, no runtime table
  mutation.
- `EntityFaction` gets a new public setter (`SetFaction(FactionDefinition)`)
  — the only required API change to the faction system. `MatchManager`
  calls it immediately after `Instantiate`, before the entity's `OnEnable`
  self-registers it into `EntityTargetRegistry`, so registration always
  sees the correct faction.

### 3. Spawning

~16 plain, untagged `SpawnPoint` objects (existing component, unchanged)
placed around the arena at edit time. At match start, `MatchManager`
collects all of them via `FindObjectsByType<SpawnPoint>`, shuffles, and
assigns positions by index:

- **Team Deathmatch**: shuffled list split into 2 even blocks, one per
  active team.
- **Free-For-All / Battle Royale**: one shuffled position per entity
  (player(s) + bots).

This keeps `SpawnPoint` and `SpawnManager`'s existing single-spawn-point
logic (used by the main game and other flows) completely unchanged —
`MatchManager` does its own collection/assignment independently for
match entities.

### 4. Bots — new `DeathmatchBot` prefab variant

A prefab variant of `PatrolRobot` (`Assets/Prefabs/agents/prefab/RobotPatrol/`)
with edit-time module changes only (no runtime toggling):

- `AgentRangedCombatModule` **enabled**.
- `WanderModule`, `PatrolModule`, `HerdModule` **disabled**.
- `ChaseModule` stays enabled (needed to close distance in an arena-sized
  space).
- `CloseCombatModule` stays enabled as a melee fallback.

`MatchManager` instantiates the configured count, calls
`EntityFaction.SetFaction(...)` (team faction for TDM, a unique `SoloN` for
FFA/BR), and places each at its assigned spawn point.

### 5. Player weapon + faction

- New `PlayerRangedCombat.cs` component added to
  `PlayerCharacterNetworked.prefab`: fires the same
  `AgentWeaponDefinition`-based projectile the robots use (either reusing
  `WPN_RobotPistol.asset` or a new player-tuned copy of it), triggered by an
  existing fire input action, aim direction from the existing
  `AimProvider.cs` camera-raycast helper. Projectile spawn is
  server-authoritative (an `Rpc(SendTo.Server)` call from the firing client)
  for consistency with the rest of the networked game.
- `EntityFaction` component also added to the player prefab (previously
  absent). `MatchManager` calls `SetFaction(...)` on each connected
  player's instance post-spawn — team faction for TDM (based on the
  ally/enemy split the host configured; with multiple humans, each new
  human picks a team from a simple team-select prompt shown when they
  join, per team counts still available), or a unique `SoloN` for FFA/BR.
  This is the first time the player becomes visible to
  `EntityTargetRegistry` and thus targetable by (and able to target) bots.

### 6. Win conditions

`MatchManager` subscribes to each spawned entity's `HealthComponent.OnDeath`
and polls `EntityTargetRegistry` membership to track living entities per
faction.

- **Kill Target** (Team Deathmatch only): server tracks a kill counter per
  team; first team to reach the configured target wins. Killed entities
  **respawn**: after a short delay, `MatchManager` re-enables the
  GameObject, repositions it to a freshly shuffled spawn point, and calls
  `HealthComponent.Heal` to full — new logic, since today's
  `HealthReactionModule` only ever despawns permanently.
- **Lives Per Player** (Team Deathmatch only): each entity has a configured
  number of lives; on death it respawns (same mechanism as above) and its
  lives counter decrements; when a team's combined lives are exhausted (all
  members simultaneously out of lives), that team is eliminated for good;
  last remaining team wins.
- **Last Standing**: no respawns — today's existing despawn-on-death
  behavior is used as-is. Team Deathmatch: ends when only one team has
  living members. Free-For-All: ends when one entity remains. Battle
  Royale: Free-For-All with lives forced to 1 (i.e. Last Standing is Battle
  Royale's only win condition — it has no separate win-condition selector).
- On match end, all connected human players see a simple win/loss result
  screen (new minimal UI: outcome message + "Return to Menu" button, no
  further HUD).

### 7. Eliminated players — spectator camera

In Lives Per Player or Last Standing modes, a human player can be
eliminated (out of lives, or despawned) while the match continues for
others. A new lightweight free-fly spectator camera component activates on
that player's local client immediately on elimination (detaches from the
player object, free movement, no interaction with the match), replaced by
the win/loss result screen once `MatchManager` broadcasts match end to all
clients.

## Out of scope / explicit non-goals

- No in-match HUD beyond the win/loss result screen (no health bar, kill
  feed, or live remaining-count display).
- No shrinking-zone/storm/damage-circle system — Battle Royale is
  mechanically Free-For-All with lives forced to 1, not a separate system.
- No runtime `ScriptableObject` creation for factions — team/solo factions
  are a fixed pre-authored pool (4 team + 16 solo), capping match size
  accordingly (max 4v4 for TDM, max 16 entities for FFA/BR).
- No changes to `SpawnPoint`, `SpawnManager`, or the main game's spawn flow
  — `MatchManager` does its own independent spawn-point collection for
  match entities.
- No changes to the existing minigame scene-load flow
  (`MainMenuUI.StartMinigame`, `NetworkGameManager`) beyond inserting the
  new config screen before it and having it pass `MatchSettings` along.
- No matchmaking/lobby integration — this is the existing host-starts-alone-
  or-with-direct-joiners flow, not the separate `StartMultiPlayer` → lobby
  system.

## Open items for implementation planning

- Exact `MatchSettings` field list and where the static class lives.
- Team-select prompt UI for additional human players joining a Team
  Deathmatch match (shown per-joining-client, separate from the host's
  config screen).
- `EntityFaction.SetFaction` — exact signature and whether it needs to
  handle re-registration if called after the entity is already registered
  (e.g. team reassignment), or only ever before first `OnEnable`.
- Respawn timing/placement details (delay length, invulnerability window on
  respawn, whether respawn position avoids currently-occupied spawn
  points).
- Spectator camera implementation specifics (input handling, movement
  bounds within the arena).
- Authoring the 20 new `FactionDefinition` assets (4 team + 16 solo) and
  populating all pairwise relationships in `GlobalRelationships` — bulk
  editor scripting vs. manual asset creation.
- `DeathmatchBot` prefab variant creation and module configuration
  (Editor MCP tooling, consistent with how the prior minigame spec handled
  scene content).
- Placement of ~16 `SpawnPoint` objects in `MinigameArena.unity`.
