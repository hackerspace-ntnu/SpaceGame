---
system: Lifting
layer: characters
summary: "A heavy load lifted by one end and carried: near end in the hands, far end on the ground, posed from the body"
paths:
  - Assets/Game/Scripts/World/Lifting/
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
reads_with: [Pushables, Oxygen, HumanoidAnimation, PlayerCharacter, Multiplayer, Persistence]
updated: 2026-10-06
---

# Lifting

Replaces the drag (`Haulable`, deleted 2026-10-06), which slid the oxygen plant after its haulers from the server at 1.6 m/s
and read as slow and laggy. Now the player presses interact at the load's handle, squats and heaves the near end up to the
waist, and walks with it at three quarters of a walk; the far end stays on the sand and slides after them. Esc or interact
puts it down. Built for any heavy runtime-spawned load; today's only one is `LooseOxygenPlant.prefab` ([Oxygen.md](Oxygen.md)).

## Model

| Idea | Mechanism |
| --- | --- |
| The pose is derived, never sent | [`LiftCarrier`](Assets/Game/Scripts/World/Lifting/LiftCarrier.cs), added at runtime to the carrier's body on **every** machine (`DefaultExecutionOrder(200)`, `LateUpdate`), poses the load from that machine's copy of the body. On the carrier's own machine that is the body its input moved this frame, so the grips are on the hands with zero lag; others see it from the interpolated owner-authoritative body. The `Pushable` cart rule, for the same reason |
| Who carries it, where it rests | [`Liftable`](Assets/Game/Scripts/World/Lifting/Liftable.cs): one server-written `NetworkVariable<LiftState>` (`Held`, `Carrier` = body `NetworkObjectId`, `RestPosition`, `RestRotation`), read on spawn. **No `NetworkTransform`** on the load: a replicated pose would fight the derived one |
| The solver | [`LiftPoseSolver`](Assets/Game/Scripts/World/Lifting/LiftPoseSolver.cs), pure: grip midpoint ON the hands, axis along the swung heading, pitched down until the first of 7 underside samples (heel→foot) meets the ground (`PitchToTouch`, closed form), max 75°. So a slope or a crest is followed, never entered. `Rest` lays it on its underside along the ground's slope; `Swing` turns the heading toward the body's at `swingRate` (5/s, exponential) so the far end trails round a turn |
| The hands | the carry point is fixed to the body: `handsBelowShoulder` 0.8 arm-lengths below the shoulders' middle and as far ahead as `armReach` 0.98 allows (≈1.5 m up, ≈0.5 m ahead on the 3 m player). `ReachingArm` (shared with carts; `ReachingArm.Of` builds one per arm) turns shoulder and elbow so each palm lands on its grip |
| Shape | four markers on the load, measured in its own frame: `Lift_Grip_L/R` on a handle bar, `Lift_Heel` under them, `Lift_Foot` under the far end (`LiftShape`) |
| Pace | the carrier's own machine calls `PlayerMovement.StartHauling(WalkSpeed × carrySpeedFraction)` — 0.75 × 6 = 4.5 m/s, sprint (20) and jump/dash blocked by the cap — and `StartHauling(0)` (clamped to 0.1) while the lift or set-down plays |
| A ghost while carried | `SetCarried(true)` turns the load's solid colliders into triggers (only those that were solid, restored after), so it cannot shove a crewmate, snag the ramp lip or push the ship's dynamic hull. Still aimable: the `Liftable` is on the collider's own object |
| One carrier | a second pair of hands would pose one load against two bodies, and neither set of hands could stay glued to it. Decided 2026-10-06; see Extending |

## Key types

| Type | Role |
| --- | --- |
| `Liftable` | the load: `IInteractable` ("RMB: lift it by the handle" / "RMB / Esc: put down"), `IInteractionMoment` (None: the body plays the lift), `ISavedPose`, server judge (carrier gone/dead/`releaseDistance` 8 m → put down; destinations), local carrier input and pace |
| `LiftCarrier` | the body's half: phases Lifting → Carrying → Lowering; `Lift(load, seconds, alreadyDone)`, `Lower(rest, seconds)`, `RestFromHere`, `Drop`, `CarryPoint`; `BodyLanguage.OccupyArms(Both)` |
| `ILiftDestination` / `LiftDestinations.Reached` | where a load is carried to (the oxygen mount, 3 m flat radius); the server checks every frame |
| `NetMsg.LiftRequest` (130) | carrier → server on the load's channel: A=1 lift, A=0 put down with P/R = the rest pose |
| `OxygenPlantRecoveryAuthoring` | *Tools ▸ SpaceGame ▸ Oxygen ▸ Author Liftable Loose Plant*: markers, handle, `Liftable` wiring, removes the `NetworkTransform`, body not interpolated; read back |

