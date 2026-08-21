# SpaceGame agent reference

Companion to `SKILL.md`. Every path is relative to the repo root. All agent code lives in
`Assets/Game/Scripts/agents/`, namespace `SpaceGame.Agents`, in **Assembly-CSharp** (no asmdef —
a new module needs no assembly wiring).

---

## 1. Tick order (AgentController.Update)

`Assets/Game/Scripts/agents/Controller/AgentController.cs`

```
Update()
 1. RefreshAuthority()          -> if this machine does NOT own the agent:
                                      TickPresentation(dt)   // IPresentationModule only
                                      return                 // nothing else runs
 2. if (Motor == null) return
 3. BuildContext()              -> AgentContext (Self, Position, Velocity, HasReachedDestination,
                                   IsImmobile, Targeting, Goal, NearbyAgent* arrays)
 4. EvaluateModules()
      a. every side-effect module (ClaimsMovement == false), in discovery order, unconditionally
      b. movement modules, Priority DESC, ties by component order; FIRST non-null wins
      c. if none: legacy IAgentBrain, else MoveIntent.Idle()
 5. ApplyFacingOverride()       -> IFacingModule[], FacingPriority DESC, first true wins;
                                   overwrites intent.FacePosition + OverrideFacing
 6. speed variation drift applied to intent.SpeedMultiplier (MoveToPosition only)
 7. Motor.Tick(in intent, dt)
 8. animatorDriver.Tick(Motor.Velocity, Motor.IsImmobile, intent.IsRunning)
```

Execution order of the whole stack:

| Order | Component |
|---|---|
| -100 | `NavMeshAgentMotor` (so it can disable the NavMeshAgent before its own Awake) |
| -50 | `AgentTargeting` — the target decision is current before anything reads it |
| -40 | `ProvocationModule` — re-asserts the grudge after targeting's staleness pass |
| 0 | `AgentController` |
| 50 | `LeggedDriver` |
| 100 | `LeggedLocomotion` (asmdef `SpaceGame.Locomotion`) |
| LateUpdate | `AgentAnimatorDriver` self-drive; `EntityEquipmentController` aim |

Modules are discovered with `GetComponentsInChildren<MonoBehaviour>(true)` in `Awake`. Adding a
module at runtime requires `AgentController.RefreshModules()`.

---

## 2. Interfaces (verbatim members)

`Modules/Core/IBehaviourModule.cs`
```csharp
int Priority { get; }
bool IsActive { get; }
bool ClaimsMovement { get; }
MoveIntent? Tick(in AgentContext context, float deltaTime);
```

`Modules/Core/IFacingModule.cs`
```csharp
int FacingPriority { get; }
bool IsActive { get; }
bool TryGetFacing(in AgentContext context, out Vector3 facePosition);
```

`Modules/Core/IPresentationModule.cs` — **marker, no members.** Currently zero implementors in the
repo; it exists so purely-local output (popups, particles, sound) keeps ticking on machines that
only watch. A presentation module may not damage, spawn, consume, move the body, or write anything
a peer can observe, must return `null` from `Tick`, and gets a reduced `AgentContext` (no Velocity,
no IsImmobile, no HasReachedDestination, no neighbour arrays).

`Modules/Core/BehaviourModuleBase.cs` — abstract `MonoBehaviour`, serializes `priority` + `active`,
exposes `ModuleDescription`, `SetPriorityDefault(int)`, `SetMinPriority(int)`, `protected virtual
void OnValidate()`.

`ModulePriority`: `Scripted 100`, `Override 30`, `MeleeAttack 23`, `RangedAttack 22`,
`Reactive 20`, `Social 15`, `Ambient 10`, `Personality 5`, `Fallback 0`.

`AI/Core/MoveIntent.cs` factories: `MoveIntent.Idle()`,
`MoveIntent.MoveTo(pos, stopDistance = 0.2f, speedMultiplier = 1f, overrideFacingDirection = false,
facingDirection = default, isRunning = false)`, `MoveIntent.StopAndFace(facePosition)`, and the
instance method `.WithFacing(Vector3)`.

`AI/Core/AgentContext.cs` fields: `Transform Self`, `Vector3 Position`, `Vector3 Velocity`,
`bool HasReachedDestination`, `bool IsImmobile`, `AgentTargeting Targeting`, `AgentGoal Goal`,
`Vector3[] NearbyAgentPositions`, `Vector3[] NearbyAgentVelocities`, `int NearbyAgentCount`,
`bool IsMoving`.

---

## 3. Module catalog

### Movement (claim the frame)

