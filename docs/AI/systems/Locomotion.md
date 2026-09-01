---
system: Locomotion
layer: characters
summary: "Procedural legged walking: one LeggedLocomotion base plus four policy objects per creature or walker"
paths:
  - Assets/Game/Scripts/Locomotion/
  - Assets/Game/Scripts/Creatures/
  - Assets/Game/Scripts/Vehicles/DesertCrawler/DesertCrawlerLocomotion.cs
  - Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs
  - Assets/Game/Tests/EditMode/WalkerTestRig.cs
symptoms:
  - "the walker stands frozen and never takes a step, with no error in the console"
  - "I moved the creature's transform and it snapped back next frame"
  - "the machine climbs into the sky when a player stands on its deck"
  - "the walker stops dead at the rim of a portal it should be able to cross"
  - "the legs hop, sync up or stutter instead of walking"
  - "a mounted ostrich vanishes out from under its rider on the other machine"
  - "the creature stopped walking after I added a LateUpdate to its subclass"
  - "the feet trail behind the body, or a planted foot slips along the ground"
reads_with: [AgentSystem, Vehicles, Persistence]
updated: 2026-09-01
---

# Locomotion

Procedural legged walking: one kinematic base class ([`LeggedLocomotion`](Assets/Game/Scripts/Locomotion/Core/LeggedLocomotion.cs)) owns the frame order and all shared arithmetic; each creature or walking vehicle is four small policy objects plugged into it.

**Scope:** [`Assets/Game/Scripts/Locomotion/`](Assets/Game/Scripts/Locomotion) (Core, Gait, Ground, Ik, Policy, Rig, Steering), [`Assets/Game/Scripts/Creatures/`](Assets/Game/Scripts/Creatures), [`DesertCrawlerLocomotion`](Assets/Game/Scripts/Vehicles/DesertCrawler/DesertCrawlerLocomotion.cs).
**Related:** [AgentSystem.md](AgentSystem.md) · [MountSystem.md](MountSystem.md) · [Vehicles.md](Vehicles.md) · [Persistence.md](Persistence.md) · asmdef `SpaceGame.Locomotion` (cannot reference Assembly-CSharp — that is why drivers live outside it).

## Model

- The base owns everything shared: rig discovery, per-leg measurement, the gait clock, foothold resolution, ground probing, gravity, the support survey, the IK call, gizmos. A machine subclasses it and returns four policies.
- **Four policy interfaces**, all plain C# (no MonoBehaviour, no scene → unit-testable), declared in [`LocomotionPolicies.cs`](Assets/Game/Scripts/Locomotion/Policy/LocomotionPolicies.cs):

| Interface | Decides | Shipped impls |
| --- | --- | --- |
| `IStrideModel` | stride length + working hip height, per leg | `YawArcStride` (splayed coxae), `HipBudgetStride` (foot under hip) |
| `IGaitPattern` | phase offset per leg, duty, may-step-early | `RippleGait`, `AlternatingGait`, `TrotGait`, `CrabWaveGait`, `CanterGait` |
| `IBodyMotion` | body height/attitude/bob, via `ref BodyPose` | `LevelDeckBody`, `BobbingBody` |
| `IFootStyle` | swing arc shape + sole normal | `FlatSole`, `ArticulatedSole` |

