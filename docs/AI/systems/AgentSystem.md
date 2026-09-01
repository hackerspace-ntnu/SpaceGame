---
system: AgentSystem
layer: characters
summary: "Creatures, NPCs, enemies and turrets: one AgentController ticking priority-arbitrated behaviour modules"
paths:
  - Assets/Game/Scripts/agents/
  - Assets/Game/Prefabs/agents/
  - Assets/Game/ScriptableObjects/Factions/Core/
  - Assets/Game/Editor/Creatures/
  - .claude/skills/spacegame-agent/SKILL.md
symptoms:
  - "the creature ignores me completely and never attacks anything"
  - "the creature's legs skate instead of walking"
  - "a provoked NPC walks toward me instead of running"
  - "every NPC swings its barrel to follow the host's head"
  - "the agent just stands there doing nothing and the console is clean"
  - "loot drops all over again every time I load the world"
  - "remote copies of the creature slide along with their feet still"
  - "my hand-added component disappeared after someone rebuilt the prefab"
reads_with: [EntitySystem, Vehicles, Combat, NavMeshSystem]
updated: 2026-09-01
---

# Agent / AI System

Creatures, NPCs, enemies and turrets are a prefab plus a stack of `IBehaviourModule` components; one [AgentController.cs](Assets/Game/Scripts/agents/Controller/AgentController.cs) ticks them and arbitrates by priority — never a behaviour tree, never an `AgentController` subclass.

**Scope:** `Assets/Game/Scripts/agents/` (namespace `SpaceGame.Agents`, Assembly-CSharp, no asmdef).
**Related:** [EntitySystem.md](EntitySystem.md) (older overview — profile/module tables there are stale, prefer this file), [MountSystem.md](MountSystem.md), [Persistence.md](Persistence.md), [WeaponSystem.md](WeaponSystem.md), [NavMeshSystem.md](NavMeshSystem.md), skill [.claude/skills/spacegame-agent/SKILL.md](.claude/skills/spacegame-agent/SKILL.md) + its `reference.md`.

## Model

- Three decisions, one owner each: **who to fight** = [AgentTargeting.cs](Assets/Game/Scripts/agents/AI/Targeting/AgentTargeting.cs); **where to go** = [AgentGoal.cs](Assets/Game/Scripts/agents/AI/Goals/AgentGoal.cs); **how to move** = [IMovementMotor.cs](Assets/Game/Scripts/agents/AI/Motors/IMovementMotor.cs). A module duplicating any of the three is the bug this design prevents.
- Modules return `MoveIntent?`. `null` = pass to the next module. `MoveIntent.Idle()` **claims the frame** and starves everything below.
- `ClaimsMovement == false` modules are side effects: ticked unconditionally, must return `null`.
- Facing is a **second channel**: `IFacingModule` overwrites the winning intent's face target after arbitration.
- `AgentTargeting` + `AgentGoal` are auto-added in `AgentController.Awake`. Modules are discovered via `GetComponentsInChildren<MonoBehaviour>(true)`; runtime additions need `RefreshModules()`.
- Legacy `IAgentBrain` ([EnemyBrain.cs](Assets/Game/Scripts/agents/AI/Brains/EnemyBrain.cs), [NpcBrain.cs](Assets/Game/Scripts/agents/AI/Brains/NpcBrain.cs)) is a fallback for old prefabs only — obsolete, do not extend.
- Riding (`MountModule` / `SteerModule` in [Modules/Riding/](Assets/Game/Scripts/agents/Modules/Riding/)) rides on this stack but is documented in [MountSystem.md](MountSystem.md).

## Key types