| Module | Default priority | What it does | Requires |
|---|---|---|---|
| `WanderModule` | Fallback 0 | Random NavMesh roaming, optional radius limit, wait between points | NavMesh |
| `AirWanderModule` | Fallback 0 | Random 3D points in a sphere around an anchor, no NavMesh | `FlyingRigidbodyMotor` |
| `PatrolModule` | Fallback 0 | RadiusBased or PatrolPoints waypoint cycling (sequence / ping-pong / random) | NavMesh, optional waypoint Transforms |
| `BasePatrolModule` | Fallback 0 | Random NavMesh points around a fixed base position | NavMesh |
| `GoalTravelModule` | Fallback+1 = 1 | Walks to `AgentGoal.Position`; returns null on arrival so wander takes over | `AgentGoal` (auto-added) |
| `HuntModule` | Ambient-1 = 9 | Walks at the nearest hostile anywhere on the map, ignoring perception and acquisition range (arena) | `EntityFaction` |
| `ApproachModule` | Ambient 10 | Walks to `conversationDistance` and faces the target | — |
| `KeepDistanceModule` | Ambient 10 | Kites: backs off when too close, faces otherwise | NavMesh |
| `SearchModule` | Reactive-1 = 19 | On losing the target, moves to `AgentTargeting.LastKnownPosition`, searches, then passes | `AgentTargeting` |
| `ChaseModule` | Reactive 20 | Drives at `AgentTargeting.Target`; auto-tightens stop distance and disables herd spread when a `CloseCombatModule` is present | `AgentTargeting` |
| `CoverModule` | Reactive+1 = 21 | Moves to the best `CoverPoint` relative to the threat | `CoverPoint` objects in the level |
| `FleeModule` | Override 30 | Runs from the nearest entity of a chosen `FactionRelationship`; hysteresis via triggerRadius/safeRadius | `EntityFaction`, NavMesh |
| `SteerModule` | Scripted 100 | Rider input, camera, jump, hold-to-leap | `MountModule`, `AgentController` |

### Social / group (claim the frame)

| Module | Default priority | What it does | Requires |
|---|---|---|---|
| `FlockingModule` | Social 15 | Separation / alignment / cohesion from neighbour arrays | `AgentController.nearbyAgentScanRadius > 0` + `nearbyAgentLayer` |
| `HerdModule` | Social 15 | Rebroadcasts the highest-priority intent in the herd; members spread onto a ring, then settle. Also provides `GetSlotPositionAround` | shared `herdId` string |
| `FormationModule` | Social 15 | Keeps a group in a column behind an unmanaged leader | leader reference / group id |

### Facing (second channel)

`WatchModule` (Ambient), `FacePlayerModule` (Ambient), `IdleLookAroundModule` (Personality) and
`InteractionFocusModule` (Scripted) are **movement** modules that return `StopAndFace` — they are
not `IFacingModule`. The only true `IFacingModule` implementors are `AgentRangedCombatModule`
(`FacingPriority => Priority`) and `NpcItemUseModule` (serialized `facingPriority`).

### Combat

| Module | Priority | Claims movement | Notes |
|---|---|---|---|
| `CloseCombatModule` | MeleeAttack 23 | yes (`StopAndFace`) | `rangeExitFactor` hysteresis + `attackCommitDuration`; exposes `AttackRange` |
| `AgentRangedCombatModule` | RangedAttack 22 | yes | Owns the whole engagement (backs off to `preferredRange`, strafes). Needs `AgentWeaponDefinition` + `AgentFireProfile` + `AgentAimProfile`; exposes `MaxRange`. Also `IFacingModule` |
| `NpcItemUseModule` | RangedAttack 22 | **no** | Fires a real `InventoryItem` from `EntityInventoryComponent`; triggers `TargetInRange` / `WhenHurt` / `OnInterval`. Needs `EntityEquipmentController` |
| `TurretModule` | n/a (not a BehaviourModule) | n/a | Stationary; resolves its own target. `[RequireComponent(typeof(EntityFaction))]` |
| `WeaponMount` / `WeaponSelector` | n/a | n/a | Multiple pre-placed weapon slots; `AgentRangedCombatModule` reads `ActiveMuzzle`/`ActiveDefinition` |

### Sensing / reaction

| Module | Priority | Notes |
|---|---|---|
| `AlertReceiverModule` | Reactive-1 = 19 | `AgentTargeting.ForceTarget` from an ally's `AlertBroadcaster` |
| `NoiseReceiverModule` | Reactive-2 = 18 | Hears `NoiseEmitter` events; investigate or aggro per `NoiseType` |
| `PerceptionModule` | n/a | FOV + LoS. `CanSee` writes memory, `IsVisible` does not. `occlusionLayers = Nothing` falls back to Default/Ground/Interior with a warning |

### Personality / tasks

