# Legged robotics architecture: one core, many robots

**Date:** 2026-08-10
**Status:** approved design, ready for planning
**Base commit:** `c37b96b` ("feat: robotics, minigame, ship, models")
**Working-tree state this was written against:** `WalkerGround.cs` is the *expanded* 167-line
version (has `BelowUnder` / `TrySurface` / `HighestAlong`); `WalkerLegSolver.cs` is the *trimmed*
222-line plain two-link version — the sole-tilt null-space parameter present in `c37b96b` has been
removed in the working tree. Nothing references the removed API, so the tree is self-consistent.
**These files were observed changing outside the design session; re-check them before starting.**

---

## 1. Problem

Two legged machines exist and both work. Neither shares its locomotion with the other.

| Machine | Component | Size | Assembly |
| --- | --- | --- | --- |
| Six-legged Desert Crawler | `Assets/Scripts/Vehicles/SpiderWalkerLocomotion.cs` | 608 lines, monolithic | Assembly-CSharp |
| Robot ostrich (biped) | `Assets/Scripts/Creatures/Ostrich/OstrichLocomotion*.cs` | ~1000 lines over 5 partials | `SpaceGame.Vehicles.Ostrich` |

A genuinely reusable library already exists underneath them — `SpaceGame.Walker`
(`Assets/Scripts/Vehicles/Walker/`): `WalkerRig` (rig discovery and measurement), `WalkerLegSolver`
(analytic per-leg IK), `WalkerGait` (pure gait-clock statics), `WalkerGround` + `WalkerSurface`
(ground probing), `WalkerAxle`, `WalkerLegPose`, `WalkerPath`.

**The pieces are shared; the assembly is copy-pasted.** Both components independently reimplement:
`LegState`, `Initialise`, leg sorting and phase assignment, `MeasureGait`, `SetTwist` / `Pace` /
`Diagnostics` / `TryGetFoot` / `IsReady` / `LegCount` / `MeasuredVelocity`, the whole `UpdateGait`
loop, `SwingLiftFor`, `SolveLegs`, `SnapToGround` / `GroundFeet`, and the gizmos.

### The copies have drifted, and the drift is a live bug

- `SpiderWalkerLocomotion` carries **verbatim private copies** of ground probing —
  `GroundRay` (line 570), `SampleGround` (line 454), `SwingLiftFor` (line 432) — and **never calls
  `WalkerGround` at all**. `SampleGround` still contains the fabricated-surface-point defect
  documented in `2026-08-10-walker-leg-grounding-design.md`: it pairs the centre ray's x/z with the
  *highest neighbour's* y, producing a point that lies on no surface.
- `SpiderWalkerLocomotion.ResolveFoothold` (lines 481–504) clamps the foothold **radially in 3D**
  against the hip's *current* position (lines 498–501). `OstrichLocomotion.ResolveFoothold`
  (`OstrichLocomotion.Gait.cs:156`) clamps **horizontally** against the hip's position *at
  touchdown*, with the reach margin applied to the horizontal result rather than to the leg's
  length. The ostrich version is the corrected one; the fix never travelled back.
- `SpiderWalkerLocomotion.Step` (line 322) runs `MoveHull → UpdateGait → LevelBody → SolveLegs`.
  The ostrich runs `MoveBody → PoseBody → UpdateGait → SolveLegs` because posing the body *after*
  the gait leaves every foothold a frame stale. The crawler still has that bug.
- The crawler has **no gravity and no fall handling**. Any legged machine spawned above its terrain
  in a streamed world hangs in the sky; this was found and fixed on the ostrich only.

The drivers are near-twins too. `Assets/Scripts/Creatures/OstrichDriver.cs:20-22` says so in a
comment: *"This is a near-twin of SpiderWalkerDriver. The shared steering logic wants extracting
into a common base once both are known good."*

### The generalisation problem

`WalkerRig.Build` looks up `Coxa_<id>`, `Hip_<id>`, `Knee_<id>`, `Ankle_<id>`, `Foot_<id>` **by
name**, and `WalkerLegSolver` stage 2 is hard-wired to `SolveTwoLink` plus a fixed-length foot. The
architecture is therefore pinned to exactly one leg shape. Supporting other limb shapes requires
generalising both.

---

## 2. Decisions taken

These were settled in the brainstorming session and are **not open for re-litigation** during
planning or implementation.

| Decision | Value |
| --- | --- |
| Scope | Legged robots **plus limb variety** — 2-joint stubs, 5-joint insect legs, and arms that use the same IK but are not walked on |
| Existing machines | **Both migrate** onto the new core; the old components are replaced |
| Concurrency target | **1–5 walking robots at once.** Readability wins over throughput. No LOD system, no Jobs/Burst |
| Authoring model | **One small C# file per robot**, deriving from a shared base |
| Architecture shape | **Thin base + policies** — the base owns the frame order; four library interfaces carry the variation and the robot's file assembles them |
| Folder layout | Shared library moves to a **top-level `Assets/Scripts/Locomotion/`**; robots live under `Creatures/` and `Vehicles/` |
| Type naming | `Walker*` prefix **kept** ("walker" still accurately means "legged machine"); avoids renaming six test files |

