# Three new armatures on the legged locomotion core — agent handoff

**Date:** 2026-08-11
**Status:** ready to dispatch
**Base:** the legged locomotion core landed 2026-08-10 —
`docs/superpowers/specs/2026-08-10-legged-robotics-architecture-design.md` is the architecture it
implements. Read that spec's §5 and §6 before touching anything; this document assumes it.
**Suite at handoff:** 232/232 EditMode passing.

Three machines, one agent each:

| Agent | Machine | Shape |
| --- | --- | --- |
| **A** | Humanoid robot | 2 legs, 2 arms, torso, head |
| **B** | Crab walker | 4–8 legs (parameterised), 2 front claw-arms, sideways travel |
| **C** | Horse robot | Quadruped, unequal front/rear legs, neck + spine, rideable |

---

## 0. Read this part whatever you are building

### 0.1 What already exists

`Assets/Scripts/Locomotion/` (`SpaceGame.Locomotion`) is the shared core. **A robot is one small
file.** `OstrichLocomotion.cs` is 97 lines and `DesertCrawlerLocomotion.cs` is 49 — that is the
target size for yours. If your locomotion component is growing past ~150 lines, something you are
writing belongs in the core or in a policy instead.

```csharp
public class MyRobotLocomotion : LeggedLocomotion
{
    protected override IStrideModel CreateStride() => ...;   // how far a leg can step
    protected override IGaitPattern CreateGait()   => ...;   // when each leg swings
    protected override IBodyMotion  CreateBody()   => ...;   // where the body goes
    protected override IFootStyle   CreateFeet()   => ...;   // what the foot does
}
```

Shipped policies — **use these before writing new ones**:

| Interface | Implementations |
| --- | --- |
| `IStrideModel` | `YawArcStride(yawRange, fraction)` — splayed legs, stride from the coxa's arc<br>`HipBudgetStride(hipHeightFraction, strideFraction)` — legs under the body, stride from what the hip pitch can reach |
| `IGaitPattern` | `RippleGait(swingSlots, minPlanted)` · `AlternatingGait(walkDuty, runDuty)` · `TrotGait(walkDuty, runDuty)` |
| `IBodyMotion` | `LevelDeckBody(slopeFollow, maxTilt, tiltSmooth)` · `BobbingBody(bob, sway, runPitch, turnRoll, attitudeSmooth, slopeFollow, maxTilt)` |
| `IFootStyle` | `FlatSole()` · `ArticulatedSole(toeOff, swingToe)` |

The base already owns, for every machine: rig discovery and per-leg measurement, the gait clock,
foothold resolution, ground probing, the support-plane survey and slope following, gravity and
falling, IK application, `SetTwist` clamping, `MaxSpeed`/`MaxYawRate` derivation, diagnostics and
gizmos. **Do not reimplement any of it.** The whole point of this architecture is that the two
machines that came before had drifted copies of exactly those things.

Drivers derive from `LeggedDriver` (`Assets/Scripts/agents/AI/motor/LeggedDriver.cs`), which already
does the rider channel, the AI channel, the rider-frame guard, acceleration, NavMesh path following
and `ForceStop`. `DesertCrawlerDriver` is 14 lines. Yours should be too.

### 0.2 Frame order — do not change it

```
Step(dt)
  1  AdvancePath      commanded twist → pathPos, currentYaw; gait clock advances by DISTANCE
  2  Survey           feet → SupportState (support height, plane, carrying/grounded counts)
  3  Body.Pose        IBodyMotion writes target height, attitude, display offset
  4  ReachCorrection  core; drops the body until every grounded foot is spannable
  5  Fall             gravity; vetoes 3's height when nothing is carrying
  6  UpdateGait       IGaitPattern picks who swings; shared foothold resolve; IFootStyle arc
  7  SolveLegs        IFootStyle tip direction → WalkerLimbSolver → Apply
  8  TrackVelocity
```

The body is posed **before** the gait picks footholds. The gait reads hips and rest footholds off
the body transform; posing afterwards leaves both a frame stale and it reads as the feet trailing
the body.

### 0.3 Invariants — these are review gates, not guidelines

- **I1 — No policy may return a state that stops the machine.** The clock advances by *distance
  travelled*, so speed 0 means the gait never turns, no phase slice ever opens, and nothing can
  un-stick it. Policies may step a leg early, refuse a foothold, or ask for a shorter stride. They
  may **not** gate motion on their own output. This has bitten twice: once as an `IsBlocked` latch
  that cost a session, once as an unbound `RippleGait` reporting duty 0 → `MaxSpeed` 0 → `SetTwist`
  clamping everything to 0. **If you add a gait pattern, `Duty()` must never return 0.**