| Module | Priority | Claims movement | Notes |
|---|---|---|---|
| `NpcTaskModule` | Fallback 0 | **no** | Picks a weighted `NpcTask`, writes `AgentGoal`, three phases (Choosing/Travelling/Dwelling) |
| `ChatterModule` | Personality 5 | **no** | Speaks the current task line when a player is in `hearingRadius`; shared static `globalCooldown` |

### Riding

`MountModule` (Fallback 0, `IInteractable`, `IPersistentEntity`; `allowAISelfMovementWhenMounted`
decides whether AI keeps running while ridden) + `SteerModule` (Scripted 100). Optional
`MountNetworkSync`, `MountedRiderPose`, `NpcPassenger`.

---

## 4. Motors

`Assets/Game/Scripts/agents/AI/Motors/`

| Motor | For | Extra interfaces |
|---|---|---|
| `NavMeshAgentMotor` | Everything that walks on the baked NavMesh | `IMountJumpMotor`, `IMountLeapMotor`, `IRiderControllable`, `ISelfDrivingMotor` |
| `RigidbodyMotor` | Physics ground vehicles | `IRiderControllable` |
| `HoverRigidbodyMotor` + `HoverGroundSensor` | Hovercraft | |
| `FlyingRigidbodyMotor` | Free 3D flight (pair with `AirWanderModule`) | |
| `OrnithopterFlightMotor` | The ornithopter's energy flight model | |
| `LeggedDriver` (abstract) | Procedurally animated legged rigs | `IRiderControllable`, `IMovementMotor` |

`IMovementMotor`: `Velocity`, `IsImmobile`, `HasReachedDestination`, `CurrentDestination`,
`Tick(in MoveIntent, float)`, `ForceStop()`, `NudgeDestination(Vector3)`,
`SuggestDestination(Vector3)`.

`ISelfDrivingMotor`: `SuspendSelfDrive()` / `ResumeSelfDrive()`, both idempotent. Implement it on
any motor that keeps moving the transform when nobody ticks it — a `NavMeshAgent` does.

**NavMesh vs procedural rig.** A NavMesh creature = `NavMeshAgent` + `NavMeshAgentMotor` +
`Animator` + `AgentAnimatorDriver`, and the NavMeshAgent owns the transform. A legged creature =
a `LeggedDriver` subclass in `Assets/Game/Scripts/Creatures/Drivers/` (Assembly-CSharp, because
`IMovementMotor`/`IRiderControllable` live there and no asmdef may reference the default assembly)
plus a `LeggedLocomotion` subclass in its own `SpaceGame.Creatures.<Name>` asmdef. The legs own
the pose; there is no NavMeshAgent, `NavMesh.CalculatePath` is used for routing only, and there is
no `AgentAnimatorDriver` because nothing is keyframed.

---

## 5. Targeting, factions, goals

- `EntityTargetRegistry` (static) — `Register`/`Unregister` from `EntityFaction.OnEnable/OnDisable`,
  `Query(owner, relationship, position, maxRange, results)`, `ResolveNearest`, `All`, `HasAny`.
- `EntityFaction` — `faction` + `relationshipTable`. `EntityFaction.Ensure(go, faction, table)` is
  the spawn-path helper. **An entity with no `EntityFaction` is invisible to every targeting
  module**, with no error.
- `FactionRelationshipTable.Get(a, b)` returns `Allied` for `a == b` and **`Neutral` for any pair
  with no row**.
- `AgentTargeting` — auto-added by `AgentController`. Public: `Target`, `HasTarget`,
  `DistanceToTarget`, `CanSeeTarget`, `LastKnownPosition`, `HasLastKnownPosition`, `TimeSinceSeen`,
  `LastAttacker`, `Relationship`, `SightRange`, `LoseRange`, `SimulatesHere`, `ForceTarget`,
  `ClearTarget`, `ApplyProfile`, `IsFightingWith`, `RestoreMemory`, `GetOrAdd`.
  Acquisition range is auto-widened at Awake to `longest weapon range + 5 m` by reading
  `AgentRangedCombatModule.MaxRange`, `CloseCombatModule.AttackRange` and `NpcItemUseModule.MaxRange`.
  Scoring is "effective distance": `currentTargetBias`, `lastAttackerBias`, `occludedPenalty`.
  Sight range is multiplied by `Sandstorms.SightFactorAt(position)`, floored at
  `proximityAcquireRange`.
- `TargetingProfile` — `Assets > Create > Agents > Targeting Profile`. Overrides every inline field
  on `AgentTargeting` when assigned. `MatchManager` swaps it at spawn for arena bots.
- `TargetResolution.IsViable(t)` / `.Refresh(current, ref timer, interval, dt, selfFaction,
  relationship, position)` — for modules resolving a NON-hostile candidate (allies, neutrals).
  Never hand-roll `if (target) return;`.
