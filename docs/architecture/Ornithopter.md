# Dune Ornithopter — a flapping-wing flyer carried in the inventory

A 10 m flapping-wing machine the player carries folded, uses in mid-air, and flies lying prone
underneath. Wings beat, spread open at launch, twist through the stroke, and a tail fan steers.

**Controls** — W/S pitch, A/D roll, **Space** flap, **Left Ctrl** tuck and dive, **Escape** bail out.

## The pieces

| Where | What it does |
|---|---|
| `OrnithopterFlightModel` | The flight, as pure functions. No MonoBehaviour, no Transform, no clock. |
| `OrnithopterFlightConfig` | Every tuning number, so the physics file has no magic constants. |
| `OrnithopterFlightState` | What carries between steps. A struct a test can build and step. |
| `OrnithopterWingRig` | Binds the 30 bones by name, caches the rest pose. Nothing else. |
| `OrnithopterWingAnimator` | Poses those bones from the flight state. The articulation. |
| `IOrnithopterFlightState` | The seam between the two assemblies (see below). |
| `OrnithopterFlightMotor` | *(Assembly-CSharp)* Owns the Rigidbody, translates rider input. |
| `WingPackItem` | *(Assembly-CSharp)* The inventory item. Spawns the craft and mounts the player. |

Built by **Tools ▸ Vehicles ▸ Build Dune Ornithopter Prefab** and **▸ Build Wing Pack Item**, from
`Assets/Game/Art/Models/Vehicles/Ornithopter/dune_ornithopter.fbx`. Both are re-runnable and measure
everything off the meshes, so re-exporting a re-proportioned model and re-running still lands right.

### Why the motor is in a different assembly from the physics

`IRiderControllable`, `MountModule` and `SteerModule` all live in Assembly-CSharp, and an assembly
definition cannot reference a predefined assembly. So the physics and the articulation live in
`SpaceGame.Vehicles.Ornithopter`, the motor lives in Assembly-CSharp, and `IOrnithopterFlightState`
is the seam. That is not a workaround — it is what lets the flight model be tested without a scene
and the animator be driven by a stub.

## The flight model

A point-mass model. **Two angles matter and confusing them is the classic bug:**

- **Flight path angle** (gamma) — the direction the craft is *moving*.
- **Body pitch** (theta) — the direction it is *pointing*. What the stick sets.

The difference is the angle of attack, and angle of attack is what makes lift. Pull the nose up and
you do not go up — you increase the angle of attack, which makes more lift, which curves the flight
path upward a moment later. Pull too hard and the wing stalls: lift collapses, the nose falls, and
the machine recovers once it has speed again.

Gliding, diving, climbing and stalling all fall out of those equations. None of them is scripted.

**There is no throttle.** Airspeed is bought with altitude or with flapping, and flapping costs
stamina that only recovers in a glide. Sustained level flight is a rhythm, not a held button.

At the shipped tuning: stall around 11 m/s, glide roughly 9:1. Both are *derived* — after changing
mass, wing area or the lift curve, check with `OrnithopterFlightModel.StallSpeed` rather than
assuming.

## The articulation

Every rotation is a delta on the rest pose the rig cached, on the axis the model's build record
specifies:

| Motion | Bone | Axis | Per-side sign? |
|---|---|---|---|
| Wing beat | `Bone_Shoulder_L/R` | local **X** | **No** |
| Wing sweep | `Bone_Arm_L/R` | local **Z** | **Yes** |
| Digit splay | `Bone_Digit_*` | local **Z** | **Yes** |
| Digit twist | `Bone_Digit_*` | local **Y** | **No** |
| Gear spin | `Bone_Gear_L/R` | local **Y** | either |
| Tail fan splay | `Bone_TailDigit_1..5` | local **Z** | n/a |
| Boom pitch | `Bone_Boom_1/2` | local **X** | n/a |

**The signs are not uniform, and this is the part that bites.** The two wing bones point outboard in
*opposite* directions, so their local X and Y are already mirrored and the same angle on both sides
comes out symmetric. Local Z points up on both sides and is *not* mirrored, so anything about Z
needs an explicit per-side sign — get it wrong and one wing opens while the other closes. This
caused two real bugs during the model build; `OrnithopterWingAnimatorTests` now asserts both the
symmetry of the beat and the symmetry of the splay in world space.

What each motion is for:

- **Flap** — shoulder X on a sine of the beat phase, amplitude from effort. The gears and cranks
  turn with it, so the visible mechanism agrees with the wings.
- **Spread** — opens digit splay and sweeps the arms forward over 0.6 s at launch. This is the
  "wings snap open" moment.
- **Digit twist** — positive on the downstroke so the membrane bites, slightly negative on the
  upstroke so it feathers. **This is what makes a flap propel rather than just wave**; set it to
  zero and the wings flap without driving the craft anywhere.