| Type | File | Role |
|---|---|---|
| `AgentController` | [Controller/AgentController.cs](Assets/Game/Scripts/agents/Controller/AgentController.cs) | Ticks modules, arbitrates, drives motor + animator. `IPersistentEntity` |
| `IBehaviourModule` / `BehaviourModuleBase` / `ModulePriority` | [Modules/Core/](Assets/Game/Scripts/agents/Modules/Core/IBehaviourModule.cs) | `Priority`, `IsActive`, `ClaimsMovement`, `Tick`. Priorities: Scripted 100 · Override 30 · MeleeAttack 23 · RangedAttack 22 · Reactive 20 · Social 15 · Ambient 10 · Personality 5 · Fallback 0 |
| `IFacingModule` | [Modules/Core/IFacingModule.cs](Assets/Game/Scripts/agents/Modules/Core/IFacingModule.cs) | Second channel. Only impls: `AgentRangedCombatModule`, `NpcItemUseModule` |
| `IPresentationModule` | [Modules/Core/IPresentationModule.cs](Assets/Game/Scripts/agents/Modules/Core/IPresentationModule.cs) | Marker: keeps ticking on non-authoritative machines. Only impl: `ChatterModule` |
| `MoveIntent` / `AgentContext` | [AI/Core/](Assets/Game/Scripts/agents/AI/Core/MoveIntent.cs) | `Idle()` / `MoveTo()` / `StopAndFace()`; per-frame snapshot |
| `AgentAuthority` | [Core/AgentAuthority.cs](Assets/Game/Scripts/agents/Core/AgentAuthority.cs) | Cached "does this machine drive me". Caches the `NetworkObject`, never the bool |
| `AgentActionRelay` | [Core/AgentActionRelay.cs](Assets/Game/Scripts/agents/Core/AgentActionRelay.cs) | Encodes/decodes `NetMsg.AgentActed`. Presentation only, both directions |
| `NpcSpawn` | [Core/NpcSpawn.cs](Assets/Game/Scripts/agents/Core/NpcSpawn.cs) | Spawns NPCs network-visible but **save-invisible** (never `GameServices.World.Spawn`) |
| `EntityTargetRegistry` | [Core/EntityTargetRegistry.cs](Assets/Game/Scripts/agents/Core/EntityTargetRegistry.cs) | Static registry; `Query`/`ResolveNearest` by relationship. Fed by `EntityFaction.OnEnable` |
| **Motors** ([AI/Motors/](Assets/Game/Scripts/agents/AI/Motors/)) | | |
| `NavMeshAgentMotor` | [NavMeshAgentMotor.cs](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs) | Everything that walks the baked NavMesh. Also `IMountJumpMotor`, `IRiderControllable`, `ISelfDrivingMotor` |
| `RigidbodyMotor` · `HoverRigidbodyMotor`+`HoverGroundSensor` · `FlyingRigidbodyMotor` · `OrnithopterFlightMotor` | [Motors/](Assets/Game/Scripts/agents/AI/Motors/RigidbodyMotor.cs) | Physics ground vehicles · hovercraft · free 3D flight (pair `AirWanderModule`) · energy flight ([Ornithopter.md](Ornithopter.md)) |
| `LeggedDriver` (abstract) | [LeggedDriver.cs](Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs) | Procedural legged rigs; subclasses in `Assets/Game/Scripts/Creatures/Drivers/`. No NavMeshAgent, no `AgentAnimatorDriver` |
| `AgentTargeting` | [AI/Targeting/AgentTargeting.cs](Assets/Game/Scripts/agents/AI/Targeting/AgentTargeting.cs) | Order −50. Acquisition range auto-widened to longest weapon range + 5 m. Scores by "effective distance" (`currentTargetBias`, `lastAttackerBias`, `occludedPenalty`); sight × `Sandstorms.SightFactorAt` |
| `TargetingProfile` | [TargetingProfile.cs](Assets/Game/Scripts/agents/AI/Targeting/TargetingProfile.cs) | SO overriding every inline `AgentTargeting` field. `MatchManager` swaps it for arena bots |
| `TargetResolution` | [TargetResolution.cs](Assets/Game/Scripts/agents/AI/Targeting/TargetResolution.cs) | `IsViable` / `Refresh` for NON-hostile candidates. Never hand-roll `if (target) return;` |
| `ProvocationModule` | [ProvocationModule.cs](Assets/Game/Scripts/agents/AI/Targeting/ProvocationModule.cs) | Order −40. Peaceful-until-hurt: `leashRange`, `calmDownDelay`, `damageThreshold` |
| `EntityFaction` / `FactionDefinition` / `FactionRelationshipTable` | [Faction/](Assets/Game/Scripts/agents/Faction/EntityFaction.cs) | `Get(a,b)` = `Allied` for a==b, **`Neutral` for any pair with no row**. Assets in `Assets/Game/ScriptableObjects/Factions/Core/`, one table: `GlobalRelationships.asset` |
| **Movement modules** ([Modules/Movement/](Assets/Game/Scripts/agents/Modules/Movement/)) | | |
| `WanderModule` 0 · `AirWanderModule` 0 · `GoalTravelModule` 1 · `HuntModule` 9 · `ApproachModule` 10 · `KeepDistanceModule` 10 · `SearchModule` 19 · `ChaseModule` 20 · `FleeModule` 30 | | Roam · roam in air · walk to `AgentGoal` · walk at nearest hostile anywhere (arena) · close to talk distance · kite · investigate last-known · pursue target · run from a relationship |
| `PatrolModule` / `BasePatrolModule` 0 | [Modules/Patrol/](Assets/Game/Scripts/agents/Modules/Patrol/PatrolModule.cs) | Waypoints or radius-around-base |
| `CoverModule` 21 / `CoverPoint` | [Modules/Cover/](Assets/Game/Scripts/agents/Modules/Cover/CoverModule.cs) | Best self-registering `CoverPoint` relative to the threat |
| `GroundAnchorOnLand` | [Movement/GroundAnchorOnLand.cs](Assets/Game/Scripts/agents/Modules/Movement/GroundAnchorOnLand.cs) | Freezes a Rigidbody on first ground contact (deployed turrets) |
| `FlockingModule` 15 / `HerdModule` 15 | [Modules/Flocking/](Assets/Game/Scripts/agents/Modules/Flocking/FlockingModule.cs) | Separation+alignment+cohesion (needs `nearbyAgentScanRadius`/`Layer`) / rebroadcasts the herd's top intent, self-registers by `herdId` · `FormationModule` 15 + `FormationMath` ([Modules/Formation/](Assets/Game/Scripts/agents/Modules/Formation/FormationModule.cs)) walks a column behind a leader |
| **Combat** ([Modules/Combat/](Assets/Game/Scripts/agents/Modules/Combat/)) | | |
| `CloseCombatModule` 23 | [CloseCombatModule.cs](Assets/Game/Scripts/agents/Modules/Combat/CloseCombatModule.cs) | Melee; `rangeExitFactor` hysteresis + `attackCommitDuration` |
| `AgentRangedCombatModule` 22 | [AgentRangedCombatModule.cs](Assets/Game/Scripts/agents/Modules/Combat/AgentRangedCombatModule.cs) | Owns the whole engagement (backs to `preferredRange`, strafes). Also `IFacingModule` |
| `NpcItemUseModule` 22 | [NpcItemUseModule.cs](Assets/Game/Scripts/agents/Modules/Combat/NpcItemUseModule.cs) | Side-effect: fires a real `InventoryItem` via `EntityEquipmentController` |
| `TurretModule` · `RocketLauncherTurret` · `TurretProjectile` · `WeaponMount` · `WeaponSelector` | [Modules/Combat/](Assets/Game/Scripts/agents/Modules/Combat/TurretModule.cs) | Stationary guns (resolve their own target) · multi-slot weapon rigs |
| `AgentWeaponDefinition` / `AgentFireProfile` / `AgentAimProfile` / `AgentProjectile` | [Weapons/](Assets/Game/Scripts/agents/Weapons/AgentWeaponDefinition.cs) | `Assets > Create > Agents > …` SOs tuning built-in agent weapons |
| `WatchModule` · `FacePlayerModule` · `IdleLookAroundModule` · `InteractionFocusModule` | [Modules/Facing/](Assets/Game/Scripts/agents/Modules/Facing/WatchModule.cs) | Despite the folder these are **movement** modules returning `StopAndFace`, not `IFacingModule`. `ChatterModule` 5 ([Personality/](Assets/Game/Scripts/agents/Modules/Personality/ChatterModule.cs)) speaks the current task line inside `hearingRadius` on a shared static cooldown |
| `PerceptionModule` | [Perception/PerceptionModule.cs](Assets/Game/Scripts/agents/Perception/PerceptionModule.cs) | FOV + LoS. `CanSee` writes memory, `IsVisible` does not |
| `AlertBroadcaster` / `AlertReceiverModule` 19 | [Perception/](Assets/Game/Scripts/agents/Perception/AlertBroadcaster.cs) | Pack aggro via `AgentTargeting.ForceTarget` |
| `NoiseEmitter` / `NoiseReceiverModule` 18 / `NoiseType` | [Audio/](Assets/Game/Scripts/agents/Audio/NoiseEmitter.cs) | Sphere-broadcast hearing; investigate or aggro per type (`Footstep` `Alert` `Hurt` `Death` `Gunshot` `Explosion` `Custom`). [EntityAudioModule.cs](Assets/Game/Scripts/agents/Audio/EntityAudioModule.cs) plays the FMOD side via an `SfxId` catalog slot + optional `EventReference` override |
| `HealthReactionModule` · `EntityLootTable` · `EntityInventoryComponent` · `EntityEquipmentController` | [Entity/](Assets/Game/Scripts/agents/Entity/HealthReactionModule.cs) | Hurt/death triggers + despawn · weighted drops (a MonoBehaviour, no assets) · same `Inventory` as the player · lets NPCs hold and fire the player's `UsableItem` prefabs |
| `NpcTask` · `NpcTaskPlanner` (static, pure) · `NpcTaskModule` 0 · `NpcSpeechTokens` | [Tasks/](Assets/Game/Scripts/agents/Tasks/NpcTask.cs) | A task names a *kind of place* + a dwell time, never a position. Planner is shared by live NPCs and virtual groups so both decide identically |
| `NpcWorldSim` / `NpcGroup` | [World/](Assets/Game/Scripts/agents/World/NpcWorldSim.cs) | Server-only. Groups are VIRTUAL records that lerp along a straight line, becoming SPAWNED prefabs inside `spawnRadius` and unwinding at `despawnRadius` (hysteresis) |
| `AgentAnimatorDriver` | [Animation/AgentAnimatorDriver.cs](Assets/Game/Scripts/agents/Animation/AgentAnimatorDriver.cs) | Writes `SpeedX` `SpeedY` `FallSpeed` `IsGrounded` `IsImmobalized` *(sic)* `IsAiming`; triggers `Hurt` `Die` `ShootRifle` `SpearAttack` |
| `EntityProfile_{BaseAgent,GenericEnemy,NPC,Vehicle}` | [Profiles/](Assets/Game/Scripts/agents/Profiles/EntityProfile_BaseAgent.cs) | Authoring components with a **Generate** button ([EntityProfileEditors.cs](Assets/Game/Editor/Agents/EntityProfileEditors.cs)). Only these four exist. [DuneRiderController.cs](Assets/Game/Scripts/agents/Controller/DuneRiderController.cs) is a direct-drive rider vehicle that bypasses the module stack |

