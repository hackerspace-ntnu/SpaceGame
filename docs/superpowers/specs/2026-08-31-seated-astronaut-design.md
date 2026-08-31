# Seated astronaut idle

A looping in-place seated idle for the astronaut, so a player in a ship chair sits in it
instead of standing to attention above it.

## Scope

Applies wherever a *chair* seats a player:

- the PlayerShip cockpit — the helm chair and the passenger chairs, both `MountModule` seats
  built by `PlayerShipBuilder`;
- the crash-landing arrival descent, which seats people through `SeatedRider` instead;
- any future chair, by dropping one component on it.

**Out of scope:** the ostrich saddle. Straddling an animal is a different pose and
`MountedRiderPose` already does it well. Nothing in this work touches it.

**Out of scope:** a pilot-versus-passenger hand variation, and body reaction to ship motion.
One seated idle, everybody the same. If the cockpit later wants hands on the yoke, it is a
second clip and a second bool, not a redesign.

## Measurements this rests on

Established by inspecting the rig and the prefab, not assumed:

| Fact | Value | Source |
| --- | --- | --- |
| Astronaut rig | Mixamo, 91 bones, Humanoid | `astronaut.blend`, `astronaut.fbx.meta` `animationType: 3` |
| Armature-local space | Y up, +Z forward, +X the character's left; units cm (object scale 0.01) | rest-pose dump |
| Character height | ~3.0 m | `PlayerCharacter.prefab` capsule height 2 on a child at `localScale.y 1.5` |
| Player pivot above soles | 1.0 m | `Collider` at `y 0.5`, 3 m capsule → soles at −1.0 |
| Hip joint, standing | 1.19 m above soles | `mixamorig:LeftUpLeg` head Y 119.08 cm |
| Thigh / shin length | 0.551 m / 0.454 m | rest-pose bone lengths |
| Ankle above sole | 0.192 m | `LeftFoot` head Y 20.10 cm, sole ≈ 0 |
| **Hip joint, seated** | **0.752 m above soles** | solved from the posed skeleton by `solve_sit_drop()` |

The last row is the whole geometric content of the clip: seated, the hips drop **0.442 m**
from their standing height.

## Architecture

Three pieces, each independently checkable.

### 1. The clip — `Sit Idle.fbx`

Authored in Blender on `astronaut.blend`'s armature (the one source file that carries the mesh,
so the pose can be judged in a render before it ever reaches Unity), exported armature-only with
`bake_anim=True`, `add_leaf_bones=False`. Lands in `Assets/Game/Art/Animations/Player/`
alongside the existing Mixamo clips.

Imported Humanoid with **Create From This Model**, not Copy From Other Avatar as first planned.
The armature-only export roots at `Armature.002`, which neither existing avatar's
HumanDescription expects in that position — `astronaut.fbx` wants it as Hips' *parent* and
`AstronautArmature.fbx` does not have it at all, so copying either fails the rig check. The
skeleton is stock `mixamorig` naming and identical in rest pose to `astronaut.fbx`, which is
itself imported this way, so the generated avatar and the player's agree bone for bone and
Mecanim retargets 1:1. Verified: `isHumanMotion=True`, `isLooping=True`, `length=6.00s`.

The authoring script `sit_idle.py` lives beside the .blend and, like `astronaut_export.py`,
**never writes back to it**. The .blend stays the source of truth.

Limbs are **aimed at directions** rather than rotated by chained deltas — a direction is
checkable against the rest pose ("the shin hangs plumb") where a chain of deltas is only
checkable by rendering it:

- thighs 10° below horizontal and splayed 9°, shins plumb, feet near rest so they land flat;
- hips lowered **0.442 m**, solved from the posed skeleton rather than written down, so changing
  a knee or ankle angle re-solves the drop instead of leaving the astronaut hovering;
- ~8° of recline over three spine joints, head counter-pitched to look level;
- **arms at the sides, hands beside the hips** — not on the thighs. That was the intent and it
  is not reachable on this character: the shoulder sits 77 cm above the top of the thigh and the
  whole arm is ~86 cm, so reaching the thigh forces the forearm near-vertical, which lands the
  hand at the *shoulder's* depth — and the shoulder is 12 cm behind the hips. Every version that
  aimed for the thighs put the hands inside the belly, which is 94 cm across.

Roughly 6 s of loop carrying only breathing and one slow weight shift — `GDC-L1-ANIM-0005`
("life is in the secondary motion"), kept deliberately small so it stays subordinate and the
body never drifts off the cushion.

**No root motion** (`GDC-L1-ANIM-0004`). The seat owns the body's world pose — `MountModule`
parents the rider and already force-disables root motion on mounted rigs, and `SeatedRider`
rewrites the pose every `LateUpdate`. So the clip imports with Bake Into Pose on for rotation,
Y and XZ (`lockRootRotation`, `lockRootHeightY`, `lockRootPositionXZ` all `1`). This matters
more than it looks: with Bake Into Pose Y **off**, the 0.442 m hip drop becomes root motion,
the player's Animator ignores root motion, and the astronaut silently stands back up.

