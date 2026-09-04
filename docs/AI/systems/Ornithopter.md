---
system: Ornithopter
layer: vehicles
summary: Folded wing pack deployed mid-air; point-mass energy flight model, stalls, crash damage.
paths:
  - Assets/Game/Scripts/Vehicles/Ornithopter/
  - Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs
  - Assets/Game/Scripts/Items/Equipped/WingPackItem.cs
  - Assets/Game/Scripts/Vehicles/Ornithopter/Flight/FlightLaunch.cs
  - Assets/Game/Prefabs/agents/Vehicles/Aircraft/DuneOrnithopter.prefab
  - Assets/Game/Editor/Vehicles/WingPackBuilder.cs
  - "Assets/Game/Art/Models/_Source~/models/vehicles/ornithopter_worn.py"
symptoms:
  - "the wing pack refuses to launch and logs that there is no room"
  - "pulling back on the stick does not climb, or the craft drops like a brick"
  - "the stall never ends and the nose stays up"
  - "one wing opens while the other closes"
  - "the craft is invisible to everyone but the host after launching"
  - "a mid-air save reloads with the ornithopter falling out of the sky"
  - "a hard dive into a cliff does almost no damage while a gentle landing hurts"
  - "the craft grinds along a rock face forever instead of crashing"
  - "the wing pack looks like a toy lying on the backpack, far smaller than the rack it is strapped to"
  - "the wing pack is worn as a folded bundle rather than as wings"
  - "the worn wings hang beside the pack touching nothing"
  - "the worn wings' roots float off the pack's bar tips"
  - "the worn wings sweep through the ground when the player walks"
  - "the worn wing is five bare spars with the cloth scalloped between them"
  - "the worn wing is edge-on to the gear screen and reads as a sliver"
  - "one worn wing fans outward and the other fans inward"
  - "rebuilding the wing pack item makes it invisible to clients and stops it surviving a reload"
  - "the grappling hook draws its rope but pulls nothing while I am flying"
  - "firing the grappling hook from the cradle hooks the aircraft I am sitting in"
  - "a grapple swing's speed vanishes the moment the wings deploy"
  - "the craft is hauled into the cliff it was hooked to"
reads_with: [Vehicles, Multiplayer, Persistence, audio, Backpack, Wingsuit]
updated: 2026-09-04
---

# Ornithopter

A 10 m flapping-wing aircraft carried folded in the inventory, deployed in mid-air, flown prone from a cradle on a point-mass energy model.
**Scope:** [`Assets/Game/Scripts/Vehicles/Ornithopter/`](Assets/Game/Scripts/Vehicles/Ornithopter) (Flight/ + Wings/, own asmdef), [OrnithopterFlightMotor.cs](Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs) + [.Replication.cs](Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.Replication.cs), [WingPackItem.cs](Assets/Game/Scripts/Items/Equipped/WingPackItem.cs), [DuneOrnithopter.prefab](Assets/Game/Prefabs/agents/Vehicles/Aircraft/DuneOrnithopter.prefab), [WingPack.prefab](Assets/Game/Prefabs/Items/Equipment/WingPack.prefab).
**Related:** [Vehicles.md](Vehicles.md) · [MountSystem.md](MountSystem.md) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [Artifacts.md](Artifacts.md) · [Combat.md](Combat.md) · [audio.md](audio.md)

## Model