- **Invariant I1: no policy may return a state that stops the machine.** The clock advances by *distance travelled*, so speed 0 ⇒ no phase slice ever opens ⇒ permanent latch. `Duty()` never returns 0; min-planted gates always have an escape hatch.
- **Invariant I3:** nothing in the per-frame path allocates (angle buffers, support points, climb samples all allocated at `Initialise`).
- **Invariant I4:** the locomotion is the *single owner* of the body transform — it holds `pathPos` and writes `body.position` every `LateUpdate`. Moving the transform from outside does nothing (see Gotchas).
- **Invariant I5:** a leg needs a yaw (coxa) joint or a planted foot cannot stay planted while the body travels.
- Foot articulation is done by **pitching the sole normal handed to the solver**, never by driving the ankle — the solver places the *contact point*, so a planted foot cannot slip.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `LeggedLocomotion` | [Core/LeggedLocomotion.cs](Assets/Game/Scripts/Locomotion/Core/LeggedLocomotion.cs) | Abstract base; serialized fields, `SetTwist`, `Step(dt)`, diagnostics. Order 100 |
| ⤷ `.Rig.cs` | [Core/LeggedLocomotion.Rig.cs](Assets/Game/Scripts/Locomotion/Core/LeggedLocomotion.Rig.cs) | Discovery, per-leg measurement, `MaxSpeed`/`MaxYawRate`, ride-height calibration |
| ⤷ `.Gait.cs` | [Core/LeggedLocomotion.Gait.cs](Assets/Game/Scripts/Locomotion/Core/LeggedLocomotion.Gait.cs) | Clock, swing timers, foothold resolution, swing lift, load transfer |
| ⤷ `.Body.cs` | [Core/LeggedLocomotion.Body.cs](Assets/Game/Scripts/Locomotion/Core/LeggedLocomotion.Body.cs) | Path integration, climb gate, `Survey()`, reach correction, gravity, `FollowBody` |
| ⤷ `.Ik.cs` | [Core/LeggedLocomotion.Ik.cs](Assets/Game/Scripts/Locomotion/Core/LeggedLocomotion.Ik.cs) | `SolveLegs`, `SolveArms`, gizmos |
| ⤷ `.Save.cs` / `.Teleport.cs` | [Save](Assets/Game/Scripts/Locomotion/Core/LeggedLocomotion.Save.cs) / [Teleport](Assets/Game/Scripts/Locomotion/Core/LeggedLocomotion.Teleport.cs) | Stance snapshot; `ITeleportAware` + `IGroundProbeExclusions` |
| `WalkerRig` | [Rig/WalkerRig.cs](Assets/Game/Scripts/Locomotion/Rig/WalkerRig.cs) | Finds limbs by root prefix (`Limb_`/`Coxa_`/`Hip_`/`Arm_`), classifies joints by **longest run of parallel axles** (measured off `*Pin*` meshes) |
| `WalkerLimbGeometry` / `LegMeasurement` | [Rig/](Assets/Game/Scripts/Locomotion/Rig) | Immutable measured geometry; per-leg stride/reach/hip/footprint numbers |
| `LegState` | [Rig/LegState.cs](Assets/Game/Scripts/Locomotion/Rig/LegState.cs) | Limb + gait slot + foothold + load + swing bookkeeping |
| `WalkerArm` | [Ik/WalkerArm.cs](Assets/Game/Scripts/Locomotion/Ik/WalkerArm.cs) | An `Arm_` limb: target + tip direction, no gait slot, never walked on |
| `WalkerLimbSolver` | [Ik/WalkerLimbSolver.cs](Assets/Game/Scripts/Locomotion/Ik/WalkerLimbSolver.cs) | Analytic IK: yaw the limb plane onto the target, then `WalkerPlanarChain` (2-link analytic / 1-link aim / CCD for ≥3) |
| `WalkerGait` | [Gait/WalkerGait.cs](Assets/Game/Scripts/Locomotion/Gait/WalkerGait.cs) | The clock (one `Phase` field), `MaxSpeed`, `FootDrift`, `SwingPoint`, `BlendOffset` |
| `WalkerGround` | [Ground/WalkerGround.cs](Assets/Game/Scripts/Locomotion/Ground/WalkerGround.cs) | All raycasting; rejects own colliders, loose Rigidbodies and excluded surfaces; grows its buffer |
| `WalkerSurface` / `WalkerSupportPlane` | [Ground/](Assets/Game/Scripts/Locomotion/Ground) | Supporting plane under one sole / least-squares plane under the whole body |
| `WalkerFoothold` | [Ground/WalkerFoothold.cs](Assets/Game/Scripts/Locomotion/Ground/WalkerFoothold.cs) | The one correct foothold clamp: **horizontal**, against the hip **at touchdown** |
| `WalkerClimb` | [Ground/WalkerClimb.cs](Assets/Game/Scripts/Locomotion/Ground/WalkerClimb.cs) | Sustained-grade + wall test over a sampled profile; pure arithmetic, no rays |
| `BodyFeet` | [Ground/BodyFeet.cs](Assets/Game/Scripts/Locomotion/Ground/BodyFeet.cs) | How far *any* body's pivot sits above its soles (the player's is ~1 m off) |
| `WalkerPath` / `WalkerSteering` | [Steering/](Assets/Game/Scripts/Locomotion/Steering) | Forward-only polyline cursor (flat arrival test); heading error → twist |
| `LeggedDriver` | [agents/AI/Motors/LeggedDriver.cs](Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs) | Order 50. Rider (`IRiderControllable`) + AI (`IMovementMotor`) → `SetTwist`. Uses `NavMesh.CalculatePath`, never a `NavMeshAgent` |

## Creatures

