# Mounting System

Guide for setting up mountable entities (rovers, creatures, vehicles, etc.).

The system is split into two responsibilities:

- **`MountController`** -- mount-state only: rider attach/detach, component toggling, seat/dismount points.
- **`MountSteeringController`** -- mounted input, camera perspective, look, steering override, and visual lean.

This separation means you can have mountable objects that don't give the rider direct steering control (e.g. a transport pod), or full rider-steerable mounts with camera and lean.

## Components Overview

| Component | Where | Purpose | Required? |
|-----------|-------|---------|-----------|
| `MountController` | Mount object | Rider attach/detach lifecycle, component toggling | Always |
| `MountSteeringController` | Mount object | Mounted input, camera, steering override | Only for rider-steerable mounts |
| `MountInteractor` | Mount object | Lets players interact to mount via the interaction system | Always |
| `MountedAgentBrain` | Mount object | Hybrid brain: AI fallback + rider steering override | Only for creatures/vehicles with AI |
| `IMountJumpMotor` | Mount motor (optional) | Implement on a motor to support mounted jumping | Optional |

## Setup Recipes

### A. Rider-steerable creature with AI (e.g. ant)

Add all three components:

1. **`MountController`** -- mount/dismount lifecycle
2. **`MountSteeringController`** -- rider camera + steering
3. **`MountedAgentBrain`** -- blends AI with rider override

Key behavior: when mounted but idle (no WASD input), the creature continues its normal AI movement. As soon as the rider gives steering input, the mounted override takes control.

### B. Rider-steerable vehicle (no AI)

1. **`MountController`**
2. **`MountSteeringController`**

No `MountedAgentBrain` needed -- wire `MountSteeringController.CurrentMoveInput` / `CurrentSteeringForward` into your vehicle's motor directly.

### C. Non-steerable mount (e.g. transport pod)

1. **`MountController`** only

The rider sits and rides along but has no steering control. The mount moves via its own AI or scripted path.

### Backwards compatibility

`MountedAgentBrain` auto-adds `MountSteeringController` at runtime if the object has a `MountController` but no steering component yet. Existing scene objects (e.g. ants) will keep working without immediate scene changes.

## Component Details

### MountController

Mount-state only. No input, camera, or steering logic.

| Field | Default | Description |
|-------|---------|-------------|
| `seatPoint` | self | Transform where the rider sits |
| `dismountPoint` | -- | Transform where rider is placed on dismount (falls back to offset from mount) |
| `disablePlayerMovement` | true | Disable `PlayerMovement` while mounted |
| `disablePlayerLook` | true | Disable `PlayerLook` while mounted |
| `disablePlayerInteractor` | true | Disable `Interactor` while mounted |
| `mountCooldown` | 0.25s | Cooldown between mount/dismount |
| `fallbackDismountDistance` | 1.6 | Fallback offset if no dismount point assigned |

Events:
- `Mounted(PlayerMovement)` -- fired after rider is attached
- `Dismounted(PlayerMovement)` -- fired after rider is detached

Public API:
- `TryMount(Interactor, Transform mountPointOverride)` -- attempts to mount the rider
- `Dismount()` -- detaches the rider
- `CanMount(Interactor)` -- availability check
- `IsMounted`, `IsAvailableForMount` -- state queries
- Exposes cached rider references: `MountedPlayerMovement`, `MountedPlayerLook`, `MountedFirstPersonCamera`, etc.

### MountSteeringController

Requires `MountController` on the same object (`[RequireComponent]`). Subscribes to `Mounted`/`Dismounted` events to activate/deactivate.

| Field | Default | Description |
|-------|---------|-------------|
| `lookSensitivity` | 1.0 | Mouse/stick sensitivity |
| `lookPitchClamp` | 75 deg | Vertical look limit |
| `steerSpeed` | 120 deg/s | Mount rotation speed |
| `turnSmoothTime` | 0.12s | Input dampening |
| `leanAmount` | 10 deg | Max visual tilt angle |
| `leanSmoothTime` | 0.18s | Lean animation smoothness |
| `momentumDamping` | 7.0 | Steering momentum decay |
| `visualTiltRoot` | -- | Object that tilts for visual lean |
| `defaultPerspective` | ThirdPerson | Camera on mount |
| `thirdPersonCamera` | -- | Auto-created if empty |
| `thirdPersonPivot` | -- | Camera pivot point |
| `thirdPersonOffset` | (0, 2.2, -3.8) | Camera framing offset; `x/y` frame the shot and the `z` sign controls back/front direction |
| `thirdPersonDistance` | 3.8 | How far from the mount the third-person camera sits |
| `thirdPersonFollowLerp` | 14 | Camera follow speed |
| `cameraAutoAlignSpeed` | 90 deg/s | Look auto-center speed |
| `cameraAutoAlignDelay` | 0.5s | Delay before auto-center |
| `perspectiveToggleActionName` | "Next" | Input action to toggle camera |
| `steeringOverrideThreshold` | 0.1 | Input magnitude before rider takes steering control |

Public API:
- `HasSteeringOverride` -- true when rider is actively giving move input
- `CurrentMoveInput` -- smoothed Vector2 from rider
- `CurrentSteeringForward` -- world direction the rider is looking/steering toward
- `ThirdPersonDistance` / `SetThirdPersonDistance(float)` -- read or change mounted camera distance at runtime
- `ConsumeMountedJumpPressed()` -- returns true once per jump press

### MountInteractor