## Flows

**Per frame — `AgentController.Update`:**
1. `RefreshAuthority()` — if this machine does not own the agent: `TickPresentation(dt)` (`IPresentationModule` only) and **return**.
2. Bail if `Motor == null`. Build `AgentContext`.
3. Tick every side-effect module (`ClaimsMovement == false`) in discovery order, unconditionally.
4. Tick movement modules, priority DESC (ties by component order); **first non-null wins**. None → legacy `IAgentBrain` → `MoveIntent.Idle()`.
5. `ApplyFacingOverride()` — `IFacingModule[]` by `FacingPriority` DESC, first `true` wins, overwrites `FacePosition`.
6. Speed-variation drift applied to `SpeedMultiplier` (`MoveToPosition` intents only; phase is saved).
7. `Motor.Tick(intent, dt)` → `animatorDriver.Tick(Motor.Velocity, Motor.IsImmobile, intent.IsRunning)`.

**Execution order:** −100 `NavMeshAgentMotor` · −50 `AgentTargeting` · −40 `ProvocationModule` · 0 `AgentController` · 50 `LeggedDriver` · 100 `LeggedLocomotion` (asmdef `SpaceGame.Locomotion`) · LateUpdate `AgentAnimatorDriver`, `EntityEquipmentController` aim.