- **I2 — The 2-free-link IK path is the existing analytic solve.** Any change to `SolveTwoLink` is a
  regression, not a refactor.
- **I3 — Nothing in the per-frame path allocates.** Solver arrays are allocated once per limb at
  `Initialise`. If you need a buffer, size it in `Initialise`.
- **I4 — `LeggedLocomotion` is the single owner of the body's transform.** Drivers hand over a twist
  and read back achieved motion. Rigidbodies stay kinematic with gravity off.
- **I5 — A planar limb (no yaw joint) cannot hold a planted foot through a turn.** Model a yaw joint
  at the base of every leg.
- **New — a gait pattern must be `Bind`ed before speeds are derived.** The base does this; do not
  reorder `Initialise`.
- **New — "grounded" and "carrying" are different questions.** A leg past its reach stops carrying
  but is still grounded. The support plane needs the grounded set; gravity needs the carrying set.
  Do not merge them.

### 0.4 Unity discipline that will silently destroy work

- **A `.cs` bound by a prefab must be overwritten in place, never deleted and recreated.** Unity
  binds components by the script's `.meta` GUID. Recreating the file mints a new GUID and strips the
  component off every prefab using it. When you rename a script, `mv` the `.cs` **and** its `.meta`
  together.
- **Serialized values survive a class rename and a move to a base class only if the FIELD NAME
  survives.** Renaming `hipRange` loses whatever the prefab had in it.
- **Rig conventions the discovery depends on** (`Assets/Scripts/Locomotion/Rig/WalkerRig.cs`):
  - The classic chain is looked up **by name across the whole armature**:
    `Coxa_<id>` → `Hip_<id>` → `Knee_<id>` → `Ankle_<id>` → `Foot_<id>`. Use these names and
    discovery is guaranteed. Anything else falls back to walking the bone hierarchy by pin, which
    is more fragile.
  - Every joint needs a `*Pin*` mesh child — a cylinder whose **longest axis is the hinge axis**.
    That is how the axle is measured; there is no fallback worth relying on.
  - Collision boxes must be named `COL_*`. Export with `add_leaf_bones=False` — `_end` bones are
    skipped but are a hazard.
  - **The first pitch joint must sit ON the yaw axis.** The IK's two stages are independent only
    because yawing the coxa does not move the hip. It warns and degrades if you break this.
  - Limbs are classified by measurement: the **longest run of mutually-parallel axles** is the pitch
    chain, joints before it are base DOFs, joints after it are the tip roll.
  - A limb may have **any number** of pitch joints. Free links = pitch segments − 1. Two free links
    take the analytic solve; one aims; three or more take an arc seed + CCD polish.

### 0.5 Verification — how to actually know it works

Unity is driven through the `unity-mcp` bridge. Quirks that will waste your time otherwise:

- `Unity_RunCommand` **mangles nested classes** — write ONE class named `CommandScript`. To run the
  test API, have `CommandScript` implement `ICallbacks` itself.
- It wraps your code in a namespace, so `CompilationPipeline` must be written
  `UnityEditor.Compilation.CompilationPipeline`.
- **The test runner can silently use a STALE test assembly.** Before trusting a green run, confirm
  your new test types are in the loaded assembly by reflecting over
  `SpaceGame.Tests.EditMode`. If they are absent, the run means nothing. Recompilation sometimes
  needs the editor to regain focus.

Run the suite by calling `TestRunnerApi.Execute` with an `EditMode` filter and writing results to a
file. **232/232 is the number to preserve.** Every one of those assertions must still pass when you
are done; if one has to move, that is a regression to explain, not a test to update.

Measure your machine with the same harness the others were held to — instantiate the prefab, call
`Initialise()` then `SnapToGround()`, then `SetTwist` + `Step(1/60)` in a loop. `Initialise` and
`Step` are public and dt-driven precisely so this works with no player loop.

Baselines the shipping machines hold, for calibration:

| | crawler | ostrich |
| --- | --- | --- |
| planted-foot slip | 0.0000 m | 0.00000 m |
| worst reach fraction | 0.834 | 1.07 @ half speed |
| distance vs commanded | exact | exact |
| false falls | 0 | 0 |

### 0.6 Modelling

Blender models live in `models/` (see `Assets/Models/_Source~/LIBRARY.md`). There is already a reusable
`Assets/Models/_Source~/components/mechanical/walker_leg.blend` + `.py` — **instance it rather than modelling legs
from scratch.** Drive Blender headlessly; the Blender MCP is not wired up. Use the `blender-model`
skill. Read the `project_blender_workflow` and `project_desert_crawler` memories for the four Unity
rig gotchas that will otherwise cost you a day (splayed rest pose, root on the hip plane, `COL_`
boxes as direct joint children, no leaf bones).

