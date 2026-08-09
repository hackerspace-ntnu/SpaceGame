# Agent Movement & Combat — Review and Redesign

Date: 2026-08-06
Scope: `Assets/Scripts/agents/**`, `Assets/Scripts/Minigame/MatchManager.cs`, `DeathmatchBot.prefab`,
`PlayerCharacterNetworked.prefab`
Goal: one agent stack that is **scalable, modifiable and layered**, works in the open world *and* in the
minigame arena, with per-context tuning rather than per-context prefabs.

---

## 1. How the system works today

```
AgentController (Update)
  ├─ BuildContext()            → AgentContext { position, velocity, reachedDest, immobile, neighbours }
  ├─ tick all side-effect modules (ClaimsMovement == false)   ← currently: none on any shipped prefab
  ├─ walk movement modules high→low priority, first non-null MoveIntent wins
  ├─ apply sine-wave speed drift
  └─ Motor.Tick(intent)        → NavMeshAgentMotor / RigidbodyMotor / FlyingRigidbodyMotor
```

`MoveIntent` is one of three things: `Idle`, `MoveToPosition`, `StopAndFacePosition`.
Targeting is not centralised: each module that needs a target calls
`EntityTargetRegistry.ResolveNearest(selfFaction, relationship, position)` for itself.

`DeathmatchBot.prefab` (also the arena bot) runs, in resolved priority order:

| Priority | Module | Notes |
|---|---|---|
| 23 | CloseCombatModule | `attackRange 5` |
| 22 | AgentRangedCombatModule | `FIRE_PistolBurst`, `maxRange 22` |
| 20 | ChaseModule | `detectRange 30`, `loseTargetRange 40` |
| 20 | AlertReceiverModule | **tied with Chase** |
| 19 | SearchModule | |
| 18 | NoiseReceiverModule | |
| 15 | HerdModule | `herdId herd2` |
| 14 | PatrolModule | `RadiusBased`, `patrolRadius 15`, no waypoints |
| 10 | KeepDistanceModule | `preferredDistance 6` |
| 9 | HuntModule | arena "go find someone" module |
| 5 | IdleLookAroundModule | |
| 0 | WanderModule | |

---

## 2. Correctness bugs

