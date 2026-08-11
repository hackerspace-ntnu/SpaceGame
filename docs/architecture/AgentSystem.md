# AGENT.CLAUDE — Claude Reference for the Agent System

Purpose: a single file Claude can read to answer any question about the agent/AI system in `Assets/Scripts/agents/` — how components fit together, what every module does, and how to assemble new agents from parts.

The system is a modular **drag-and-drop behaviour architecture**. One `AgentController` ticks a set of `IBehaviourModule` components each frame. Each module can return a `MoveIntent` or pass. Priority-based arbitration picks the winner; side-effect modules run unconditionally.

---

## 1. Big-picture architecture

```
                  ┌──────────────────────────────────────┐
                  │            AgentController           │
                  │  (ticks modules, picks winner)       │
                  └──────────────┬───────────────────────┘
                                 │ MoveIntent
                                 ▼
 ┌─────────────────┐   ┌──────────────────────┐   ┌──────────────────┐
 │  Movement       │   │  IMovementMotor      │   │ AgentAnimator    │
 │  modules        │──►│  (NavMeshAgentMotor) │──►│ Driver           │
 │  (Chase, Patrol)│   │                      │   │ (anim params)    │
 └─────────────────┘   └──────────┬───────────┘   └──────────────────┘
                                  ▼
                          ┌──────────────┐
                          │ NavMeshAgent │  (Unity's pathfinder)
                          └──────────────┘

 Side-effect modules (RangedAttackModule, EntityAudioModule, etc.)
 are ticked every frame regardless of who owns movement.
```

**Per-frame flow** (inside `AgentController.Update`, see [AgentController.cs](Assets/Scripts/agents/controller/AgentController.cs)):

1. Build an `AgentContext` snapshot (position, velocity, reached destination, nearby agents if enabled).
2. Tick every `ClaimsMovement == false` module (side effects — never returns a MoveIntent).
3. Iterate `ClaimsMovement == true` modules highest-priority first; the first one to return a `MoveIntent` wins.
4. If no module wins, fall back to the legacy `IAgentBrain` if present, else `MoveIntent.Idle()`.
5. Apply speed-variation drift, pass the intent to the motor.
6. Drive the animator from motor velocity + running flag.

Key contracts:

| Interface / struct | File | Purpose |
|---|---|---|
| `IBehaviourModule` | [IBehaviourModule.cs](Assets/Scripts/agents/modules/IBehaviourModule.cs) | Module contract: `Priority`, `IsActive`, `ClaimsMovement`, `Tick()` |
| `BehaviourModuleBase` | [BehaviourModuleBase.cs](Assets/Scripts/agents/modules/BehaviourModuleBase.cs) | Abstract base; exposes priority + active toggle in Inspector |
| `ModulePriority` | same file | Named priority constants (`Scripted=100`, `Override=30`, `MeleeAttack=23`, `RangedAttack=22`, `Reactive=20`, `Social=15`, `Ambient=10`, `Personality=5`, `Fallback=0`) |
| `AgentContext` | [AgentContext.cs](Assets/Scripts/agents/AI/AgentContext.cs) | Frame snapshot passed to every module |
| `MoveIntent` | [MoveIntent.cs](Assets/Scripts/agents/AI/MoveIntent.cs) | `Idle` / `MoveTo` / `StopAndFace`; carries speed, facing override, stop distance |
| `IMovementMotor` | [IMovementMotor.cs](Assets/Scripts/agents/AI/motor/IMovementMotor.cs) | Applies a `MoveIntent` (NavMesh, rigidbody, or any motor) |
| `IAgentBrain` (legacy) | [IAgentBrain.cs](Assets/Scripts/agents/AI/brains/IAgentBrain.cs) | Old single-brain fallback — still supported; don't extend |

---

## 2. Required Unity components on every agent

Every agent **must** have this baseline. The editor's `Generate` button on any `EntityProfile_*` wires all of them via `EntityProfileEditorUtils.SetupBaseComponents` ([EntityProfileEditors.cs:86](Assets/Editor/EntityProfileEditors.cs#L86)).

