# Mounted riding feel — design

Date: 2026-08-14
Branch: `Feat/robotics-and-minigame`

Riding the ostrich has four problems. The interaction prompt stays on screen for the whole
ride, the camera is 20× less responsive than it is on foot, it refuses to hold a side view,
and the rider stands bolt upright in mid-air above the saddle.

## 1. The interaction prompt (and crosshair) stay lit while mounted

### Cause

`MountModule.DisableRiderComponentsForMount` sets `mountedInteractor.enabled = false`
(`MountModule.Mounting.cs:227`). A disabled `Behaviour` stops receiving `Update`, so
`Interactor.HoveredInteractable` and `Interactor.IsHoveringInteractable` **freeze** at whatever
the player was looking at when they pressed E — which is, necessarily, the mount.

Both HUD readers keep polling those frozen values:

- `InteractionPromptUI.cs:76` reads `HoveredInteractable` → panel stays up reading "Ostrich / Press E".
- `CrosshairUI.cs:25` reads `IsHoveringInteractable` → crosshair stays lit.

`FindFirstObjectByType<Interactor>()` keeps returning the disabled component, because its
GameObject is still active — so the UI never even notices it went away.

### Fix

Two changes, and both are wanted:

1. **`Interactor.OnDisable`** clears `HoveredInteractable` and `IsHoveringInteractable`. This is
   the root fix: hover state can no longer outlive the component that owns it.
2. **`InteractionPromptUI.Update`** hides when `!playerInteractor.isActiveAndEnabled`. This is
   the guard: any future path that stops the Interactor updating without disabling it cannot
   resurrect the panel.

`CrosshairUI` needs no change — change 1 makes its flag read `false`.

## 2. Mounted look is unusable

### Cause

Three separate faults compound.

**Sensitivity is 20× too low.** `PlayerLook.sensitivity` is `20` on `PlayerCharacter.prefab`;
`MountModule.lookSensitivity` is `1`. Both multiply mouse delta by `Time.deltaTime`, so the
mounted camera orbits at 1/20th of on-foot speed.

**The side view will not hold.** `cameraAutoAlignDelay` is `0.5 s` and `cameraAutoAlignSpeed`
is `90 °/s`, so half a second after you stop moving the mouse the camera swings back behind
the mount within a second.

**Vertical mouse does nothing.** `mountedPitch` is only ever written to
`mountedFirstPersonCameraRoot` (`MountModule.Camera.cs:41-42`). The third-person orbit in
`LateUpdate` never reads it. Riding in third person — the default — you can only look left
and right.

### Fix

New defaults on `MountModule`, all still per-mount serialized fields:

| field | now | after |
|---|---|---|
| `lookSensitivity` | 1 | 20 |
| `cameraAutoAlignDelay` | 0.5 s | 3.0 s |
| `cameraAutoAlignSpeed` | 90 °/s | 8 °/s |

At 8 °/s a 90° side view holds for three seconds and then takes about eleven more to drift
home — "very slowly get back to normal", and always available without touching anything.

Three correctness changes that only start to matter once large offsets can persist:

- **Honour the player's settings.** Multiply by `GameSettings.MouseSensitivity` and respect
  `GameSettings.InvertLookY`, exactly as `PlayerLook` does. Mounted look ignores both today.
- **Reset the recentre timer on any look input, not just yaw.** Today the timer only resets on
  `|lookInput.x| > 0.01`, so pitching the camera while parked at 90° lets the yaw creep home
  underneath you.
- **Wrap the yaw offset to (−180, 180].** It is unbounded, so orbiting past 180° makes the
  drift-back unwind the long way round instead of taking the short path.

**Orbit pitch.** A second pitch channel, `orbitPitch`, driven by the same look input and
clamped separately from the first-person `mountedPitch` (which keeps its own meaning and its
own `defaultMountedPitch` starting value). The third-person camera offset is rotated by it:

```
Quaternion orbit = yawRot * Quaternion.Euler(orbitPitch, 0, 0);
targetPosition   = pivot.position + orbit * GetThirdPersonCameraOffset();
targetAimPoint   = pivot.position + yawRot * (Vector3.forward * thirdPersonLookAhead);  // unchanged
```