## Flows

- **Lift.** Interact within `gripReach` (2.5 m, flat) of the grips → `MayLift` (alive, not seated, no cart, humanoid arms) → `LiftRequest A=1` → server re-checks, writes `Held` → every machine `Apply` → `LiftCarrier.Lift(load, liftSeconds)`. The owner also plays **Lift Heavy**, deselects the hotbar, subscribes interact/slot events. Lift timing is the action's own length (`CharacterAction.Seconds` at the body's seeded speed), the same on every machine. Before `gripFraction` (0.45) the arms reach down to the resting grips; after it the near end follows the animated hands, blending to the carry point.
- **Put down.** Esc (only while `GameplayMenuScope.AcceptsGameplayInput`), interact (not on the granting frame), a drawn hotbar item or death → the owner computes `RestFromHere`, plays **Set Down Heavy**, lowers at once and sends `A=0` with the pose → server accepts it unless it is > `restTolerance` (2 m) from its own → `Held=false` → every other machine lowers onto that rest. The rule is `Liftable.PutsDown`.
- **Snap home.** Server `Judge`: a destination that `Accepts` and is `Reached` takes the load (`Receive` → despawn). The carrier's `LiftCarrier` drops it in `OnDisable`.

## Multiplayer

- **Owner-authoritative motion, server-authoritative possession.** Matches the player's own `NetworkTransform` (owner) and the cart's derived pose; the server never moves the load. A late joiner reads `LiftState` with the spawn and, finding it held, lifts with `alreadyDone = 1` (straight to carrying).
- Animation: the player's `ClientNetworkAnimator` replays the owner's Full-slot lift for everyone; non-owners only read its length.
- **Not run:** any of this on a client, or in Play at all (2026-10-06: EditMode only, see Testing below).

## Persistence

The load is its own runtime entity (`SaveableEntity` + `TransformSaveable`, prefab id `07ddab4c…`). `ISavedPose` answers the
**rest pose** while carried, so a save taken mid-carry reloads the load lying where it was carried, never hanging from hands
that are gone. On spawn the server publishes its transform as the rest. Carrying itself is not saved.

## Gotchas

- **Never give the load a `NetworkTransform` or an interpolating body.** The first replicates the server's lagging copy over the carrier's own pose; the second restores the body's position within the frame (INVARIANTS: something else owns that transform). `LiftCarryTests.TheLoosePlantIsLiftableAndPosedOnlyByItsCarrier` pins both.
- **The hands are placed from the body, not from the clip.** The carry walk is ordinary locomotion with the arms reached onto the grips; a hold-pose clip was not derived from the take because a whole-torso hold bends the walk (see HumanoidAnimation's arms-only gotcha) and IK onto the grips already fixes the hands.
- **The take is a crouched heave, not a stand-up carry.** `Lifting Object.fbx` squats, grips at shin height and strains the load to mid-thigh while still crouched, then drops it. *Lift Heavy* is 0.55–3.4 s at speed 1.6 (1.8 s), *Set Down Heavy* 6.8–10.0 s at speed 2 (1.6 s); the body stands up in the Full slot's fade-out while the near end blends from the animated hands to the carry point.
- **A carried load clips walls**, like a cart: it is a trigger while held. The ship's back-door opening is 4.4 m wide against the plant's 2.45 m.

## Extending

- **A new heavy load:** a runtime-spawned prefab (registered, `SaveableEntity` with prefab id) with a `Liftable`, the four markers and a handle, no `NetworkTransform`, kinematic body with interpolation off. Give it an `ILiftDestination` if it has somewhere to go.
- **Two carriers:** would need the far end to become a second body's grip and a joint pose solved from both bodies, which only one machine can know in the same frame — so one carrier's view would lag. Not built.
- Tests: `LiftCarryTests` (solver, real rig at 0–9 m/s and up to 400°/s, pace, exit rule, save mid-carry), `OxygenPlantHaulTests` (the mount and the step).