| Creature | Files | Rig notes / policies |
| --- | --- | --- |
| Ostrich (biped) | [OstrichLocomotion](Assets/Game/Scripts/Creatures/Ostrich/OstrichLocomotion.cs), [SpineMotion](Assets/Game/Scripts/Creatures/Ostrich/OstrichSpineMotion.cs) (150), [NeckMotion](Assets/Game/Scripts/Creatures/Ostrich/OstrichNeckMotion.cs) (160), [NeckGaze](Assets/Game/Scripts/Creatures/Ostrich/OstrichNeckGaze.cs), [NeckSpring](Assets/Game/Scripts/Creatures/Ostrich/OstrichNeckSpring.cs) | `HipBudgetStride` + `AlternatingGait` + `BobbingBody` + `ArticulatedSole`; authored `maxYawRate`. Spine spends the neck **undoing** the body bob so the head is still in world space; NeckMotion *adds* on top (gaze snaps, never sweeps; 11 vertebrae weighted, not evenly shared) |
| Crab (4–8 legs) | [CrabLocomotion](Assets/Game/Scripts/Creatures/Crab/CrabLocomotion.cs), [CrabWaveGait](Assets/Game/Scripts/Creatures/Crab/CrabWaveGait.cs), [CrabClaws](Assets/Game/Scripts/Creatures/Crab/CrabClaws.cs) (150), [CrabClawMotion](Assets/Game/Scripts/Creatures/Crab/CrabClawMotion.cs) | `YawArcStride` + `CrabWaveGait` + `LevelDeckBody` (shell hugs the ground) + `FlatSole`. **Travels across its own nose** — wave runs along X in fore/aft antiphase, derived from `HomeLocal` not leg index. Leg count, swing slots and min-planted all derived at `Bind` |
| Horse (quadruped) | [HorseLocomotion](Assets/Game/Scripts/Creatures/Horse/HorseLocomotion.cs), [CanterGait](Assets/Game/Scripts/Creatures/Horse/CanterGait.cs), [HorseSpineMotion](Assets/Game/Scripts/Creatures/Horse/HorseSpineMotion.cs), [HorseRideSpring](Assets/Game/Scripts/Creatures/Horse/HorseRideSpring.cs) | `HipBudgetStride` (per-leg matters: forelegs ≠ hind legs) + `CanterGait` (walk→trot→canter/gallop as one continuous fn of `runBlend`, asymmetric lead + suspension) + flat-tuned `BobbingBody` + `ArticulatedSole`. Neck bounce is a **second-order spring**, not a filter |
| Humanoid | [HumanoidLocomotion](Assets/Game/Scripts/Creatures/Humanoid/HumanoidLocomotion.cs), [ArmSwing](Assets/Game/Scripts/Creatures/Humanoid/HumanoidArmSwing.cs), [SpineMotion](Assets/Game/Scripts/Creatures/Humanoid/HumanoidSpineMotion.cs) | Same four as the ostrich, every amplitude a fraction of it. First rig with **forward** knees — `BendSign` is measured from the rest pose, nothing selects it. Arms are `Arm_` limbs driven from `LastFrame.Phase` + the legs' own offsets (cannot drift); target is an **arc** about the shoulder |
| Desert Crawler (hexapod vehicle) | [DesertCrawlerLocomotion](Assets/Game/Scripts/Vehicles/DesertCrawler/DesertCrawlerLocomotion.cs), [WalkerPlatformCarrier](Assets/Game/Scripts/Vehicles/Systems/WalkerPlatformCarrier.cs) | `YawArcStride` + `RippleGait(swingLegs, minPlanted=3)` + `LevelDeckBody` + `FlatSole`. Statically stable (3 feet down); deck follows ~60% of slope, capped, because riders stand on it |

Drivers: [`OstrichDriver`](Assets/Game/Scripts/Creatures/Drivers/OstrichDriver.cs) (adds autoWalk idle), [`CrabDriver`](Assets/Game/Scripts/Creatures/Drivers/CrabDriver.cs), [`HorseDriver`](Assets/Game/Scripts/Creatures/Drivers/HorseDriver.cs), [`HumanoidDriver`](Assets/Game/Scripts/Creatures/Drivers/HumanoidDriver.cs), [`DesertCrawlerDriver`](Assets/Game/Scripts/Vehicles/Drivers/DesertCrawlerDriver.cs) — all thin subclasses of `LeggedDriver`.

## Flows

