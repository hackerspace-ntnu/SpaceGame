# Walking station: grounded feet and terrain-aware legs

**Date:** 2026-08-10
**Component:** `SpiderWalkerLocomotion` and the shared `SpaceGame.Walker` assembly
**Status:** approved design, ready for planning

## Problem

Two reported faults on the six-legged walking station:

1. **Feet float above or sink into the ground.**
2. **Legs pass through terrain** — the thigh and shin clip rock and hillside even when the foot itself looks placed.

They have different causes. The first is three specific defects in the existing code. The second is
not a missing check at all: it is structurally impossible for the current design to avoid, and the
fix has to create the freedom to solve it.

### Why the legs cannot currently avoid terrain

One leg has five degrees of freedom: coxa yaw, three parallel pitch hinges (hip, knee, ankle), and
the sole's roll. `WalkerLegSolver.Solve` asks for five things: the foot's contact position (3) and
the sole laid flat on the ground normal (2).

The leg is therefore **exactly determined**. Once the foot target is fixed there is precisely one
pose, and the knee lands wherever the arithmetic puts it — possibly inside a rock. Adding collision
tests to the current solver would detect the problem and be unable to do anything about it, because
there is nothing left to vary.

### The three grounding defects

| Defect | Location | Effect |
| --- | --- | --- |
| Fabricated surface point: the centre ray's x/z paired with the *highest neighbour's* y | `SpiderWalkerLocomotion.cs:475`, and identically `WalkerGround.cs:84` | The returned point lies on no surface. Feet hover or bury. Also affects the ostrich. |
| Radial reach clamp drags the target off the surface it was just raycast onto | `SpiderWalkerLocomotion.cs:498-501` | Any foothold beyond reach is repositioned into the air or into the ground. |
| Swing arc is a straight 3D lerp plus one sine hump with its peak pinned at `t=0.5` | `WalkerGait.cs:126-132` | `SwingLiftFor` can correctly measure an obstacle at `t=0.8` and the foot still passes through it. |

Two further latent faults found while reading:

- `Physics.RaycastNonAlloc` uses a 24-hit buffer against a machine carrying 33 colliders. `NonAlloc`
  does not sort; a full buffer discards hits arbitrarily, so the real ground hit can be silently
  dropped before the self-filter ever sees it.
- `groundMask` defaults to `Everything`, so feet can plant on the player's capsule.

## Decisions taken

- **Hull:** level deck with **adaptive ride height**. The deck stays flat (it carries crew), but the
  ride height flexes so legs stay inside reach on rough ground.
- **Unusable foothold:** search nearby candidates; if none is acceptable the walker **refuses to
  advance** rather than planting badly.