---

## 3. Target layout

```
Assets/Scripts/

  Locomotion/                          [SpaceGame.Locomotion]   (was Vehicles/Walker)
    Rig/       WalkerRig, WalkerLimb, WalkerLimbGeometry, WalkerAxle
    Ik/        WalkerLimbSolver (+ .Types), WalkerLimbPose, WalkerPlanarChain
    Ground/    WalkerGround, WalkerSurface
    Gait/      WalkerGait
    Policy/    IStrideModel, IGaitPattern, IBodyMotion, IFootStyle
               YawArcStride, HipBudgetStride
               RippleGait, AlternatingGait, TrotGait
               LevelDeckBody, BobbingBody
               FlatSole, ArticulatedSole
    Core/      LeggedLocomotion (+ .Rig .Gait .Body .Ik), LegState,
               LegMeasurement, WalkerFoothold, Diagnostics

  Creatures/
    Ostrich/                           [SpaceGame.Creatures.Ostrich]
      OstrichLocomotion.cs             (policy assembly only)
      OstrichNeckMotion.cs, OstrichNeckGaze.cs, OstrichNeckSpring.cs,
      OstrichSpineMotion.cs, OstrichSteering.cs
    OstrichDriver.cs                   (Assembly-CSharp)

  Vehicles/
    DesertCrawler/                     [SpaceGame.Vehicles.Crawler]
      DesertCrawlerLocomotion.cs       (policy assembly only)
    DesertCrawlerDriver.cs             (Assembly-CSharp)
    MountStation.cs, ArticulatedPart.cs, WalkerPlatformCarrier.cs, ...  unchanged
```

### Assembly rules and why

- **`WalkerPath.cs` stays in `SpaceGame.Locomotion`.** It is driver-side path following rather than
  locomotion, but it has no Assembly-CSharp dependency and `WalkerPathTests` already reaches it
  through that assembly. Moving it would buy nothing and cost a test reference.
- **Locomotion subclasses live in an asmdef; drivers cannot.** `IRiderControllable` and
  `IMovementMotor` live in Assembly-CSharp, which no asmdef may reference. That is why
  `OstrichDriver` already sits outside its own asmdef. Locomotion needs neither interface, so it
  stays testable; only the ~20-line driver shells drop into Assembly-CSharp.
  **Moving those two interfaces into their own assembly is out of scope.**
- **`SpaceGame.Vehicles.Ostrich` is replaced by `SpaceGame.Creatures.Ostrich`.** The existing name
  is wrong (the ostrich is a creature) and the assembly should not be the home of all robots.
- **`SpaceGame.Tests.EditMode` references** change from
  `["SpaceGame.Minigame.Core", "SpaceGame.Walker", "SpaceGame.Vehicles.Ostrich", ...]` to
  `["SpaceGame.Minigame.Core", "SpaceGame.Locomotion", "SpaceGame.Creatures.Ostrich",
  "SpaceGame.Vehicles.Crawler", ...]`.
- **Namespace** `SpaceGame.Walker` becomes `SpaceGame.Locomotion`. Roughly eight `using` sites.

### Files that MUST be overwritten in place, never deleted and recreated

Unity prefabs bind components by the script's `.meta` GUID. Recreating these files silently strips
the components off the prefabs.

| File | GUID | Bound by |
| --- | --- | --- |
| `Assets/Scripts/Creatures/Ostrich/OstrichLocomotion.cs` | `fdb87fb2bba7844c3a62ea2d74a6f3af` | `Assets/Prefabs/agents/creatures/Ostrich.prefab` |
| `Assets/Scripts/Vehicles/SpiderWalkerLocomotion.cs` | `dbb3fe638ed3e41c1a5c210c79f9577f` | `Assets/Prefabs/agents/vehicle/DesertCrawler.prefab` |
| `Assets/Scripts/Creatures/OstrichDriver.cs` | `937c6882de8f04080b3e01d87087d8f7` | `Ostrich.prefab` |
| `Assets/Scripts/Vehicles/SpiderWalkerDriver.cs` | `9c8c7e9e8a6d84fef8456f2430952331` | `DesertCrawler.prefab` |

`SpiderWalkerLocomotion.cs` → `DesertCrawlerLocomotion.cs` and `SpiderWalkerDriver.cs` →
`DesertCrawlerDriver.cs` are **renames carrying the existing `.meta`**, with the class renamed
inside. Rename the `.cs` and its `.meta` together; do not let Unity mint a new GUID.

`Assets/Tests/EditMode/OstrichLocomotionTests.cs` hard-codes the prefab path in a `PrefabPath`
const. Prefabs are not moving in this work, but if that changes the const must change with it.

### Type renames

Leg-specific names become limb-specific ones, because the types now describe chains that are not
always legs. **These are mechanical find-and-replace renames.** They touch test *call sites* but no
test *assertion* — every expected value stays exactly as written.

