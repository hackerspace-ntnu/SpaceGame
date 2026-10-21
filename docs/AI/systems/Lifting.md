---
system: Lifting
layer: characters
summary: "Both hands on a heavy load: a prop lifted by one end and carried, or a tied captive over the shoulder"
paths:
  - Assets/Game/Scripts/World/Lifting/
  - Assets/Game/Scripts/Items/Artifacts/Lasso/CaptiveHaul.cs
  - Assets/Game/Editor/World/OxygenPlantRecoveryAuthoring.cs
  - Assets/Game/Editor/Tests/LiftCarryTests.cs
  - Assets/Game/Prefabs/Agents/Vehicles/Spacecraft/LooseOxygenPlant.prefab
  - Assets/Game/Art/Animations/Humanoid/Lifting Object.fbx
symptoms:
  - "the dragging is awful: the oxygen plant trails behind me very slowly"
  - "the lifted end floats off my hands, lags behind them or rubber-bands when I turn"
  - "the far end of the plant sinks into the sand or a ramp"
  - "Esc does not put the plant down, or closing the chat box drops it"
  - "the plant reloads hanging in the air where I was carrying it"
  - "a second player cannot take the other end of the plant"
  - "the carried plant shoves crewmates or the ship about"
  - "a hogtied person cannot be picked up and carried"
  - "the carried person's legs stick straight up in front of my face"
  - "the carried person is a stiff plank on my shoulder, not bent over it"
  - "the carried person's tied arms stick up in the air"
  - "putting a captive down is too quick, they should be thrown off the shoulder"
  - "a carried Nomad's cape fills the whole screen"
  - "a carried creature sits on one shoulder lengthwise and blocks half the view"
  - "the backpack shows in first person again after putting a captive down"
  - "carrying somebody over my shoulder I can't see their feet, only my own shoulder pad"
  - "the carried person's legs vanish from my first-person view when I pick them up looking down"
  - "the carried person's feet are splayed apart instead of tied together"
  - "a carried Nomad's boots stand up in front of my eye"
reads_with: [Pushables, Oxygen, Lasso, HumanoidAnimation, PlayerCharacter, Multiplayer, Persistence]
updated: 2026-10-13
---

# Lifting

Replaces the drag (`Haulable`, deleted 2026-10-06), which slid the oxygen plant after its haulers from the server at 1.6 m/s
and read as slow and laggy. Now the player presses interact at the load's handle, squats and heaves the near end up to the
waist, and walks with it at three quarters of a walk; the far end stays on the sand and slides after them. Esc or interact
puts it down. Built for any heavy runtime-spawned load; today's only one is `LooseOxygenPlant.prefab` ([Oxygen.md](Oxygen.md)).

**Any `ILiftLoad` (2026-10-13).** The carry is split from what is carried. [`LiftCarrier`](Assets/Game/Scripts/World/Lifting/LiftCarrier.cs)
(the body: phases, timing, arms, carry point) holds an [`ILiftLoad`](Assets/Game/Scripts/World/Lifting/ILiftLoad.cs), and
[`LiftHands`](Assets/Game/Scripts/World/Lifting/LiftHands.cs) is the carrier's own machine for every load (hotbar put away,
interact anywhere / Esc / a drawn item / death lets go, the pace). Two loads: `Liftable` (a rigid prop, carried in front) and
[`CaptiveHaul`](Assets/Game/Scripts/Items/Artifacts/Lasso/CaptiveHaul.cs) — somebody tied up by the lasso, over the shoulder.

## Model

