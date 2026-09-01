---
system: Ornithopter
layer: vehicles
summary: Folded wing pack deployed mid-air; point-mass energy flight model, stalls, crash damage.
paths:
  - Assets/Game/Scripts/Vehicles/Ornithopter/
  - Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs
  - Assets/Game/Scripts/Items/Equipped/WingPackItem.cs
  - Assets/Game/Prefabs/agents/Vehicles/Aircraft/DuneOrnithopter.prefab
symptoms:
  - "the wing pack refuses to launch and logs that there is no room"
  - "pulling back on the stick does not climb, or the craft drops like a brick"
  - "the stall never ends and the nose stays up"
  - "one wing opens while the other closes"
  - "the craft is invisible to everyone but the host after launching"
  - "a mid-air save reloads with the ornithopter falling out of the sky"
  - "a hard dive into a cliff does almost no damage while a gentle landing hurts"
  - "the craft grinds along a rock face forever instead of crashing"
reads_with: [Vehicles, Multiplayer, Persistence, audio]
updated: 2026-09-01
---

# Ornithopter

A 10 m flapping-wing aircraft carried folded in the inventory, deployed in mid-air, flown prone from a cradle on a point-mass energy model.
**Scope:** [`Assets/Game/Scripts/Vehicles/Ornithopter/`](Assets/Game/Scripts/Vehicles/Ornithopter) (Flight/ + Wings/, own asmdef), [OrnithopterFlightMotor.cs](Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs) + [.Replication.cs](Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.Replication.cs), [WingPackItem.cs](Assets/Game/Scripts/Items/Equipped/WingPackItem.cs), [DuneOrnithopter.prefab](Assets/Game/Prefabs/agents/Vehicles/Aircraft/DuneOrnithopter.prefab), [WingPack.prefab](Assets/Game/Prefabs/Items/Equipment/WingPack.prefab).
**Related:** [Vehicles.md](Vehicles.md) · [MountSystem.md](MountSystem.md) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [Artifacts.md](Artifacts.md) · [Combat.md](Combat.md) · [audio.md](audio.md)

## Model

- Point mass on a flight path. **Two angles**: `Gamma` = where it is *moving*, `Pitch` = where it is *pointing*. `AngleOfAttack = Pitch - Gamma`, and AoA is what makes lift. Pulling the stick does not climb — it raises AoA, which curves gamma up a moment later.
- Per step ([`OrnithopterFlightModel.Step`](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightModel.cs)): spread/tuck → stamina & flap phase → attitude (rate × authority) → `cl`/`cd` → `speedRate = thrust − drag − g·sin γ` → `γ̇ = (lift·cos roll − g·cos γ) / v` → `turnRate = lift·sin roll / v + Turn·TailYawRate·authority`.
- `g = 9.81` is a **const in the model**, not `Physics.gravity`; the Rigidbody runs `useGravity = false`, damping 0. One source of weight.
- Lift curve is linear (`LiftSlopePerDegree·aoa`) to `StallAngle`, then lerps to `PostStallLiftFraction` of peak across `StallFadeAngle`. Post-stall lift **must stay > 0** or the nose never falls and the stall never ends.
- No throttle. Speed is bought with altitude or with flapping; flapping spends stamina (recovers only while gliding), and `FlapEffort = demand × clamp01(Stamina / StaminaFadeBand)` so exhaustion fades in. Thrust pulses on a half-cosine of `FlapPhase` (never negative).
- Control authority scales with `Airspeed / FullAuthoritySpeed` and is multiplied by `StalledAuthority` in a stall. Roll self-centres. Tucking (`Flap < 0`) sheds `TuckSpreadLoss` of wing area — less lift *and* less drag, which is how the dive gets fast.
- State (`OrnithopterFlightState`): Airspeed, Gamma, Pitch, Roll, Heading, TurnRate, FlapPhase, FlapEffort, WingSpread, Deployment, Stamina, Stalled. `Deployment` is the caller's ramp, not the model's.
- `StallSpeed(cfg)` and `VelocityOf(state)` are derived, never tuned. Heading is measured **+Z toward +X**. At shipped defaults: stall ≈ 11.3 m/s, glide ≈ 9:1.
- Control mapping lives in `OrnithopterFlightMotor.ApplyRiderInput`, **not** in `SteerModule`: `Move.y → Pitch`, `Move.x → Roll` (and `Turn` when the turn axis is idle), `Vertical → Flap` (Space beats, LeftCtrl tucks), Escape dismounts via [MountModule](Assets/Game/Scripts/agents/Modules/Riding/MountModule.cs). Jump and leap are disabled on the prefab.