### 2. Playing it — the animator

`AstronautArmature.controller` gains:

- a `Seated` bool parameter;
- a `Sit Idle` state on the Base Layer holding the clip;
- Any State → `Sit Idle` on `Seated`, and `Sit Idle` → the default state on `!Seated`, with
  *Can Transition To Self* off so the Any State edge does not retrigger every frame.

The `Upper Body` mask layer is untouched, so a seated player holding an item keeps the hold
pose on the arms and the sit on the legs.

### 3. Turning it on — `ChairPose`

A new component, the chair counterpart to `MountedRiderPose`, with the same
`PoseRider(Transform)` / `ReleaseRider(Transform)` entry points. It does not write bones; it
sets `Seated` on the rider's Animator and clears it on release.

Two consequences follow from putting it there:

- **The chair declares itself, not the player.** A player cannot tell a cockpit chair from an
  ostrich, so the knowledge lives on the seat. Adding `ChairPose` next to a `MountModule` is
  what makes that seat a chair; the ostrich simply does not get one.
- **It needs no blend weight and no per-frame pass.** Setting a bool is idempotent and Mecanim
  owns the blend, so unlike `MountedRiderPose` there is no `LateUpdate` and no bone maths. It
  does keep a small list of who it has seated — not for posing, but so that a chair disabled or
  destroyed with somebody in it can clear their flag. Without that, a body is left latched to
  the seated idle with nothing alive to release it: permanently sitting in mid-air.

Wiring:

- `PlayerShipBuilder` adds it to the helm module and to each `BuildPassengerSeat` module. It has
  to be the builder: that script rewrites `PlayerShip.prefab` wholesale, so a hand-added
  component survives exactly until the next rebuild.
- `SeatedRider.Attach` / `Detach` call it for the arrival descent. Those already run on every
  machine for every player, including the idempotent repair pass.

## Multiplayer

Nothing new on the wire. `MountNetworkSync` already replicates mounting, and `SeatedRider`
carries both a `TakeSeat` event and an `occupants` state channel for late joiners. `ChairPose`
hangs off events that already fire on every machine, for every player — remote bodies included.

This is reasoning from the code paths, **not yet a client test**: `SeatedRider.Attach` runs for
every player on every machine, and `MountModule.Mounted` is raised on peers by
`MountNetworkSync`. It still has to be seen on a real client before the feature is finished.

## Persistence

Nothing to save. `Seated` is derived presentation, recomputed from whatever puts the player back
into a seat on load. Recording it would create a second source of truth that could disagree with
the seat itself.

## Fit — the seat is measured off the chair's mesh

The clip is authored correct in the *character's own frame*: soles on the floor plane, the
body's underside 0.43-0.49 m above it across the seat pan. Putting the body in a chair is the
seat's job, and `MeasureSeat` does it by asking the chair's geometry two questions:

- **Cushion height** — the height with the most upward-facing area in the chair's middle band,
  over the middle of its footprint. Both filters matter: without the height band the base plate
  wins, without the footprint one an armrest does. The pivot then rides
  `SeatedPivotAboveCushion` (0.55 m) above it.
- **Facing** — the backrest is the mass above the cushion, the pan is the mass just below, and a
  chair faces from back to front.

Neither can come from the chair's transform: all four arrive from the FBX at ~150x scale with
the exporter's axis rotation baked in, reading **yaw 180 whichever way the chair actually
points**. Geometry is the only thing that separates the two chairs facing the nose from the two
facing sideways.

Facing is applied in two places, because two different things read it: `SeatPoint`'s rotation
turns the **body** (both `MountModule` and `SeatedRider` take the rider's rotation from the seat
marker), and the passenger `MountModule`'s own transform turns the **first-person camera**. With
only the first, a sideways passenger looked out of the side of their own head.

Measured after the change — cushion +0.55 m on every seat, and the two sideways chairs at yaw 90:

| Seat | Cushion | Pivot | Yaw |
| --- | --- | --- | --- |
| Helm (`Cockpit_Seat_Command`) | 5.35 | 5.90 | 0° |
| PassengerSeat1 | 4.70 | 5.25 | 0° |
| PassengerSeat2 | 4.10 | 4.65 | **90°** |
| PassengerSeat3 | 4.15 | 4.70 | **90°** |

### Two bugs this uncovered on the way

Both pre-existing in `PlayerShipBuilder`, neither about animation:

1. **The cockpit `SeatPoint` was a metre too low** — at `chairBounds.min.y + 0.02`, the chair's
   base, while the player pivot sits 1 m above the soles. Anyone seated was buried to the waist
   in the deck, sitting or standing.