- **Code quality:** no overly long files, no duplicated subsystems. Constraints in
  [Structure and code quality](#structure-and-code-quality) are part of the acceptance criteria.

## Approach

### Chosen: null-space obstacle avoidance, with physics geometry as the detector

Demote "sole lies flat on the ground normal" from a hard constraint to a bounded preference. That
frees exactly one degree of freedom. The ankle joint then slides along a circular arc of radius
`FootLength` centred on the **fixed** contact point, and the knee and thigh move with it. Sweep that
one parameter, capsule-test thigh and shin against terrain at each sample, and take the pose with
the most clearance and the least sole tilt.

The foot never moves during this sweep. That is the property the whole design rests on, and it is
what stops obstacle avoidance from reintroducing defect 1.

It is cheap because the IK is analytic: a sweep is five closed-form solves, not an iterative solver.

**Honest limit:** authority is bounded by `FootLength` — roughly 2–3 units of knee travel on this
machine. It resolves a shin grazing or embedded in a slope. It does not resolve a boulder sitting
squarely in the leg's plane; that case belongs to the foothold search, which rejects such footholds
before they are ever committed.

### Rejected: per-frame `ComputePenetration` on the existing `COL_*` boxes

Geometry-exact, and the colliders already exist in the prefab. But the only thing it can push is the
foot target, so resolving a *shin* penetration unplants the *foot* — that is defect 1 rebuilt
deliberately. Also order-dependent, jittery, and 18 overlap queries plus penetration solves per
frame. Its exactness is still worth having, so the chosen approach borrows the same collider
geometry as a **detector** and never as a corrector.

### Rejected: baked per-leg clearance volumes

Fast at runtime, but requires baking against terrain this project streams procedurally. Not
applicable.

## Architecture

Physics queries and pure geometry are separated throughout: everything in `SpaceGame.Walker` that
makes a decision is pure and takes its world knowledge through injected delegates, so it is
unit-testable with no physics scene. Only the query units call `Physics`.

```
SpiderWalkerLocomotion (MonoBehaviour, orchestration only)
  ├── WalkerGround          physics: rays → surfaces and ground profiles
  │     └── WalkerSurface   pure: supporting-plane fit from footprint samples
  ├── WalkerLegClearance    physics: capsule overlap along thigh and shin
  ├── WalkerFoothold        pure: candidate generation and scoring
  │     └── (ground and clearance injected as delegates)
  ├── WalkerSwing           pure: swing trajectory with a clearance envelope
  ├── WalkerLegSolver       pure: analytic IK (extended with sole tilt)
  └── WalkerLegPose         pure: forward kinematics to world joint points
```

### 1. `WalkerSurface` (new, pure)

The supporting-plane construction, extracted as pure math so it can be tested without rays.

Given the footprint sample hits, fit a plane through them; the contact height at the centre is that
plane's height **plus the largest positive residual**, so no sample ever ends up below the sole.
This is what a rigid disc actually rests on, and unlike the current code it returns a point that is
genuinely supported.

Carries `Point`, `Normal`, `Flatness` (worst sample-normal agreement) and `IsLedge` (height spread
across the footprint exceeds a threshold).

### 2. `WalkerGround` (extend)

The single place in the project that raycasts for ground. Currently duplicated almost verbatim
inside `SpiderWalkerLocomotion`; that copy is **deleted**, not left alongside.

- `TrySurface(at, footprintRadius, out WalkerSurface)` — five rays, delegated to `WalkerSurface` for
  the fit. Replaces both broken `Sample` implementations, which also repairs the ostrich.
- `ProfileAlong(from, to, samples, buffer)` — ground heights along a swing, for the envelope.
- Rays start from the querying leg's **hip height**, floored to at least the query point plus the
  existing `rayStartAbove`, rather than a fixed 8–12 units above the foot. That keeps the deck and
  hull colliders out of the ray path while still finding ground under a foot that has sunk slightly.
- Hit buffer raised to 32.

### 3. `WalkerLegClearance` (new)

Capsule overlap along thigh and shin, self-filtered by `IsChildOf` — the same idiom the existing
raycasts use, so no new physics layer is needed.

Capsule radii are measured **once from the segments' own `COL_Hip_*` / `COL_Knee_*` / `COL_Ankle_*`
boxes**, so the test matches the geometry that is visible rather than a guessed number: the radius is
half the smaller two extents of the segment's box.

Each segment is tested at two radii — the nominal radius (any non-self overlap means **blocked**) and
an inflated one (overlap there but not at nominal means **tight**). That yields a three-level
clearance band rather than a bare boolean, which is what lets the null-space sweep prefer the
roomiest pose instead of merely a legal one.

The overlap call is an injectable delegate.

### 4. `WalkerLegPose` (new, pure)

Forward kinematics from a solve result to world hip / knee / ankle / sole points. The clearance test
needs these, and extracting them keeps `WalkerLegSolver` from growing. `SoleFromResult` moves here;
the two must agree, which is a test.

### 5. `WalkerLegSolver` (extend, strictly additive)

`Solve(..., float soleTiltDeg)` rotates the desired sole normal within the leg's plane before
solving. **`soleTiltDeg = 0` reproduces today's behaviour exactly**, so the ostrich — which shares
this solver — is untouched by the change.

The sweep over this parameter is owned by the caller, not the solver. Its sample count and angular
span are serialized fields on `SpiderWalkerLocomotion` (default: five samples spanning the ankle's
own travel limit), so the cost is tunable without touching the shared assembly.

### 6. `WalkerFoothold` (new, pure)

Where a leg chooses to place itself down.

**Candidates:** the nominal foothold from `WalkerGait.Foothold`, plus a polar spread around it biased
inboard, since reaching toward the hull is safer than reaching away from it. Nominal wins ties.

**Rejection** — a candidate is discarded if any of these hold:

- no ground beneath it
- slope exceeds `maxFootholdSlope` (serialized on `SpiderWalkerLocomotion`, default 40°)
- the footprint straddles a ledge (`WalkerSurface.IsLedge`)
- the IK returns `Clamped`
- thigh or shin is blocked across the *entire* sole-tilt sweep

**Scoring** of survivors: distance from nominal (dominant term), flatness, reach comfort (best near
0.7 of `MaxReach`), clearance margin.

**Out-of-reach handling** replaces the radial clamp that causes defect 1: pull the candidate toward
the hip *horizontally*, then **re-raycast**, so it lands back on real ground instead of hovering.
Bounded to two iterations.

Returns the winning foothold together with its chosen sole tilt, or reports that none was acceptable.

### 7. `WalkerSwing` (new, pure)

Replaces the fixed-peak sine arc. `WalkerGait.SwingPoint` moves here; the spider is its only caller,
and `WalkerGaitTests` moves with it.

Foot height at `t` is `max(eased endpoint interpolation + hump, groundProfile(t) + clearance)`. The
arc clears what is under it *wherever* that is, not only at the midpoint. Horizontal easing is
unchanged.

Per frame during a swing, a cheap clearance check raises the remaining envelope on a hit, and the
foot plants early when ground arrives sooner than planned.

### 8. `SpiderWalkerLocomotion` (split and shrink)

608 lines today, split into partials following the established `OstrichLocomotion.*` pattern. The
MonoBehaviour keeps orchestration and Unity lifecycle; every decision lives in the pure units above.

| File | Responsibility | Target |
| --- | --- | --- |
| `SpiderWalkerLocomotion.cs` | fields, `Initialise`, `Step` orchestration, public API, diagnostics | ~180 |
| `SpiderWalkerLocomotion.Rig.cs` | leg discovery, gait ordering, `MeasureGait`, capsule radii, `SnapToGround` | ~130 |
| `SpiderWalkerLocomotion.Gait.cs` | step scheduling, foothold commitment, blocked-advance state | ~150 |
| `SpiderWalkerLocomotion.Body.cs` | hull motion, level deck, adaptive ride height, planted-foot verification | ~120 |
| `SpiderWalkerLocomotion.Ik.cs` | per-leg solve with the sole-tilt sweep | ~100 |
| `SpiderWalkerLocomotion.Gizmos.cs` | debug drawing | ~50 |

Three behaviours are added here:

**Adaptive ride height.** Target height scales with the worst reach fraction across planted legs and
the tightest shin clearance, clamped to 0.8–1.25× base and smoothed with hysteresis. The machine
squats and stands like a real walker, and legs stop running out of reach on broken ground.

**Refuse to advance.** When a scheduled step finds no acceptable foothold, the leg holds and forward
speed is clamped to zero — **but yaw is not**, so the walker can turn away from the cliff or wall
instead of deadlocking. It clears the instant a foothold is found. Exposed via `Diagnostics` and a
public `IsBlocked`. At zero speed the nominal foothold collapses back to home beneath the hull,
which is almost always valid, so the resting state at a cliff edge is "stands there", not "frozen".

**Planted-foot verification.** Round-robin one or two planted legs per frame, re-query the surface,
correct height drift beyond an epsilon. This is what makes "all legs always on the ground" survive
terrain chunks streaming in underneath the machine.

`groundMask` is narrowed from `Everything` so feet cannot plant on the player.

## Structure and code quality

Part of the acceptance criteria, not aspirations.

1. **No file over 250 lines.** Matches the house norm (`OstrichLocomotion` partials run 97–255).
   Methods stay under ~40 lines and nesting under three levels.
2. **One owner per concern.** After this change `WalkerGround` is the only code in the project that
   raycasts for ground. The duplicate inside `SpiderWalkerLocomotion` is deleted, not deprecated.
3. **Physics behind delegates.** Everything in `SpaceGame.Walker` that decides something is pure and
   takes world knowledge through injected delegates. No `MonoBehaviour` dependency in that assembly.
4. **Additive shared API only, with one audited exception.** Every addition to `WalkerLegSolver`,
   `WalkerGait` and `WalkerGround` defaults to today's behaviour, so `OstrichLocomotion` needs no
   change beyond inheriting the `Sample` bug fix. The exception is `WalkerGait.SwingPoint` moving to
   `WalkerSwing`: verified safe because `SpiderWalkerLocomotion.cs:394` is its only caller.
5. **No magic numbers in behaviour code.** Thresholds are named constants or serialized fields with
   tooltips, matching the surrounding style.
6. **Allocation-free per frame.** Reused buffers, no LINQ, no per-frame `new`, no per-frame
   `GetComponent`. Everything measurable is measured once in `Initialise`.
7. **Comments explain why, not what** — the existing header-comment style in these files is kept,
   because it is the reason the rig's geometry assumptions are followable at all.

## Testing

EditMode, in the existing `SpaceGame.Tests.EditMode` assembly, which already references
`SpaceGame.Walker`.

| Test | Property |
| --- | --- |
| `WalkerLegSolverTests` | across the whole sole-tilt sweep the contact point stays exactly at target — the invariant the design rests on |
| `WalkerLegPoseTests` | `JointPoints` agrees with `SoleFromResult` |
| `WalkerSurfaceTests` | the supporting-plane point is never below any footprint sample |
| `WalkerFootholdTests` | against a synthetic heightfield: rejects steep, ledge and blocked candidates; prefers nominal; an out-of-reach candidate is re-projected **onto** the surface, never off it |
| `WalkerSwingTests` | the envelope never dips below `profile + clearance` anywhere on [0,1]; endpoints are exact |
| `WalkerLegClearanceTests` | radii derive from the `COL_*` extents; self-hits are filtered |

PlayMode / manual: walk the station across real terrain and assert `Diagnostics.UnreachableLegs == 0`
and `WorstReachFraction < 1` throughout, with no visible shin penetration.

## As built

Implemented 2026-08-10. Seven deviations from the design above, all deliberate:

1. **`WalkerGait.SwingPoint` was NOT moved to `WalkerSwing`.** The design called this an audited
   exception on the grounds that the spider was its only caller. That stopped being true mid-
   implementation: a concurrent change gave the ostrich its own `SwingLiftFor` built on
   `SwingPoint` and `WalkerGround.HighestAlong`. Both are therefore left exactly where they were,
   and the spider simply no longer calls them. Nothing is dead.

2. **`WalkerLegSolver.Result.ContactHeld` was added.** The null-space sweep rests entirely on the
   foot not moving, and the existing `Clamped` flag could not answer that question: it does not
   include the ankle, and an ankle at its stop is exactly how a tilted sole loses its target.
   Measured against the achieved sole rather than inferred from which limits bit. Deliberately kept
   separate from `Clamped`, because an ankle stop is a reason to reject a candidate pose but not a
   reason to step a leg early.

3. **Out-of-reach candidates are rejected, not pulled into range.** The design proposed pulling a
   candidate horizontally toward the hip and re-probing. The implementation is stronger and
   simpler: candidates are *probe positions*, and what comes back is always a point the ground
   probe returned, so nothing is ever moved after being probed. A candidate whose real surface
   point is out of reach is discarded. This makes "every foothold is on the ground" true by
   construction rather than by care.

4. **`SpiderWalkerLocomotion` is seven partials, not six.** `.Api.cs` was split out to bring the
   main file under the line cap; it holds the surface other components see.

5. **`WalkerLegSolver.Types.cs` was split out** for the same reason. The types stay nested in a
   `partial` class, so every call site reads unchanged and the ostrich is untouched.

6. **The whole-machine test lives in `Assets/Editor/Tests/`, not `Assets/Tests/EditMode/`.** Unity
   forbids an asmdef from referencing the predefined `Assembly-CSharp`, where
   `SpiderWalkerLocomotion` lives. `Assembly-CSharp-Editor` can see it, and the test runner
   discovers tests there.

7. **`WalkerRig.cs` (303 lines) is still over the 250-line cap.** It was not touched by this work
   and splitting it would be unrelated churn against a file another session is working near. Every
   file this change wrote or edited is under the cap.

### Verification

175 EditMode tests pass, from a 127-test baseline. The additions:

- 42 unit tests across the six new pure units.
- 6 whole-machine tests driving the real `rig_walker.prefab` across level ground, a ramp, a
  plateau and a boulder: no leg inside terrain on any frame, every planted foot on the surface
  beneath it, no leg beyond its reach, at least three feet down throughout, and the machine
  actually crosses the ground rather than deadlocking on the refuse-to-advance rule.

Two results are worth recording because they were earned rather than assumed:

- The swing tests were first run against the OLD arc to prove they catch the bug. They failed with
  "foot at t=0.005 is below ground 10.16" and "clipped the lip at t=0.285" — the reported fault,
  reproduced — and pass against the envelope.
- A leg reporting itself unreachable while pivoting turned out to be transient and self-clearing,
  which is the early-step mechanism working as designed. The test asserts recovery rather than
  absence; asserting absence was wrong.

## Out of scope

- The hull and deck themselves clipping terrain.
- Leg-versus-leg self-collision.
- Any change to `SpiderWalkerDriver`'s pathfinding beyond honouring `IsBlocked`.