- Point mass on a flight path. **Two angles**: `Gamma` = where it is *moving*, `Pitch` = where it is *pointing*. `AngleOfAttack = Pitch - Gamma`, and AoA is what makes lift. Pulling the stick does not climb — it raises AoA, which curves gamma up a moment later.
- Per step ([`OrnithopterFlightModel.Step`](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightModel.cs)): spread/tuck → stamina & flap phase → attitude (rate × authority) → `cl`/`cd` → `speedRate = thrust − drag − g·sin γ + tow·p̂` → `γ̇ = (lift·cos roll − g·cos γ + tow·û) / v` → `turnRate = lift·sin roll / v + Turn·TailYawRate·authority + tow·r̂ / v`.
- **A rope is a fourth force, not a fourth system.** `Step` takes an optional world-space `towAcceleration`; `PathAxes` gives the flight path's own frame (`p̂` along, `û` perpendicular-up, `r̂` perpendicular-horizontal) and the pull is split across the same three rates lift and weight already feed. That is why a tow composes with the stall, the glide and the coordinated turn with no special case anywhere — and why the along-path share is **signed**: a hook set ahead adds speed, one set behind bleeds it.
- **The tow is priced in stamina**, the same reserve flapping spends (`TowStaminaDrainPerSecond`, and neither refills while either runs). Flapping and being hauled are the two ways energy enters the machine; a second source with no sink makes "hook something and hold on" strictly better than flying and turns the energy model into scenery. The pull fades out on the same `StaminaFadeBand` the beat does.
- `g = 9.81` is a **const in the model**, not `Physics.gravity`; the Rigidbody runs `useGravity = false`, damping 0. One source of weight.
- Lift curve is linear (`LiftSlopePerDegree·aoa`) to `StallAngle`, then lerps to `PostStallLiftFraction` of peak across `StallFadeAngle`. Post-stall lift **must stay > 0** or the nose never falls and the stall never ends.
- No throttle. Speed is bought with altitude or with flapping; flapping spends stamina (recovers only while gliding), and `FlapEffort = demand × clamp01(Stamina / StaminaFadeBand)` so exhaustion fades in. Thrust pulses on a half-cosine of `FlapPhase` (never negative).
- Control authority scales with `Airspeed / FullAuthoritySpeed` and is multiplied by `StalledAuthority` in a stall. Roll self-centres. Tucking (`Flap < 0`) sheds `TuckSpreadLoss` of wing area — less lift *and* less drag, which is how the dive gets fast.
- State (`OrnithopterFlightState`): Airspeed, Gamma, Pitch, Roll, Heading, TurnRate, FlapPhase, FlapEffort, WingSpread, Deployment, Stamina, Stalled. `Deployment` is the caller's ramp, not the model's.
- `StallSpeed(cfg)` and `VelocityOf(state)` are derived, never tuned. Heading is measured **+Z toward +X**. At shipped defaults: stall ≈ 11.3 m/s, glide ≈ 9:1.
- **The launch carries the pilot's whole velocity, direction included.** `WingPackItem.CarryFrom` reads speed, compass bearing and climb angle off one vector: the heading is where the pilot was *going* above `headingFromSpeed`, and where they were *looking* below it. `Launch(speed, heading, gamma)` then sets **`Pitch = Gamma`** — angle of attack zero — so the wings arrest the arrival instead of biting against it. A nose-level craft on a path climbing at 30° is at *minus* thirty degrees of attack and makes lift downward.
- **Worn it is WINGS, not a bundle.** `ornithopter_worn.fbx` is the aircraft with everything but the two wings and the two spoked shoulder assemblies culled — no fuselage, no boom, no tail, no cradle, no pylons — posed to hang down each side of the wearer off the **two ends of the expedition rig's lash rail**, which is the one part of the pack that does not fold in. The bundle (`wing_pack_folded.fbx`) is still what you see in the hand and on the mat. [`WornVisual`](Assets/Game/Scripts/Items/Equipped/WornVisual.cs) swaps them; see [BodyEquipment.md](BodyEquipment.md). The worn model is authored at true wearer scale with its origin ON the rail — its two shoulder pivots are the measured bar tips at x = ±0.885 m in the spine bone's frame — so `WornSeat`'s ordinary rail mount puts it exactly where it belongs and `WornFit.size` (5.51) is only there to catch a re-export that changed the scale. **Re-posed and enlarged 2026-09-04** — reach 1.85 → 2.775, `FLAP` −72 → −52, `SPLAY` −52 → −105 — so the wings are half again as long, carried outboard instead of down, and the fan is open enough that the web reads as one sail rather than as spars with cloth scalloped between them. Roots unmoved, because `ROOT_HALF` is the mount.
- **It is a back item.** `WingPack.asset` is `EquipKind.Back`: it lives in the body's Back slot, is worn on the spine where its `WornFit` says, and deploys on a **double tap of Space** through `BodyEquipmentController`'s back channel — the same `Use`/`Present` path as before, from a different button. In a hotbar slot it is inert. See [BodyEquipment.md](BodyEquipment.md). The **wingsuit** is the other back item and therefore its rival for the one torso slot: same button, same channel, and it flies the player's own body on this model with the thrust set to zero — see [Wingsuit.md](Wingsuit.md).
- **The flight model is now shared, and the launch arithmetic with it.** `FlightLaunch.CarryFrom` — what a pilot's motion is worth to a wing — was `WingPackItem.CarryFrom` until the wingsuit needed the identical answer; it moved here rather than being called across from one item to another. `WingPackItem.CarryFrom` remains as a thin forwarder, because the tests that pin the rule are written against the launcher.
- **Stowed, it is sized by the surface, not by the hand.** `WingPack.prefab` is the only item whose `ItemGrip.packSize` is *derived* rather than typed: `WingPackBuilder.PackSizeForRack` solves it from the rack's own width (9 cells) times a 0.96 fill, the folded mesh's short:long proportions and `PackScale.Factor`. It comes to **1.824**, drawing the craft 1.17 x 2.74 m on the mat — a 9 x 21 shape that fills the rack edge to edge and hangs 0.76 m off each end, so it only goes on roughly centred. `holdSize` stays 1.26; the hand is unaffected. See [Backpack.md](Backpack.md) for what a derived shape and an overhang clamp mean.
- Control mapping lives in `OrnithopterFlightMotor.ApplyRiderInput`, **not** in `SteerModule`: `Move.y → Pitch`, `Move.x → Roll` (and `Turn` when the turn axis is idle), `Vertical → Flap` (Space beats, LeftCtrl tucks), Escape dismounts via [MountModule](Assets/Game/Scripts/agents/Modules/Riding/MountModule.cs). Jump and leap are disabled on the prefab.

