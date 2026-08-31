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
| **Hip joint, seated** | **≈0.726 m above soles** | derived: thigh 10° below horizontal, shin vertical, sole on the floor |

The last row is the whole geometric content of the clip: seated, the hips drop **0.465 m**
from their standing height.

## Architecture

Three pieces, each independently checkable.

### 1. The clip — `Sit Idle.fbx`

Authored in Blender on `astronaut.blend`'s armature (the one source file that carries the mesh,
so the pose can be judged in a render before it ever reaches Unity), exported armature-only with
`bake_anim=True`, `add_leaf_bones=False`. Lands in `Assets/Game/Art/Animations/Player/`
alongside the existing Mixamo clips and imports the same way they do: Humanoid,
`avatarSetup: 2` (Copy From Other Avatar).

The authoring script `sit_idle.py` lives beside the .blend and, like `astronaut_export.py`,
**never writes back to it**. The .blend stays the source of truth.

Pose, as bone deltas in armature space:

- thighs rotated 80° forward (10° below horizontal), shins returned to vertical, feet left at
  rest so they land flat — a chair pose keeps both legs in the sagittal plane, which is why
  plain pitch deltas suffice here and the saddle pose needed abduction;
- hips lowered 0.465 m so the soles reach the floor plane;
- ~8° of recline spread over three spine joints, head counter-pitched to look level;
- upper arms brought down and slightly forward, elbows bent, hands resting on the lap.

Roughly 6 s of loop carrying only breathing and one slow weight shift — `GDC-L1-ANIM-0005`
("life is in the secondary motion"), kept deliberately small so it stays subordinate and the
body never drifts off the cushion.

**No root motion** (`GDC-L1-ANIM-0004`). The seat owns the body's world pose — `MountModule`
parents the rider and already force-disables root motion on mounted rigs, and `SeatedRider`
rewrites the pose every `LateUpdate`. So the clip imports with Bake Into Pose on for rotation,
Y and XZ (`lockRootRotation`, `lockRootHeightY`, `lockRootPositionXZ` all `1`). This matters
more than it looks: with Bake Into Pose Y **off**, the 0.465 m hip drop becomes root motion,
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
- **It holds no rider state.** Setting a bool is idempotent and release names its own rider, so
  unlike `MountedRiderPose` it needs no blend weight and no tracked occupant set — which is what
  lets one ship carrying four people use a single component per seat without bookkeeping.

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
Verified on an actual client, not only the host.

## Persistence

Nothing to save. `Seated` is derived presentation, recomputed from whatever puts the player back
into a seat on load. Recording it would create a second source of truth that could disagree with
the seat itself.

## Fit, and how it gets verified

The clip is authored correct in the *character's own frame*: feet on the floor plane, hips
0.726 m above it. Seating the body at the right height in a given chair is the chair's job,
tuned through `MountModule.seatOffset` — one knob, per seat, exactly where the existing seat
geometry already lives.

`PlayerShipBuilder` currently puts `SeatPoint` at `chairBounds.min.y + 0.02` (deck level), and
the player pivot sits 1 m above the soles, so an offset is expected rather than surprising. The
four cockpit chair variants differ (the pilot chair is raked), so each is checked.

Verification, in order:

1. Blender render of the posed astronaut from the side and front — is it a person sitting?
2. Unity: clip imports as Humanoid, plays on the player, no root motion, loops seamlessly.
3. Unity: astronaut placed in each cockpit chair variant; tune `seatOffset` until the hips are
   on the cushion and the soles are on the deck.
4. Host + client: a client sees the host seated and the host sees the client seated.
5. Save while seated, reload: the player is seated again, from the seat rather than from a
   saved flag.

## Risks

- Posing a humanoid numerically without a viewport is iterative. Budgeted for: render, look,
  adjust, before anything is exported.
- The astronaut is 3 m tall and `crew_seat.py` authored its cushion at 0.46 m for a human-sized
  occupant. The cockpit chairs are far larger than that source figure, but if a chair turns out
  to be genuinely too small for a 0.726 m sit height, that is a chair problem to report, not
  something to hide by bending the clip's legs.