**Attack:** authority module fires → damages/spawns locally → `AgentActionRelay.Broadcast(this, AgentAction.Melee|Ranged, origin, dir)` → peers `TryReadRay` and draw **presentation only** (no damage; `NetDamage` would bill the target once per machine).

**Caravan:** `NpcWorldSim` ticks `NpcGroup` records (lerp + `NpcTaskPlanner`) → player within `spawnRadius` → `NpcSpawn.Create` per `NpcGroupMemberSpec` in formation → members run their own AI → `despawnRadius` folds them back into the record.

## Multiplayer

- The **owner** machine runs the whole stack; every other machine runs only `IPresentationModule`s. Ownership, not server-ness — [AgentAuthority.cs](Assets/Game/Scripts/agents/Core/AgentAuthority.cs). Modules must contain **no** authority check; the controller already gated the tick.
- Body transform + animator replicate through `NetworkObject` / [NetAuthority.cs](Assets/Game/Scripts/Core/Multiplayer/Authority/NetAuthority.cs), which disables listed simulation drivers on remotes.
- Attacks replicate as `NetMsg.AgentActed` (69) via `AgentActionRelay`; damage stays on the deciding machine.
- `NpcWorldSim` is server-only. `NpcSpawn.Create` must be called behind `Network.Simulates`.
- An `InventoryItem` an NPC carries needs its `itemPrefab` registered as a network prefab; projectiles and equipped visuals must **not** be. See [spacegame-multiplayer](.claude/skills/spacegame-multiplayer/SKILL.md).