| Now | Becomes |
| --- | --- |
| `SpaceGame.Walker` (namespace, asmdef) | `SpaceGame.Locomotion` |
| `WalkerLegSolver` (+ `.Types`) | `WalkerLimbSolver` |
| `WalkerLegGeometry` | `WalkerLimbGeometry` |
| `WalkerLegPose` | `WalkerLimbPose` |
| `WalkerRig.Leg` | `WalkerRig.Limb` |

Unchanged: `WalkerRig`, `WalkerGait`, `WalkerGround`, `WalkerSurface`, `WalkerAxle`, `WalkerPath`.
Test *file* names are unchanged too — `WalkerLegSolverTests.cs` keeps its name and GUID and simply
exercises the renamed type. Renaming test files as well is optional tidying and is **not** part of
this work.

---

## 4. The limb: from a fixed 4-joint leg to an N-joint chain

### 4.1 Discovery by geometry, not by name

Today `WalkerRig.Build` (line 97) requires the exact names `Coxa_/Hip_/Knee_/Ankle_/Foot_`. The
replacement finds the limb root and then **walks the bone chain**, measuring each joint's axle from
its pin mesh as it goes — `WalkerRig` already does this measurement and already checks parallelism
(lines 180–200); it currently discards the information.

**Classification rule:** measure every joint's axle down the chain, then find the **longest run of
mutually-parallel axles**. That run is the *pitch chain*. Joints **before** it are *base DOFs* (the
yaw coxa, and optionally a roll trochanter). Joints **after** it are *tip DOFs* (the sole's roll
hinge).

Why not the simpler "consume parallel joints until one isn't": an insect leg is often
coxa (yaw) → **trochanter (roll)** → femur → tibia → tarsus, and the simple rule stops dead at the
trochanter and misclassifies it as the sole. The longest-run rule handles it.

**Both existing rigs re-import unchanged.** `Coxa → Hip → Knee → Ankle` is a run of three parallel
pitch axles with one non-parallel joint before it (the yaw coxa) and one after it (`Foot_`). That is
exactly what the walk finds. **No re-export of `ostrich.blend` or `desert_crawler.blend` is
required** — which matters, because `ostrich.blend` is hand-modelled and `ostrich_build.py` would
destroy it.

**Guard rails:**
- A bone explicitly named `Foot_<id>` is **forced** to the tip-roll role regardless of measurement,
  so a mis-measured pin cannot silently truncate a chain.
- The walk follows **only bones carrying a `*Pin*` mesh child**. Leaf bones (`_end`, produced when
  a model is exported without `add_leaf_bones=False`) and `COL_` collision boxes are skipped. This
  is a new hazard that name-lookup did not have — hierarchy-walking must handle it explicitly.
- **Branching** (a joint with more than one pin-carrying bone child) warns naming the limb id and
  does not guess.
- The limb root is any bone matching `Limb_<id>` or `Coxa_<id>`, falling back to `Hip_<id>`. A limb
  whose root axle is *parallel to the pitch chain* has **no yaw joint**: it is planar, stage 1 is a
  no-op, and the gait is told (see §7).

### 4.2 Geometry becomes arrays

`WalkerLegGeometry` → `WalkerLimbGeometry`:

```csharp
public struct WalkerLimbSegment
{
    public Vector3    AxleLocal;     // hinge axis in the joint's own space
    public Quaternion RestLocal;     // rest localRotation
    public float      RestAngle;     // absolute plane angle at rest, atan2(up, fwd)
    public float      Length;        // to the next joint (last: to the contact point)
}

public struct WalkerLimbGeometry
{
    public bool       HasYaw;
    public Vector3    YawAxisBody, YawAxisLocalRoot;
    public Quaternion RestRootLocal;
    public Vector3    RestFwdBody;

    public WalkerLimbSegment[] Pitch;   // 1..N, was Upper/Lower/Foot

    public bool       HasRoll;          // tip roll (the sole)
    public Vector3    RollAxisLocalTip;
    public Quaternion RestTipLocal;

    public float      BendSign;
    public Vector3    ContactLocalTip;

    public float      RestFootRadius;
    public float      MaxReach;         // sum of Pitch[].Length * 0.97f
}
```

`BendSign` stays a single value: it pins the elbow solution of the **two-link analytic path** only.
The CCD path for three or more free links is seeded from the rest pose, so its bend directions are
implicit in the seed and need no stored sign.

`WalkerLimbSolver.Limits` → `{ float Yaw; float[] Pitch; float Roll; }`. The ostrich already needs
per-joint values (hip 55 / knee 90 / ankle 60) and currently expresses them as named fields.
Provide `Limits.Uniform(yaw, pitch, roll, count)` for the simple case.

`WalkerLegSolver.Result` → `{ float Yaw; float[] Pitch; float Roll; bool Clamped; float
ReachFraction; }`. **Allocate the arrays once per leg at `Initialise`** and reuse them; nothing in
the per-frame path may allocate.

### 4.3 Stage 1 untouched, stage 2 gets one new front door

Stage 1 — yawing the leg's plane onto the target — does not care how many joints follow it, and is
copied across verbatim.