## Key types

| Type | File | Role |
|---|---|---|
| `OrnithopterFlightModel` | [Flight/OrnithopterFlightModel.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightModel.cs) | Static, pure: `Step`, `LiftCoefficient`, `VelocityOf`, `StallSpeed`. No MonoBehaviour, Transform or clock. |
| `OrnithopterFlightState` / `OrnithopterFlightInput` | [Flight/OrnithopterFlightState.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightState.cs) | What carries between steps; input axes are all −1..1. `Launch(speed, heading, gamma = 0)` starts on the given path with **pitch equal to gamma**, wings folded. |
| `OrnithopterFlightConfig` | [Flight/OrnithopterFlightConfig.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightConfig.cs) | Every tunable, serialized on the motor. |
| `OrnithopterCrash` / `OrnithopterCrashConfig` / `OrnithopterTouchdown` | [Flight/OrnithopterCrash.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterCrash.cs) | `ClosingSpeed(v, n)`, `ImpactDamage(v, cfg)`, and how a flight ended. |
| `IOrnithopterFlightState` | [Flight/IOrnithopterFlightState.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/IOrnithopterFlightState.cs) | Seam between the asmdef and Assembly-CSharp; also the test-double surface. |
| `ITowable` | [Motors/ITowable.cs](Assets/Game/Scripts/agents/AI/Motors/ITowable.cs) | `TowAttachPoint` + `RequestTow(anchor)`. The rope channel, parallel to `IRiderControllable`. Implemented by the motor; the hook only supplies the far end. |
| `WingPackItem.LaunchCarry` / `CarryFrom` | [Items/Equipped/WingPackItem.cs](Assets/Game/Scripts/Items/Equipped/WingPackItem.cs) | Pure static: velocity + facing → heading, speed, climb. Checkable without a scene, like `LaunchPosition`. |
| `OrnithopterWingRig` | [Wings/OrnithopterWingRig.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Wings/OrnithopterWingRig.cs) | Binds bones by exact name (throws naming the miss), caches the rest pose, holds each wing's `Sign`. |
| `OrnithopterWingAnimator` | [Wings/OrnithopterWingAnimator.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Wings/OrnithopterWingAnimator.cs) | Poses the rig as deltas on rest. `Tick(dt)` is public so tests/editor can drive it. |
| `OrnithopterAudio` | [Wings/OrnithopterAudio.cs](Assets/Game/Scripts/Vehicles/Ornithopter/Wings/OrnithopterAudio.cs) | Wind loop, per-stroke flap, stall warning, deploy/fold — off the same seam. |
| `OrnithopterFlightMotor` (partial ×2) | [Motors/](Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs) + [.Replication.cs](Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.Replication.cs) | Owns the Rigidbody, rider input, touchdowns, the tow, and the wire. `IMovementMotor`, `IRiderControllable`, `IOrnithopterFlightState`, `IExternallyPosed`, `ITeleportAware`, `ITowable`. |
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
| `TowAcceleration` / `TowStaminaDrainPerSecond` | 14 m/s² / 0.22 | The rope's pull and its price. Compare `FlapThrust` 9: a tow should out-pull the wings or there is no reason to throw one. 0.22/s ≈ 4.5 s of tow from full. **Zero drain flies, and makes hooking beat flying.** |
| Motor: `spreadDuration` / `launchAirspeed` | 0.6 s / 12 m/s | Wings-open ramp; floor on launch speed (a run-up keeps more). |
| Motor: `maxLaunchClimb` / `maxLaunchDive` | 45° / 12° | What the airframe accepts of the pilot's arrival path. **Asymmetric on purpose:** a slingshot enters climbing, while the ordinary step off a ledge is already falling and taking that literally would deploy the craft pointing at the ground. |
| Motor: `towReleaseDistance` | 12 m | Where the tow lets go. Far wider than the hook's own 2.5 m arrival: the craft is 10 m across and does not *arrive* at a rock face at 25 m/s. |
| Pack: `headingFromSpeed` | 4 m/s | Above it the launch heading is the pilot's velocity, below it their facing. |
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