`orbitPitch` starts at 0, so at rest the framing is byte-for-byte what it is today. Positive
pitch lifts the camera and looks down; negative drops it and looks up. Clamped to
`[-25°, +60°]` by default — negative far enough to look up at the ostrich's head, not so far
the camera digs into the ground. It recentres to 0 on the same delay and speed as yaw.

### Testable core

The recentre and wrap arithmetic moves into a static `MountLookMath` class so it can be tested
without a scene:

- `WrapAngle(float degrees)` → (−180, 180]
- `StepRecentre(offset, timeSinceInput, delay, speed, deltaTime)` → new offset

## 3. Camera framing — Red Dead Redemption style

Today the ostrich camera sits 3 m above and 7 m behind the rider, aiming 6 m ahead: a 13°
downward, distant, map-like view. RDR2's horse camera is closer, lower, roughly level with the
rider's head, offset slightly to the right, and only a few degrees down.

The downward angle is `atan(y / (distance + lookAhead))`, which gives the target numbers:

| field | `MountModule` default | `Ostrich.prefab` | why the ostrich differs |
|---|---|---|---|
| `thirdPersonOffset` | `(0.35, 1.7, -1)` | `(0.45, 2.0, -1)` | 1.5× root scale, taller than a horse |
| `thirdPersonDistance` | 4.5 | 5.5 | ditto |
| `thirdPersonLookAhead` | 7 | 8 | flattens the aim |

Ostrich: `atan(2.0 / 13.5)` ≈ **8.4° down**, against 13° today. The `x` offset is the
over-the-shoulder bias. `thirdPersonOffset.z` stays negative only to pick the sign —
`GetThirdPersonCameraOffset` takes the magnitude from `thirdPersonDistance`.

Numbers are a starting point, verified against an in-editor screenshot and adjusted.

## 4. Lowering the rider

### Cause

`ParentRiderToMount` hardcodes `mountedPlayer.localPosition = Vector3.zero`
(`MountModule.Mounting.cs:280`). The player's root is at their feet, so the feet land on the
seat point and the body stands above it.

### Fix

New serialized `Vector3 seatOffset` on `MountModule`, applied in place of the hardcoded zero.
Default `Vector3.zero`, so no existing mount moves.

The ostrich gets roughly `y = -0.55` local. The seat point is a child of the 1.5-scaled root,
so that is ≈ 0.83 m of world drop — enough to put the pelvis rather than the feet at the
saddle, which is what the riding pose below assumes.

A field on `MountModule` rather than just moving `SeatPoint` in the prefab: the seat marker
should stay where the saddle actually is, and how deep the rider sits into it is a separate,
tunable intention.

## 5. `MountedRiderPose` — the riding pose

New component on the mount, beside `MountModule`, subscribing to its `Mounted`/`Dismounted`
events. `[DefaultExecutionOrder(900)]` — after Unity evaluates the Animator (which happens in
`PreLateUpdate`, before any `LateUpdate`), and before `MountModule`'s order-1000 camera pass.

Bones resolve through `Animator.GetBoneTransform(HumanBodyBones.…)`. `AstronautArmature` is a
Humanoid avatar (`animationType: 3`), so this works for any humanoid rider without knowing the
rig's transform paths.

### Applying the pose

Each bone is written as a **multiplicative offset on the Animator's output**:

```
bone.localRotation = animatorLocalRotation * Quaternion.Slerp(identity, authoredOffset, weight);
```

Not an absolute local rotation, and not a base captured at mount time. A humanoid bone's rest
local rotation is not identity and is not available at runtime, and a mount-time capture would
sample whatever frame the idle clip happened to be on, giving a slightly different pose every
time you mount. An offset needs no base, cannot drift, lets the idle clip's breathing show
through, and degrades to "the standing pose" rather than to garbage if a rig disagrees.

`SteerModule.Update` already calls `ForceIdleAnimation()` every frame while mounted, so the
only thing underneath is a static idle. Nothing fights the writes.

### Two rotation frames, not one