| Component | Role | Notes |
|---|---|---|
| `Rigidbody` | Collision layer queries | **Kinematic + no gravity**. NavMeshAgent owns movement; the rigidbody is only for `OverlapSphere`/layer lookups. |
| `CapsuleCollider` | Physics/collision shape | Required for hits (projectile impacts, interaction raycasts). |
| `NavMeshAgent` | Unity pathfinding | Requires a baked NavMesh in the scene. Set speed, radius, height here. |
| `NavMeshAgentMotor` | `IMovementMotor` impl | Translates `MoveIntent` → `NavMeshAgent.SetDestination` / `isStopped` / facing. Also implements `IMountJumpMotor` and `IMountLeapMotor`. See [NavMeshAgentMotor.cs](Assets/Scripts/agents/AI/motor/NavMeshAgentMotor.cs). |
| `AgentController` | Module coordinator | Main tick loop. Auto-resolves motor + animator if the Inspector slots are empty. |
| `Animator` | Unity's animator | Usually on a child mesh; needs params `SpeedX`, `SpeedY`, `FallSpeed`, `IsGrounded`, `IsImmobalized`, and optional triggers (`Hurt`, `Death`, `Meele`, `AssualtShoot`, `IsAiming`). |
| `AgentAnimatorDriver` | Animator bridge | Converts motor `Velocity` → local-space `SpeedX/Y`. Walk speed is boosted with `walkAnimBoost` so walk anims don't look sluggish. [File](Assets/Scripts/agents/animation/AgentAnimatorDriver.cs). |
| `HealthComponent` | HP tracking | Standard damage/death events. |
| `HealthReactionModule` | Reacts to damage | Plays `Hurt`/`Death` triggers, emits `NoiseType.Hurt`/`Death`, runs threshold reactions (e.g. enable `FleeModule` at 30% HP), despawn timer. [File](Assets/Scripts/agents/entity/HealthReactionModule.cs). |
| `EntityFaction` | Faction tag | Without it, the agent **cannot target or be targeted correctly**. Assign a `FactionDefinition` + `FactionRelationshipTable`. [File](Assets/Scripts/agents/faction/EntityFaction.cs). |
| `EntityAudioModule` | Footstep/aggro/ambient | Plays FMOD + emits `NoiseType.Footstep` on each step, `NoiseType.Alert` on aggro transition. [File](Assets/Scripts/agents/audio/EntityAudioModule.cs). |
| `NoiseEmitter` | Sound propagation | `Emit(type, radius)` calls `OverlapSphere` and pokes `NoiseReceiverModule`s. Footsteps, hurt, gunshots all route through this. |
| `EntityInventoryComponent` | Inventory slots | Same underlying `Inventory` the player uses. Required for loot drops and equipment. |
| `EntityLootTable` | Death drops | Drops starting inventory + weighted `lootEntries` when `HealthComponent.OnDeath` fires. |

Optional baseline extras:

- `EntityEquipmentController` + `handSocket` Transform — for NPCs that carry items.
- `RegisterAsTarget` — on anything that other agents should find via `EntityTargetRegistry` (the player usually).
- `AlertBroadcaster` — lets this agent broadcast target sightings to nearby allies.

---

## 3. Targeting: how agents find things

Three overlapping systems are in play — modules use all three:

1. **`EntityTargetRegistry`** (static) — tag-keyed registry. `RegisterAsTarget` on the player adds it under `"Player"`. Modules call `EntityTargetRegistry.Resolve("Player", position)` to get the nearest live transform. Faster than `GameObject.FindWithTag` and survives respawn without stale references. [File](Assets/Scripts/agents/EntityTargetRegistry.cs).
2. **`EntityFaction` + `FactionRelationshipTable`** — every targeting module has a `requiredRelationship` field (`Hostile` / `Neutral` / `Allied`). `EntityFaction.IsValidTarget(owner, candidate, required)` is the shared gate. Unfactioned entities can never be Allied and cannot target at all.
3. **`CoverPointRegistry`** (static, in [CoverPoint.cs](Assets/Scripts/agents/modules/CoverPoint.cs)) — self-registering cover markers. `CoverModule` asks `CoverPointRegistry.FindBest(self, threat, radius)`.

**All module target fields resolve in this order**: serialized `target` Transform → `EntityTargetRegistry.Resolve(targetTag)` → faction check → accept.

---

## 4. Movement modules (claim movement)

All inherit from `BehaviourModuleBase`. Only the one that wins the priority race drives the motor this frame.

