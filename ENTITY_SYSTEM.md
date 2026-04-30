# Entity System

Entities are GameObjects with a stack of module components. `AgentController` asks each module (highest priority first) for a `MoveIntent`. First one that returns something wins the frame.

**Every entity needs:** `AgentController` + `NavMeshAgentMotor` + `NavMeshAgent`  
**For wandering:** also add `WanderBehaviour`  
**For animations:** also add `Animator` + `AgentAnimatorDriver`

---

## Creating an entity

**Using a profile (recommended)**  
Add an `EntityProfile_*` component to the prefab, tweak values, click **Generate**. All modules are added and configured. Remove the profile component when done.

| Profile | Description |
|---|---|
| `EntityProfile_HostileRobot` | Generic melee robot |
| `EntityProfile_RobotPhil` | Charger — fast, high damage |
| `EntityProfile_RobotRoberto` | Scout — long range, alerts the group |
| `EntityProfile_RobotCath` | Cover shooter — ranged burst fire |
| `EntityProfile_RobotErnst` | Heavy — slow, tanky, relentless |
| `EntityProfile_RobotHerdPatrol` | Anchored robot herd patrol with perception, alerts, and melee/ranged/kiting attack styles |
| `EntityProfile_NPC` | Friendly wandering civilian |
| `EntityProfile_DesertRat` | Fleeing wildlife |
| `EntityProfile_BountyHunter` | Mercenary with blaster |
| `EntityProfile_MountableAnt` | Rideable creature |

**Building manually**  
Add whatever modules you want. Priority controls who wins each frame, not component order. Each module sets a sensible default priority when added.

---

## Priority

| Constant | Value | Use for |
|---|---|---|
| `Scripted` | 100 | Cutscenes, forced overrides |
| `Override` | 30 | Flee, mount suppressor |
| `Reactive` | 20 | Chase, combat, cover, strafe |
| `Social` | 15 | Flocking |
| `Ambient` | 10 | Watch, keep distance, approach |
| `Personality` | 5 | Idle look-around |
| `Fallback` | 0 | Wander, patrol |

---

## Modules

| Module | Priority | What it does |
|---|---|---|
| **Movement** | | |
| `WanderModule` | 0 | Roams randomly using `WanderBehaviour`. Fallback for any idle entity. |
| `PatrolModule` | 0 | Walks between assigned patrol point Transforms. Waits at each point. |
| `ChaseModule` | 20 | Chases a target by tag. Stops and faces at `attackRange`. Fires `OnEnterAttackRange` event. |
| `FleeModule` | 30 | Sprints away from a threat when it enters `triggerRadius`. Stops when beyond `safeRadius`. |
| `ApproachModule` | 10 | Walks toward a target and stops at conversation distance. Faces target once arrived. |
| `KeepDistanceModule` | 10 | Backs away if a target gets closer than `preferredDistance`. Good for ranged kiting. |
| `StrafeModule` | 20 | Orbits a target at a fixed radius while in `engageRange`. Pair with ranged attack. |
| `CoverModule` | 21 | Finds the nearest free `CoverPoint` and moves behind it. Faces threat from cover. |
| `FlockingModule` | 15 | Separation + cohesion + alignment flocking. Needs `AgentController.nearbyAgentScanRadius` > 0. |
| `HerdModule` | 15 | Keeps herd members from overlapping — pure separation only. Yields when no one is crowding, so each entity's own AI runs normally. Self-registering by `herdId`, no scan radius needed. |
| `BasePatrolModule` | 0 | Roams random NavMesh points within a radius of a fixed base (or spawn point). No herd logic — pair with `HerdModule` at Social priority for group movement. |
| `SearchModule` | 19 | When `ChaseModule` loses its target, moves to the last known position and searches briefly. |
| **Combat** | | |
| `EntityCombatModule` | 20 | Deals melee damage when target is in range. Never claims movement — pair with `ChaseModule`. |
| `RangedAttackModule` | 20 | Fires a projectile at a target. Supports burst fire, spread angle, lead prediction. Never moves. |
| **Perception** | | |
| `PerceptionModule` | — | FOV + line-of-sight gate. `ChaseModule` uses it to require actual sightlines before chasing. Tracks `LastKnownPosition` for `SearchModule`. |
| `AlertBroadcaster` | — | When a target is spotted, notifies nearby allied `AlertReceiverModule`s within radius. |
| `AlertReceiverModule` | 18 | Receives group alerts. Immediately aggroes if a target is given, otherwise investigates last known position. |
| `NoiseReceiverModule` | 18 | Hears `NoiseEmitter` events. Investigates on footsteps/gunshots, aggroes on alert/hurt sounds. |
| **NPC / personality** | | |
| `WatchModule` | 10 | Stops and faces a nearby target without moving. Good for curious NPCs or sentries. |
| `IdleLookAroundModule` | 5 | Periodically turns to face a random direction when idle. Adds personality. |
| `InteractionFocusModule` | 100 | Stops and faces the player during a dialog interaction. Triggered by the dialog system. |
| `MountSuppressorModule` | — | Disables all other modules while a player is mounted. Re-enables on dismount. |

