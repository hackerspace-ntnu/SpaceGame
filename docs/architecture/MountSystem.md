# Mounting System

Guide for setting up mountable entities — creatures, vehicles, ships, anything a player can climb onto and drive.

The runtime is **two components** plus **one motor**. Pick the motor based on how the entity should move; everything else is the same.

---

## 1. The Two-Component Core

Every mountable entity needs:

| Component | File | Responsibility |
|---|---|---|
| `MountModule` | `Assets/Scripts/agents/modules/MountModule.cs` | Mount/dismount lifecycle, `IInteractable` surface, AI suppression while ridden, mounted camera (FP/TP), look input. |
| `SteerModule` | `Assets/Scripts/agents/modules/SteerModule.cs` | Reads rider input each frame, forwards it to the motor as a `RiderInput`, handles jump/hold-to-leap, optional visual lean. |

Both are `IBehaviourModule`s — they sit on the entity alongside its normal AI modules and an `AgentController` resolves them automatically.

`MountModule` fires `Mounted` / `Dismounted` events. While mounted, it suppresses every other `IBehaviourModule` (and legacy `IAgentBrain`) on the entity unless `allowAISelfMovementWhenMounted = true`.

---

## 2. The Motor — pick one

The motor is the physics/locomotion layer. Whichever you pick must implement `IRiderControllable` (`ApplyRiderInput`) so `SteerModule` can drive it. Optional interfaces (`IMountJumpMotor`, `IMountLeapMotor`) unlock the corresponding rider features.

| Motor | File | Movement model | Use for |
|---|---|---|---|
| `NavMeshAgentMotor` | `agents/AI/motor/NavMeshAgentMotor.cs` | Unity NavMeshAgent — follows baked navmesh, AI does pathfinding. | Mountable creatures and NPCs that should *also* wander/patrol/flee with proper pathfinding when not ridden. |
| `RigidbodyMotor` | `agents/AI/motor/RigidbodyMotor.cs` | Direct `Rigidbody.linearVelocity` writes along forward; tank steer via `transform.Rotate`. Gravity + collisions intact. | Ground vehicles (rovers, dune riders, hover bikes) and physics creatures that don't need pathfinding. |
| `FlyingRigidbodyMotor` | `agents/AI/motor/FlyingRigidbodyMotor.cs` | Throttle + yaw + ascend/descend on a Rigidbody, optional altitude hold, no gravity dependence. | Flying vehicles, drones, ships. |

All three implement `IMovementMotor` (so `AgentController` can also drive them with AI `MoveIntent`s) **and** `IRiderControllable` (so `SteerModule` can drive them with `RiderInput`).

### Motor Capability Matrix

| Capability | NavMeshAgentMotor | RigidbodyMotor | FlyingRigidbodyMotor |
|---|---|---|---|
| AI pathfinding (`MoveIntent.MoveToPosition`) | ✅ via NavMesh | ✅ direct steering, no obstacle avoidance | ✅ direct steering, free-flight |
| Rider steering (`IRiderControllable`) | ✅ tank steer | ✅ tank steer | ✅ throttle + yaw + vertical |
| Jump (`IMountJumpMotor`) | ✅ | ✅ (kinematic arc) | ❌ |
| Leap (`IMountLeapMotor`) | ✅ | ✅ (kinematic arc) | ❌ |
| Vertical / altitude axis | ❌ | ❌ | ✅ |
| Honors gravity / ground friction | NavMesh decides | ✅ | ❌ (altitude controlled) |
| Required scene support | Baked NavMesh | Rigidbody + Collider | Rigidbody + Collider |

---

## 3. Setup Recipes

### A. Mountable creature with AI (e.g. a rideable ant)

Components on the root GameObject:

1. `MountModule`
2. `SteerModule`
3. `AgentController`
4. `NavMeshAgentMotor` + Unity `NavMeshAgent`
5. Any AI modules you want — `WanderModule`, `PatrolModule`, `FleeModule`, etc.
6. `MountModule.allowAISelfMovementWhenMounted` → set to `true` if you want the creature to keep wandering while the rider is idle, `false` to make it stand still until the rider gives input.

The creature pathfinds through AI when not ridden, and the rider tank-steers it through `SteerModule` → `NavMeshAgentMotor.ApplyRiderInput`. Jump and leap come for free.

### B. Ground vehicle (e.g. dune rider, rover)

1. `MountModule`
2. `SteerModule`
3. `Rigidbody` + `Collider`
4. `RigidbodyMotor` (auto-added by `SteerModule.EnsureRuntimeMovementPath` if missing, but add explicitly so you can tune fields)
5. `AgentController` (also auto-added if missing)
6. No AI modules → vehicle is inert until ridden.

Tune `RigidbodyMotor.maxSpeed`, `walkSpeedMultiplier`, `acceleration`, `riderTurnSpeed`, `enableJump`. If the rider should be able to sprint, set `SteerModule.riderCanRun = true` and assign `runActionName` (default `"Sprint"`).

### C. Flying vehicle / ship

1. `MountModule`
2. `SteerModule` + set `verticalActionName` to a Vector2 action whose Y axis is ascend/descend (or a float action).
3. `Rigidbody` + `Collider`
4. `FlyingRigidbodyMotor`
5. Optional `AgentController` if you want autopilot AI when not ridden.

`FlyingRigidbodyMotor.altitudeHold = true` keeps the craft at `cruiseAltitude` when the rider isn't pushing the vertical axis. Jump/leap are intentionally absent — flying motors don't implement those interfaces.

---

## 4. Per-Component Field Reference

### `MountModule`

| Field | Purpose |
|---|---|
| `seatPoint` | Where the rider is parented while mounted. Defaults to the entity itself. |
| `dismountPoint` | Where the rider lands on dismount. Falls back to `fallbackDismountDistance` ahead of the seat if null. |
| `disablePlayerMovement` / `disablePlayerLook` / `disablePlayerInteractor` | Toggle individual rider components off while seated. |
| `mountCooldown` | Seconds after dismount before re-mount is allowed. |
| `defaultPerspective` | First-person or third-person on mount. |
| `thirdPersonPivot` / `thirdPersonOffset` / `thirdPersonDistance` / `thirdPersonFollowLerp` / `thirdPersonLookAhead` | TP camera rig. |
| `lookActionName` / `lookSensitivity` / `lookPitchClamp` / `defaultMountedPitch` | Mounted look input. |
| `cameraAutoAlignSpeed` / `cameraAutoAlignDelay` | Camera snaps back behind the entity after the rider stops looking around. |
| `allowAISelfMovementWhenMounted` | If false, all other modules are disabled while ridden. |

### `SteerModule`

| Field | Purpose |
|---|---|
| `moveActionName` | Vector2 input action for steer. Default `"Move"`. |
| `jumpActionName` | Button action for jump/leap. Default `"Jump"`. |
| `verticalActionName` | Optional ascend/descend axis (only used by flying motors). Leave blank for ground. |
| `runActionName` | Sprint button. Active only if `riderCanRun = true`. |
| `steeringOverrideThreshold` | Below this magnitude, rider input is ignored and AI can run (if allowed). |
| `turnSmoothTime` | SmoothDamp window on rider stick input. |
| `riderCanRun` | If false, rider is locked to walk speed regardless of sprint button. |
| `jumpEnabled` / `leapEnabled` / `leapHoldTime` / `leapHorizontal` / `leapVertical` / `leapDuration` | Tap-jump vs hold-leap config. |
| `visualTiltRoot` / `leanAmount` / `leanSmoothTime` | Cosmetic body lean during turns. |

### `RigidbodyMotor` (rider-relevant)