Inside the plane the chain is N links with the **tip direction prescribed** (the ground normal, for
a foot), which is the classic reduction to N−1 free links:

| Free links | Case | Method |
| --- | --- | --- |
| **2** | today's leg — ostrich and crawler | **the existing analytic `SolveTwoLink`, bit-for-bit unchanged** |
| **1** | stubby 2-joint leg | direct aim; exact when reachable, `ReachFraction > 1` otherwise |
| **≥3** | 5-joint insect leg, arms | CCD seeded from the rest pose, **fixed iteration budget** (8), joint limits applied every pass |

This table is the load-bearing decision of the whole section. **Both shipping machines keep the
identical code path through the IK**, so migrating them cannot perturb their gait and
`WalkerLegSolverTests` keeps passing as written. New limb shapes get a general path that is
deterministic — a fixed iteration count, not a convergence gamble.

New file `WalkerPlanarChain.cs` owns the dispatch and the CCD; `WalkerLimbSolver` keeps stage 1,
the limit application, and `Apply`.

### 4.4 A limb is not a leg

```
WalkerLimb  = rig chain + solver + a world target + a tip direction
LegState    = WalkerLimb + gait slot + foothold + load share + swing bookkeeping
```

An arm is a `WalkerLimb` driven by something that is not a gait. Keeping gait state out of the limb
type is the entire cost of supporting arms in this design.
**Building an arm/manipulator controller is out of scope.**

### 4.5 A precondition that currently has no warning

Stage 1 is exact only because the first pitch joint sits **on** the yaw axis (that is why yawing
the coxa does not move the hip, and why the two stages cannot interfere). `WalkerRig` will check
this at measure time and warn naming the limb id, the same way it already warns about non-parallel
pins.

---

## 5. The core: `LeggedLocomotion`

An abstract `MonoBehaviour`, `[DefaultExecutionOrder(100)]`, split five ways by the question each
file answers — the same split `OstrichLocomotion` already uses, which is a proven structure here:

| File | Answers | Approx. |
| --- | --- | --- |
| `LeggedLocomotion.cs` | the component: every base `[SerializeField]`, public API, `Step()` order | 180 |
| `LeggedLocomotion.Rig.cs` | what the machine IS — discovery, `LegMeasurement`, `LegState`, cadence | 180 |
| `LeggedLocomotion.Gait.cs` | when/where a foot goes — clock, swing bookkeeping, foothold, lift | 200 |
| `LeggedLocomotion.Body.cs` | where the body goes — survey, gravity, fall, land, snap | 180 |
| `LeggedLocomotion.Ik.cs` | posing legs onto it — solver call, apply, gizmos | 110 |

**All base `[SerializeField]` fields stay in `LeggedLocomotion.cs`.** Inspector *order* across
partials is left to the compiler, which interleaves `[Header]` groups. This is a recorded lesson
from the ostrich; do not scatter them.

~850 lines of base replacing ~1600 lines of duplicated locomotion. The base is not tiny; it is the
only copy.

### 5.1 Frame order

```
Step(dt)
  1  AdvancePath      commanded twist → pathPos, currentYaw; gait.Advance(distance travelled)
  2  Survey           → SupportState { supportHeight, carryingCount, loadCentroid, groundY, hasGround }
  3  Fall             gravity / land / GroundFeet     ← core; when falling it VETOES step 4's height
  4  Body.Pose        IBodyMotion writes height, attitude, display offset
  5  UpdateGait       IGaitPattern picks who swings; shared foothold resolve; IFootStyle arc
  6  SolveLegs        IFootStyle tip direction → WalkerLimbSolver → Apply
  7  TrackVelocity
```

**The body is posed before the gait picks footholds, and the order is the whole point.** The gait
reads hips and rest footholds off the body transform; posing last leaves both a frame stale — ten
centimetres at a run, a whole frame of yaw in a turn, and it reads exactly as feet trailing the
body. Reading `Hip.position` before `SolveLegs` is safe because the hip sits *on* the coxa's yaw
axis.

### 5.2 What the base owns

Rig discovery and per-leg measurement · `LegState[]` and all swing bookkeeping including the
`SwingSpan` freeze at lift-off · the gait clock · **foothold resolution (the one correct copy)** ·
swing lift via `WalkerGround.HighestAlong` · all ground probing via `WalkerGround` · the support
survey · gravity, fall, land, `GroundFeet` · `SnapToGround` · IK application and `Unreachable`
detection · `SetTwist` clamping and `MaxSpeed` / `MaxYawRate` derivation · `Diagnostics` ·
`TryGetFoot` / `IsReady` / `LegCount` / `MeasuredVelocity` · gizmos.

Base serialized fields: ground mask, `rayStartAbove`, `rayLength`, `snapToGroundOnStart`, joint
limit ranges, `stepDuration`, `stepClearance`, `obstacleClearance`, `autoCalibrateRideHeight`,
`rideHeight`, `heightSmooth`, `fallGravity`, `maxFallSpeed`, `footGroundTolerance`,
`fallThresholdFraction`, `drawGizmos`.