### B1 — The player is invisible to every AI in the game
`SpawnManager.SpawnPlayerForClient` instantiates `networkPlayerPrefab`
(`Assets/Prefabs/Player/PlayerCharacterNetworked.prefab`). That prefab has **no `EntityFaction`
component**. `EntityTargetRegistry` is faction-only by design ("an entity without an EntityFaction is
invisible to the targeting system"), and every consumer null-guards it silently:

- `MatchManager.RegisterEntity` — `if (entityFaction != null) SetFaction(...)` → no-op for players.
- `MatchManager.SetTargetable` — `if (faction != null) faction.enabled = ...` → no-op, so the
  "eliminated player stays targetable" fix it was written for never actually runs.
- Every targeting module — the player is never in the registry, so no bot ever picks them.

The older `Assets/Prefabs/Player/PlayerCharacter.prefab` still has the component. It was lost when the
player prefab was swapped (`fcd20b0 chore: replace player`). **This alone explains "enemies ignore me".**

### B2 — `PatrolModule` starves every module below it
`PatrolModule.Tick` never returns `null` on its normal paths:

- waiting between points → `MoveIntent.Idle()` (claims the frame)
- arrived at a point → `MoveIntent.Idle()` (claims the frame)
- otherwise → `MoveIntent.MoveTo(...)`

At priority 14 on `DeathmatchBot`, that permanently starves **KeepDistance (10), Hunt (9),
IdleLookAround (5) and Wander (0)**. `HuntModule` exists specifically so arena bots walk at each other
across a large map — it has never executed on that prefab. Bots instead pace a 15 m circle around their
spawn point and only fight when someone strays inside `detectRange`.

### B3 — Targeting is duplicated per module and mostly never re-evaluated
Eleven call sites resolve targets independently: `ChaseModule`, `CloseCombatModule`,
`AgentRangedCombatModule`, `HuntModule`, `FleeModule`, `KeepDistanceModule`, `WatchModule`,
`ApproachModule`, `CoverModule`, `FacePlayerModule`, `TurretModule` (plus the two legacy brains).

Three different staleness policies coexist:

| Policy | Modules | Consequence |
|---|---|---|
| Hold until the target *dies* | Chase, CloseCombat, Ranged | Locks onto whoever was nearest at first resolve. Never re-picks for a closer enemy. |
| `if (target) return;` — never drop at all | Flee, KeepDistance, Watch, Approach, Cover, FacePlayer | Keeps fleeing / kiting / staring at a **corpse**, forever. |
| Re-resolve on a timer + `Alive` check | Hunt only | The only module that behaves. |

Because each resolve happens at a different moment, **the same agent can chase A while shooting at B
while kiting away from C**. That is the single largest source of the "janky, doesn't always work"
feeling. It also means every new module adds another independent targeting policy.

### B4 — Equal priorities arbitrate nondeterministically
`AgentController.ResolveModules` sorts with `List<T>.Sort`, which is introsort — **unstable**. On
`DeathmatchBot`, `ChaseModule` and `AlertReceiverModule` are both priority 20, so which one is asked
first is arbitrary and can differ between agents, runs and builds. `HuntModule`'s own source comment
already flags this hazard.

### B5 — Perception is configured to "see everything through everything"
`PerceptionModule.occlusionLayers` on `DeathmatchBot` is `m_Bits: 0` (Nothing) — the code logs a warning
about exactly this at `Awake`. Consequences:

- `HasLineOfSightFrom` raycasts an empty mask → always returns `true`.
- `CanSee` therefore degenerates to a pure FOV cone (130° + 40° while moving = **170°**).
- `AgentAimProfile.requireLineOfSight` is a no-op; bots shoot through walls.

`AgentController.nearbyAgentLayer` is likewise `0` while `nearbyAgentScanRadius` is `30` — a
`Physics.OverlapSphereNonAlloc` per agent per frame that can never return a hit, feeding a
`FlockingModule` that isn't on the prefab.

### B6 — `ChaseModule` silently rewrites its own serialized ranges at `Awake`
`ExpandLoseTargetRangeForAttackModules()` and `ConfigureMeleeMovement()` mutate `detectRange`,
`loseTargetRange` and `chaseStopDistance` in place. On `DeathmatchBot` the authored
`chaseStopDistance: 12.8` becomes `4.6` because a `CloseCombatModule` with `attackRange 5` is present.
The bot carries a 22 m gun but is configured to close to melee range. A designer tuning the Inspector
value sees no effect and no explanation.

### B7 — Motor thrashes the NavMeshAgent at every range boundary
`NavMeshAgentMotor.Tick`:

- `StopAndFacePosition` → `StopAgentPath()` → `isStopped = true` **+ `ResetPath()`**, and resets
  `speed`/`stoppingDistance` to defaults.
- `MoveToPosition` → `isStopped = false` + `SetDestination(...)`.

Attack modules flip between claiming and passing on a *hard* distance compare with no hysteresis
(`distance > attackRange` → `return null`; `distance > maxRange` → `return null`). At 5 m and at 22 m the
winner alternates frame to frame, so the path is discarded and re-requested repeatedly. The agent never
builds up velocity — this is the mechanical cause of the visible stutter. `CloseCombatModule`'s
`attackCommitDuration` is a partial patch over the melee half of the same problem.

### B8 — Per-shot `Debug.Log`
`AgentRangedCombatModule.FireOne` logs `"[RangedCombat] {name} FIRING at {target.name}"` on **every
projectile**. With 16 arena bots that is a continuous console flood and a measurable editor cost.

### B9 — Weapon assets carry fields the scripts no longer have
`FIRE_PistolBurst.asset` still serialises `allowFireWhileRunning: 0`; `AIM_GruntPoor.asset` still
serialises `lineOfSightMask`. Neither field exists on `AgentFireProfile` / `AgentAimProfile` any more.
Harmless at runtime, but it means the fire/aim contract has already been silently changed once and the
data was never migrated.

---

## 3. Architectural problems

### A1 — No shared per-agent state
`AgentContext` carries position, velocity, arrival flags and neighbour arrays — but no target, no threat,
no combat state. There is no single source of truth for *who this agent is fighting*, so modules cannot
agree with each other and each must re-derive everything from a global scan. B3 is a symptom, not the
disease.

### A2 — Arbitration is winner-take-all over one flat list
A module either owns the entire frame or contributes nothing. Every combination therefore has to be
hard-coded inside one module:

- `ChaseModule` contains melee-awareness (`ConfigureMeleeMovement`) and herd-slot logic.
- `AgentRangedCombatModule` contains a retreat behaviour (`ComputeRetreatIntent`).
- `HerdModule` contains a re-broadcast of everyone else's intents.

You cannot express "move like Chase but face like Ranged", or "chase with separation from allies" —
which is precisely what *layered* means. `MoveIntent` has no representation for a partial contribution.

### A3 — Facing is underspecified
`MoveIntent` has `FacePosition` (only read for `StopAndFacePosition`) and
`FacingDirection` + `OverrideFacingDirection` (only used to *disable* NavMesh auto-rotation for the mount
system). There is no way to say "move to X while facing Y". Combat consequently has to come to a full
stop to aim — no strafing, no fire-while-repositioning, and the halt is what makes the range-boundary
oscillation visible.

### A4 — `Idle()` is overloaded
It means both "I claim this frame and want to stand still" (Patrol's wait) and "nothing to contribute"
(the controller's fallback). Modules that idle to hold a pause starve everything below them — B2 is the
direct consequence.

### A5 — Tuning lives in per-prefab serialized fields
Roughly 15 modules × 5–10 fields each, duplicated in every prefab, with no shared asset. Making the same
agent behave differently in the arena means forking the prefab and hand-editing dozens of numbers, which
is exactly the requirement the minigame has. Weapons already got this right (`AgentWeaponDefinition` /
`AgentFireProfile` / `AgentAimProfile` ScriptableObjects); movement and targeting did not.

### A6 — Targeting cost is O(agents × modules × entities) per frame
`ResolveNearest` is a full linear scan with `Vector3.Distance` (a `sqrt` per candidate) and no range
cut-off, run once per targeting module per agent per frame. A 16-bot arena is ~16 × 5 × 17 ≈ 1400
relationship lookups and sqrt calls per frame, growing quadratically with entity count.

### A7 — Sibling caching at `Awake` makes ordering load-bearing
`ChaseModule` reads `CloseCombatModule.AttackRange` and `AgentRangedCombatModule.MaxRange` during
`Awake` and bakes the result. Runtime changes — a `WeaponMount` slot swap, a module enabled by a
`HealthReactionModule` threshold — are never picked up. `AgentController.ResolveModules` likewise runs
once and needs a manual `RefreshModules()`.

---

## 4. Target architecture

Four layers, each independently testable, each with one job.

```
┌─ SENSE ────────────────────────────────────────────────────────────┐
│ EntityTargetRegistry  (spatial query, range-limited, zero-alloc)   │
│ PerceptionModule      (FOV + LoS, single source of truth)          │
│ AgentTargeting        (scores candidates → writes the blackboard)  │
└────────────────────────────────────────────────────────────────────┘
                              ↓ AgentBlackboard
┌─ DECIDE ───────────────────────────────────────────────────────────┐
│ IBehaviourModule[]    (read the blackboard, return MoveIntent?)    │
│ AgentController       (arbitrates locomotion and facing separately)│
└────────────────────────────────────────────────────────────────────┘
                              ↓ MoveIntent
┌─ ACT ──────────────────────────────────────────────────────────────┐
│ IMovementMotor        (NavMesh / Rigidbody / Flying)               │
└────────────────────────────────────────────────────────────────────┘
                              ↑ tuned by
┌─ CONFIGURE ────────────────────────────────────────────────────────┐
│ AgentTuningProfile    (ScriptableObject; world vs arena variants)  │
└────────────────────────────────────────────────────────────────────┘
```

### 4.1 `AgentBlackboard` — shared per-agent state
One component per agent, written by the sense layer, read by everything else. Exposed through
`AgentContext.Blackboard` so modules keep their existing `Tick(in AgentContext, float)` signature.

```csharp
Transform  Target;              // the one target this agent is committed to
IDamageable TargetHealth;       // cached, not re-fetched per frame per module
float      DistanceToTarget;    // computed once per frame
bool       HasTarget;           // Target != null && TargetHealth.Alive
bool       CanSeeTarget;        // routed through PerceptionModule
Vector3    LastKnownTargetPos;
float      TimeSinceSeen;
Transform  LastAttacker;        // for retaliation scoring
```

This removes B3 entirely: modules stop resolving and start reading. It removes A6: one scan per agent
per interval instead of five per agent per frame.

### 4.2 `AgentTargeting` — the only place a target is chosen
A side-effect component (`ClaimsMovement == false`) ticked before every movement module. Replaces every
`TryResolveTarget()` in the codebase.

- Re-evaluates on an interval (default 0.5 s), not every frame.
- **Scores** candidates instead of taking the raw nearest: distance, line-of-sight, recency of damage
  received, and a **stickiness bonus for the current target** so it does not flip-flop between two
  equidistant enemies.
- Evicts dead / de-registered targets in one place.
- Range-limited query so distant entities are never scored at all.

### 4.3 Layered intent — split locomotion from facing
`MoveIntent` gains an explicit facing channel:

```csharp
enum FacingMode { Auto, FaceTarget, FaceDirection, Free }
```

- `Auto` — NavMesh rotates along the path (today's `MoveToPosition` default).
- `FaceTarget` / `FaceDirection` — the motor rotates the body independently of travel direction.
- `Free` — nobody touches rotation (mount/rider ownership; today's `OverrideFacingDirection`).

A module may now return a **facing-only claim** (`AgentIntentType.FaceOnly`) that arbitrates on a
separate channel: the ranged module can own facing while `ChaseModule` still owns locomotion, so an
agent walks and shoots. This is the "layered" requirement, and it is what removes the stop-start
oscillation at range boundaries.

`MoveIntent.Idle()` keeps its "claim the frame, stand still" meaning; modules that merely want to pause
must return `null`. Documented on `IBehaviourModule`.

### 4.4 Motor stability
- `StopAgentPath()` stops calling `ResetPath()` on every frame it is already stopped.
- `SetDestination` only when the destination moved more than a dead-band (already partly present at
  0.2 m — extend the same idea to the stop/start transition).
- Attack modules get **enter/exit hysteresis bands** (`attackRange` to enter, `attackRange * exitFactor`
  to leave) so a target hovering at the boundary cannot alternate the winner every frame.

### 4.5 `AgentTuningProfile` — one asset, two games
A ScriptableObject bundling the numbers that differ between the open world and the arena:

- targeting: re-evaluate interval, stickiness, max acquisition range, LoS requirement
- movement: chase stop distance, speed multipliers, hysteresis factor
- combat: engagement band preference (prefer-ranged vs prefer-melee), commit durations
- perception: FOV, occlusion mask, memory duration

Applied at `Awake` by a small applier component, so **the same prefab** ships in both contexts.
`MatchManager` injects the arena profile at spawn. This is the "same agents, modified differently"
requirement, and it also fixes B5/B6 by giving the numbers one authoritative home instead of leaving
them as invisible `Awake` mutations.

---

## 5. Implementation phases

Ordered so each phase is independently valuable and independently verifiable.

**Phase 1 — unblock (correctness only, no architecture change)**
1. `EntityFaction` on `PlayerCharacterNetworked.prefab`; `MatchManager.RegisterEntity` adds it if absent
   and logs loudly rather than silently no-opping. (B1)
2. `PatrolModule` / `WanderModule` return `null` while waiting instead of `Idle()`. (B2, A4)
3. Deterministic module order: stable sort, documented tiebreak. (B4)
4. `PerceptionModule` falls back to a sane occlusion mask when configured to `Nothing`, and warns once
   rather than silently seeing through walls. (B5)
5. Drop the per-shot `Debug.Log`. (B8)

**Phase 2 — shared targeting**
6. `AgentBlackboard` + `AgentTargeting` + `TargetingProfile`; `AgentContext.Blackboard`.
7. Range-limited, zero-alloc registry query.
8. Migrate Chase / CloseCombat / Ranged / KeepDistance / Flee / Hunt / Watch / Approach to read the
   blackboard, keeping a per-module relationship override for special cases. (B3, A1, A6)

**Phase 3 — layered intent + motor stability**
9. `FacingMode` + `FaceOnly` intents; separate facing arbitration in `AgentController`.
10. Motor dead-bands; attack-module hysteresis bands. (B7, A2, A3)

**Phase 4 — tuning surface**
11. `AgentTuningProfile` + applier; world and arena assets; `MatchManager` injection. (A5, B6)

Phases 1 and 2 remove the observable jank. Phases 3 and 4 deliver the scalable/layered/modifiable goal.

---

## 6. What was implemented

All four phases landed. Both `Assembly-CSharp` and `Assembly-CSharp-Editor` type-check clean
(verified with Unity's own Roslyn outside the editor — the MCP bridge was unavailable).
**Nothing has been play-tested.**

### New files
| File | Role |
|---|---|
| `agents/AI/AgentTargeting.cs` | The one place a target is chosen. Scores candidates, holds memory, publishes through `AgentContext.Targeting`. |
| `agents/AI/TargetingProfile.cs` | ScriptableObject tuning asset (`Assets > Create > Agents > Targeting Profile`). |
| `agents/AI/TargetResolution.cs` | Viability + interval re-resolution for modules that pick their own non-hostile targets. |
| `agents/modules/IFacingModule.cs` | Opt-in second arbitration channel for facing. |

### Behaviour changes worth knowing about
- **Fire-while-moving is now on for `FIRE_PistolBurst`.** Robots advance and shoot instead of
  planting at 22 m. This affects `PatrolRobot` in the open world too. Turn
  `allowFireWhileRunning` back off on the asset if you want the old planted behaviour.
- **Chase no longer owns detection.** `detectRange`, `loseTargetRange` and `proximityDetectRange`
  moved off `ChaseModule` onto `AgentTargeting`. The orphaned keys stay in prefab YAML until
  Unity next re-serialises them; they are inert.
- **`AgentTargeting` is auto-added** by `AgentController` at `Awake` when a prefab lacks one, so
  no existing prefab breaks. Add it explicitly to get a tuning surface in the Inspector.
- **Melee and ranged now have enter/exit hysteresis** (`rangeExitFactor`, 1.15 and 1.1). This is
  what stops the path being discarded and re-requested at the range boundary.
- **`SpawnManager` assigns the player's faction on spawn** (`EntityFaction.Ensure`), wired to
  `PlayerFaction` + `GlobalRelationships` in `SpawnManager.prefab`. This is the fix for B1 and it
  works without touching the player prefab.
- **`MatchManager` retunes match entities** with arena targeting (250 m acquisition, no
  line-of-sight requirement) and gives each team its own herd id.

### Data edits
| Asset | Change | Why |
|---|---|---|
| `SpawnManager.prefab` | + `playerFaction`, `relationshipTable` | B1 — makes the player targetable |
| `DeathmatchBot.prefab` | `PatrolModule.priority` 14 → 1 | B2 — unblocks `HuntModule` |
| `DeathmatchBot.prefab` | `AlertReceiverModule.priority` 20 → 19 | B4 — breaks the tie with Chase |
| `DeathmatchBot.prefab` / `PatrolRobot.prefab` | `occlusionLayers` Nothing → Default+Ground+Interior | B5 — line-of-sight actually works |
| `DeathmatchBot.prefab` / `PatrolRobot.prefab` | `nearbyAgentScanRadius` 30 → 0 | dead `OverlapSphere` every frame |
| `FIRE_PistolBurst.asset` | `allowFireWhileRunning` 0 → 1 | enables the new facing channel |

### Still to do by hand
1. **Let Unity import the four new scripts** so `.meta` files are generated. They were created
   outside the editor.
2. **Create the tuning assets** once the scripts are imported — a world profile and an arena
   profile — and assign the arena one to `MatchManager.arenaTargetingProfile`. Until then the
   arena runs on `BuildDefaultArenaTargeting()`, which is functional but not designer-visible.
3. **Play-test.** Nothing here has been run. The first things to check are: bots find each other
   in a Free-For-All, a human player gets shot at, and the stutter at weapon range is gone.

### Follow-up: ranged engagement band (same day)

The first cut of fire-while-moving was wrong in play: the ranged module passed on movement and
only claimed facing, so `ChaseModule` — whose only goal is to close the distance — kept driving
the agent into its target's face while shooting.

The ranged module now owns positioning for the whole engagement instead:

| Distance | Behaviour |
|---|---|
| `> maxRange` | pass — `ChaseModule` closes the gap |
| `< preferredRange - tolerance` | back away to `preferredRange`, running, still firing and facing |
| in band | hold station, strafing on an interval |

`AgentFireProfile` gained `preferredRange`, `rangeTolerance`, `strafeWhileEngaged`,
`strafeDistance` and `strafeInterval`. `FIRE_PistolBurst` is tuned to hold ~14 m of a 22 m band.
Firing starts on the first frame the target is inside `maxRange` with line of sight — no wind-up
and no closing first.

`CloseCombatModule.attackRange` on both robot prefabs dropped 5 m → 2.5 m. At 5 m melee
(priority 23) outranked the ranged module (22) and hijacked the frame well inside the standoff,
so a bot that should have been backing off stopped to swing. It is now a genuine contact-range
last resort.

Two traps worth remembering, both hit while writing this:
- Strafing is force-disabled when `allowFireWhileRunning` is off. Otherwise the agent sidesteps
  through the whole engagement without ever taking a shot, because the fire gate rejects
  `MoveToPosition` intents.
- `KeepDistanceModule` on `DeathmatchBot` is now dead config — the ranged band owns standoff, and
  at priority 10 it never outranks Chase at 20 anyway.

### Known remaining rough edges
- `HuntModule` is close to redundant now that acquisition range is profile-driven — it only
  matters when acquisition is deliberately short. Worth deleting if the arena profile keeps its
  long range.
- The two legacy brains (`EnemyBrain`, `NpcBrain`) and `TurretModule` still resolve their own
  targets. Deliberate: the brains are frozen for prefab compatibility, and a turret is stationary
  with its own range gating.
- `AgentTargeting` raycasts once per candidate per re-evaluation for the occlusion penalty. Fine
  at arena scale (~600 raycasts/sec for 16 bots); worth a cheap early-out if entity counts grow.

## 7. Out of scope

- The legacy `EnemyBrain` / `NpcBrain` `IAgentBrain` path stays as-is (still supported by the controller
  fallback, still used by older prefabs).
- Mount / rider (`MountModule`, `SteerModule`, `DuneRiderController`) is a separate concern that only
  touches the motor's rider path; not changed here.
- Networking authority for agents. Bots are currently server-side and not `NetworkObject`-replicated
  beyond `NetworkObject.Spawn()`; that is a separate design question.