---

## 1. Two shared foundations — these must land FIRST

Both are changes to `SpaceGame.Locomotion`, which all three machines share. **They are not
parallelisable with each other or with the robot work**, because all three agents would otherwise
edit `LeggedLocomotion` at once and conflict.

> **Recommended sequencing:** land F1, then F2, then dispatch A, B and C in parallel. Each robot's
> own work after that touches only its own files, its own prefab, and its own tests.

### F1 — Limbs have roles: not every limb is a leg

**Owner: Agent A (humanoid). Agent B depends on it. Agent C does not.**

**The problem.** `WalkerRig.RootPrefixes` is `{ "Limb_", "Coxa_", "Hip_" }`, and
`LeggedLocomotion.Initialise` turns **every** limb `WalkerRig.Build` returns into a `LegState`. So
today an arm named `Arm_L` is not discovered at all, and an arm named `Limb_L` is discovered and
then **walked on**. Two of the three machines here have arms.

**What to build.**

1. `WalkerRig.Limb` gains a role — `Leg` or `Arm` — taken from the root bone's prefix. Add `Arm_` to
   the recognised roots. Keep `Limb_`/`Coxa_`/`Hip_` meaning `Leg` so both shipping rigs are
   unaffected.
2. `LeggedLocomotion.Initialise` builds `LegState` only for `Leg` limbs. Arms are handed to
   something else.
3. A minimal arm seam. The architecture spec §4.4 already names the shape:

   ```
   WalkerLimb = rig chain + solver + a world target + a tip direction
   LegState   = WalkerLimb + gait slot + foothold + load share + swing bookkeeping
   ```

   Build the smaller half: a `WalkerLimbPose`-driven controller that takes a **world target** and a
   **tip direction** and solves the limb through the existing `WalkerLimbSolver`. It needs no gait,
   no foothold, no load. Keep it a plain class in `SpaceGame.Locomotion` so it is testable without a
   scene, and expose it from `LeggedLocomotion` as a list the subclass can drive.

**Do not** build a full IK/manipulation system. The deliverable is: an arm can be posed at a world
point without being walked on.

**Acceptance.** A synthetic rig with two `Coxa_*` legs and two `Arm_*` arms reports `LegCount == 2`;
the arms solve to a commanded target within 1e-3; the ostrich and crawler are byte-identical on
their baselines; 232/232 still passes.

### F2 — Travel that is not along the machine's nose

**Owner: Agent B (crab). Agents A and C do not depend on it but must not break it.**

**The problem.** `SetTwist(speed, yawRate)` has no lateral channel, and the travel direction is
derived from `currentYaw` in six places. A crab travels sideways with its body square to the
direction of motion; it cannot be expressed today.

**Every call site that assumes travel is along the nose** — change all of them together or the gait
and the footholds disagree:

| File | What assumes it |
| --- | --- |
| `LeggedLocomotion.cs` | `SetTwist` clamping · `Pace` · `RunBlend` |
| `LeggedLocomotion.Body.cs` | `AdvancePath` — `forward * (CommandedSpeed * dt)` · `diagnostics.AchievedSpeed` |
| `LeggedLocomotion.Gait.cs` | `UpdateGait`'s `linear` vector · `ResolveFoothold`'s `HipAtTouchdown` |
| `Core/WalkerFoothold.cs` | `HipAtTouchdown(hipNow, yaw, speed, swing)` takes a yaw + a scalar |

**Recommended shape.** Make the commanded twist a **planar velocity in body space** rather than a
scalar: `SetTwist(Vector2 velocityLocal, float yawRate)`, keeping a
`SetTwist(float forward, float yawRate)` overload that forwards to it so nothing else changes.
`WalkerFoothold.HipAtTouchdown` should take a world velocity vector instead of `(yaw, speed)`.
`Pace` becomes `velocity.magnitude + |yawRate| · maxFootRadius`.

**I1 applies with force here.** A machine whose lateral speed is clamped to zero by a bug stops
dead and never restarts.

**Acceptance.** Ostrich and crawler baselines unchanged (they only ever command forward). A
synthetic machine commanded purely sideways travels sideways at the commanded speed with planted
slip < 0.01 m and worst reach ≤ 1. 232/232 still passes.

---

## 2. Agent A — Humanoid robot