**Initialise (Awake) — the order is load-bearing:**
1. Build `WalkerGround` (holding the exclusion set *by reference*) → create the four policies → `WalkerRig.FindArmature` / `WalkerRig.Build`.
2. Split limbs: `Arm_` → `WalkerArm`; everything else → `LegState` (angle buffers allocated here, once).
3. `SortLegs()` — fixed order from rest footholds (left→right, rear→front), never from live positions.
4. `MeasureLegs()` — per leg: `LegReach`, `RestHipHeight`, `HipLocal`, footprint (from sole meshes), then the stride model's `WorkingHipHeight` + `StrideLength`. Global `cycleStride` = **shortest** leg's stride.
5. `gaitPattern.Bind(measurements)` — **before** `DeriveSpeeds()`.
6. `DeriveSpeeds()` → `MaxSpeed = stride / stanceDuration` at the run duty; `MaxYawRate` from the outermost foot radius (bipeds override with an authored value).
7. `CalibrateRideHeight()` (rest body height minus the mean stand-down) → `ResetBodyState()`.
8. `Start`: `SnapToGround()` + `GroundFeet()`, **unless** a save restore already spoke.

**Per frame — `LateUpdate` → `Step(dt)` (order 100):**
1. **Owning** (`AdvancePath`): advance yaw → resolve the body-space twist onto forward/right → `ApplyClimbGate` (once, on the raw command) → integrate `pathPos` → advance the gait clock by `Pace * dt`. **Followed** (`FollowBody`, `ExternallyPosed`): read the transform, back out `commandedWorldVelocity`/`yawRate` (clamped to `MaxSpeed`), advance the clock. No raycasts.
2. `PoseBody`: `Survey()` (per planted foot: one ray, is it on ground within `CarryTolerance`; fit the support plane in the yaw frame; accumulate load and `LeanX`) → `bodyMotion.Pose` → `ApplyReachCorrection` (drop the body until every grounded foot is back inside 95% reach) → `UpdateFall` **or** `SettleOnto` → write `body.rotation` and `body.position = pathPos + DisplayOffset`.
3. `UpdateGait`: re-read each leg's phase offset (continuous in `runBlend`), advance in-flight swings, else test slice-opened **or** `MayStepEarly`; on a new step resolve the foothold (`WalkerGait.Foothold` → `WalkerFoothold.Clamp` → ground sample) and freeze `SwingSpan` at lift-off. Then `UpdateLoad`.
4. `SolveLegs`: per leg build the `Frame`, ask `footStyle.SoleNormal`, `WalkerLimbSolver.Solve` + `Apply`; mark `Unreachable` only when `ReachFraction > 1`.
5. `TrackVelocity`. Subclass components run **after** at 150/160 (`OstrichSpineMotion`, `CrabClaws`, …) and call `SolveArms()` themselves — arms are deliberately not part of `Step`.

**The body is posed BEFORE the gait picks footholds.** Reversing it aims every step from last frame's hips — the feet visibly trail the body.

## Multiplayer

- The legs simulate **everywhere**. Only who owns the body transform changes.
- [`NetAuthority`](Assets/Game/Scripts/Core/Multiplayer/Authority/NetAuthority.cs) sets `IExternallyPosed.ExternallyPosed = true` on every non-simulated copy (filtered by `SimulationDrivers.BelongsTo`, so it does not reach a parented rider), and **skips disabling** drivers that implement `IExternallyPosed`.
- Replicates: the body transform (position + rotation), via the normal entity transform sync. Presented locally: gait phase, footholds, IK, bob/lean, neck/arm motion — all re-derived from measured body motion.
- Neither switching the locomotion **off** (remote slides with still feet) nor leaving it **on** (it overwrites the wire every `LateUpdate`; a mounted ostrich vanishes out from under its rider) is correct — following is.
- `SnapToGround()` is refused while externally posed. `ExternallyPosed` setter calls `ResetBodyState()` in **both** directions, or the machine teleports back to where ownership last changed.

## Persistence