## Key types

| Type | File | Role |
|---|---|---|
| `OrnithopterFlightModel` | [Flight/OrnithopterFlightModel.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightModel.cs) | Static, pure: `Step`, `LiftCoefficient`, `VelocityOf`, `StallSpeed`. No MonoBehaviour, Transform or clock. |
| `OrnithopterFlightState` / `OrnithopterFlightInput` | [Flight/OrnithopterFlightState.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightState.cs) | What carries between steps; input axes are all −1..1. `Launch(speed, heading)` starts level, wings folded. |
| `OrnithopterFlightConfig` | [Flight/OrnithopterFlightConfig.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightConfig.cs) | Every tunable, serialized on the motor. |
| `OrnithopterCrash` / `OrnithopterCrashConfig` / `OrnithopterTouchdown` | [Flight/OrnithopterCrash.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterCrash.cs) | `ClosingSpeed(v, n)`, `ImpactDamage(v, cfg)`, and how a flight ended. |
| `IOrnithopterFlightState` | [Flight/IOrnithopterFlightState.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/IOrnithopterFlightState.cs) | Seam between the asmdef and Assembly-CSharp; also the test-double surface. |
| `OrnithopterWingRig` | [Wings/OrnithopterWingRig.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Wings/OrnithopterWingRig.cs) | Binds bones by exact name (throws naming the miss), caches the rest pose, holds each wing's `Sign`. |
| `OrnithopterWingAnimator` | [Wings/OrnithopterWingAnimator.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Wings/OrnithopterWingAnimator.cs) | Poses the rig as deltas on rest. `Tick(dt)` is public so tests/editor can drive it. |
| `OrnithopterAudio` | [Wings/OrnithopterAudio.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Wings/OrnithopterAudio.cs) | Wind loop, per-stroke flap, stall warning, deploy/fold — off the same seam. |
| `OrnithopterFlightMotor` (partial ×2) | [Motors/](Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs) + [.Replication.cs](Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.Replication.cs) | Owns the Rigidbody, rider input, touchdowns, and the wire. `IMovementMotor`, `IRiderControllable`, `IOrnithopterFlightState`, `IExternallyPosed`, `ITeleportAware`. |
| `WingPackItem` | [Items/Equipped/WingPackItem.cs](Assets/Game/Scripts/Items/Equipped/WingPackItem.cs) | `UsableItem`: launch window, spawn+seat, adoption, the one teardown path. |
| `OrnithopterSaveable` | [Persistence/Adapters/OrnithopterSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/OrnithopterSaveable.cs) | Save key `ornithopter`; deferred relaunch and pack re-adoption. |
| Builders | [OrnithopterBuilder.cs](Assets/Game/Editor/Vehicles/OrnithopterBuilder.cs), [WingPackBuilder.cs](Assets/Game/Editor/Vehicles/WingPackBuilder.cs) | **Tools ▸ Vehicles ▸ Build Dune Ornithopter Prefab / Build Wing Pack Item**, from [dune_ornithopter.fbx](Assets/Game/Art/Models/Vehicles/Ornithopter/dune_ornithopter.fbx). Re-runnable; measures off the meshes. |

## Tunables