## Persistence

`AgentController` implements `IPersistentEntity`, so **every agent is save-eligible with no opt-in**. Savers live outside this tree in [Core/Persistence/Adapters/](Assets/Game/Scripts/Core/Persistence/Adapters/AgentStateSaveable.cs): `AgentStateSaveable` (key `"agent"` — target/lastAttacker as `SaveRef`, last-known position, `timeSinceSeen`, `profileId`, patrol index/direction), plus `PatrolSaveable`, `SearchSaveable`, `AlertResponseSaveable`, `NoiseInvestigationSaveable`; and the generic `TransformSaveable` + `HealthSaveable`. Add them inside the prefab's builder, then run `Tools/Save System/Wire Saveable Prefabs`. Details: [Persistence.md](Persistence.md).

## Gotchas

- **No `EntityFaction` → invisible to every targeting module, silently.** Needs both a `FactionDefinition` and `GlobalRelationships.asset`. `EntityFaction.Ensure(go, faction, table)` on spawn paths.
- **Peaceful = zero relationship rows** + `ProvocationModule` (`leashRange` ≤ `AgentTargeting.loseRange`). Adding a row "for completeness" makes the whole faction attack on sight. `FaunaFaction.asset` appears in zero rows; `WildlifeFaction` is already Hostile to the player.
- **A module returning `MoveIntent.Idle()` while merely waiting starves everything below it.** Return `null`.
- **A script-added module keeps priority 0** — Unity does not call `Reset()` for `AddComponent`, so it ties with wander. Set `priority` explicitly.
- **`*Builder` scripts in `Assets/Game/Editor/` overwrite prefabs wholesale** with no warning; hand-added components vanish on rebuild. `GolemBuilder` lost the Golem's `SaveableEntity` this way.
- **Feet skate** when only `animationSpeedMultiplier` / `walkAnimBoost` were tuned — those pick the *clip*. `animatorSpeedScale = groundSpeed / strideSpeed` sets the *rate*, applied once in `Awake`, and is global (attacks slow with it).
- **Trigger names disagree by default:** driver fires `"Die"`, `HealthReactionModule.dieAnimTrigger` = `"Death"`, `CloseCombatModule` = `"Meele"`, `AgentRangedCombatModule` = `"AssualtShoot"` — the misspellings are real. `Golem.controller` carries both `Die` and `Death`.
- **`PerceptionModule.occlusionLayers` left at `Nothing`** falls back with a warning and the agent shoots through walls intermittently.
- **A `LeggedLocomotion` left in `NetAuthority.simulationDrivers`** makes remote copies slide with still feet.
- **Provoked NPC walks instead of running:** `NavMeshAgent.speed` must be the **run**; scale `walkSpeedMultiplier` down from it, because `ChaseModule` asks for `isRunning`.
- **Loot duplicates on load** — a death reaction ran during a restore. Check `HealthComponent.IsRestoring`.
- **A spawner-owned group duplicates on load** unless members call `SaveableEntity.DisownToExternal()`; this is why `NpcSpawn` deliberately avoids `GameServices.World.Spawn`.
- **Every NPC aims at the host's head** unless `EntityEquipmentController` sets `Weapon.ExternallyAimed` (keep `aimHeldItem` on) — the server owns every NPC and `Weapon` aims at `Camera.main` for its owner.
- **Two `AgentTargeting` components** — `[RequireComponent]` may already have added one; guard builders with a `GetComponent` null check.
- Bone-parented rigs freeze mid-stride off screen: `animator.cullingMode = AlwaysAnimate`. A humanoid FBX re-export can silently downgrade to `isHuman = false` — the character then stands still with a clean console.

