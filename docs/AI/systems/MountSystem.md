---
system: MountSystem
layer: vehicles
summary: Merged into Vehicles.md; only the motor capability matrix and mount setup dials remain here.
paths:
  - Assets/Game/Scripts/agents/Modules/Riding/
symptoms:
  - "looking for the mount system doc"
  - "which motor supports jump, leap or a vertical axis"
  - "what does seatOffset, followMountPitch or leapHoldTime do"
reads_with: [Vehicles, Ornithopter, PlayerShip]
redirect_to: Vehicles
updated: 2026-09-01
---

# Mount system

**Merged into [Vehicles.md](Vehicles.md)** — model, key types, flows, multiplayer, persistence, gotchas and the recipe for a new rideable all live there. This page keeps only the setup dials.

Files moved: the mount code is `Assets/Game/Scripts/agents/Modules/Riding/`, **not** `agents/modules/`.

## Motor capability matrix

| Motor ([Motors/](Assets/Game/Scripts/agents/AI/Motors/)) | Rider steer | Jump / Leap | Vertical axis | Needs |
|---|---|---|---|---|
| `NavMeshAgentMotor` | tank | ✅ / ✅ | ❌ | baked NavMesh + `NavMeshAgent` |
| `RigidbodyMotor` | tank | ✅ / ✅ (kinematic arc) | ❌ | Rigidbody + Collider |
| `FlyingRigidbodyMotor` | throttle + yaw | ❌ | ✅ (`altitudeHold`, `cruiseAltitude`) | Rigidbody + Collider |
| `HoverRigidbodyMotor` | throttle + yaw | ❌ | ❌ — `input.Vertical` ignored; holds `rideHeight` over ground | Rigidbody + `HoverGroundSensor` |
| `OrnithopterFlightMotor` | `Move.y` = pitch, `Move.x` = roll | ❌ | `Vertical` = flap (beat / tuck) | see [Ornithopter.md](Ornithopter.md) |
| `LeggedDriver` (+ `OstrichDriver`, `DesertCrawlerDriver`, `HorseDriver`, `CrabDriver`, `HumanoidDriver`) | gait-bound | ❌ | ❌ | a `LeggedLocomotion` |

## Fields worth knowing

| Field | Where | Note |
|---|---|---|
| `seatOffset` | `MountModule` | Player origin is at the FEET — push down ~leg length or the rider stands on the saddle. |
| `mountableByDirectInteraction` | `MountModule` | `false` + a `MountStation` = cockpit-only boarding. |
| `allowAISelfMovementWhenMounted` | `MountModule` | `false` also suppresses root motion and `ForceStop`s the motor. |
| `followMountPitch` | `MountModule` | On for anything that pitches in flight; the camera never follows roll either way. |
| `firstPersonYawClamp` / `orbitPitchMin`·`Max` | `MountModule` | FP head is clamped (180 = full circle, the default); the TP orbit yaw is unbounded. |
| `cameraAutoAlignDelay` / `Speed` | `MountModule` | Orbit drifts home only; the FP head never re-centres itself. |
| `steeringOverrideThreshold` | `SteerModule` | Below it the rider's frame is released back to the AI channel. |
| `turnActionName` | `SteerModule` | Separate yaw axis for a rig that spends `Move.x` on strafe (the crab). |
| `leapHoldTime` | `SteerModule` | Tap = `RequestJump`, hold ≥ this then release = `RequestLeap`. |