1. **Take off.** `CanUse` → `HasLaunchRoom`: already falling (no ground within 0.6 m), **or** no ground within 6 m from a point 1.5 m ahead — the forward ray is what makes a cliff *edge* work. Refusal logs, deliberately. `OnRequestUse` (owner) runs `CarryFrom` and stamps heading into `arg.R`, speed into `arg.P.x` and climb into `arg.P.y`; server `Use()` → `SpawnAndMountCraft`: pose from `LaunchPosition(prefab, …)` (seat marker measured off the **prefab**, before spawning) → `World.Spawn` with the **pilot as owner** → `MountNetworkSync.ServerMount` → `NetworkLaunch` → held pack renderers hidden.
2. **Cruise.** `SteerModule` → `ApplyRiderInput` latches input on the render loop; `FixedUpdate` advances `Deployment`, runs `Step`, `ApplyPose` (velocity set outright, attitude `Quaternion.Euler(-Pitch, Heading, -Roll)` via `MoveRotation`), then `CheckForLanding`.
3. **Tow.** The pilot fires the hook while flying. `GrapplingHookArtifact.FixedUpdate` sees its own body is kinematic — which is what mounting does — resolves `owner.GetComponentInParent<ITowable>()` once and calls `RequestTow(anchor)` every step instead of running the capsule constraint. The motor latches the pull, `FixedUpdate` consumes it into one `Step` and clears it, so a rope that stops asking stops towing. `RequestTow` returns false — and the hook drops the rope, announcing it — on arrival inside `towReleaseDistance`, on exhausted stamina, on landing or dismount, and on `ExternallyPosed`. The hook adds one rule of its own: further from the anchor than `maxRange` means the craft has flown off the end of its own cable. A **swing already in progress survives the launch** and becomes the tow, because nothing tears the rope down in between.
4. **Stall.** AoA > `StallAngle` → `Stalled`, lift fades, authority ×0.3; the nose drops, gamma steepens, AoA falls back and the wing flies again. No script, no special case.
5. **Land.** Downward probe finds ground within 1.4 m after the grace window → `ReportTouchdown(wasImpact: false)`. A wing flown onto sand *lands*; it never waits for a collision.
6. **Crash.** `OnCollisionEnter` (contact 0) → same `ReportTouchdown(true)` — a cliff face is never under the craft, so without it the machine grinds along the rock forever.
7. **Both endings** funnel through `ReportTouchdown`: velocity from the **flight state**, `ClosingSpeed = max(0, dot(−v, n))`, `ResolveGroundPosition` (step out along the normal, ray down, ignore own children) → `EndFlight` → `PublishTouchdown` → `Landed` → `WingPackItem.HandleLanded`: price first, `ReleaseCraft(dismountFirst: authoritative, standAt)`, `NetDamage.Apply` last.
8. **Damage** is `t = (v² − safe²)/(lethal² − safe²)` × `MaxDamage`, floored at 1 past `safe`. Glide onto sand 20 m/s at −6° → 2.1 m/s → 0. Level into a cliff at 20 → 35. Held dive 42 m/s at −60° → 36.4 → 100. Wingtip scraped along a wall → 0.