| Field | Purpose |
|---|---|
| `maxSpeed` | Top speed when running. |
| `walkSpeedMultiplier` | Fraction of `maxSpeed` when not running. |
| `acceleration` / `deceleration` | Throttle ramp rates. |
| `riderTurnSpeed` | Yaw degrees/sec under rider control. |
| `enableJump` / `jumpHeight` / `jumpDuration` / `jumpCooldown` | Tap-jump parameters (kinematic arc). |
| `enableLeap` / `leapCooldown` | Hold-jump leap parameters. |

### `NavMeshAgentMotor` (rider-relevant)

| Field | Purpose |
|---|---|
| `NavMeshAgent.speed` (on the agent itself) | Base run speed. Captured at Awake as `defaultSpeed`. |
| `walkSpeedMultiplier` | Fraction of base speed when not running. |
| `riderTurnSpeed` | Yaw degrees/sec under rider control. |

### `FlyingRigidbodyMotor` (rider-relevant)

| Field | Purpose |
|---|---|
| `maxSpeed` | Horizontal top speed. |
| `maxVerticalSpeed` | Ascend/descend top speed. |
| `acceleration` / `deceleration` | Throttle ramp rates. |
| `riderTurnSpeed` | Yaw degrees/sec. |
| `altitudeHold` / `cruiseAltitude` / `altitudeHoldGain` | Hands-off altitude maintenance. |

---

## 5. Frame Flow

While mounted *and* rider input is above `steeringOverrideThreshold`:

1. `SteerModule.Update` — read input, smooth, build a `RiderInput`.
2. `SteerModule.Tick` (called from `AgentController.Update`) — call `motor.ApplyRiderInput(input, dt)` and return `MoveIntent.Idle()` to claim the frame.
3. `AgentController` calls `motor.Tick(Idle)`. The motor's rider-frame guard skips the `MoveIntent` branch so the rider's writes stand.

When rider input drops below threshold, `SteerModule.Tick` returns `null` — other modules can produce intents (only if `allowAISelfMovementWhenMounted = true`).

Jump / leap go through `IMountJumpMotor` / `IMountLeapMotor` — motors that don't implement them just ignore the rider's button.

---

## 6. Common Tweaks

| I want… | Do this |
|---|---|
| Faster vehicle | Increase `RigidbodyMotor.maxSpeed`. For runtime boosts, wrap it in a setter. |
| Faster creature | Increase `NavMeshAgent.speed` on the prefab. (Note: motor caches it on Awake, so runtime changes need a setter that updates `defaultSpeed`.) |
| Sprint while ridden | `SteerModule.riderCanRun = true` and assign a sprint action. |
| Mount keeps wandering when rider is idle | `MountModule.allowAISelfMovementWhenMounted = true`. |
| Add a flying ship | Use `FlyingRigidbodyMotor` and set `SteerModule.verticalActionName`. |
| Disable jump on a particular vehicle | `SteerModule.jumpEnabled = false` or `RigidbodyMotor.enableJump = false`. |
| Custom motor (water, grappling, etc.) | Implement `IMovementMotor` + `IRiderControllable`. Optionally implement `IMountJumpMotor` / `IMountLeapMotor`. `SteerModule` will pick it up automatically. |

---

## 7. Notes / Gotchas

- **Ground friction draining throttle**: `RigidbodyMotor.ApplyRiderInput` keeps an internal `riderForwardSpeed` and re-asserts absolute velocity each call. If you write a custom rigidbody motor, do the same — reading back `Rigidbody.linearVelocity` after a friction-damped FixedUpdate makes the rider feel glued to the ground.
- **Rider/mount collisions**: `MountModule` ignores collider pairs between rider and mount while seated, then restores them on dismount. Rigidbody constraints are also captured/restored.
- **Animator root motion**: captured at mount time and restored on dismount, so root-motion-driven drift can't fight rider input.
- **Mount cooldown**: `IsAvailableForMount` enforces `mountCooldown` seconds between dismount and re-mount.