| Module | Default priority | Effect | Key fields | Dependencies |
|---|---|---|---|---|
| `InteractionFocusModule` | `Scripted` (100) | Stops and faces a target for N seconds. Call `FocusOn(t, duration)` externally. | (none — driven externally) | — |
| `SteerModule` | `Scripted` (100) | Rider-steered movement when mounted. Tank steer, jump/leap, 1st/3rd person camera. Only claims frame while rider inputs. | `moveSpeed`, `turnSpeed`, `leapHoldTime`, camera fields | `MountController`, Unity Input System actions |
| `MountModule` | `Fallback` (0) | Mount lifecycle wrapper; returns null from Tick(). When `allowAISelfMovementWhenMounted=false`, suppresses all other modules while mounted. | `allowAISelfMovementWhenMounted` | `MountController`, `SteerModule` |
| `FleeModule` | `Override` (30) | Runs away when threat is within `triggerRadius`; stops past `safeRadius`. | `triggerRadius`, `safeRadius`, `fleeSpeedMultiplier`, `ignoreFaction` | NavMesh, optionally `EntityFaction` |
| `CloseCombatModule` | `MeleeAttack` (23) | `StopAndFace` when in `attackRange`, deals `attackDamage` on cooldown. Null when out of range (Chase takes over). | `attackRange`, `attackCooldown`, `attackDamage`, `attackAnimTrigger` | target w/ `HealthComponent` |
| `RangedAttackModule` | `RangedAttack` (22) | `StopAndFace` inside `[minRange, maxRange]` band, fires `projectilePrefab` from `muzzleTransform`. Retreats to `minRange` when too close (unless `CloseCombatModule` is also present). | `projectilePrefab`, `muzzleTransform`, `minRange`, `maxRange`, `fireCooldown`, `burstCount`, `spreadAngle`, `leadTarget` | `Animator` (optional for triggers) |
| `AgentRangedCombatModule` | `RangedAttack` (22) | Same as above but driven by three ScriptableObject assets (`AgentWeaponDefinition`, `AgentFireProfile`, `AgentAimProfile`) + optional `WeaponMount`. Supports `requireLineOfSight`. | `weapon`, `fireProfile`, `aimProfile`, `muzzleSocket`, `muzzleForwardOffset`, `spawnWeaponModel` | `PerceptionModule` if LoS required |
| `CoverModule` | `Reactive+1` (21) | Finds nearest `CoverPoint`, moves to it, then `StopAndFace` the threat. Vacates when threat > `threatRange`. | `threatRange`, `coverSearchRadius`, `speedMultiplier` | `CoverPoint`s in scene |
| `ChaseModule` | `Reactive` (20) | Detects target inside `detectRange` (requires FoV+LoS if `PerceptionModule` is present; inner `proximityDetectRange` bypasses LoS). Always `MoveTo` while holding target. Loses at `loseTargetRange`. | `detectRange`, `proximityDetectRange`, `loseTargetRange`, `chaseStopDistance`, `chaseSpeedMultiplier` | Optional: `PerceptionModule`, `AlertBroadcaster`, `HerdModule` |
| `ApproachModule` | `Ambient` (10) | Walks toward target, stops at `conversationDistance`. Faces when arrived. | `detectRadius`, `conversationDistance`, `speedMultiplier` | — |
| `KeepDistanceModule` | `Ambient` (10) | Backs away if threat closer than `preferredDistance`, otherwise `StopAndFace`. Great with `RangedAttackModule` for kiting. | `detectRadius`, `preferredDistance`, `speedMultiplier` | — |
| `WatchModule` | `Ambient` (10) | Stops and faces a target in range. No movement. | `detectRadius`, `requiredRelationship` | — |
| `FacePlayerModule` | `Ambient` (10) | Stops & faces the Player tag when inside `triggerRadius`. Ignores faction. | `triggerRadius`, `targetTag` | — |
| `FlockingModule` | `Social` (15) | Separation + cohesion + alignment using nearby agent buffers. | `separationRadius`, `perceptionRadius`, weights, `minNeighbours` | `AgentController.nearbyAgentScanRadius > 0` + `nearbyAgentLayer` |
| `HerdModule` | `Social` (15) | Static-registry per `herdId`. Distributes the frame's best broadcast intent so members fan out on a circle, settle evenly, and pass reactive intents through unchanged. | `herdId`, `settleRadius`, `settleStopDistance`, `combatSpreadRadius`, `settleTimeoutSeconds` | All herd members must share `herdId` |
| `SearchModule` | `Reactive-1` (19) | When `ChaseModule` just lost its target, go to the last-known position for `searchDuration` seconds. Prefers `PerceptionModule.LastKnownPosition` if present, else `ChaseModule.LastKnownPosition`. | `searchDuration`, `stopDistance`, `speedMultiplier` | `ChaseModule` (required) |
| `AlertReceiverModule` | `Reactive-1` (19) | On `ReceiveAlert(target, pos)`, forces Chase to target. If Chase can't confirm, drives the agent to the alert position for `alertDuration`. | `alertDuration`, `stopDistance` | `ChaseModule` |
| `NoiseReceiverModule` | `Reactive-2` (18) | Hears `NoiseEmitter` events. `aggroOn` mask forces Chase; `investigateOn` mask drives to the sound origin. | `investigateOn`, `aggroOn`, `investigateDuration` | `ChaseModule` for aggro; scene `NoiseEmitter`s |
| `IdleLookAroundModule` | `Personality` (5) | While idle, periodically turns a few degrees to add life. | `minInterval`, `maxInterval`, `turnAngle`, `lookDuration` | — |
| `PatrolModule` | `Fallback` (0) | Two modes: `RadiusBased` picks random NavMesh points around `radiusCenter` (or spawn); `PatrolPoints` cycles Transforms (SequentialLoop / PingPong / Random). Waits at each point. | mode, `patrolRadius`, `patrolPoints`, `selectionMode`, wait times | — |
| `BasePatrolModule` | `Fallback` (0) | Simpler anchored radius patrol around `baseTransform` (or spawn). Pair with `HerdModule` for group roaming. | `baseTransform`, `patrolRadius`, `minDestinationDistance`, wait times, `speedMultiplier` | — |
| `WanderModule` | `Fallback` (0) | Truly random roam. Can be limited (`wanderRadius`) or free-roam across the NavMesh. | `limitWanderRadius`, `wanderRadius`, `freeRoamRadius` | — |