## Multiplayer

- Server **spawns**, then hands ownership to the pilot; the prefab carries [`ClientNetworkTransform`](Assets/Game/Scripts/Core/Multiplayer/Authority/ClientNetworkTransform.cs), so the **pilot's machine is the one that simulates and its pose is the truth**. Stick and wings sit on one machine with no round trip.
- `NetMsg.CraftLaunch` goes `NetToAll` (idempotent — a craft already flying is left alone); `NetMsg.CraftDown` goes `NetToServer` from the pilot only (`IsFromThePilot` checks `OwnerClientId`; the server is waved through, which is also the offline path). Speeds ride `NetArg.A` as cm/s. On `CraftLaunch` **`B` is the launch climb in centi-degrees**; on `CraftDown` `B` is `wasImpact` and `P` the resolved ground spot.
- **The tow adds no messages.** The rope already replicates through `GrappleVerb`, and the craft's pose already replicates through `ClientNetworkTransform` — so the one machine that owns both is the one machine that resolves the pull, and every peer sees the result as motion. `RequestTow` refuses outright when `ExternallyPosed`, and the hook's own `OwnsMovement()` gate stops a peer asking in the first place: two guards, because a second machine towing its own copy would be a second authority on a craft whose pose arrives over the wire.
- Non-owners get `ExternallyPosed = true` from `NetAuthority`, but the motor **stays enabled**: `Update` *measures* airspeed, attitude and gamma off the replicated transform and fakes `FlapEffort = clamp01(0.15 + vy·0.25)`. Running the real model there would produce a second, divergent flight.
- **Known limit:** `CraftLaunch` is an event, so a late joiner draws an airborne craft with its wings shut until it lands. Cosmetic only; closing it needs a `NetworkVariable`.
- Registered in [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset) (`GlobalObjectIdHash 3255795817`).

## Persistence

- Craft: `SaveableEntity` with a stamped `prefabId` (`2b557799…`, scope World) + `TransformSaveable`, `RigidbodySaveable`, `MountSaveable`, `OrnithopterSaveable`.
- `OrnithopterSaveable` stores `{ flying, airspeed }` under key `ornithopter` and re-`Launch`es in the **deferred** pass (`OnLoadComplete`) — a mid-air save otherwise reloads with the flight model off and the craft drops out of the sky. Stamina and flap phase restart; both are unnoticeable.
- Pack: `WingPackItem.CaptureItemState` stores a `SaveRef` to the craft under `craft`; `IItemDeferredRestore.TryCompleteRestore` → `AdoptCraft`. The other direction is `OrnithopterSaveable` subscribing `MountModule.Mounted` and handing the craft to the rider's pack. Both are idempotent and both run on the ordinary launch too, so there is one path.

## Gotchas