### 5.3 Behaviour carried over from the ostrich verbatim

These are recorded as expensive lessons and must survive the extraction unchanged:

- **Freeze a swing's duration at lift-off** (`LegState.SwingSpan`). Recomputing it each frame lets a
  step begun at speed stretch when the rider slows; the foot is still airborne when its slice
  reopens, misses the slot, and limps.
- **Nothing at stride frequency goes through a filter.** `heightSmooth` filters the **ground** only.
  Bob and lean are added on top, unfiltered and in phase.
- **Sway/bob are display offsets on top of `pathPos`** and are never written back, or they integrate
  into a drift. Keeping bob out of `pathPos` is also what keeps it out of the fall test.
- **`Unreachable` is `ReachFraction > 1`, not `Result.Clamped`.** `Clamped` means "some joint is on
  a limit", which on a bird is most frames; reading it as unreachable fires the step-early rule on
  two thirds of frames and syncs the legs into a hop.
- **Clamp footholds horizontally, then drop to the ground**, against the hip's position **at
  touchdown**. Clamping the 3D vector drags the target back along the line to the hip, lifting it,
  and the foot plants in mid-air.
- **Foot articulation is done by pitching the ground normal handed to the solver**, never by driving
  the ankle. The solver places the *contact point* and derives the ankle from it, so the foot pivots
  about the point it stands on and a planted foot cannot slip.
- **A ray that finds no ground is deliberately not a fall.** In a streamed world that means the
  chunk has not loaded.

### 5.4 Per-leg stride, not an averaged one

`OstrichLocomotion.Rig.cs:113` (`MeasureGait`) and `SpiderWalkerLocomotion:241` both **average**
`legReach` / `restHipHeight` / `RestFootRadius` across all legs into a single `strideLength`. A
quadruped with shorter front legs is an entirely natural rig and gets a stride that fits neither
pair.

`LegMeasurement` becomes per-leg and carries `legReach`, `restHipHeight`, `workingHipHeight`,
`strideLength`, `stepHeight`, `footprintRadius`, `homeLocal`, `footRadiusFromCentre`. **Cycle
distance stays global** — the body only travels one distance per cycle — and is derived from the
*shortest* leg's stride, since that is the one that runs out first.

### 5.5 Gravity moves into the core

Currently ostrich-only (`OstrichLocomotion.Body.cs:176` `UpdateFall`). Any legged machine spawned
above its terrain in a streamed world hangs in the sky. `UpdateFall`, `IsCarryingWeight`, `Land`
and `GroundFeet` move to `LeggedLocomotion.Body.cs` and apply to every robot. Both gates are
load-bearing and both come across:

1. No foot is carrying weight — a planted foot counts only if it is within `footGroundTolerance` of
   the ground under *it* **and** within `MaxReach` of its hip.
2. The body is more than `fallThresholdFraction * legReach` above `groundY + rideHeight` — this is
   what stops a run's flight phase reading as a fall.

The drop is written straight to `pathPos`, never through `heightSmooth`: an exponential approach is
the opposite shape to a fall.

---

## 6. The four policies

All are plain C# classes in `SpaceGame.Locomotion` — no `MonoBehaviour`, no scene, no rig required
to test.

```csharp
public interface IStrideModel                     // how far a leg can step
{
    float StrideLength(in LegMeasurement m);      // yaw-arc chord, or hip-height budget
    float WorkingHipHeight(in LegMeasurement m);  // ostrich stands down; crawler returns rest
}

public interface IGaitPattern                     // when each leg swings
{
    void  Bind(IReadOnlyList<LegMeasurement> legs);  // once, at Initialise: learn the layout
    float PhaseOffset(int leg, float runBlend);      // ripple / trot / pace / bound
    float Duty(float runBlend);                      // fraction of the cycle airborne
    bool  MayStepEarly(in StepEarlyRequest r);       // min-planted gate, or stance-time gate
}

public interface IBodyMotion                      // where the body goes
{
    void Pose(in BodyFrame f, in SupportState s, float dt, ref BodyPose pose);
}

public interface IFootStyle                       // what the foot does
{
    Vector3 SwingPoint(Vector3 from, Vector3 to, float t, float lift);
    Vector3 SoleNormal(in LegState leg, in WalkerLimbSolver.Frame frame);
}
```

Supporting types:

```csharp
public struct BodyPose      { public Vector3 PathPos; public float Yaw;
                              public Vector3 DisplayOffset; public Quaternion Attitude; }
public struct SupportState  { public float SupportHeight; public int CarryingCount;
                              public float Load; public float LeanX;
                              public float GroundY; public bool HasGround; }
public struct BodyFrame     { public Transform Body;
                              public float CommandedSpeed, CommandedYawRate, MaxYawRate;
                              public float RunBlend, Effort, Phase, Duty, RideHeight, LegReach; }
public struct StepEarlyRequest { public int LegIndex; public int PlantedCount; public int LegCount;
                              public float StanceTime; public float SwingDuration;
                              public bool Unreachable; }
```