**Priority arbitration example** (a melee-and-ranged robot with perception + cover):

```
frame → InteractionFocus(100)? no
      → SteerModule(100)?        no (not mounted)
      → FleeModule(30)?          no (HP ok, no threat close)
      → CloseCombatModule(23)?   YES if target ≤ attackRange  → StopAndFace + hit
      → RangedAttackModule(22)?  YES if target in [min,max]   → StopAndFace + fire
      → CoverModule(21)?         YES if threat in range + cover found
      → ChaseModule(20)?         YES if target visible/known   → MoveTo
      → HerdModule(15)?          YES broadcast spread if herd active
      → AlertReceiver(19)        ...
      → NoiseReceiver(18)        ...
      → IdleLookAround(5)        personality filler while idle
      → BasePatrol / Wander(0)   fallback loop
```

---

## 5. Side-effect modules (do not claim movement)

These inherit `BehaviourModuleBase` and override `ClaimsMovement => false`, or are plain `MonoBehaviour`s that other modules consult.

| Component | Type | What it does |
|---|---|---|
| `PerceptionModule` | plain MB | Authoritative FoV + LoS. `CanSee(t)` (FoV + LoS from eye, updates last-known), `HasLineOfSight(t)` (LoS only), `HasLineOfSightFrom(origin, t)` (from a muzzle). Tracks `LastKnownPosition`, `TimeSinceLastSeen`, `memoryDuration`. Emits `NoiseType.Alert` when spotting. Needs `eyeTransform` (bone), `occlusionLayers`, `fieldOfViewAngle`. [File](Assets/Scripts/agents/perception/PerceptionModule.cs). |
| `AlertBroadcaster` | plain MB | `Broadcast(target, lastKnown)` calls `OverlapSphere` within `alertRadius` on `receiverLayers` and pokes every `AlertReceiverModule` belonging to an allied `EntityFaction` (when `alliedOnly`). Call from `ChaseModule` on first spot. |
| `NoiseEmitter` | plain MB | `Emit(NoiseType, radius)` → `OverlapSphereNonAlloc` on `receiverLayers` → `NoiseReceiverModule.OnNoiseHeard`. |
| `EntityAudioModule` | plain MB | Auto-emits `NoiseType.Footstep` while moving; `NoiseType.Alert` on the Chase aggro edge; plays FMOD ambient SFX on a random interval. |
| `WeaponSelector` | plain MB | Place on a hand bone. At Awake picks melee vs ranged child model based on which combat module is active on the agent. |
| `WeaponMount` | plain MB | List of `WeaponSlot { model, muzzle, definition }`. `AgentRangedCombatModule` reads `ActiveDefinition` / `ActiveMuzzle` when present (overrides its own serialized weapon + muzzle). |
| `HealthReactionModule` | plain MB | HP-driven reactions: anim triggers, noise emit, threshold-based module enable/disable (e.g. enable `FleeModule` at 30% HP), death despawn. |
| `EntityLootTable` | plain MB | Drops `EntityInventoryComponent` contents + rolls `LootEntry`s on death. |
| `RangedAttackModule` / `AgentRangedCombatModule` | movement module | Listed under movement — they claim the frame with `StopAndFace` while in band. They do *not* block Chase when out of band (return null so Chase can approach). |

---

## 6. Data assets (ScriptableObjects)

| Asset | Create | Contents |
|---|---|---|
| `FactionDefinition` | Assets ▸ Create ▸ Factions ▸ Faction Definition | `factionName`, `debugColor` |
| `FactionRelationshipTable` | Assets ▸ Create ▸ Factions ▸ Relationship Table | List of `(factionA, factionB, relationship)` tuples; same faction is always Allied, missing pairs default Neutral |
| `AgentWeaponDefinition` | Assets ▸ Create ▸ Agents ▸ Weapon Definition | `weaponModelPrefab`, `projectilePrefab`, `projectileSpeed`, `damagePerHit`, `fireSound` |
| `AgentFireProfile` | Assets ▸ Create ▸ Agents ▸ Fire Profile | `minRange`, `maxRange`, `fireCooldown`, `burstCount`, `burstInterval` |
| `AgentAimProfile` | Assets ▸ Create ▸ Agents ▸ Aim Profile | `baseSpreadAngle`, `spreadGrowthPerBurstShot`, `aimLeadFactor`, `requireLineOfSight` |

---

## 7. MoveIntent semantics (the module→motor contract)

`MoveIntent` ([MoveIntent.cs](Assets/Scripts/agents/AI/MoveIntent.cs)):