- **A hook fired from the cradle aims straight into the craft's own nose.** `MountModule` tells the solver to ignore rider↔mount *collisions*, but a raycast is a query and queries do not care — so `AimProvider`'s ray leaves the pilot's head and lands on `COL_Nose` about a metre later, every time. `GrapplingHookArtifact.OnRequestUse` rejects any hit under the ridden `MountModule`'s transform. Anything else that aims from a seat needs the same guard.
- **A mounted player's Rigidbody is kinematic, so writing its velocity does nothing at all.** That is the whole reason `ITowable` exists rather than the hook simply pulling harder: the rope drew, the dart set, and no force reached anything. The symptom is silent — no error, no warning, just an item that appears not to work.
- **The tow is a pull, never a constraint.** The player's swing is a hard velocity projection onto a sphere. Imposing that on a craft that carries `Gamma`/`Heading` as *state* rather than deriving them from its Rigidbody puts two authorities on one machine and makes it jerk between what the model flew and what the rope insisted on. If a genuine pendulum is ever wanted, the flight model has to absorb an imposed velocity, not have one written over it.
- **A tow is exempt from the hook's hold timeout, and must be.** `GrapplingHookArtifact` does not override `WantsHold`, so `UseChannel.Release` ends its hold stream the instant the trigger comes up; `Update`'s `holdTimeout` safety net then starts counting on a rope that is still pulling. A tow ends when the *vehicle* says so, never on a stream that has legitimately stopped. Only the owner holds a tow, so peers keep the full net.
- **`RequestTow` is latched, not applied.** The motor runs at `[DefaultExecutionOrder(-100)]`, ahead of the item, so a request lands one physics step later. Harmless at 50 Hz, and the reason the tow is order-independent — but do not "fix" it by applying the pull inside `RequestTow`, which would write a body mid-frame from the item's clock.
- **Re-seat the rope length when a tow hands back to a swing.** `_ropeLength` was measured where the dart bit; a tow has since carried the far end hundreds of metres. Resuming the constraint on that stale length hauls the dismounted pilot onto a sphere they left a flight ago — `ReclaimRopeFromTow` exists for exactly this.
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
- **The worn pack is drawn at 5.51 m across and untilted** (`WornFit.size` and `localEuler` on `WingPack.prefab`). That is three times the wearer's height and is meant to be: it is a pair of wings, and the gear screen is the only place a player ever sees their own back, so the model *is* the interface there (`GDC-L1-UX-0003`, objective/4). `size` is the longest axis in metres, measured by `WornSeat` off whatever renderers are switched on, **after** `WornVisual` has swapped the models — so it is the wings that are measured, not the bundle.
- **`WornFit.size` is a measurement, never a size dial — growing the wings with it breaks both ends at once.** It is a uniform scale about the rail, so raising it moves the two shoulder pivots off the bar tips they are measured onto (0.885 m outboard each at 2×) *and* drops the tips through the ground, which `ornithopter_worn.py`'s reach is bounded to avoid — the rail sits 0.63 m above the spine and the soles 1.45 m below it, so the ground is 2.08 m under the mount and that is the entire budget. Both failures shipped on 2026-09-04 and were visible in one session. A bigger worn wing is a `TARGET_REACH` change in the `.blend` followed by a pose sweep, and then this number is re-pinned to whatever the exporter prints.
- **`SPLAY`'s magnitude IS the fan's opening angle**, because the digits are graded across `SPLAY * (k - 0.30)` — so the total swing is exactly `SPLAY`. It is the cheapest knob in the file: opening it from −52 to −105 turned five bare spars with cloth scalloped between them into one continuous sail and stopped the spar tips protruding past the trailing edge, with no geometry touched. `ROLL` and `TWIST` decide how square that sail is to a head-on camera and are worth re-rendering rather than re-deriving — at `ROLL` 26 / `TWIST` 8 the panel turned nearly edge-on and the fabric collapsed to a sliver.
- **`WingPackBuilder` writes the `WornFit` now.** It used to be YAML-edited into the prefab, so a rebuild stripped it along with the four components below and the pack fell back to the spine bone's origin at prefab scale. It went into the builder when the worn wings did: the fit and the model whose size it pins have to be written by the same hand.
- **`WingPackBuilder` used to be lossy, and the shape of that trap is worth keeping in mind.** It builds a fresh `GameObject` and `SaveAsPrefabAsset`s over the path, so anything it does not add is silently stripped on every run. `NetworkObject`, `PickupableItem`, `SaveableEntity` and `TransformSaveable` had all been added to `WingPack.prefab` by hand after the builder was last run, and a rebuild took all four away — the pack stopped replicating, stopped being pickable and stopped surviving a reload, with no error anywhere. All four are in the builder as of 2026-09-03, along with `ItemWorldPresence.Apply` for the body, collider and netcode, so a re-run is safe. The rule stands for anything added next: author it in the builder, not in the Inspector.
- **The wing pack's stowed size is a ceiling, not a taste.** Past the rack's 9 columns the derived shape rounds to 10, which the rack refuses outright *and* the ship's gear wall — strict on both axes at 30 x 22 — cannot take either, so the craft becomes unstorable everywhere with nothing but red cells to say why. `WingPackStowTests` pins both ends because the inputs (the rack's cut, `PackScale.Factor`, a re-export of `wing_pack_folded.fbx`) all move without anyone re-running the builder.
- Tests: `OrnithopterFlightModelTests` (32 — 10 cover the tow and the launch path), `OrnithopterCrashTests` (4) in [Tests/EditMode](Assets/Game/Tests/EditMode); `OrnithopterWingAnimatorTests` (10), `OrnithopterRigWiringTests` (10), `WingPackLaunchTests` (10) in [Editor/Tests](Assets/Game/Editor/Tests); `WingPackStowTests` (2) in [Tests/Editor](Assets/Game/Tests/Editor). Run via `HeadlessTestRunner.RunEditMode("<fixture>")` → `Temp/headless_tests.txt`.