## Extending

**New creature** (copy [GolemBuilder.cs](Assets/Game/Editor/Creatures/GolemBuilder.cs), menu `Tools/Creatures/Build Golem Prefab`):
1. Mesh + rig via the `blender-model` skill; verify `avatar.isHuman` for humanoids.
2. Prefab under `Assets/Game/Prefabs/agents/…`. Reuse the FBX's own `Animator`, `applyRootMotion = false`, `cullingMode = AlwaysAnimate`.
3. Collider + kinematic `Rigidbody` (`useGravity = false` for NavMesh creatures).
4. Motor: `NavMeshAgent` + `NavMeshAgentMotor` (speed = run, `walkSpeedMultiplier` = walk/run), or `LeggedDriver`, or `FlyingRigidbodyMotor`, or `RigidbodyMotor`.
5. `AgentController` on the root; assign `MotorComponent` + `animatorDriver`.
6. `EntityFaction` (faction + `GlobalRelationships.asset`), then `HealthComponent` + `HealthReactionModule` (+ `EntityLootTable`).
7. Modules: one Fallback (`WanderModule`/`PatrolModule`) plus reactive/combat ones, each with an explicit `priority`. Perception: `PerceptionModule` (set `occlusionLayers`), `AlertBroadcaster`/`AlertReceiverModule`, `NoiseEmitter`/`NoiseReceiverModule`.
8. Animator controller in `Assets/Game/Art/Animations/Creatures/` with the exact parameter names above; set `animatorSpeedScale`.
9. `SceneTracked` (`policy = Migrate`) for roamers; saveables + `Tools/Save System/Wire Saveable Prefabs`; `NetworkObject` + `NetworkedHealthComponent` + `NetAuthority` + `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`.
10. Into the world via `NpcWorldSim.templates` (inlined in `Assets/Game/Scenes/world/persistentScene.unity`), `SettlementConfig.asset → robotPrefabs`, `MatchManager.deathmatchBotPrefab`, or a hand-placed chunk instance.

**New behaviour module** — last resort; first check whether existing data (`TargetingProfile`, `NpcTask[]`, a relationship row, a different priority) answers it:
1. Decides *where*, not *how*? Set `ClaimsMovement => false` and write `AgentGoal`; `GoalTravelModule` walks there (`NpcTaskModule` is the worked example).
2. Decides only where the body *points*? Implement `IFacingModule` — not a movement module returning `StopAndFace`.
3. Otherwise subclass `BehaviourModuleBase` in the matching `Modules/` folder; `Reset() => SetPriorityDefault(...)`; override `ModuleDescription`.
4. In `Tick`: read the target from `context.Targeting` (never query the registry for a hostile; use `TargetResolution.Refresh` for non-hostiles); return `null` to pass; run expensive queries on an interval; keep collections instance-level, not static.
5. Reset per-module state in `OnEnable` (respawn, streaming and loads re-enable agents). No authority checks inside the module.
6. Attack-style modules need enter/exit hysteresis (`rangeExitFactor`) or the winner flips every frame at the range boundary and the agent stutters.