### 6.1 `PhaseOffset` takes `runBlend`, and that is the point

`WalkerGait.MetachronalOffset(i, n)` spreads legs **evenly**, which is a ripple wave and nothing
else. A quadruped built on it can only ever ripple. Real quadruped gaits are not even spreads:

| Gait | Offsets | Assigned by |
| --- | --- | --- |
| Ripple / metachronal | `i / n` | leg order around the hull |
| Trot | `{0, ½, ½, 0}` | **diagonal pairs** |
| Pace | `{0, ½, 0, ½}` | **lateral pairs** |
| Bound | `{0, 0, ½, ½}` | **front pair, then rear pair** |

Because offsets are a **continuous function of `runBlend`** rather than a table that gets
reassigned, a walk→trot transition blends instead of teleporting a foot mid-swing. `Bind` gives the
policy the leg layout once so it can work out which legs are diagonal, lateral, front or rear from
`homeLocal` rather than from hard-coded indices.

### 6.2 Foothold resolution is core, not policy — deliberately

It is the single most bug-prone piece in the system and the two machines have drifted copies. The
ostrich's version becomes the only one, in `WalkerFoothold.Resolve`:

```csharp
// margin on the HORIZONTAL result, never on the leg's length
float limit      = geometry.MaxReach;
float rise       = Mathf.Max(0f, hipAtTouchdown.y - probe.y);
float horizontal = Mathf.Sqrt(Mathf.Max(limit*limit*0.04f, limit*limit - rise*rise))
                   * footholdReachFraction;
```

Taking the hip height out of an already-shortened radius is a different sum. On the ostrich,
0.72 × 1.79 m = 1.29 m is *less than* the 1.40 m hip height, the square root goes imaginary, and the
budget collapses onto its degenerate `limit²*0.04` floor — every step landing 0.25 m ahead of the
hip instead of 0.80 m, then dragging to 1.6 m behind, which a 1.79 m leg cannot hold.
`SpiderWalkerLocomotion` has the same expression and is not bitten only because its legs stick out
sideways so `rise` stays well under `limit`.

The stride model contributes a length and a stand-down height. Nothing else gets an opinion.

### 6.3 Shipped implementations

| Interface | Implementation | Source | Notes |
| --- | --- | --- | --- |
| `IStrideModel` | `YawArcStride(yawRange, fraction=0.85)` | crawler | `2·RestFootRadius·sin(yawRange·0.85)`; `WorkingHipHeight` = rest |
| | `HipBudgetStride(hipHeightFraction=0.86, strideFraction=0.72)` | ostrich | `2·sqrt(L²−h²)·0.72` with the degenerate floor |
| `IGaitPattern` | `RippleGait(swingSlots, minPlanted)` | crawler | duty = `swingSlots/legCount`; min-planted step-early gate |
| | `AlternatingGait(walkDuty, runDuty)` | ostrich | offsets `{0, ½}`; duty blended by speed; stance-time gate |
| | `TrotGait(walkDuty, runDuty)` | **new** | diagonal pairs derived from `homeLocal` |
| `IBodyMotion` | `LevelDeckBody(heightSmooth)` | crawler | level attitude, smoothed height |
| | `BobbingBody(bob, sway, runPitch, turnRoll)` | ostrich | bob at 2× stride frequency, load-weighted lean |
| `IFootStyle` | `FlatSole()` | crawler | `WalkerGait.SwingPoint` sine arc, sole flat on the normal |
| | `ArticulatedSole(toeOff, swingToe)` | ostrich | `sin(π·t(2−t))` arc, apex at 29%, toe curve |

### 6.4 A robot file, in full

```csharp
public class OstrichLocomotion : LeggedLocomotion
{
    [SerializeField] private float hipHeightFraction = 0.86f;
    [SerializeField] private float walkSwingDuty = 0.44f, runSwingDuty = 0.62f;
    [SerializeField] private float bobAmount = 0.055f, swayAmount = 0.55f;
    [SerializeField] private float runPitch = 16f, turnRoll = 8f;
    [SerializeField] private float toeOffAngle = 18f, swingToeAngle = 12f;

    protected override IStrideModel CreateStride() => new HipBudgetStride(hipHeightFraction);
    protected override IGaitPattern CreateGait()   => new AlternatingGait(walkSwingDuty, runSwingDuty);
    protected override IBodyMotion  CreateBody()   => new BobbingBody(bobAmount, swayAmount, runPitch, turnRoll);
    protected override IFootStyle   CreateFeet()   => new ArticulatedSole(toeOffAngle, swingToeAngle);
}
```

A quadruped that bobs like the bird but keeps three feet down like the station is
`new BobbingBody(...)` beside `new RippleGait(swingSlots: 1, minPlanted: 3)` — no new base class,
no copied code.

---

## 7. Invariants