- `Idle()` — claim the frame but request no movement. Use when waiting, paused, or intentionally holding.
- `MoveTo(pos, stopDistance, speedMultiplier, overrideFacingDirection, facingDirection, isRunning)` — path to `pos`. `isRunning=true` tells the motor to use full NavMesh speed (walking uses `walkSpeedMultiplier`, default 0.65). `overrideFacingDirection` disables NavMesh auto-rotation so another system (e.g. `MountController`) can own facing.
- `StopAndFace(worldPos)` — halt the path, rotate toward `worldPos`. Used by every "in range, stand & act" module.

Returning `null` from `Tick()` means "pass" — arbitration proceeds to the next module. Side-effect modules (`ClaimsMovement=false`) must always return null.

---

## 8. Profile → Generate workflow

`EntityProfile_*` MonoBehaviours are data-only. The custom editor at [EntityProfileEditors.cs](Assets/Editor/EntityProfileEditors.cs) draws a big green "Generate" button that:

1. Calls `EntityProfileEditorUtils.SetupBaseComponents` — adds Rigidbody, CapsuleCollider, NavMeshAgent, NavMeshAgentMotor, AgentController, AgentAnimatorDriver, HealthComponent, HealthReactionModule.
2. Calls `GetOrAdd<>` for every behaviour module this archetype needs.
3. Writes profile fields into those modules via `SerializedObject` (`SetFloat`, `SetInt`, `SetObject`, `SetLayerMask`, `SetString`, `SetBool`).
4. `SetModuleActive` toggles which modules start enabled (e.g. disables `WatchModule` on NPCs that shouldn't auto-track the player).

Existing profiles (all in [Assets/Scripts/agents/profiles/](Assets/Scripts/agents/profiles/)):

- `EntityProfile_BaseAgent` — minimal (HP, despawn). Does not add any behaviour modules; use as a starting baseline.
- `EntityProfile_NPC` — friendly wanderer. Adds `FleeModule`+`WanderModule`+`WatchModule`+`ApproachModule`+`KeepDistanceModule`+`InteractionFocusModule` (latter three inactive by default).
- `EntityProfile_GenericEnemy` — full robot: BasePatrol + Herd + Chase + Perception + Search + AlertBroadcaster + AlertReceiver + NoiseReceiver + NoiseEmitter + Melee + Ranged + KeepDistance. `attackStyle` enum (`Melee`/`Ranged`/`KitingRanged`/`Mixed`) flips which modules start active.

After Generate the profile component can be deleted — all modules are fully wired on the prefab.

---

## 9. How to create a new agent (from scratch)

**Option A — fastest, use a profile**

1. Duplicate a mesh/prefab in `Assets/Prefabs/entities/` and rename.
2. Add the matching `EntityProfile_*` component.
3. Fill in the Inspector fields (HP, ranges, patrol base, `targetTag`, layer masks, projectile prefab…).
4. Click **⚙ Generate**. All modules appear.
5. Assign the scene-specific things that can't live on a prefab (faction asset + relationship table on `EntityFaction`; `baseTransform` for `BasePatrolModule`; `muzzleTransform` on ranged modules; `AgentController.nearbyAgentLayer`).
6. Bake the NavMesh in the scene if it isn't already.
7. Optionally remove the profile component — modules are persistent.

**Option B — manual composition**

1. Add the baseline 12 components (see §2). The simplest way is to add `EntityProfile_BaseAgent` and click Generate, then delete the profile.
2. Pick behaviour modules. A useful mental checklist:
   - *What does it do when nobody's around?* → `WanderModule` / `PatrolModule` / `BasePatrolModule` (pick one).
   - *Does it need to group with others?* → add `HerdModule` (share `herdId`) and/or `FlockingModule` (enable scan radius on `AgentController`).
   - *Does it react to enemies?* → `ChaseModule` + `PerceptionModule` + `SearchModule` for pursuit; `FleeModule` for cowards; `KeepDistanceModule` for kiters.
   - *Does it attack?* → `CloseCombatModule` (melee) and/or `RangedAttackModule`/`AgentRangedCombatModule` (ranged). Always add `CloseCombatModule` if ranged should push close rather than retreat.
   - *Does it hear / receive alerts?* → `NoiseReceiverModule` + `AlertReceiverModule`.
   - *Does it warn allies?* → `AlertBroadcaster` (and optionally `PerceptionModule` calls `NotifySpotted` which also emits noise).
   - *Personality filler?* → `IdleLookAroundModule`.
   - *Mountable?* → `MountController` + `MountModule` + `SteerModule` + seat/dismount transforms. Optionally `IMountJumpMotor`/`IMountLeapMotor` (NavMeshAgentMotor already implements both).
3. For each module, check the default priority via its `Reset()` method — adjust only if the archetype calls for it (e.g. make `WatchModule` win over `ChaseModule` for a security camera by raising it above 20).
4. Assign every `target` / `targetTag`. Default is `"Player"` — a `RegisterAsTarget` on the player keeps the registry populated.
5. Scene wiring: bake NavMesh, place `CoverPoint`s if using `CoverModule`, set layers on `AlertBroadcaster.receiverLayers`, `NoiseEmitter.receiverLayers`, `AgentController.nearbyAgentLayer`.

---

## 10. Behaviour recipes (combinations → observable behaviour)

Each recipe lists the minimum active module set that produces the behaviour. The base 12 components (§2) are implied.

### Passive wanderer
- `WanderModule`
- → Roams aimlessly within `wanderRadius`. Stops briefly between destinations.

### Friendly NPC that flees danger and runs over to chat
- `FleeModule` (Override) + `WanderModule` (Fallback) + `ApproachModule` (Ambient, target = Player) + `InteractionFocusModule` (Scripted)
- → Wanders until the player steps inside `ApproachModule.detectRadius`, walks over and stops at `conversationDistance`. If hit or threatened, flees. Dialog system can call `InteractionFocusModule.FocusOn` to lock the NPC for a cutscene.

### Ranged kiter
- `RangedAttackModule` (RangedAttack) + `KeepDistanceModule` (Reactive) + `ChaseModule` (Reactive) + `WanderModule` (Fallback)
- → Chases when out of range; once inside the firing band, `RangedAttackModule` stands & fires. If the target gets too close, `KeepDistanceModule` pushes backward. No `CloseCombatModule` ⇒ `RangedAttackModule` actively retreats when inside `minRange`.

### Melee brute (Phil-style charger)
- `CloseCombatModule` (MeleeAttack) + `ChaseModule` (Reactive) + `PerceptionModule` + `SearchModule` + `BasePatrolModule` (Fallback) + `HerdModule` (Social, optional)
- → Patrols base. Spots player via FoV + LoS, chases, hits. Loses sight ⇒ goes to last known for a few seconds.

### Mixed herd (robot band)
- Each member: `BasePatrolModule` + `HerdModule` (same `herdId`) + `ChaseModule` + `PerceptionModule` + `SearchModule` + `CloseCombatModule` + `RangedAttackModule` + `KeepDistanceModule` + `AlertBroadcaster` + `AlertReceiverModule` + `NoiseReceiverModule`
- → Patrol as a group (HerdModule distributes + settles). One sees the player → `AlertBroadcaster` wakes the others. Individual members pick melee or ranged based on distance; kiters back away when crowded.

### Cover shooter (Cath-style)
- `CoverModule` (Reactive+1 = 21) + `RangedAttackModule` (RangedAttack = 22) + `ChaseModule` (Reactive = 20) + `PerceptionModule`
- → Cover wins over chase. `RangedAttackModule` (priority 22) beats cover ⇒ fires whenever in band, otherwise CoverModule moves to cover and stands firing from behind it.

### Skittish wildlife (DesertRat)
- `FleeModule` (Override) + `WanderModule` (Fallback) + `IdleLookAroundModule` (Personality) + no faction, `FleeModule.ignoreFaction = true`
- → Wanders, peeks around, bolts when anything tagged `Player` gets within `triggerRadius`.

### Mountable creature (MountableAnt)
- `MountController` + `MountModule` + `SteerModule` (Scripted = 100) + optional AI modules (Wander etc.)
- → While unmounted, AI modules drive the mount normally. When a player mounts, `SteerModule` claims the frame whenever there is rider input. `MountModule.allowAISelfMovementWhenMounted` controls whether the AI runs between rider inputs (true) or the mount idles (false, modules are disabled).

### Guard that investigates sounds
- `ChaseModule` + `PerceptionModule` + `SearchModule` + `NoiseReceiverModule` (investigateOn = Footstep|Gunshot, aggroOn = Alert|Hurt) + patrol of choice
- → Normal patrol. Hears the player's footsteps → walks over. Hears a hurt/alert noise → immediately force-aggros via `ChaseModule.ForceTarget`.

### Turret (no movement)
- `WatchModule` at high priority (e.g. ModulePriority.Reactive+2) + `RangedAttackModule` + `PerceptionModule` + static prefab (no NavMeshAgent movement)
- → The agent never wanders; `WatchModule` keeps it facing the nearest target. `RangedAttackModule` fires when in band.

---

## 11. Priority recipes (tuning arbitration)

Default priorities work for 90% of agents. Override only when an archetype's decision tree differs.

- Boost `WatchModule` to `Reactive+5` for a security camera that should stare instead of chase when it has a target.
- Drop `RangedAttackModule` to `Reactive-1` to create a reluctant shooter that prefers running.
- Raise `CoverModule` above `RangedAttack` for a sniper that always hides first, peeks second.
- Set `FleeModule` below `Chase` for a hostile that never disengages.

Priorities must be integers and are only read once per frame via the `Priority` property. To change at runtime, disable the module and re-enable after editing.

---

## 12. Scene setup checklist

For the agent system to work in a scene:

1. **Bake NavMesh** (Window ▸ AI ▸ Navigation) covering every area agents move in.
2. **Register the player** — `RegisterAsTarget` with `targetTag = "Player"` (plus `EntityFaction` + `NoiseEmitter` so it can be heard and targeted).
3. **Faction ScriptableObjects** — `RobotsFaction`, `PlayerFaction`, `NPCFaction`, `WildlifeFaction` + a `GlobalRelationships` table.
4. **Layers** — one layer for entities (e.g. `Entity`, layer 8), one for the player (`Player`, layer 9). Set:
   - `AgentController.nearbyAgentLayer`
   - `AlertBroadcaster.receiverLayers`
   - `NoiseEmitter.receiverLayers`
5. **Cover points** — drop `CoverPoint` components on any cover prop if you use `CoverModule`. No wiring needed; they self-register.
6. **Patrol bases** — each anchored patroller needs a `baseTransform` set in its `BasePatrolModule` (or it uses its spawn).

---

## 13. Common extension points

### Writing a new movement module

```csharp
public class MyModule : BehaviourModuleBase
{
    [SerializeField] private float someRange = 5f;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);

    public override string ModuleDescription =>
        "One-line summary\n\n• field — what it does";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        // Return null to pass, or a MoveIntent to claim the frame.
        return MoveIntent.MoveTo(context.Position + context.Self.forward * someRange);
    }
}
```

### Writing a side-effect module

Override `ClaimsMovement => false` and always return null from `Tick()`. Use this for attacks, audio triggers, status effects, buffs, cooldowns.

### Talking across modules

- `GetComponent<OtherModule>()` in `Awake()` and cache it. `ChaseModule` grabs `PerceptionModule`, `AlertBroadcaster`, `HerdModule` this way.
- Public state on modules: `ChaseModule.HasTarget`, `ChaseModule.LastKnownPosition`, `PerceptionModule.LastKnownPosition`, `MountModule.IsMounted`. Prefer public properties over serialized cross-references.
- For a one-shot event (attack landed, shot fired) expose a `UnityEvent<T>` + a C# `event Action` like `CloseCombatModule.OnAttack` / `RangedAttackModule.OnFire`.

### Writing a new motor

Implement `IMovementMotor` and drop on the agent. `AgentController.ResolveMotor()` auto-picks the first `IMovementMotor` it finds if `MotorComponent` is empty. Optionally implement `IMountJumpMotor`/`IMountLeapMotor` for mount support.

---

## 14. FAQ

**Why did my agent stop moving?** `AgentController` logs `"found no movement IBehaviourModule or IAgentBrain"` if there's no module. If modules exist but all return null, check `IsActive` (module enabled + GameObject active + `active` bool). `NavMeshAgentMotor.Awake()` disables the NavMeshAgent if there's no NavMesh within `navMeshSnapDistance` — bake a NavMesh.

**Why doesn't my agent see the player?** `ChaseModule` with a `PerceptionModule` requires FoV *and* LoS. Check `eyeTransform` (not a Blender-imported bone whose `.forward` points the wrong way — `PerceptionModule` uses the root transform's forward for FoV direction on purpose). Widen `fieldOfViewAngle` or drop `PerceptionModule` for pure radius detection. `proximityDetectRange` bypasses FoV for close range.