**Base patrol herds:** use `EntityProfile_RobotHerdPatrol`. Set `baseTransform`, `patrolRadius`, and `herdId`; Generate wires `BasePatrolModule` (Fallback) + `HerdModule` (Social). If `baseTransform` is empty, the robot uses its spawn point as the patrol base. Set `herdLayer` so `AgentController` feeds nearby positions into `HerdModule`.

---

## Faction system

1. Create `FactionDefinition` assets per faction *(right-click → Factions → Faction Definition)*
2. Create one `FactionRelationshipTable` *(right-click → Factions → Faction Relationship Table)* — add rows like `RobotsFaction ↔ PlayerFaction = Hostile`
3. Add `EntityFaction` to every entity, assign faction + table

All targeting modules (`ChaseModule`, `FleeModule`, etc.) have a `requiredRelationship` field and check this automatically.

---

## Noise & alerts

**Noise** — `NoiseEmitter` emits sounds as physics spheres. `NoiseReceiverModule` hears them and either investigates (`investigateOn` mask) or immediately aggroes (`aggroOn` mask). `EntityAudioModule` handles footstep + aggro sounds automatically.

**Alerts** — `AlertBroadcaster` notifies nearby allied `AlertReceiverModule`s when a target is spotted. The whole robot band wakes up when one member sees the player.

Noise types: `Footstep` `Alert` `Hurt` `Death` `Gunshot` `Explosion` `Custom`

---

## Health & loot

| Component | Description |
|---|---|
| `HealthReactionModule` | Enable/disable modules at HP thresholds. Handles hurt sound, death sound, despawn. |
| `EntityLootTable` | Drops items on death. Configure entries with item, drop chance, quantity. |
| `EntityInventoryComponent` | Gives the entity an inventory (same class as the player). |

---

## One-time project setup

```
Player prefab:
  + RegisterAsTarget      (targetTag = "Player")
  + EntityFaction         → PlayerFaction
  + NoiseEmitter          → set receiverLayers

Hostile entity:
  + EntityFaction         → assign faction + relationship table
  + AlertBroadcaster      → set receiverLayers to entity layer
  + NoiseEmitter          → set receiverLayers to entity layer
  AgentController         → nearbyAgentScanRadius = 12, nearbyAgentLayer = entity layer

Scene:
  - Bake NavMesh (Window → AI → Navigation → Bake)
  - Place CoverPoint components behind rocks/crates for CoverModule users
```

---

## File map

```
Scripts/agents/
  controller/     AgentController.cs
  AI/             MoveIntent.cs, AgentContext.cs, WanderBehaviour.cs, motor/
  modules/        all behaviour modules
  perception/     PerceptionModule, AlertBroadcaster, AlertReceiverModule
  audio/          NoiseType, NoiseEmitter, NoiseReceiverModule, EntityAudioModule
  faction/        FactionDefinition, FactionRelationshipTable, EntityFaction
  entity/         EntityInventoryComponent, EntityLootTable, HealthReactionModule
  profiles/       EntityProfile_*.cs  (data components + Generate button)
  EntityTargetRegistry.cs, RegisterAsTarget.cs

Editor/
  EntityProfileEditors.cs   (Generate buttons for all profiles)
```