Implements `IInteractable`. Place on the mount or a child object (e.g. a saddle collider).

| Field | Description |
|-------|-------------|
| `mountController` | Reference to `MountController` (auto-found in parent if empty) |
| `mountTransform` | Optional seat position override |

### MountedAgentBrain

Implements `IAgentBrain`. Hybrid brain that delegates to fallback AI or rider input.

Decision flow each `Tick()`:
1. If mounted **and** `steeringController.HasSteeringOverride` -- use rider input (tank steering: W/S forward/back, A/D handled by `MountSteeringController`)
2. If mounted but **no steering override** -- fall through to fallback AI (creature keeps moving on its own)
3. If not mounted -- use fallback AI

| Field | Default | Description |
|-------|---------|-------------|
| `fallbackBrain` | -- | `NpcBrain` to use when unmounted or idle-mounted |
| `mountController` | -- | Auto-found if empty |
| `steeringController` | -- | Auto-found; auto-added if missing |
| `mountedMoveDistance` | 2.0 | NavMesh target distance ahead |
| `mountedStopDistance` | 0.15 | Stop threshold |
| `mountedSpeedMultiplier` | 2.4x | Top mounted speed |
| `mountedAcceleration` | 4.0 | Speed ramp rate (units/sec) |
| `mountedNavMeshSampleDistance` | 4.0 | NavMesh sample radius |
| `faceMouseLookDirection` | true | Face steering forward |
| `enableMountedJump` | true | Forward jump to `IMountJumpMotor` |

## Rider Requirements

The player object needs these components reachable from the interacting transform:

- `PlayerMovement` on a parent -- disabled while mounted.
- `PlayerLook` -- disabled while mounted.
- `Interactor` -- disabled while mounted.
- `Rigidbody` -- set to kinematic during mount, restored on dismount.
- `Camera` -- first-person camera, toggled off in third-person mode.

No extra setup needed on the player side; `MountController` finds and manages these automatically.

## Mount / Dismount Flow

**Mounting** (triggered via `MountInteractor`):
1. `MountController.TryMount()` validates availability, caches rider references.
2. Disables rider movement, look, and interaction components.
3. Parents rider to seat point, sets rigidbody to kinematic.
4. Fires `Mounted` event.
5. `MountSteeringController` (if present) receives the event, initializes view state, and applies camera perspective.

**Dismounting** (Escape key, or calling `Dismount()`):
1. Unparents rider, places at dismount point.
2. Restores rigidbody state and re-enables rider components.
3. Fires `Dismounted` event.
4. `MountSteeringController` resets steering/camera state.
5. Cooldown starts to prevent spam.

## Camera System

Managed by `MountSteeringController`. Two perspectives, toggled via `perspectiveToggleActionName` (default: `"Next"`).

- **First-person** -- uses the rider's own camera, positioned at head. Visor distortion enabled.
- **Third-person** -- offset behind/above the mount, smooth-follows with lerp. Rider head made visible.

Look input adjusts `cameraYawOffset`. After `cameraAutoAlignDelay` (0.5s) of no look input, the camera auto-aligns back to mount forward.

## Steering Override Behavior

When the rider is mounted but not pressing movement keys (`HasSteeringOverride == false`):
- `MountSteeringController` tracks the mount's current yaw but does not rotate it
- `MountedAgentBrain` falls through to the fallback AI brain
- The creature continues its normal AI movement

When the rider gives input (`HasSteeringOverride == true`):
- `MountSteeringController` takes over rotation via momentum-based steering
- `MountedAgentBrain` uses rider input for `MoveIntent`
- Visual lean activates based on steering momentum and speed

The `steeringOverrideThreshold` (default 0.1) controls how much input is needed before the override kicks in.

## Adding Jump Support

Implement `IMountJumpMotor` on your mount's movement motor. `MountedAgentBrain` calls `RequestJump()` when the rider presses Jump (if `enableMountedJump` is true).

## Quick Checklist

**Every mount:**
- [ ] `MountController` with seat/dismount transforms assigned
- [ ] `MountInteractor` referencing the `MountController`

**Rider-steerable mounts (add):**
- [ ] `MountSteeringController` with camera and steering settings
- [ ] (Optional) `visualTiltRoot` assigned for lean animation
- [ ] (Optional) Third-person camera/pivot assigned, or left empty for auto-creation

**AI creatures (add):**
- [ ] `MountedAgentBrain` with a fallback `NpcBrain` assigned
- [ ] Mount has a NavMeshAgent or compatible motor
- [ ] (Optional) `IMountJumpMotor` on motor for jump support

## File Locations

```
Assets/Scripts/agents/controller/mount/
  MountController.cs                -- Core partial: fields, state, Awake/OnDisable
  MountController.Mounting.cs       -- Mount/dismount API (TryMount, Dismount)
  MountController.MountState.cs     -- Rider component caching, toggling, rigidbody state
  MountSteeringController.cs        -- Core partial: fields, state, public API
  MountSteeringController via:
    MountController.Lifecycle.cs    -- Update loop, input reading, event handlers
    MountController.Camera.cs       -- Camera perspective, first/third person toggling
    MountController.Steering.cs     -- Look, steering rotation, visual lean

Assets/Scripts/InteractionSystem/Interactions/
  MountInteractor.cs                -- IInteractable entry point

Assets/Scripts/agents/AI/brains/
  MountedAgentBrain.cs              -- Hybrid AI/rider brain

Assets/Scripts/agents/AI/motor/
  IMountJumpMotor.cs                -- Jump interface
```