2. **Every arrival seat marker was two metres in the air.** `FloorUnder` dropped a ray and took
   the highest ship collider under the chair — and the chair has a `MeshCollider` of its own, so
   the ray landed on its *backrest*. Markers at y 7.01 against a deck at 3.78; the whole crew
   rode the descent standing in mid-air.

The deck turned out to be the wrong question entirely. A chair seats a body on its **cushion**,
which is where `MeasureSeat` now reads.

### Consequence: the feet no longer reach the deck

Sitting on the cushion means the legs hang where the chair puts them, and these chairs are built
for a larger occupant — cushions at 0.83-1.56 m above their own decks against a character whose
natural seated contact is 0.43 m. The two sideways chairs have footrests the boots land on; the
raised helm chair leaves the pilot's feet about 1.1 m above the deck. That is what sitting *on*
an oversized chair looks like, and it reads better than sinking into one. Lowering the seat pans
in `ship_lander_blockout.blend` is the only real fix, and that file is hand-built and untouched.

## Getting out of the seat

**Dismount points** are measured with the rest of the seat. Which way you step out is decided by
whether the chair looks along the hull or across it, so a chair added later is handled without
being named: a chair facing down the ship has the console in front of it, so its occupant steps
out **backwards** into the cabin; a chair facing across the ship has the aisle in front of it, so
its occupant steps out **forwards**. The step clears the chair's own edge along whichever axis it
is taking, so the clearance means the same thing on a deep command chair and a shallow bench.

The dismount is placed at **deck + pivot height**, not relative to the raised seat — otherwise
standing up left the player hanging in the air by however high the cushion was. Verified: all
four land at exactly 1.00 m above their deck.

**The arrival now ends with the crew still seated.** Landing used to empty every chair
automatically a moment after touchdown; it now calls `SeatedRider.AllowRelease()` instead and the
crew get up when they press Escape. Being teleported out of your own seat at the exact moment the
game hands the controls back reads as the game taking them away again.

- `releasable` is a server-written `NetworkVariable`, because the machine that knows the flight is
  over is the server and the machine that draws the prompt and reads the key is the client. It
  also survives a late join, which a one-shot event would not. It is what stops Escape being a
  bail-out button halfway down the arc.
- `NetMsg.LeaveSeatRequest` (94 — 92/93 are retired and must not be reused) carries the request
  client → server. The server releases the **sender's own** body: the reference on the wire is
  checked against `OwnerClientId` rather than trusted, so a malformed message cannot turf a
  crewmate out of their seat.
- The key read is gated on `GameplayMenuScope.AcceptsGameplayInput`. Escape already means "never
  mind" in the chat box and the settings fields, and without the gate closing one of those would
  eject the player from their seat as a side effect. The same gate is false while the cutscene
  holds the controls, which is a second reason the descent cannot be bailed out of.
- **`strandedSeatTimeout` (180 s) is the backstop.** "Get up when you like" and "cannot get up at
  all" look identical from outside, and one of them is a player stuck in a chair for the session.

**`SeatPromptUI`** draws "ESCAPE to leave the seat" while the local player is in a releasable
seat. It decides nothing — `SeatedRider` owns whether the key does anything — so the prompt
cannot offer an action the seat would refuse. Shown at the moment of need rather than in a
front-loaded controls screen (`GDC-L1-UX-0001`), and it names the key that already gets you off
every mount in the game, so it teaches a convention rather than an exception (`GDC-L1-UX-0004`).
The crash landing is the one seat you are *put* into rather than choosing to sit in, so nothing
else has taught you how to leave it.

## Verification

1. ✅ Blender renders, side/front/three-quarter, against a floor plane — it reads as sitting.
2. ✅ Unity import: `isHumanMotion=True`, `isLooping=True`, `length=6.00s`, avatar valid.
3. ✅ All four cockpit chairs: seated on the cushion at the chair's own facing, verified by
   measurement (pivot = cushion + 0.55 on every seat; yaw 90 on the two sideways chairs) and by
   screenshot from two angles.
4. ⬜ Host + client — a client sees the host seated and vice versa. **Not yet done.**
5. ⬜ Save while seated, reload. **Not yet done.** Nothing new is persisted, so this is
   confirming the flag is re-derived rather than that it was stored.

## Open finding: the chairs are built for a larger occupant

Measured cushion heights above the deck: **0.83, 0.88, 0.92 m**, and **1.56 m** on the raised
helm chair. The astronaut's natural seated contact is **~0.65 m**. So the crew sit with their
soles on the deck and their backsides slightly below and forward of the cushion rather than on
it, and the effect is largest at the helm.

It reads acceptably and it is not an animation problem — bending the clip's legs to reach a
1.56 m cushion would produce a worse pose, not a better fit. The fix, if wanted, is to lower
the seat pans in `ship_lander_blockout.blend`. **That file is hand-built and I have not touched
it.**