- **Tail** — boom pitches with the stick; the fan splays open in turns and under braking,
  asymmetrically, which is what actually yaws the machine.
- **Bank** — differential twist between the wings.

**Nothing that moves at beat frequency is filtered.** The flap, the gear spin and the twist are
driven from the phase directly. Only bank, pitch trim and the tail fan are damped. Smoothing
anything at stroke rate detunes it from the thing it is showing — the lesson from the ostrich gait.

## Two decisions that look odd and are not

- **`useGravity` is off on the Rigidbody.** The flight model integrates weight itself as part of
  the same equation that produces lift and drag. Unity's gravity on top is a second, uncoordinated
  pull, and the stall reads as a brick rather than as a wing running out of air.
- **The prefab origin is on the cradle**, not the model origin. The motor writes rotation to the
  root, so the root is what the craft pivots about. On the cradle, pitching feels like the pilot
  dropping a shoulder; on the model origin it swings the rider through an arc.

## The wing pack

Air-only by design. `CanUse` allows it when the player is already falling, **or** when a ray cast
forward and down finds no ground within 6 m — that second clause is what makes a cliff edge work,
since the ray straight down hits the ledge you are standing on.

Unlimited uses, and teardown funnels through one method reached four ways: landing, bailing out,
switching hotbar slot mid-flight, and the item being destroyed. Bailing out at altitude drops you —
intentionally. The pack is usable while falling, so bailing and redeploying is a move.

## Ending a flight: landing and crashing

Two things end a flight and they share one path, `OrnithopterFlightMotor.ReportTouchdown`:

- **The ground probe** — a short ray under the craft. This is what a landing is: ground appears
  beneath you and the flight is over before the hull ever touches it.
- **`OnCollisionEnter`** — you flew into something. A cliff face is not under the craft, so the
  probe never sees it; without this the machine grinds along the rock with its velocity rewritten
  from the flight model every step and the flight never ends.

Both raise `Landed` with an `OrnithopterTouchdown`, and the wing pack does three things with it:
hurts the pilot, stands them somewhere solid, despawns the craft.

**Damage is priced on closing speed, never airspeed.** Closing speed is how fast the craft was
moving *towards* the surface — `dot(-velocity, surfaceNormal)`. This is the whole reason landing and
crashing need no special-casing against each other:

| | closing speed | damage |
|---|---|---|
| Trimmed glide onto sand, 20 m/s at −6° | 2.1 m/s | 0 |
| Botched flare, 30 m/s at −30° | 15.0 m/s | 17 |
| Level into a cliff at 20 m/s | 20.0 m/s | 35 |
| Held dive into the ground, 42 m/s at −60° | 36.4 m/s | 100 |
| Wingtip dragged down a rock wall | 0 m/s | 0 |

The curve is energy-proportional (`v²`) between `SafeClosingSpeed` (8 m/s, free) and
`LethalClosingSpeed` (32 m/s, the player's full 100 health) — a crash has to shed ½mv², so doubling
the speed quadruples the cost. All of it is in `OrnithopterCrashConfig` on the motor.

**Where the pilot ends up.** The craft's `DismountPoint` is useless after a crash: the wreck is
embedded in rock at whatever attitude it hit at, and the marker swung with it. So the motor probes
straight down from the impact (stepped clear of the surface first, and ignoring its own hull and the
pilot parented into the cradle) and hands the resolved spot to `MountModule.DismountAt`. Nothing
within `GroundSearchDistance` — a cliff face 200 m up — releases the pilot at the crash site
instead, and they fall; `PlayerMovement` already prices that.

Two consequences of that ordering are load-bearing: the impact speed is read from the *flight state*
before `EndFlight` zeroes the body (by the time a collision callback runs, the solver's contact
response has already eaten the Rigidbody's velocity, which would under-report exactly the hardest
hits), and the damage lands *after* the dismount, so a fatal crash leaves the body at the wreck
rather than mid-air.

## Tests

| Suite | Covers |
|---|---|
| `OrnithopterFlightModelTests` (22) | Glide descends, dive gains speed, climbing costs speed, flapping climbs, stall and recovery, banked turns, stamina, NaN-freedom over 2 minutes of varied input. |
| `OrnithopterCrashTests` (4) | A glide onto sand is free, the same airspeed into a cliff is not, a held dive is fatal, a wingtip scrape is not an impact. |
| `OrnithopterWingAnimatorTests` (10) | The wings actually move: beat, gear spin, spread, per-side symmetry, tail boom, tail fan asymmetry, roll differential. |
| `OrnithopterRigWiringTests` (10) | Prefab wiring: bones resolve, panels skinned, seat lays the rider prone, camera boom clears the span, pack points at the craft. |

Run headlessly with `HeadlessTestRunner.RunEditMode("<fixture>")`, which writes to
`Temp/headless_tests.txt` — the Test Runner API is asynchronous and an external caller cannot see
its callback.