[`LeggedGaitSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/LeggedGaitSaveable.cs) (key `"gait"`) round-trips `LeggedLocomotion.Snapshot`: gait `Phase`, `pathPos`, yaw, `smoothedHeight`/`heightPrimed`, fall velocity, and per-leg `LegSnapshot` (foot, ground normal, swing endpoints, offset, `SwingT`/`Span`, stance time, load). `LeggedLocomotion` itself is `IPersistentEntity` — declared on the base so every machine is covered, and needed because these are kinematic bodies that fail the non-kinematic-Rigidbody heuristic.

Purely **cosmetic**: it removes one stumble per creature per load. Phase is assigned verbatim, never blended. `SwingSpan` is floored at `1e-3` on restore. Not deferred — footholds are world positions captured in the same frame as the pose.

## Gotchas

- **Bind the gait pattern before deriving speeds.** An unbound pattern can report duty 0 → `MaxSpeed` 0 → `SetTwist` clamps everything to 0 → the distance-driven clock stops → no slice reopens. Dead machine, no error. (Invariant I1; cost a session on the crawler.)
- **You cannot move a walker by writing its transform.** `pathPos` overwrites it next frame — silently. Teleports, respawns, save restores and portals must go through `ITeleportAware.OnTeleported`, which rebases path, footholds, normals, swing arcs and arm targets by the same rigid transfer.
- **Ground probes ignore non-kinematic Rigidbodies** (`WalkerGround.IsLooseBody`). A player standing on the crawler deck was read as ground: deck rises → carrier lifts the rider → probe finds them higher → the machine climbs into the sky. Only ever in the middle of the deck, where the single central ray is.
- **`Physics.IgnoreCollision` does nothing to a raycast.** Portals must call `IGroundProbeExclusions.ExcludeFromGroundProbes` (idempotent, safe before the rig exists) or a walker stops dead at the rim of a hole it may legally walk through.
- **Nothing at stride frequency may go through a filter.** `heightSmooth` is for terrain noise only; bob/lean/arm-swing are added on top, unfiltered and in phase. Springs (`HorseRideSpring`, `OstrichNeckSpring`) are second-order and are allowed to lag — that lag *is* the effect.
- **Display offsets are never written back into `pathPos`** or the lean integrates and walks the machine off course — and it would leak into the fall test, which reads `pathPos.y`.
- **`Result.Clamped` is not `Unreachable`.** Only `ReachFraction > 1` counts; reading `Clamped` fired step-early on ~⅔ of frames, syncing the legs into a hop.
- **Climb limits:** `maxClimbAngle` (35° default) is deliberately below the NavMesh bake slope limit. Refusal needs a *sustained grade* over the run **or** one segment that is both too steep and rises more than `stepUpHeight` (the tallest single leg lift — not leg reach, or a machine steps onto things taller than itself). Yaw is never gated, reverse probes behind, downhill is never gated — those three are what keep the gate from latching. Missing ground is **never** a refusal (unloaded chunk ≠ void); likewise a fall needs `hasGround && carrying == 0 && above the threshold`.
- **Foothold clamp:** horizontal-only, and against the hip **at touchdown**. Clamping the 3D vector lifts the foothold off the ground and the machine levitates; clamping against the current hip drags every step short into a thrash.
- **Don't add a `LateUpdate` to a subclass** — it hides the base's and the machine stops walking. Use a separate component at order 150+ (`CrabClaws` is the model).
- `IsFalling`, `ClimbBlocked`/`ClimbScale` and `LastFrame` (`Diagnostics`) are the outside view; `ClimbBlocked` is only true while something is *asking* the machine to move.

## Extending

1. Model the rig: limb roots prefixed `Limb_`/`Coxa_`/`Hip_` (legs) or `Arm_` (not walked on), with a `*Pin*` cylinder mesh at each joint — hinge axes are measured from the pin, not guessed.
2. Subclass `LeggedLocomotion` in its own folder (+ asmdef referencing `SpaceGame.Locomotion`). Implement `CreateStride/CreateGait/CreateBody/CreateFeet`; add only the fields that are genuinely this machine's — joint travel, ground mask, step duration, gravity are all on the base.
3. Pick shipped policies first. Splayed coxae → `YawArcStride`; foot under hip → `HipBudgetStride`. Crewed → `LevelDeckBody`; creature → `BobbingBody`. Pad → `FlatSole`; toe roll → `ArticulatedSole`.
4. Only write a new `IGaitPattern` if the *shape* is new (as `CanterGait` and `CrabWaveGait` are). Derive leg roles from `LegMeasurement.HomeLocal` in `Bind`, never from leg indices; make offsets continuous in `runBlend`; never return duty 0.
5. Override `DeriveMaxYawRate()` for a biped (no long outboard leg to bound the pivot), `BuildArmLimits` if it has arms, `FootholdReachFraction` if steps land over-extended.
6. Subclass `LeggedDriver` (in Assembly-CSharp) — usually an empty class; set `lateralSteering` on the prefab for a crab-style machine.
7. Add `LeggedGaitSaveable` to the prefab, plus the usual entity persistence wiring.
8. Add EditMode tests against the plain policy classes ([`WalkerTestRig`](Assets/Game/Tests/EditMode/WalkerTestRig.cs) builds a procedural rig with no scene). Verify on an actual client — a machine that only walks on the host is not finished.