**Why are herd members stacking on the player?** `ChaseModule` asks `HerdModule.GetSlotPositionAround(target.position)` — make sure `HerdModule` is on the same GameObject and `herdId` matches across members.

**Why doesn't the ranged enemy back away when I'm point-blank?** `RangedAttackModule` only retreats if there's no `CloseCombatModule` on the same agent. If both are present, `CloseCombatModule` is expected to engage instead.

**Why are my alerts not reaching anyone?** `AlertBroadcaster.receiverLayers` or `NoiseEmitter.receiverLayers` is empty (defaults to Nothing). A warning is logged at Awake.

**Why doesn't `AgentController.RefreshModules()` pick up my runtime-added module?** It should — it calls `GetComponentsInChildren<MonoBehaviour>(true)` and resorts. But `MountModule.CacheSuppressibleModules` doesn't — call `MountModule.RefreshModuleCache()` manually if you add suppressible modules at runtime.

---

## 15. File map

```
Assets/Scripts/agents/
├── AI/
│   ├── AgentContext.cs           frame snapshot struct
│   ├── MoveIntent.cs             Idle/MoveTo/StopAndFace
│   ├── WanderBehaviour.cs        reusable wander/patrol helper (legacy but active)
│   ├── brains/
│   │   ├── IAgentBrain.cs        legacy single-brain interface
│   │   ├── MountedAgentBrain.cs  legacy mount brain
│   │   └── Enemy|NPC/...         legacy brains (still compiled, fallback only)
│   └── motor/
│       ├── IMovementMotor.cs     motor contract
│       ├── IMountJumpMotor.cs    jump + leap optional extensions
│       └── NavMeshAgentMotor.cs  the canonical motor
├── animation/AgentAnimatorDriver.cs
├── audio/
│   ├── EntityAudioModule.cs      footsteps + aggro SFX
│   ├── NoiseEmitter.cs           emit sounds
│   ├── NoiseReceiverModule.cs    hear sounds, investigate/aggro
│   └── NoiseType.cs              enum
├── controller/
│   ├── AgentController.cs        tick coordinator
│   └── mount/MountController.*   mount lifecycle (partial class)
├── entity/
│   ├── EntityEquipmentController.cs
│   ├── EntityInventoryComponent.cs
│   ├── EntityLootTable.cs
│   └── HealthReactionModule.cs
├── faction/
│   ├── EntityFaction.cs
│   ├── FactionDefinition.cs             (ScriptableObject)
│   └── FactionRelationshipTable.cs      (ScriptableObject)
├── modules/                               ← all IBehaviourModule implementations
│   ├── IBehaviourModule.cs
│   ├── BehaviourModuleBase.cs             ← base + ModulePriority constants
│   ├── ChaseModule.cs  FleeModule.cs  WanderModule.cs  PatrolModule.cs  BasePatrolModule.cs
│   ├── HerdModule.cs  FlockingModule.cs
│   ├── CloseCombatModule.cs  RangedAttackModule.cs  AgentRangedCombatModule.cs
│   ├── CoverModule.cs  CoverPoint.cs (+ static CoverPointRegistry)
│   ├── ApproachModule.cs  KeepDistanceModule.cs  WatchModule.cs  FacePlayerModule.cs
│   ├── IdleLookAroundModule.cs  InteractionFocusModule.cs  SearchModule.cs
│   ├── MountModule.cs  SteerModule.cs (+ partial .Camera/.Input/.SelfDrive)
│   └── WeaponMount.cs  WeaponSelector.cs
├── perception/
│   ├── PerceptionModule.cs       FoV + LoS + memory
│   ├── AlertBroadcaster.cs       broadcast to allies
│   └── AlertReceiverModule.cs    receive alerts
├── profiles/
│   ├── EntityProfile_BaseAgent.cs
│   ├── EntityProfile_NPC.cs
│   ├── EntityProfile_GenericEnemy.cs
│   └── EntitySystemSetup.cs      doc-only setup guide
├── weapon/
│   ├── AgentAimProfile.cs            (ScriptableObject)
│   ├── AgentFireProfile.cs           (ScriptableObject)
│   ├── AgentWeaponDefinition.cs      (ScriptableObject)
│   └── AgentProjectile.cs
├── EntityTargetRegistry.cs       static tag registry
└── RegisterAsTarget.cs           add on Player et al.

Assets/Editor/EntityProfileEditors.cs   generator buttons for every profile
```