**I1 — No policy may return a state that stops the machine.**
The clock advances by *distance travelled*, so speed 0 means `gait.Advance` never turns, no phase
slice ever opens, and nothing can un-stick it. This is exactly the `IsBlocked` latch that broke the
crawler rewrite and cost a session: `SetTwist` forced `commandedSpeed = 0`, speed 0 stopped the
gait, and no slice reopened. Policies may step a leg early, refuse a foothold, or ask for a shorter
stride. They may **not** gate motion on their own output.

**I2 — The 2-free-link IK path is bit-for-bit the existing analytic solve.**
Any change to `SolveTwoLink` is a regression, not a refactor.

**I3 — Nothing in the per-frame path allocates.**
Solver arrays are allocated once per leg at `Initialise`. `WalkerGround` already owns its buffers.

**I4 — `LeggedLocomotion` is the single owner of the body's transform.**
Drivers hand over a twist and read back achieved motion. Nothing else writes the pose. Rigidbodies
stay kinematic with gravity off.

**I5 — A planar limb (no yaw joint) cannot hold a planted foot through a turn.**
The gait is told at `Bind` time and does not promise zero slip for such a machine.

---

## 8. Failure modes

| Condition | Response |
| --- | --- |
| Limb unmeasurable (zero-length segment, non-parallel pins, ball joint modelled as one bone) | warn naming the limb id; skip that limb |
| Branching chain (>1 pin-carrying bone child) | warn naming the limb id; do not guess |
| Zero legs discovered | disable the component with a message naming the expected bone convention (existing behaviour) |
| Leg with no yaw joint | solve planar, stage 1 a no-op; invariant I5 |
| First pitch joint not on the yaw axis | warn naming the limb id; solve anyway (degrades, does not fail) |
| Ray finds no ground | hold height and hold the foothold — a streamed chunk has not loaded, it is not a void |
| Min-planted unsatisfiable (too few legs, damaged rig) | the gate degrades to "never block"; invariant I1 |
| Prefab binding | overwrite the two locomotion files in place; never delete and recreate |

---

## 9. Migration plan

### Step 0 — Baselines

Run **both** machines through the existing headless harness (unity-mcp bridge; EditMode plus Play
mode, per `project_headless_verification`) and record, for 600 frames at several speeds plus a
pivot:

- planted-foot slip (metres)
- worst reach fraction
- distance covered vs commanded
- stance-leg count distribution
- pivot drift over a 133° turn
- full EditMode suite result

Without this, Step 3's behaviour change is a vibe rather than a measured delta. Ostrich reference
figures already on record: slip 0.00000 m, worst reach 0.93–1.15, touchdown descent 0.30 m/s, top
speed 8.67 m/s, suite 117/117. Crawler after its revert: 40.0 m in 600 frames at 4 m/s, worst reach
0.834, ≥3 stance legs, 133.3° pivot with 0.06 m drift.

### Step 1 — Library, behaviour-neutral

Move `Assets/Scripts/Vehicles/Walker/` → `Assets/Scripts/Locomotion/`, rename the assembly and
namespace, generalise `WalkerRig` and `WalkerLimbSolver` to N joints per §4, add
`WalkerPlanarChain`. **No locomotion component is touched.**

*Acceptance:* `WalkerLegSolverTests`, `WalkerAxleTests`, `WalkerSurfaceTests`, `WalkerLegPoseTests`,
`WalkerGaitTests`, `WalkerPathTests` all pass with **every assertion and expected value unchanged**
— only the `using` line and the renamed type names in §3 may differ. Both machines' Step 0 numbers
are **identical, not merely close**.

### Step 2 — Extract the base from the ostrich

Not written from scratch. The ostrich is the verified machine and its five partials are already
split by exactly the questions the base needs. Its code moves up into `LeggedLocomotion.*`;
`OstrichLocomotion.cs` is **overwritten in place** (GUID `fdb87fb2…`) and shrinks to the policy
assembly of §6.4. `SpaceGame.Vehicles.Ostrich.asmdef` → `SpaceGame.Creatures.Ostrich.asmdef`.

*Acceptance:* ostrich Step 0 numbers identical; `OstrichLocomotionTests` and
`OstrichNeckMotionTests` pass; the `Ostrich.prefab` inspector still shows a bound component with its
serialized values intact.

### Step 3 — Crawler onto the proven base

`SpiderWalkerLocomotion.cs` → `DesertCrawlerLocomotion.cs`, **overwritten in place** carrying GUID
`dbb3fe63…`, assembling `YawArcStride` + `RippleGait(swingSlots, minPlanted: 3)` + `LevelDeckBody`
+ `FlatSole`. Its private `GroundRay` / `SampleGround` / `SwingLiftFor` are **deleted**.

**Behaviour changes here by design:** the crawler inherits the corrected foothold clamp, the
pose-before-gait order, `WalkerGround` in place of its own copies, and gravity it never had.

*Acceptance:* measured against the Step 0 baseline, with any regression explained rather than
accepted — worst reach must not rise, ≥3 stance legs must hold, pivot drift must not grow,
distance-covered must stay within 5%. `SpiderWalkerGroundingTests` (in `Assets/Editor/Tests/`)
passes.

### Step 4 — Prove the leg count actually generalises