| Field | Default | Effect |
|---|---|---|
| `Mass` / `WingArea` / `AirDensity` | 150 kg / 14 m² / 1.225 | The three numbers that set stall speed. Re-check with `StallSpeed` after any edit. |
| `LiftSlopePerDegree` / `StallAngle` | 0.09 / 15° | Lift per degree of AoA; where the wing lets go. |
| `StallFadeAngle` / `PostStallLiftFraction` | 12° / 0.45 | Wide+high = gentle recoverable stall. Zero fraction = unrecoverable. |
| `DragCoefficientZeroLift` / `InducedDragFactor` | 0.05 / 0.06 | Parasitic drag; `k·Cl²` price of lift. Raising `k` flattens the glide. |
| `FlapThrust` / `FlapHzIdle` / `FlapHzMax` | 9 m/s² / 0.35 / 1.6 Hz | Peak downstroke accel (≈half averaged over a beat); idle beat is never 0. |
| `StaminaDrainPerSecond` / `RecoverPerSecond` / `FadeBand` | 0.16 / 0.22 / 0.25 | ≈6 s of full-effort climb, ≈4.5 s to refill. |
| `PitchRate` / `RollRate` / `RollCentringRate` | 45 / 90 / 60 °/s | Stick authority at full airspeed. |
| `MaxPitch` / `MaxRoll` / `TailYawRate` | 60° / 60° / 25 °/s | Attitude limits; direct yaw so it still answers slow. |
| `FullAuthoritySpeed` / `StalledAuthority` | 12 m/s / 0.3 | Below the speed, controls fade proportionally. |
| `TuckSpreadLoss` | 0.65 | Area shed at full tuck. |
| Motor: `spreadDuration` / `launchAirspeed` | 0.6 s / 12 m/s | Wings-open ramp; floor on launch speed (a run-up keeps more). |
| Motor: `groundProbeDistance` / `landingGraceSeconds` | 1.4 m / 0.35 s | Landing probe; window where the world is ignored after launch. |
| Crash: `SafeClosingSpeed` / `LethalClosingSpeed` / `MaxDamage` | 8 / 32 m/s / 100 | Free arrival ↔ full player health. |
| Crash: `GroundSearchDistance` / `SurfaceClearance` | 12 m / 0.6 m | How far down to look for somewhere to stand; step out of the rock first. |
| Pack: `groundClearance` / `minLaunchClearance` / `ledgeProbeForward` | 0.6 / 6 / 1.5 m | Air-only launch window. |
| Pack: `speedCarry` / `launchLift` | 1.0 / 1.2 m | Fraction of the pilot's flat speed carried in; lift above the ledge. |
| Animator: `flapAmplitude` / `glideFlapFraction` | 38° / 0.12 | Shoulder swing at full effort; gliding wings still breathe. |
| Animator: `downstrokeTwist` / `upstrokeTwist` / `twistWashout` | 20° / −12° / 0.6 | **What makes a flap propel** — bite down, feather up. Zero and the wings just wave. |
| Animator: `splayOpen/Folded`, `sweepOpen/Folded` | 42/−34°, 18/−46° | The spread pose the deployment ramp lerps to. |
| Animator: `rollTwistPerDegree` / `boomPitchRange` / `tailSplayOpen` / `tailYawSplay` / `trimResponse` | 0.22 / 22° / 34° / 16° / 8 | Bank differential, tail boom and fan; `trimResponse` smooths **only** those slow channels. |

## Flows