**Files:** `Assets/Scripts/Creatures/Humanoid/` (new asmdef `SpaceGame.Creatures.Humanoid`,
referencing `SpaceGame.Locomotion`), plus `HumanoidDriver.cs` in Assembly-CSharp.

**Also owns shared foundation F1.**

### The shape
2 legs, 2 arms, torso, head. Upright, plantigrade, forward-bending knee.

### Policy assembly — start here, change only what you must
- `IStrideModel` → `HipBudgetStride`. Legs sit under the body exactly as the ostrich's do, so the
  yaw arc is near zero and a `YawArcStride` would give it a stride of centimetres. `hipHeightFraction`
  will want to be **higher** than the bird's 0.86 — a human stands straighter — but that trades
  directly against stride length. Derive it, do not guess: measure the reach the stride needs.
- `IGaitPattern` → `AlternatingGait`. A biped is a biped. Duty below 0.5 walks, above 0.5 runs.
- `IBodyMotion` → `BobbingBody` with **much less** bob, sway and run-pitch than the ostrich. A human
  torso stays near vertical; the ostrich pitches 16° toward horizontal at speed and that is exactly
  what you do not want.
- `IFootStyle` → `ArticulatedSole`. Heel-strike to toe-off is the whole read of a human walk.

### What is genuinely new
1. **The knee bends forward.** The ostrich has a *reverse* knee and the crawler's is reversed too.
   `BendSign` is measured from the rest pose and should handle it — **verify it does** rather than
   assuming. A knee that pops to the mirror solution mid-stride is the failure to watch for; there
   is already a test shape for it (`BendSign_KeepsTheKneeBendingTheWayTheMachineWasBuilt`).
2. **Arm counter-swing.** The signature of a humanoid gait: each arm swings opposite its diagonal
   leg. This is the first consumer of F1's arm seam and should be driven from the gait clock
   (`LastFrame.Phase`) rather than from a separate timer, so it cannot drift out of phase with the
   footfalls. Nothing at stride frequency may go through a filter.
3. **A torso that counter-rotates.** Small, opposite the pelvis. Model it on `OstrichSpineMotion`
   (`Assets/Scripts/Creatures/Ostrich/OstrichSpineMotion.cs`), which runs after the locomotion at
   order 100 and spends the neck against the body's own motion.

### Acceptance
- `LegCount == 2`, arms discovered as arms and not walked on.
- Planted-foot slip < 0.01 m at 0, 25%, 50% and 95% of `MaxSpeed`, and through a turn.
- A walk keeps a foot down most of the cycle; a run has a flight phase; standing still takes no
  steps and drifts < 0.05 m.
- Worst reach fraction ≤ 1.15 at a walk.
- Spawned 20 m up it falls, accelerating, and lands with its feet re-planted.
- Arms stay in phase with the gait across a speed change.
- 232/232 plus your own tests.

---

## 3. Agent B — Crab walker

**Files:** `Assets/Scripts/Creatures/Crab/` (new asmdef `SpaceGame.Creatures.Crab`), plus
`CrabDriver.cs` in Assembly-CSharp.

**Also owns shared foundation F2. Depends on Agent A's F1 for the claws.**

### The shape
4 to 8 legs — **one component and one rig convention that covers the whole range**, not five
variants. Two front claw-arms. Wide, low, splayed. Travels sideways.

### Policy assembly
- `IStrideModel` → `YawArcStride`. The legs stick out sideways on long coxae exactly as the
  crawler's do; this is what that model is for.
- `IGaitPattern` → **new.** A crab does not ripple front-to-back, it waves **side to side** along
  its direction of travel. `RippleGait` orders legs by `HomeLocal.z` (rear to front) within each
  side; a crab wants the sequence ordered along its travel axis instead. Derive the ordering from
  `HomeLocal` in `Bind` — never from leg indices — exactly as `GaitLayout.RippleOrder` does.
  Keep the min-planted gate: a crab is statically stable and should stay so.
  **`Duty()` must never return 0** (invariant I1).
- `IBodyMotion` → `LevelDeckBody` with a **high** `slopeFollow` and a generous `maxTilt`. A crab has
  no crew to tip off and hugs the terrain; this is the opposite tuning from the crawler's deck.
- `IFootStyle` → `FlatSole` to start. Pointed dactyls that pierce rather than pad may want their own
  style later; do not start there.

### What is genuinely new
1. **Sideways travel** — F2, which you own. The crab is the reason it exists.
2. **Leg count 4–8 from one rig.** `RippleGait(swingSlots, minPlanted)` already generalises and
   `WalkerRig` already discovers any number of limbs; the work is in the *model* and in choosing
   `swingSlots`/`minPlanted` as a function of leg count so support is guaranteed at every count.
   Note `LeggedLocomotion` derives cycle distance from the **shortest** leg's stride, so mismatched
   leg lengths are already handled.