## Extending

1. **Retune the flight model.** Edit `OrnithopterFlightConfig` on the prefab's motor only — the model file holds no constants. After touching `Mass`, `WingArea`, `AirDensity`, `LiftSlopePerDegree` or `StallAngle`, read `OrnithopterFlightMotor.StallSpeed` back rather than assuming, and re-run `OrnithopterFlightModelTests`.
2. **Change how it looks, not how it flies:** the animator fields. Anything at beat frequency stays unfiltered; only add smoothing to a channel driven by the stick.
3. **Make another vehicle towable.** Implement `ITowable` on its motor: return the rope's attach point, and answer `RequestTow` with whatever ending conditions that machine has. The hook needs no edit at all — it finds the interface up the rider's hierarchy. A vehicle that should *not* be towable simply does not implement it, and a rope thrown from its seat hangs slack.
4. **New aircraft, same physics.** Implement `IOrnithopterFlightState` (or reuse the motor), give the prefab `MountModule` + `MountNetworkSync` + `SteerModule` (`verticalActionName: Vertical`, jump/leap off) + `ClientNetworkTransform` + `NetworkObject`, and copy the persistence stack (`SaveableEntity` with a stamped `prefabId`, `Transform`/`Rigidbody`/`Mount`/`OrnithopterSaveable`).
5. **Change the worn wings.** They are a *derivation*, not a model: `_Source~/models/vehicles/ornithopter_worn.py` opens `dune_ornithopter.blend` (never writing to it), culls, poses the rig and bakes. Iterate with `--preview <png> --view front|side|iso`, commit with `--commit` (it refuses to overwrite), then re-run the export and `Tools ▸ Vehicles ▸ Build Wing Pack Item`. Look at the result on a body with *Tools ▸ SpaceGame ▸ Items ▸ Preview Worn Gear*. Reasoning and the measured wearer numbers are in `ornithopter_worn_BUILD.md`.
6. **New rig.** Bone names are bound literally in `OrnithopterWingRig.Build`; export from the `.blend` with the armature, then re-run the builder. Rest poses are cached, so a re-proportioned model needs no animator edits.
7. **Register the prefab** in `DefaultNetworkPrefabs.asset` (Sync Network Prefabs) and verify on a real client — an unregistered craft is invisible to everyone but the host.