1. **Take off.** `CanUse` → `HasLaunchRoom`: already falling (no ground within 0.6 m), **or** no ground within 6 m from a point 1.5 m ahead — the forward ray is what makes a cliff *edge* work. Refusal logs, deliberately. `OnRequestUse` (owner) stamps heading into `arg.R` and carried speed into `arg.P.x`; server `Use()` → `SpawnAndMountCraft`: pose from `LaunchPosition(prefab, …)` (seat marker measured off the **prefab**, before spawning) → `World.Spawn` with the **pilot as owner** → `MountNetworkSync.ServerMount` → `NetworkLaunch` → held pack renderers hidden.
2. **Cruise.** `SteerModule` → `ApplyRiderInput` latches input on the render loop; `FixedUpdate` advances `Deployment`, runs `Step`, `ApplyPose` (velocity set outright, attitude `Quaternion.Euler(-Pitch, Heading, -Roll)` via `MoveRotation`), then `CheckForLanding`.
3. **Stall.** AoA > `StallAngle` → `Stalled`, lift fades, authority ×0.3; the nose drops, gamma steepens, AoA falls back and the wing flies again. No script, no special case.
4. **Land.** Downward probe finds ground within 1.4 m after the grace window → `ReportTouchdown(wasImpact: false)`. A wing flown onto sand *lands*; it never waits for a collision.
5. **Crash.** `OnCollisionEnter` (contact 0) → same `ReportTouchdown(true)` — a cliff face is never under the craft, so without it the machine grinds along the rock forever.
6. **Both endings** funnel through `ReportTouchdown`: velocity from the **flight state**, `ClosingSpeed = max(0, dot(−v, n))`, `ResolveGroundPosition` (step out along the normal, ray down, ignore own children) → `EndFlight` → `PublishTouchdown` → `Landed` → `WingPackItem.HandleLanded`: price first, `ReleaseCraft(dismountFirst: authoritative, standAt)`, `NetDamage.Apply` last.
7. **Damage** is `t = (v² − safe²)/(lethal² − safe²)` × `MaxDamage`, floored at 1 past `safe`. Glide onto sand 20 m/s at −6° → 2.1 m/s → 0. Level into a cliff at 20 → 35. Held dive 42 m/s at −60° → 36.4 → 100. Wingtip scraped along a wall → 0.

## Multiplayer

- Server **spawns**, then hands ownership to the pilot; the prefab carries [`ClientNetworkTransform`](Assets/Game/Scripts/Core/Multiplayer/Authority/ClientNetworkTransform.cs), so the **pilot's machine is the one that simulates and its pose is the truth**. Stick and wings sit on one machine with no round trip.
- `NetMsg.CraftLaunch` goes `NetToAll` (idempotent — a craft already flying is left alone); `NetMsg.CraftDown` goes `NetToServer` from the pilot only (`IsFromThePilot` checks `OwnerClientId`; the server is waved through, which is also the offline path). Speeds ride `NetArg.A` as cm/s, `B` is `wasImpact`, `P` is the resolved ground spot.
- Non-owners get `ExternallyPosed = true` from `NetAuthority`, but the motor **stays enabled**: `Update` *measures* airspeed, attitude and gamma off the replicated transform and fakes `FlapEffort = clamp01(0.15 + vy·0.25)`. Running the real model there would produce a second, divergent flight.
- **Known limit:** `CraftLaunch` is an event, so a late joiner draws an airborne craft with its wings shut until it lands. Cosmetic only; closing it needs a `NetworkVariable`.
- Registered in [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset) (`GlobalObjectIdHash 3255795817`).

## Persistence

- Craft: `SaveableEntity` with a stamped `prefabId` (`2b557799…`, scope World) + `TransformSaveable`, `RigidbodySaveable`, `MountSaveable`, `OrnithopterSaveable`.
- `OrnithopterSaveable` stores `{ flying, airspeed }` under key `ornithopter` and re-`Launch`es in the **deferred** pass (`OnLoadComplete`) — a mid-air save otherwise reloads with the flight model off and the craft drops out of the sky. Stamina and flap phase restart; both are unnoticeable.
- Pack: `WingPackItem.CaptureItemState` stores a `SaveRef` to the craft under `craft`; `IItemDeferredRestore.TryCompleteRestore` → `AdoptCraft`. The other direction is `OrnithopterSaveable` subscribing `MountModule.Mounted` and handing the craft to the rider's pack. Both are idempotent and both run on the ordinary launch too, so there is one path.

## Gotchas