- `AgentGoal` — auto-added. `Set(pos, arriveRadius, reason, siteId, speedMultiplier)`,
  `TrySetSampled(...)`, `Clear()`, `HasGoal`, `HasArrived`, `DistanceToGoal` (flat, ignores Y),
  `Reason`, `SiteId`, `SpeedMultiplier`, `GetOrAdd`.
- `ProvocationModule` — `[RequireComponent(typeof(HealthComponent))]`, order -40. `leashRange`,
  `calmDownDelay`, `damageThreshold`. `Provoke(Transform)` / `Forget()` / `IsProvoked`.

---

## 6. Animator contract

`AgentAnimatorDriver.Tick` writes exactly these parameters — spelling included:

| Parameter | Type | Written from |
|---|---|---|
| `SpeedX` | Float | local velocity X × `animationSpeedMultiplier` × (`walkAnimBoost` when not running) |
| `SpeedY` | Float | local velocity Z, same scale |
| `FallSpeed` | Float | world velocity Y |
| `IsGrounded` | Bool | always `true` |
| `IsImmobalized` | Bool (**misspelled in code and in the controllers**) | `Motor.IsImmobile` |
| `IsAiming` | Bool | `SetIsAiming(bool)` |

Triggers: `Hurt`, `Die`, `ShootRifle`, `SpearAttack`, plus `TriggerByName(string)`.
Other components fire their own configurable triggers, and their **defaults do not match**:
`CloseCombatModule.attackAnimTrigger = "Meele"`, `AgentRangedCombatModule.shootAnimTrigger =
"AssualtShoot"`, `HealthReactionModule.hurtAnimTrigger = "Hurt"`, `dieAnimTrigger = "Death"`.
`Assets/Game/Art/Animations/Creatures/Golem.controller` carries `Death` **and** `Die` for exactly
this reason.

Existing controllers: `Assets/Game/Art/Animations/Creatures/{Golem,Vrescal,DuneRat}.controller`,
`Assets/Game/Art/Animations/Player/AstronautArmature.controller`.

**Three fields set the walk cycle and they must agree:**
1. `NavMeshAgent.speed` × `NavMeshAgentMotor.walkSpeedMultiplier` — how fast the body travels.
2. `AgentAnimatorDriver.animationSpeedMultiplier` / `walkAnimBoost` — which clip the blend tree lands on.
3. `AgentAnimatorDriver.animatorSpeedScale` — **clip playback rate**, `Animator.speed`, applied once
   in `Awake`. This is the one that stops skating: `rate = groundSpeed / strideSpeed`.

`animatorSpeedScale` is global to the Animator, so it slows attacks and one-shots too — walk speed
and animation rate cannot be tuned independently. Set `NavMeshAgent.speed` to the **run** and scale
`walkSpeedMultiplier` down from it, because `ChaseModule` asks for `isRunning`.

---

## 7. Persistence and netcode touch points

Details belong to the sibling skills `spacegame-persistence` and `spacegame-multiplayer`. The
agent-specific facts:

- `AgentController` implements `SpaceGame.Persistence.IPersistentEntity` (a marker interface), so
  **every agent is save-eligible automatically** — no per-creature opt-in.
- A creature spawned and owned by another system (`NpcWorldSim` and its caravans) must call
  `SaveableEntity.DisownToExternal()` or it is saved twice and duplicates on load.
- `AgentAuthority` is the per-component cached "does this machine drive this entity" answer.
  Ownership, not server-ness. `Invalidate()` from `OnTransformParentChanged` — that is the only
  thing that moves an entity to a different `NetworkObject`.
- `NetAuthority` (`Assets/Game/Scripts/Core/Multiplayer/NetAuthority.cs`) disables simulation
  drivers on remote machines. **Take a `LeggedLocomotion` out of its `simulationDrivers` list** or
  a remote copy slides with still feet.
- `HealthComponent.IsRestoring` is true while a save is being applied — `EntityLootTable` and
  `ProvocationModule` both check it. Any new death/damage reaction must too.
- Creatures that carry an `InventoryItem` need that item's `itemPrefab` registered as a network
  prefab (see `spacegame-multiplayer`); projectiles and equipped visuals must **not** be.

---

## 8. Where things live

| Thing | Path |
|---|---|
| Agent code | `Assets/Game/Scripts/agents/` |
| Legged locomotion policies | `Assets/Game/Scripts/Creatures/<Name>/` (own asmdef) |
| Legged drivers | `Assets/Game/Scripts/Creatures/Drivers/` (Assembly-CSharp) |
| Animator controllers | `Assets/Game/Art/Animations/Creatures/` |
| Setup notes (documentation-only file) | `Assets/Game/Scripts/agents/Profiles/EntitySystemSetup.cs` |
| World NPC groups | `Assets/Game/Scripts/agents/World/NpcWorldSim.cs`, `NpcGroup.cs` |