Two shapes at 2 and 6 legs, both of which the base was extracted from, prove very little.
`WalkerTestRig` (`Assets/Tests/EditMode/WalkerTestRig.cs`) already builds rigs procedurally, so
**synthetic 3-leg and 4-leg rigs cost no art**. Assert: a trot comes out as diagonal pairs; a
3-legged machine still keeps its min-planted promise; a quadruped with unequal front/rear leg
lengths gets per-leg strides; a 2-joint stub leg and a 5-joint insect leg both solve.

**A real quadruped model is follow-up work, not this spec.**

### Step 5 — Drivers

Extract `LeggedDriver` (abstract, Assembly-CSharp) holding the shared `IRiderControllable` +
`IMovementMotor` handling, the rider-frame guard, acceleration, `ForceStop`, and NavMesh path
following. `OstrichDriver` and `DesertCrawlerDriver` become ~20-line shells; both files keep their
existing GUIDs. The crawler's NavMesh pathfinding moves up into the base and the ostrich gains it.

*Acceptance:* both prefabs still drive under a rider and under an `AgentController`;
`OstrichSteeringTests` passes.

---

## 10. Testing

Every policy is a plain class with no scene and no rig, which turns the expensive bugs on record
into one-line assertions.

| Test file | Asserts |
| --- | --- |
| `HipBudgetStrideTests` | the `sqrt(L²−h²)` budget, the degenerate floor, and that 0.72 × MaxReach is **not** used as the radius |
| `YawArcStrideTests` | chord = `2·r·sin(yawRange·0.85)`; a near-zero foot radius yields a near-zero stride (the reason the ostrich cannot use it) |
| `WalkerFootholdTests` | the horizontal clamp, once, for both machines: margin on the horizontal result; clamp against hip-at-touchdown; ground sampled after clamping |
| `TrotGaitTests` | four legs come out as diagonal pairs from `homeLocal`; offsets stay continuous across a walk→trot blend |
| `RippleGaitTests` | even spread at 3, 4, 6, 8 legs; min-planted gate never blocks when unsatisfiable (invariant I1) |
| `AlternatingGaitTests` | offsets `{0, ½}`; duty blends walk→run monotonically |
| `BobbingBodyTests` | bob is at 2× stride frequency and in phase with footfall; no filter on the gait's own motion |
| `LevelDeckBodyTests` | attitude stays level under a tilted support state |
| `ArticulatedSoleTests` | arc apex at 29% of the swing; zero vertical speed at touchdown; sole curve continuous across both handovers |
| `FlatSoleTests` | `WalkerGait.SwingPoint` apex stays at 0.5 (pinned by `WalkerGaitTests`) |
| `WalkerPlanarChainTests` | 2 free links match `SolveTwoLink` exactly; 1 link aims correctly; ≥3 links converge inside the iteration budget and respect limits |
| `WalkerLimbDiscoveryTests` | longest-parallel-run classification on synthetic chains: no-yaw leg, coxa+trochanter insect leg, leaf-bone rejection, branch warning |

Existing tests keep running with **every assertion unchanged** — only the `using` line and the
renamed type names of §3 may differ: `WalkerLegSolverTests`, `WalkerAxleTests`,
`WalkerSurfaceTests`, `WalkerLegPoseTests`, `WalkerGaitTests`, `WalkerPathTests`,
`OstrichLocomotionTests`, `OstrichNeckMotionTests`, `OstrichSteeringTests`,
`SpiderWalkerGroundingTests`. If any expected value has to move, that is a regression to explain,
not a test to update.

**EditMode never calls `Awake`** — that is why `Initialise()` and `Step(dt)` are public and nothing
in the step path reads `Time` directly. Preserve both properties on the base.

---

## 11. Out of scope

Stated so nobody expects them:

- Ball / 3-DOF joints modelled as a single bone — hinge decomposition cannot represent them; warn
  and skip.
- Wall and ceiling walking — several places assume world up.
- Arm and manipulator **controllers**. The limb/leg seam exists so they are possible later; nothing
  is built here.
- More than one body per rig.
- Wheeled, tracked, hovering or hopping locomotion under a common motor interface.
- Moving `IRiderControllable` / `IMovementMotor` out of Assembly-CSharp.
- LOD, Jobs/Burst, or any throughput work — the target is 1–5 machines.
- A real quadruped art asset.

---

## 12. Risks

- **The crawler's behaviour changes by design in Step 3**, and could be worse in a scenario the Step
  0 baseline does not cover. The baseline is the only way we would know; do not skip it.
- **The N-link CCD path ships unproven by any real machine.** Nothing currently shipping uses it,
  so the blast radius is limited to new robots — but it is untested against real art.
- **Files under `Assets/Scripts/Vehicles/Walker/` were observed changing outside the design
  session** (`WalkerLegSolver.cs` went from 245 to 222 lines mid-conversation). Re-read the working
  tree before starting and reconcile against §"Working-tree state" at the top.
- **The crawler was recovered from a failed rewrite once already.** That rewrite died on invariant
  I1. Treat I1 as a review gate, not a guideline.