- **`useGravity` must stay off.** Unity's gravity on top of the model's own weight term makes a stall read as a brick.
- **Read impact speed from the flight state, before `EndFlight`.** By the time `OnCollisionEnter` runs, the solver has eaten the Rigidbody's velocity — asking the body under-reports exactly the hardest hits. Damage is applied *after* the dismount so a fatal crash leaves the body at the wreck.
- **`ForceStop()` is a no-op while flying.** `MountModule` calls it on every mount; without the guard, boarding an airborne craft (launch order, save restore, a late peer) turns the aircraft into a falling prop.
- **Never apply the launch locally on the server.** Ownership has already moved, `NetAuthority` froze that body, and PhysX logs per call. Launch travels as a message; `EndFlight` likewise skips velocity writes on a kinematic body.
- **The spawn pose is the only one that replicates.** Moving the craft after `Spawn` moves the server's copy alone, and the owner republishes over it — which once dragged craft, rider and chunk streamer to the world origin.
- **`OnTeleported` takes yaw only.** `state.Heading` is the truth `ApplyPose` writes every step, so an unhandled portal rotation is undone next frame; composing pitch/roll would invert the controls under a ceiling portal.
- **Ground probes must exclude `transform` children** — the hull colliders and the pilot parented into the cradle otherwise *are* the ground. `RaycastNonAlloc` is unsorted (16-hit buffer), so the nearest hit is picked manually.
- **Wing rig signs:** the two wing bones point outboard oppositely, so local **X/Y are already mirrored** (same angle → symmetric) while local **Z is not** — every Z rotation (sweep, splay) needs `Wing.Sign`. Get it wrong and one wing opens while the other closes.
- **Nothing at beat frequency is filtered.** Flap, gear spin and twist come straight off `FlapPhase`; only bank, pitch trim and the tail fan are damped. `Tick(dt)` takes its step because `Time.deltaTime` is 0 in edit mode.
- **Assembly split is structural:** `IRiderControllable` / `MountModule` live in Assembly-CSharp, which an asmdef cannot reference — hence the motor's location and `IOrnithopterFlightState` as the seam.
- Only four hull boxes collide (`COL_Fuselage/Nose/Boom/Cradle`); a 10 m span of collider would snag on terrain. The prefab origin sits on the **cradle** so pitching feels like dropping a shoulder.
- Prefab lives under `Prefabs/agents/…` (lowercase) while the builder writes `Prefabs/Agents/…` — same file only because macOS is case-insensitive.
- Tests: `OrnithopterFlightModelTests` (22), `OrnithopterCrashTests` (4) in [Tests/EditMode](Assets/Game/Tests/EditMode); `OrnithopterWingAnimatorTests` (10), `OrnithopterRigWiringTests` (10), `WingPackLaunchTests` (5) in [Editor/Tests](Assets/Game/Editor/Tests). Run via `HeadlessTestRunner.RunEditMode("<fixture>")` → `Temp/headless_tests.txt`.

## Extending

1. **Retune the flight model.** Edit `OrnithopterFlightConfig` on the prefab's motor only — the model file holds no constants. After touching `Mass`, `WingArea`, `AirDensity`, `LiftSlopePerDegree` or `StallAngle`, read `OrnithopterFlightMotor.StallSpeed` back rather than assuming, and re-run `OrnithopterFlightModelTests`.
2. **Change how it looks, not how it flies:** the animator fields. Anything at beat frequency stays unfiltered; only add smoothing to a channel driven by the stick.
3. **New aircraft, same physics.** Implement `IOrnithopterFlightState` (or reuse the motor), give the prefab `MountModule` + `MountNetworkSync` + `SteerModule` (`verticalActionName: Vertical`, jump/leap off) + `ClientNetworkTransform` + `NetworkObject`, and copy the persistence stack (`SaveableEntity` with a stamped `prefabId`, `Transform`/`Rigidbody`/`Mount`/`OrnithopterSaveable`).
4. **New rig.** Bone names are bound literally in `OrnithopterWingRig.Build`; export from the `.blend` with the armature, then re-run the builder. Rest poses are cached, so a re-proportioned model needs no animator edits.
5. **Register the prefab** in `DefaultNetworkPrefabs.asset` (Sync Network Prefabs) and verify on a real client — an unregistered craft is invisible to everyone but the host.