---

## 16. Quick answers to common asks

**"Give this robot ranged attacks"** → add `RangedAttackModule` (or `AgentRangedCombatModule` + weapon/fire/aim assets). Assign `projectilePrefab`, `muzzleTransform` (gun barrel bone), layer the projectile to hit entities. If the agent also has melee, ranged won't retreat.

**"Make them patrol together"** → all members: same `BasePatrolModule.baseTransform`, same `HerdModule.herdId`. Optionally `FlockingModule` and set `AgentController.nearbyAgentScanRadius` > 0 with the entity layer.

**"Make them see from a head bone"** → assign `PerceptionModule.eyeTransform` = head bone. FoV still uses the root forward; only the raycast origin moves.

**"Make them run when low HP"** → add a `HealthThresholdReaction` on `HealthReactionModule` with `healthPercentage = 0.3`, `enableModules = [FleeModule]`, `disableModules = [ChaseModule, CloseCombatModule]`.

**"Let them drop loot"** → add items to `EntityInventoryComponent.startingItems` (guaranteed drop) and/or `EntityLootTable.lootEntries` (weighted rolls). Both trigger on `HealthComponent.OnDeath`.

**"Give them a sword that auto-swings"** → `EntityEquipmentController` with a `handSocket`, put the weapon item in inventory slot 0, enable `autoUse` + set `autoUseInterval`. (This is the inventory-driven path — for combat AI, use `CloseCombatModule` instead.)

**"Rider-controlled mount"** → `MountController` + `MountModule` + `SteerModule`. Seat / dismount Transforms on `MountController`. `SteerModule` reads the Input System actions `Move`, `Look`, `Jump`, `Next`. `MountModule.allowAISelfMovementWhenMounted` toggles whether the mount's AI runs between rider inputs.

**"Let a whole band react when one spots the player"** → every member has `AlertReceiverModule`, the spotters have `AlertBroadcaster`. `ChaseModule` calls `AlertBroadcaster.Broadcast` on first detection. Set `AlertBroadcaster.receiverLayers` to the entity layer.

---

*When in doubt, read [BehaviourModuleBase.cs](Assets/Scripts/agents/modules/BehaviourModuleBase.cs) and any module — each one has a `ModuleDescription` string explaining its fields and behaviour inline.*