Ball joints (hips, shoulders, spine) rotate about the **rider's** axes, so "lean forward" and
"open outward" mean the same thing whatever the artist left the bone's local axes as, and
mirroring is just negating Y and Z.

Hinge joints (knees, elbows, ankles) rotate about their **own parent-relative** frame. This is
not a refinement — applying a knee bend in the rider's frame after the thigh has been abducted
out around the barrel swings the shin out sideways instead of folding it down, and the leg ends
up held out in a mid-air split. The bend has to happen in the frame the thigh is now in.

### The pose

Solved numerically against the ostrich's measured barrel rather than guessed: the thigh
abduction is swept until the knee lands **on** the barrel's edge (x ≈ ±0.43 against an edge at
−0.441/+0.565) instead of inside it, and the knee bend until the foot sits below the knee and
tucked back.

| bone | offset | frame | reads as |
|---|---|---|---|
| UpperLeg | `(-50, 0, ∓26)` | rider | thighs forward and opened round the barrel |
| LowerLeg | `(-90, 0, 0)` | hinge | knees closed, shins down the flanks |
| Foot | `(25, 0, 0)` | hinge | toes up |
| Spine | `(8, 0, 0)` | rider | slight forward lean |
| Chest | `(6, 0, 0)` | rider | ditto, split so the curve is not one hinge |
| UpperArm | `(15, 0, ±8)` | rider | arms down off the leaned chest |
| LowerArm | `(35, 0, 0)` | hinge | forearms forward, on the reins |

Two sign traps this rig sprang, both recorded because nothing about them is guessable:
**negative** Z abducts (positive drives each leg across to the far side), and **negative** X
closes the knee (positive kicks the shin up behind like a hamstring curl).

### Motion response

Layered on the spine and chest, derived **entirely from the mount transform's own measured
world motion** — never from `OstrichLocomotion`. That keeps the component mount-agnostic (it
works on the ant, the crawler, anything with a `MountModule`) and keeps Assembly-CSharp out of
the `SpaceGame.Creatures.Ostrich` asmdef.

Measured per frame from the mount transform, each smoothed:

- vertical speed → counter-lean, so the rider absorbs the stride instead of riding rigid
- forward speed → additional forward lean
- yaw rate → roll into the turn

Every channel has a gain and a hard clamp. Extracted as a pure static
`RiderPoseMath.SpineOffset(verticalSpeed, forwardSpeed, turnRate, gains)` returning a
`Vector3` of Euler degrees, so the clamps are testable without a scene.

### Blending

Weight ramps 0→1 over `blendInDuration` (0.25 s) on mount and 1→0 on dismount. The component
keeps applying during blend-out so the pose releases rather than snaps; once weight reaches 0
it stops writing and the Animator owns the bones again.

## 6. Tests

EditMode, in `Assets/Game/Editor/Tests/`. These types live in Assembly-CSharp and an asmdef
cannot reference Assembly-CSharp, so an `Editor/` folder is the only place they can go —
same as the existing `RiderTurnChannelTests.cs` and `InteractorRayResolutionTests.cs`.

**`InteractorHoverStateTests`**
- disabling an Interactor clears `HoveredInteractable`
- disabling an Interactor clears `IsHoveringInteractable`

**`MountLookMathTests`**
- `WrapAngle` maps 350° → −10°, −190° → 170°, 180° → 180°
- `StepRecentre` does not move before the delay elapses
- `StepRecentre` moves at exactly `speed × deltaTime` after the delay
- `StepRecentre` does not overshoot zero
- a wrapped offset always recentres the short way

**`RiderPoseMathTests`**
- each channel clamps at its configured maximum
- zero motion produces a zero offset
- vertical speed and turn rate produce opposite-signed leans for opposite-signed input

## Out of scope

- A "press E to dismount" prompt. Dismount is Escape (`SteerModule.cs:184`); wiring a prompt
  for it is a separate piece of work.
- A first-person riding pose. The mount defaults to third person and the first-person camera
  sits in the rider's head, where the pose is invisible.
- A manual recentre key. The slow drift-back was chosen over it.