3. **Claws.** Two front limbs on F1's arm seam. Idle sway plus a defensive raise is plenty; this is
   not a manipulation system.

### Acceptance
- Instantiates and walks at **4, 6 and 8 legs** from the same component.
- Travels sideways at the commanded speed, planted slip < 0.01 m.
- Never drops below its min-planted count at any leg count, and the gate degrades to "never block"
  when it cannot be satisfied (I1 — there is already a test shape for this).
- Worst reach ≤ 1 on flat ground.
- Holds 4+ legs down across a 25° cross-slope.
- 232/232 plus your own tests.

---

## 4. Agent C — Horse robot

**Files:** `Assets/Scripts/Creatures/Horse/` (new asmdef `SpaceGame.Creatures.Horse`), plus
`HorseDriver.cs` in Assembly-CSharp.

**Depends on neither foundation.** You can start immediately and in parallel — but do not touch
`LeggedLocomotion` while F1/F2 are in flight.

### The shape
Quadruped. Longer, straighter forelegs; shorter, more angled hind legs with a hock. Neck, head,
tail. Rideable.

### Policy assembly
- `IStrideModel` → `HipBudgetStride`.
- `IGaitPattern` → `TrotGait` exists and already derives diagonal pairs from `HomeLocal` and blends
  continuously from a ripple with `runBlend`. Start there.
- `IBodyMotion` → `BobbingBody`.
- `IFootStyle` → `ArticulatedSole`.

### What is genuinely new
1. **Unequal front and rear legs.** This is the case the per-leg measurement was built for and
   nothing has exercised it on a real rig yet — `LegMeasurement` carries a stride per leg and
   `TryGetMeasurement(i, out m)` exposes it. **Model the forelegs and hind legs at genuinely
   different lengths** and confirm each pair gets its own stride. If they come out equal, the
   measurement has regressed to an average and that is a core bug worth reporting.
2. **Gait transitions beyond trot.** Walk → trot is a continuous blend today. Canter and gallop are
   **asymmetric** — they have a leading foreleg and a suspension phase — which no current pattern
   expresses. Offsets must stay a continuous function of `runBlend`; swapping a table mid-stride
   teleports a foot that is in the air. This is the hardest single piece of the three briefs.
3. **Neck and spine.** `OstrichNeckMotion`, `OstrichNeckGaze`, `OstrichNeckSpring` and
   `OstrichSpineMotion` are a worked example of layering gaze + ride bounce over a body that is
   already moving. Read the `project_ostrich_neck_motion` memory first — in particular: **drive the
   bounce from the body transform, not from `MeasuredVelocity`**, which is smoothed.
4. **Rideable.** `MountStation`, `MountController`/`MountModule`/`SteerModule` already exist — see
   the `project_mount_system` memory. `LeggedDriver` already implements `IRiderControllable`, so
   this should be prefab wiring, not code.

### Acceptance
- `LegCount == 4`; front and rear legs report **different** `StrideLength` from
  `TryGetMeasurement`.
- Diagonal pairs come out as diagonals from `HomeLocal`, not from indices.
- Planted slip < 0.01 m at every gait and through a turn.
- Gait offsets stay continuous across the whole speed range — no discontinuity greater than a few
  percent of a cycle between adjacent speeds (there is already a test shape for this).
- Carries a rider under `SteerModule` and drives under an `AgentController`.
- 232/232 plus your own tests.

---

## 5. Rules for all three agents

1. **Do not modify `SpaceGame.Locomotion` outside your assigned foundation.** If you believe the
   core needs a change, say so and stop — do not make it. Three agents editing the shared core in
   parallel is how this architecture gets re-forked into the mess it replaced.
2. **Reuse a policy before writing one.** If you write a new `IStrideModel`, justify why neither
   shipped one fits.
3. **Every number is derived, not authored, wherever the rig can supply it.** Stride, cadence, step
   height and top speed all come from measurement, so rescaling a model re-tunes the machine with no
   numbers to edit. Do not hard-code what the rig knows.
4. **Report measured numbers, not impressions.** "It walks nicely" is not a result; "planted slip
   0.0000 m, worst reach 0.91, distance within 1% of commanded" is.
5. **If a pre-existing test fails, stop and report it.** Do not update the assertion. Those tests
   encode faults that each cost a session to find.
6. **Say what you did not finish.** A partial machine with an honest list of gaps is worth more than
   a complete-sounding one that hides them.