| Idea | Mechanism |
| --- | --- |
| The pose is derived, never sent | [`LiftCarrier`](Assets/Game/Scripts/World/Lifting/LiftCarrier.cs), added at runtime to the carrier's body on **every** machine (`DefaultExecutionOrder(200)`, `LateUpdate`), poses the load from that machine's copy of the body. On the carrier's own machine that is the body its input moved this frame, so the grips are on the hands with zero lag; others see it from the interpolated owner-authoritative body. The `Pushable` cart rule, for the same reason |
| Who carries it, where it rests | [`Liftable`](Assets/Game/Scripts/World/Lifting/Liftable.cs): one server-written `NetworkVariable<LiftState>` (`Held`, `Carrier` = body `NetworkObjectId`, `RestPosition`, `RestRotation`), read on spawn. **No `NetworkTransform`** on the load: a replicated pose would fight the derived one |
| The solver | [`LiftPoseSolver`](Assets/Game/Scripts/World/Lifting/LiftPoseSolver.cs), pure: grip midpoint ON the hands, axis along the swung heading, pitched down until the first of 7 underside samples (heel→foot) meets the ground (`PitchToTouch`, closed form), max 75°. So a slope or a crest is followed, never entered. `Rest` lays it on its underside along the ground's slope; `Swing` turns the heading toward the body's at `swingRate` (5/s, exponential), never more than `maxSwingLag` (25°) behind, so the far end trails round a turn |
| The hands | the carry point is fixed to the body: `handsBelowShoulder` 0.8 arm-lengths below the shoulders' middle and as far ahead as `armReach` 0.92 allows (1.3 m up — just above the hips — and ahead of the chest on the 3 m player; 0.98 left the far arm 7 cm short of its grip in a 360°/s turn). `ReachingArm` (shared with carts; `ReachingArm.Of` builds one per arm) turns shoulder and elbow so each palm lands on its grip |
| Shape | four markers on the load, measured in its own frame: `Lift_Grip_L/R` on a handle bar, `Lift_Heel` under them, `Lift_Foot` under the far end (`LiftShape`) |
| Pace | the carrier's own machine calls `PlayerMovement.StartHauling(WalkSpeed × carrySpeedFraction)` — 0.75 × 6 = 4.5 m/s, sprint (20) and jump/dash blocked by the cap — and `StartHauling(0)` (clamped to 0.1) while the lift or set-down plays |
| A ghost while carried | `SetHeldBy(carrier)` switches the load's colliders off (only those that were on, restored after), so it cannot shove a crewmate, snag the ramp lip or push the ship's dynamic hull, **and the carrier's crosshair passes through it** to the dock it is aimed at. A trigger was tried first and blocked the aim: a trigger answers the interaction ray when the interactable is on its own object, which the `Liftable` is |
| One carrier | a second pair of hands would pose one load against two bodies, and neither set of hands could stay glued to it. Decided 2026-10-06; see Extending |
| The load decides how it follows | `ILiftLoad.Hold(LiftFrame)` is handed the grip, heading, carry point, the from/rest blends and `Raised` (0 on the ground → 1 carried, from the carrier's own phase clock) each frame. `Liftable` solves and places itself; `CaptiveHaul` places the captive's root between the ground and the shoulder |

### A captive, over the shoulder (`CaptiveHaul`)

| Idea | Mechanism |
| --- | --- |
| Why not a `Liftable` | a captive is an NPC or a player already spawned, so no `NetworkBehaviour`/`NetworkVariable` can be added, and it is an agent whose body is posed by `TiedPose`, not a rigid prop |
| Who holds it | the server's, on the CAPTIVE's channel: `NetMsg.CaptiveHaulRequest` (140, A = 1 take / 0 let go, Target = carrier) → `CaptiveHauled` (141, absolute, re-sent to a joiner). Refused while somebody else holds them, while a lasso rope `Binds` them, out of `gripReach` (3.5 m flat to the knot), if taller than `maxHeightShare` (1.25) × the carrier (`FitsOnShoulderOf`), or by `LiftHands.MayTakeHold` |
| Where the body goes | the ROOT is placed by every machine from its own copy of the carrier in `ILiftLoad.Hold` (the carrier's LateUpdate, before `BoundBody` poses it): lerped from where it lay (`LiftFrame.From`) to the shoulder by `LiftFrame.Raised`. Humanoid: `TiedPose.CarryAnchor` (its standing hips) ON TOP of the right shoulder pad — the shoulder joint + `shoulderLift` (0.41 m) up, `shoulderOut` (0.21 m) out and `shoulderBehind` (0.08 m) back, i.e. beside the helmet and just behind the eye — and the root turned `shoulderTurn` (20°) across the chest, legs towards the middle. No avatar: its middle across both shoulders, `AcrossHalfDepth` behind the neck, root turned by `AcrossTurn` so the LONG axis is across |
| The pose | `TiedPose` blends the hogtie into a measured key (`Shouldered` 0→1), FOLDED over the shoulder: face down turned end for end, pelvis pitched `shoulderHang` 50° head-down, back curled (`shoulderBack` −0.5) so the trunk hangs down the carrier's back, the bound legs out in front in a hogtie — knees bent about a right angle (`shoulderKnees` −0.1), feet pointed soles-up (`shoulderFeet` 0.8 'Foot Up-Down') — and tied arms hanging towards the head (`shoulderArmsDownUp` 1, `shoulderArmsFrontBack` 0, `shoulderForearms` −0.4). Two leg muscles are SOLVED per body as the tie lands (`FitCarriedLegs`, halving): 'Upper Leg In-Out' until the ankles are `shoulderAnkleGap` (0.6) × the hip-joint width apart, then 'Upper Leg Front-Back' until the ankles lie `shoulderFeetDrop` (8°) below the hips. The carrier sees the feet in the lower right looking straight ahead and 20° down (`LiftCarryTests.LooksAtTheFeet`): Raxy's ankles 0.16 m under the eye and 0.5 m out, a Nomad's 0.2 m under and 0.85 m out |
| Out of physics | `BoundBody.BeginCarry`: kinematic where owned, interpolation off everywhere, the lying collider away, every live collider pair with the carrier ignored; `EndCarry` gives all of it back (also called by the tie ending) |
| The eye | once up (the frame after `Raised` reaches 1), the carrier's own machine bakes the captive's meshes and hides from that player's camera alone every renderer with a vertex above `eyeLine` (0.5) in view, measured with the camera turned to look STRAIGHT AHEAD for the measure (then put back) — `PlayerLook.HideFromEye(this, …)`, given back on release. Catches a cape or a spear standing in front of the eye; never the legs |
| The hands | right on the thighs (`nearGrip` 0.3 hips→knees), left towards the knees (`farGrip` 0.8); a creature: one over each shoulder |
| Lets go | the carrier THROWS it off (`LetGoLocal` → `ThrowOff`: the set-down clip plays, the root is tossed over `throwSeconds` (2.2 s) along the lowering curve plus a `throwArc` (0.5 m) parabola onto `Beside` — `restSide` 1.1 m right and `throwAhead` 0.8 m ahead, on the first ground a ray finds, along the carrier's heading — while the pose unfolds back to the hogtie; request A = 0), the server judging the carrier dead/gone/past `releaseDistance` (6 m), or the ropes coming off (every machine, at once) |
| Entry point | `TiedCaptive.Interact` → `CaptiveHaul.Toggle` (a lead on the presser's own lasso is let go first) |

## Key types

| Type | Role |
| --- | --- |
| `Liftable` | the load: `IInteractable` ("RMB: lift it by the handle" / "RMB / Esc: put down"), `IInteractionMoment` (None: the body plays the lift), `ISavedPose`, server judge (carrier gone/dead/`releaseDistance` 8 m → put down; destinations), an `ILiftLoad` posed rigidly from the hands (`Hold` runs the solver and the ground probes) |
| `LiftCarrier` | the body's half: phases Lifting → Carrying → Lowering; `Lift(ILiftLoad, seconds, alreadyDone)`, `Lower(rest, seconds)`, `RestFromHere`, `Drop`, `CarryPoint`, `Shoulder(right)`; `BodyLanguage.OccupyArms(Both)` |
| `ILiftLoad` / `LiftFrame` | what a carrier can hold: `CanBeHeld`, swing tuning, `GripOf(pose)`, `Grips`, `SetHeldBy`, `Hold(frame)`, `RestFromHere` |
| `LiftHands` | the carrier's own machine, any load: `Take`/`Release`/`Tick`, `SettleEndsAt`; statics `PutsDown`, `MayTakeHold`, `Play`, `SecondsOf` |
| `CaptiveHaul` / `CaptiveHaulSettings` | a lasso-tied body as a load over the shoulder, added by `BoundBody.EnsureOn`; settings on `Lasso.prefab` (`haul`). `FitsOnShoulderOf`, `HiddenFromCarrierEye` (tests) |
| `ILiftDestination` / `LiftDestinations.Reached` | where a load is carried to (the oxygen mount); `Reached` measures the CARRIER, flat, against `Radius` (5 m: the load is 3.8 m long and out in front) |
| [`LiftDock`](Assets/Game/Scripts/World/Lifting/LiftDock.cs) | the destination as something to aim at: a trigger volume (`PlantDock`, 2.4 x 3.9 x 2.4 m at the fixture's 1.7x — narrower would be hard to hit while carrying, wider reaches the transmitter cradle beside it) whose prompt is "RMB: set the oxygen plant in its mount" while you carry the load it `Takes`, else "Oxygen plant missing: find it outside" (an empty-handed press beeps `InteractDenied` locally) |
| `NetMsg.LiftRequest` (130) | carrier → server on the load's channel: A=1 lift, A=0 put down with P/R = the rest pose, A=2 set it into the destination in reach |
| `OxygenPlantRecoveryAuthoring` | *Tools ▸ SpaceGame ▸ Oxygen ▸ Author Liftable Loose Plant*: markers, handle, `Liftable` wiring, removes the `NetworkTransform`, body not interpolated; read back |

## Flows

- **Lift.** Interact within `gripReach` (2.5 m, flat) of the grips → `MayLift` (alive, not seated, no cart, humanoid arms) → `LiftRequest A=1` → server re-checks, writes `Held` → every machine `Apply` → `LiftCarrier.Lift(load, liftSeconds)`. The owner also plays **Heave Load Up**, deselects the hotbar, subscribes interact/slot events. Lift timing is the action's own length (`CharacterAction.Seconds` at the body's seeded speed), the same on every machine. Before `gripFraction` (0.45) the arms reach down to the resting grips; after it the near end follows the animated hands, blending to the carry point.
- **Put down.** Esc (only while `GameplayMenuScope.AcceptsGameplayInput`), interact (not on the granting frame), a drawn hotbar item or death → the owner computes `RestFromHere`, plays **Set Load Down**, lowers at once and sends `A=0` with the pose → server accepts it unless it is > `restTolerance` (2 m) from its own → `Held=false` → every other machine lowers onto that rest. The rule is `Liftable.PutsDown`.
- **Set it in (2026-10-06, replaced the 3 m auto-snap).** Carrying, aim at the empty mount → `LiftDock.Interact` → `Liftable.RequestPlace` → `A=2` → server: `IsCarriedBy(sender)`, then the first destination that `Accepts` and whose `Point` the carrier is `Reached` from takes it (`Receive` → despawn). The same interact press does not also put the load down: `OnLocalInteractPressed` skips when the carrier's `Interactor.HoveredInteractable` is a `LiftDock` that `Takes` it. The carrier's `LiftCarrier` drops it in `OnDisable`.

## Multiplayer

- **Owner-authoritative motion, server-authoritative possession.** Matches the player's own `NetworkTransform` (owner) and the cart's derived pose; the server never moves the load. A late joiner reads `LiftState` with the spawn and, finding it held, lifts with `alreadyDone = 1` (straight to carrying).
- Animation: the player's `ClientNetworkAnimator` replays the owner's Full-slot lift for everyone; non-owners only read its length.
- **Not run on a client.** The user played the carry on the host on 2026-10-06 ("working perfectly"). EditMode, 2026-10-06, by direct call (real player rig, real plant): `LiftCarryTests` 10/10, `OxygenPlantRecoveryTests` 12/12, `SolderingTorchTests` 4/4. The first run's failures were the rig fixture (root at the feet), a too-tight swing bound, the far fist off its grip in a 360°/s turn (`armReach` 0.98 → 0.92) and in a 400°/s spin (`maxSwingLag`), and a save mid-carry that asked the wrong field (`SetHeldBy`).

## Persistence

The load is its own runtime entity (`SaveableEntity` + `TransformSaveable`, prefab id `07ddab4c…`). `ISavedPose` answers the
**rest pose** while carried, so a save taken mid-carry reloads the load lying where it was carried, never hanging from hands
that are gone. On spawn the server publishes its transform as the rest. Carrying itself is not saved.

## Gotchas

- **The heave runs on the carrier's clock (`LiftFrame.Raised`), never a second one.** A `Time.time` clock of the load's own reads 0 in EditMode, so tests saw a body that never left the ground, and a late joiner (`alreadyDone`) would replay the heave.
- **A real fold needs the BACK bent too, not just the hips.** Unity's hip flexion stops near 90°, so with the trunk straight the legs only reach level — the first carried pose was a plank with the legs out in front of the face. The fold comes from pitching the pelvis (`shoulderHang`), curling the back (negative `shoulderBack`) and folding the thighs. Belly down, a bent knee lifts the shin UP: that is the hogtie's look, wanted, so long as the feet stay below the eye and half a metre out (`LiftCarryTests.ATiedPersonIsFoldedOverTheShoulderWithTheFeetInView`).
- **The shoulder JOINT is not where a body rests: the astronaut's pad stands 0.3 m above it and the helmet beside it** (pad top 1.27 m above the root at x 0.48; helmet x −0.49…0.34, y 1.0…1.8, eye at 1.45). With the hips at the joint + 0.22 m, the captive sat inside the pad and the helmet column, the legs hung 72° below the eye, and first person showed only the carrier's own shoulder pad. Measured by baking the carrier's meshes into a per-5 cm profile; the seat is now on top of the pad beside the helmet.
- **The same muscle values give each rig different legs.** Raxy's ankles landed 8 cm under its hips, a Nomad's boots 8 cm OVER them, at eye height; 'Upper Leg In-Out' 0 splays both, by different amounts (Raxy 0.42 m, Nomad 0.33 m). So the carried legs are solved per body (`FitCarriedLegs`), not authored as muscles.
- **Feet straight ahead of the hips ride off the right-hand edge of the view.** The hips sit 0.42 m right of the eye; with the legs pointing straight forward the feet were at viewport x 1.2. `shoulderTurn` turns the body across the chest.
- **The eye-line hide must be measured looking straight ahead.** It ran at whatever pitch the camera had when the body came up — looking down at the captive just picked up — so the raised feet stood above the line and the WHOLE body mesh (feet are in it) was hidden from the carrier: "I can't see anything I'm carrying". `ClearEye` turns the camera level for the measure.
- **Upside down, the hogtie's arms stand straight up past the hips** — they reach for ankles that are now on the other side of the shoulder. The carried key has its own arm muscles.
- **Skinned cloth follows the spine, so a carried body's cape stands straight up past its hips** and through the carrier's eye. It is hidden from that one camera by measurement (`eyeLine`), not by name: renderers authored `ShadowCastingMode` other than `On` are never registered, because the register hands back `On`.
- **A creature's Animator can sit on its ROOT** (DuneRat), and then `TiedPose` has no model to turn — it was never tipped lying down either. The across-the-shoulders turn is therefore on the root (`AcrossTurn`), which the carry owns, and measured from the body's extent, because the rat's long axis is not the root's forward.
- **`PlayerLook`'s first-person register is keyed by owner** (`HideFromEye(owner, …)`). It used to be one slot (`SetWornHidden`) that the pack owned; a carried captive writing it would have handed the pack back to the eye.
- **`ILiftLoad` members are compared by reference, and a destroyed load is only null through `UnityEngine.Object`.** `LiftCarrier.Alive` casts; `carrier.Load == this` on an interface is plain reference equality and misses a despawned plant.
- **A LineRenderer added in code is a metre wide.** `LeashRope.Dress` sets material and tiling but not width; the haul rope sets `startWidth`/`endWidth` from `LeashRope.Width` itself.

- **A whip turn on the spot outruns the arms.** Spinning at 400°/s with the swing uncapped left the far grip outside an arm's reach and the fist off it; the lag is capped (`maxSwingLag`) so past 25° the load is dragged round with the body.
- **Never give the load a `NetworkTransform` or an interpolating body.** The first replicates the server's lagging copy over the carrier's own pose; the second restores the body's position within the frame (INVARIANTS: something else owns that transform). `LiftCarryTests.TheLoosePlantIsLiftableAndPosedOnlyByItsCarrier` pins both.
- **The hands are placed from the body, not from the clip.** The carry walk is ordinary locomotion with the arms reached onto the grips; a hold-pose clip was not derived from the take because a whole-torso hold bends the walk (see HumanoidAnimation's arms-only gotcha) and IK onto the grips already fixes the hands.
- **The take is a crouched heave, not a stand-up carry.** `Lifting Object.fbx` squats, grips at shin height and strains the load to mid-thigh while still crouched, then drops it. *Heave Load Up* is 0.55–3.4 s at speed 1.6 (1.8 s), *Set Load Down* 6.8–10.0 s at speed 2 (1.6 s); the body stands up in the Full slot's fade-out while the near end blends from the animated hands to the carry point.
- **The player's root is a metre above its feet.** Its 3 m capsule runs from 1 m below the root to 2 m above it, so a rig test that stands the body with its root on the ground buries the legs and puts the hands at shin height — the solver then (correctly) tips the load's far end UP to keep the heel out of the ground. `LiftCarryTests` places the root at the capsule's bottom and samples the idle pose first (`Animator.Rebind` + `Update(0)`).
- **Action names are one namespace across every library.** The clips were first cut as `Lift Heavy` / `Set Down Heavy`; a CMU action `Lift Heavy` (Carry) already existed, and the controller builder refuses duplicate names. Check `Actions/` before naming a new cut.
- **A carried load clips walls**, like a cart: it is a trigger while held. The ship's back-door opening is 4.4 m wide against the plant's 2.45 m.

## Extending

- **A new heavy load:** a runtime-spawned prefab (registered, `SaveableEntity` with prefab id) with a `Liftable`, the four markers and a handle, no `NetworkTransform`, kinematic body with interpolation off. Give it an `ILiftDestination` if it has somewhere to go.
- **Two carriers:** would need the far end to become a second body's grip and a joint pose solved from both bodies, which only one machine can know in the same frame — so one carrier's view would lag. Not built.
- **A new kind of load** that is not a rigid prop: implement `ILiftLoad` on it (`CaptiveHaul` is the worked example), own a `LiftHands` for the carrier's machine, and decide who holds it on the server — a `NetworkVariable` if the load is a spawned prefab of your own, messages on its channel if it is added at runtime.
- Tests: `LiftCarryTests` (solver, real rig at 0–9 m/s and up to 400°/s, pace, exit rule, save mid-carry; a captive folded over the shoulder on Raxy/Nomad, ankles together and knees bent, the feet in view looking straight ahead and 20° down but never in the face; nothing left standing in the carrier's eye line and the legs never hidden though picked up looking 45° down (Raxy, Nomad), a DuneRat across the shoulders, the throw off the shoulder), `OxygenPlantRecoveryTests` (the mount, the cracks and their save, the step's status flow).
