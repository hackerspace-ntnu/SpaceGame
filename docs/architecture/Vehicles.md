# Vehicles — moving parts

Four small components for vehicles with parts that move: doors, hatches, ramps, tilting engine
pylons, landing gear. They compose; none of them knows about the ship specifically.

| Component | Job |
|---|---|
| `ArticulatedPart` | Animates one transform between a closed and an open pose. Knows nothing about who moves it. |
| `ArticulatedPartInteraction` | `IInteractable` surface that toggles one or more parts. This is what makes a door player-operable. |
| `MountStation` | `IInteractable` cockpit control that mounts the player into the vehicle. |
| `VehicleDeploymentController` | Drives parts off `MountModule`'s Mounted/Dismounted events. |
| `ShellVariantSwitcher` | Swaps a seamless hull mesh for a cut-out one while any watched panel is off its closed pose. |

## `ArticulatedPart`

Rotates or slides **its own transform**, relative to whatever pose it was authored at. Put it on a
pivot GameObject placed at the hinge line, with the mesh parented underneath — the component
rotates around its own origin, so the origin *is* the hinge.

```csharp
part.Open();                  // animate open
part.Close();
part.Toggle();
part.SetOpen(true);           // animate to a specific state
part.SetOpen(true, instant: true);   // snap, no animation (works in edit mode too)

part.IsOpen;                  // where it's heading
part.IsMoving;
part.Openness;                // 0..1, for driving audio/VFX
part.Settled += open => { };  // fires when motion finishes
```

Inspector: `motion` (Rotate/Slide), `axis` (local), `openAngle` / `openDistance` (sign picks the
direction), open/close durations, and an easing curve.

Two context-menu items — **Preview Open** / **Preview Closed** — let you tune a hinge without
entering play mode. They write the transform directly, so always finish on *Preview Closed*
before saving: whatever pose the part is saved in becomes its closed pose.

## `ArticulatedPartInteraction`

Drop on a part's pivot (or any GameObject carrying its collider) and it becomes a
look-at-it-and-press-Interact switch. `Interactor` resolves `IInteractable` by `GetComponent` then
`GetComponentInParent`, so a collider anywhere under the pivot finds it.

- `parts` — empty means "the `ArticulatedPart` on this GameObject". Fill it in to drive several
  leaves from one panel.
- `blockWhileMoving` — ignore input mid-swing so the part can't be stuttered.
- `lockedWhileMounted` — refuse while someone is piloting. Stops the ramp dropping in flight.

Mixed open/closed states resolve toward *close everything*, so one press always leaves the group in
a single predictable state.

## `MountStation`

The reason this exists: `Interactor` walks *up* from the collider it hit, so on a large vehicle
every hull collider would resolve to the root `MountModule` and let players board by looking at the
outside of the fuselage. `MountStation` gives the vehicle exactly one boarding point.

Pair it with **`MountModule.mountableByDirectInteraction = false`** (added for this). `MountStation`
calls `TryMount` directly rather than going through `MountModule.Interact`, so the cockpit control
keeps working while the hull surface is switched off.

## `VehicleDeploymentController`

Subscribes to `MountModule.Mounted` / `Dismounted`:

- `deployOnMount` — opened when a pilot takes the controls (engine pylons, gear, wings).
- `closeOnMount` — closed at the same time (doors, hatches, cabin wall panels).
- `retractOnDismount` — return the deployed parts to stowed when the pilot leaves.

## `ShellVariantSwitcher`

For hulls authored as two meshes: a seamless closed shell and one with the access openings cut out.
Watches a set of `ArticulatedPart`s and shows the cut-out mesh while any of them is off its closed
pose, the seamless one otherwise. Without it, opening a panel on ShipRV would swing it away from an
outer hull that is still solid.

It polls in `Update` rather than using `Settled`, because the swap has to happen the instant a panel
*starts* moving and there is no "started" event to hang it on.

---

# ShipRV

`Assets/Prefabs/agents/vehicle/ShipRV.prefab`, built from
`Assets/Prefabs/agents/vehicle/ship_model 1.blend` by
[`ShipRVBuilder`](../../Editor/Vehicles/ShipRVBuilder.cs) — **Tools ▸ Vehicles ▸ Build ShipRV
Prefab**. Re-running it rebuilds the prefab from scratch; every hinge position and collider is
measured off the meshes at build time, so re-exporting the model with tweaked proportions and
re-running still lands in the right place.

## What moves

| Node | Motion | Driven by |
|---|---|---|
| `Model/GarageDoor` | −100° about X, hinged along its bottom edge → drops aft into a boarding ramp | Player (Interact), closed on mount |
| `Model/CockpitDoor` | −90° about Y, hinged on its starboard edge → swings aft into the cargo bay | Player (Interact), closed on mount |
| `Model/WallPortUpper` | −105° about Z, hinged on its top edge → lifts up and out | Player (Interact), closed on mount |
| `Model/WallPortLower` | +105° about Z, hinged on its bottom edge → drops down and out | Player (Interact), closed on mount |
| `Model/WallStarboardUpper` | +105° about Z, hinged on its top edge | Player (Interact), closed on mount |
| `Model/WallStarboardLower` | −105° about Z, hinged on its bottom edge | Player (Interact), closed on mount |
| `Model/WingPort` | +90° about Z about the modelled axle | Opens on mount |
| `Model/WingStarboard` | −90° about Z | Opens on mount |

Each wing pivot carries the wing, the motor nacelle and the axle, so they swing as one rigid unit
from stowed-over-the-hull to deployed-beside-the-hull.

## The cabin walls

Each flank of the cabin is one large opening split into a stacked pair of panels, and they open as
a **clamshell**: the upper panel hinges on its top edge and lifts up-and-out, the lower one hinges
on its bottom edge and drops down-and-out. Both swing clear of the hull rather than into the cabin.

The model delivers all 13 wall panels as `cockpit_door.NNN`. The four openable ones are the four
largest and separate cleanly from the rest — 5.11 m long with 188 triangles each, against 1.91 m or
less and 44 triangles for every other panel in the group. `ShipRVBuilder.PartNames` renames just
those four.

Two things make the opening real rather than cosmetic:

- **Each panel carries its own `BoxCollider`**, so swinging it away takes the cabin wall with it.
  That is why `BuildCollision` deliberately leaves the cabin sides open and only boxes in the
  stretch aft of the panels — a static side wall there would leave an invisible barrier across
  the opening.
- **`ShellVariantSwitcher` swaps `shell_closed` for `shell_open`**, because the panels sit about
  half a metre inboard of the outer skin and the seamless shell has no hole for them to open into.

The floor collider is sized from the deck plate rather than the hull for the same reason: a floor
as wide as the outer shell would let the player walk off the deck into the void between the wall
panel and the skin.

## Model-side notes

- The model is `ship_model 1.blend`, which lives in `Assets/Prefabs/agents/vehicle/` alongside an
  older `ship_model.blend` and a Blender autosave (`ship_model 1.blend1`), with a third copy still
  in `Assets/Art/Models/Vehicles/RV/`. Only `ship_model 1.blend` is used. Worth consolidating — if the
  file is renamed or moved, update `ShipRVBuilder.ModelPath`.
- The artist names the moving parts (`baggage_door`, `left_wing`, `right_motor`,
  `left_wing_axel`, `shell_open`, `shell_closed`), and the builder passes those through untouched.
  Only the structural meshes still on Blender defaults get renamed. **If a re-export renumbers the
  `Cube.NNN` meshes, `PartNames` is what needs updating** — the builder logs a warning naming each
  mesh it could not find.
- The model still carries Blender's default scene `Camera` and `Light`; the builder strips them,
  since a stray enabled camera inside a vehicle prefab hijacks rendering.
- The cockpit has no steering wheel, so the builder still assembles a placeholder one from
  primitives. Only the `BoxCollider` and `MountStation` on `Cockpit/SteeringWheel` matter —
  replace the children with a modelled yoke freely.

## Boarding

Walk in through either door → look at `Cockpit/SteeringWheel` → Interact. That's `MountStation`;
the hull itself is not a mount point. `Escape` dismounts (`SteerModule`), putting you back on your
feet beside the wheel rather than outside the hull — stepping out at altitude would otherwise drop
the pilot through the sky.

## Flight

`FlyingRigidbodyMotor` + `SteerModule`: WASD for throttle and yaw, **Space / Left Ctrl** (or the
gamepad triggers) for climb and descent. That vertical axis is a new `Vertical` action in
`InputSystem_Actions` — `Blimp.prefab` already referenced an action by that name but it did not
exist, so the blimp gains altitude control from this too.

`altitudeHold` is off, so the ship stays put when nobody is flying it.

## Collision

The model has a modelled interior, but a concave `MeshCollider` cannot go on a non-kinematic
Rigidbody, so the walkable volume is still a hand-placed box shell under `Collision`: a continuous
deck about 10.4 m long with 2.2 m of headroom, from the cargo doorway to the nose. Every box is
derived from measured mesh bounds at build time, not hardcoded.

The cabin sides are deliberately *not* boxed — the four wall panels carry their own colliders and
own that span. See "The cabin walls" above.

---

# Spider Walker

`Assets/Prefabs/agents/vehicle/rig_walker.prefab` — the six-legged walking station.

| Component | Job |
|---|---|
| `SpiderWalkerLocomotion` | The legs. Sole owner of the hull's transform; consumes a twist and answers with what the feet can deliver. |
| `SpiderWalkerDriver` | Turns a movement request into that twist. The only thing that talks to the locomotion. |
| `WalkerPlatformCarrier` | Carries riders standing on the deck along with it. |

## What can move it

Exactly two channels, and no third:

1. **A mounted rider** — `SteerModule` → `IRiderControllable.ApplyRiderInput`. Board at
   `DOOR_MountStation`; `Escape` dismounts.
2. **An AI module** on the same `AgentController` — `IMovementMotor.Tick(MoveIntent)`, followed
   over the NavMesh. The prefab ships with a `WanderModule`; see "Roaming" below.

The driver reads **no input device of its own**. On any frame neither channel speaks, the twist
falls to zero, so an unmounted walker with no brain stands still rather than coasting on the last
order it was given. `[DefaultExecutionOrder(50)]` is what makes that check honest: the driver runs
after `AgentController` (0), so both channels have had their say, and before
`SpiderWalkerLocomotion` (100), which consumes the result.

The rider wins on any frame it arrives — the same rider-frame guard the other motors use.

## Roaming

The prefab carries a `WanderModule` at `ModulePriority.Fallback`, tuned for **long treks** rather
than the short shuffling an NPC does — a walking station that repositions by fifteen metres reads
as broken:

| Field | Value | Why |
|---|---|---|
| `limitWanderRadius` | off | Free roam, so `freeRoamRadius` is what applies. |
| `freeRoamRadius` | 450 | Most of a 500-unit chunk out. |
| `minDestinationDistance` | 150 | **The knob that makes it long.** Rejects anything nearer, so it always commits to a real walk instead of shuffling in place. |
| `sampleDistance` | 25 | Free roam scales this to `radius × 0.2` = 90, a generous snap onto real ground. |
| `maxSampleAttempts` | 12 | The 150-unit floor rejects more candidates, so it needs more tries. |
| `minWaitTime` / `maxWaitTime` | 4 / 12 s | A station pauses at length; it does not pinball. |
| `stopDistance` | 15 | Sized to the machine. `0.2` is never satisfied by something this big and it would grind at the destination forever. |

Nothing walker-specific was needed to wire it up: the driver *is* the `IMovementMotor`, so any
module that can drive an NPC can drive the station — swap in `PatrolModule` or `HuntModule` the
same way.

**A rider always wins.** `SteerModule` sits at priority 100 against wander's 0, and
`MountModule.allowAISelfMovementWhenMounted` is off, so mounting disables the wander module
outright for the duration and dismounting re-enables it. The station will not walk off under you
while you stand on the deck.

### Range is bounded by the streamer, not by the module

`rig_walker` lives in `persistentScene` and is **not** `SceneTracked`, so terrain chunks load
around the *player*, not around the walker. With `loadRadius: 1` that is a 3×3 band of 500-unit
chunks. Wander destinations are picked by sampling the NavMesh, and unbaked ground cannot be
sampled — so the walk self-limits to the loaded band rather than marching off into unbaked world.
Stray outside it and `TryPickDestination` simply fails, the module returns null, and the walker
stands still until chunks come back. Add `SceneTracked` to the prefab if you want it to pin its
own chunks and roam independently of where the player is.

## Pathfinding

The AI channel routes over the NavMesh with `NavMesh.CalculatePath`, **not** a `NavMeshAgent`
component. An agent moves the transform it sits on, and `SpiderWalkerLocomotion` is the single
owner of the hull's pose — the two would fight every frame. Here the NavMesh is asked for a route
and nothing else; the legs still carry the machine along it. `WalkerPath` holds the cursor into
the returned corners and is unit-tested in `Assets/Tests/EditMode/WalkerPathTests.cs`.

If no route can be had — unbaked test scene, a chunk `WorldStreamer` has not finished, a
destination off the mesh — the driver steers straight at the destination instead of standing
there waiting for a path that is not coming.

| Field | What it's for |
|---|---|
| `repathInterval` | Seconds between route recalculations. |
| `repathTolerance` | How far the destination must move to force an early rebuild. |
| `cornerArriveRadius` | How close to a corner counts as rounded. **Size this to the machine** — too small and a walker that cannot turn sharply grinds against the corner it is standing on. |
| `navMeshSampleDistance` | How far to search for the NavMesh. Must clear the ride height: the deck sits ~11.5 m above the ground the corners are sampled on. |
| `turnInPlaceAngle` | Heading error above which it pivots instead of driving on. |

Arrival is measured **flat** throughout, for that same reason — a 3D distance from the deck to a
corner never drops below the ride height.

---

# Dune Foil

`Assets/Prefabs/agents/vehicle/DuneFoil.prefab` — an 18 m wind-driven hydrofoil sand craft, built
from `Assets/Art/Models/Vehicles/DuneFoil/dune_foil_rig.fbx` by
[`DuneFoilBuilder`](../../Editor/Vehicles/DuneFoilBuilder.cs) — **Tools ▸ Vehicles ▸ Build Dune
Foil Prefab**.

There is no cockpit, no mount and no rudder. You walk onto the deck in normal first person and
sail it by working the rigging. Sail balance is the entire control scheme.

Design: [`docs/superpowers/specs/2026-08-11-dune-foil-sailer-design.md`](../../../docs/superpowers/specs/2026-08-11-dune-foil-sailer-design.md).

## The deck controls

Look at a winch and work it. **E is the "more" direction, left click is the "less" direction** —
two buttons rather than one toggle, because every control here runs both ways.

| Station | E | Left click |
|---|---|---|
| `Station_Hoist` (forward winch) | set every sail | furl every sail |
| `Station_MainSheet` (aft winch) | pay out main sheet | haul main in |
| `Station_JibSheet` | pay out jib sheet | haul jib in |
| `Station_MastRake` (centre console) | rake the mast aft | rake it forward |

`ISecondaryInteractable` is what gives an interactable that second button; `Interactor` raycasts
for both through the same path, so anything else in the project can opt in the same way. Ordinary
`IInteractable`s are unaffected — a click on one still falls through to the weapon.

Three details that make these actually usable:

- **What you aim at is a `Handle`**, a child sphere a metre above the deck, not a collider around
  the winch. The winches are modelled at and below deck level, so a collider centred on the mesh is
  half-buried and you have to find the sliver poking through.
- **Handle radius is derived from the spacing** between winches. A fixed radius large enough to
  make the isolated ones easy to hit makes the close pair (1.3 m apart) overlap, and then aiming at
  the far one silently hits the near one. Measured after: each control is usable from ~48 deck
  positions, closest working spot ~0.85 m. Sighting down the row past another handle is still
  blocked, which is just occlusion doing its job.
- **A press acts immediately**, then continues while held. Deferring the whole effect to the next
  `Update` makes a tap do nothing if anything interrupts the frame, and the control feels dead.

There is deliberately **no distance check on the station**. An earlier version measured from
`Camera.main` and refused beyond a few metres; `Camera.main` is whatever camera happens to be
tagged, and when it is not the player's — a spectator camera, an editor preview camera — every
control on the craft silently refuses. Range belongs to `Interactor`, which already limits its ray
to 5 m from the player actually interacting.

## Rope length is the control, not sail angle

Nothing ever sets a sail's angle. Each `SailSurface` **weathervanes** to trail the apparent wind
and the sheet stops it, exactly as on a boat: paying rope out lets the sail swing further off the
centreline, hauling in pins it closer. Measured on the built prefab, the main sweeps 5° → 90° as
its rope runs out.

That one rule is what makes the winches real controls, and it is also the helm — see below.

## Steering

| Sail | Lever arm | Effect when it makes force |
|---|---|---|
| Main, 99 m² | −1.76 m (aft of the foil) | luffs the bow **up** into the wind |
| Jib, 17 m² | +8.01 m (forward of it) | bears the bow **away** |

Trim one against the other and the craft turns. Sheet the main in and it rounds up; let the main
right out until it flags and the jib takes over and the bow falls off.

**The lever arms are placed, not measured.** Taken literally the modelled strut sits aft of every
sail, so every sail would bear away and the craft would be unsteerable. `RebalanceLevers` puts the
centre of lateral resistance a fixed fraction of the way from the main toward the jib
(`mainShare`), which is what a real designer does when setting the lead between centre of effort
and centre of lateral resistance. `mainShare` is the knob that decides how much helm each sail
has; at 0.25 the main out-torqued the jib 2:1 and no amount of jib could answer it.

The main's `maxSheetAngle` is **90°, and it has to be**. Stopped at 80 the main still makes
lateral force fully eased, and with six times the jib's area it then dominates at every trim — the
craft rounds up and can never bear away.

## Getting aboard

Four things have to line up, and each of them was wrong the first time:

- **The craft starts moored, sails furled.** With sails set it catches the wind on the frame the
  scene loads and leaves — measured 293 m from the player spawn within seconds, foiling, gangway
  stowed, unreachable. Furled, it waits. `DuneFoilBuilder` calls `SetHoisted(false)` on every sail.
- **A `WindField` must exist in the scene.** `SailRig` reads `WindField.Active`; with no wind field
  there is no wind, no force, and every control appears dead even though it is wired correctly.
  `Assets/Prefabs/environment/Wind.prefab`, one per scene.
- **The deck collider is measured from the deck plates**, not the hull's overall height — see
  below. Getting this wrong put an invisible floor 2.4 m above the planks with every control
  underneath it.
- **A gangway, not a ladder.** The player is a Rigidbody capsule with no step offset: it cannot
  climb a stack of rungs, so the original ladder was scenery. `BoardingRamp` puts a 28° slope down
  from the sand to the deck and stows it once the craft lifts onto the foil. The starboard rail is
  split around it — a continuous rail there is a wall across the top of the ramp.

## The deck is measured from the deck plates

`FindDeckPlates` picks the hull parts big enough and flat enough to stand on, and the walking
surface is the dominant one — 27.8 m² against 3–4 m² for the rest, at y 1.96.

**Never derive it from the hull's max Y.** The tallest thing on this hull is a winch, so that puts
the deck at 4.36 — an invisible platform 2.4 m above the planks, standing the player in mid-air
with all four controls hanging below their feet. That single mistake made the craft look both
unboardable and unsailable.

One flat surface, not a box per plate: the plates sit at 1.96, 2.16, 2.29 and 2.37, and the same
capsule that cannot climb rungs cannot climb a 0.4 m ledge either. `COL_Hull` is capped just below
the deck for the same reason — top it out any higher and the player is standing inside it.

## Ride height

`FoilLift` — lift goes as v², so there is a take-off speed below which the hull simply sits on the
sand.

Measured on the built prefab in a beam wind, sheets at 0.4:

| True wind | Speed | Ride height |
|---|---|---|
| 4–8 m/s | 2.2–4.3 m/s | 0 m — ploughing, hull on the sand |
| 12 m/s | 7.5 m/s | 10.0 m (76%) |
| 16 m/s | 10.6 m/s | 11.8 m (90%) |
| 20 m/s | 13.3 m/s | 12.4 m (94%) |

**`takeoffSpeed` has to sit below the speed the craft reaches while still ploughing**, or it can
never get up at all: hull drag caps it just short and the drag only falls once it is up. The
`climbRate` constant in `FoilPhysics.RideHeight` is the other half of that trap — too gradual and
the craft is stuck at a third of its height with drag it cannot out-accelerate.

## Ownership

`DuneFoilLocomotion` is the sole writer of the hull transform, the same rule
`SpiderWalkerLocomotion` follows. `SailRig` reports a force and a torque and never moves anything.
Execution order: `SailRig` 50 → `DuneFoilLocomotion` 100 → `WalkerPlatformCarrier` 200, which is
reused unchanged to carry a player standing on a transform-driven deck.

## Wind

`Assets/Prefabs/environment/Wind.prefab` — one `WindField` per scene, and everything (sails and
the cloth shader alike) reads it rather than keeping its own idea. Base bearing and speed, a slow
Perlin drift so it is never quite constant, and a gust field advected downwind so gusts arrive
from windward. `[ExecuteAlways]`, so a rig in the scene trims itself while you author it.

## The model

`Assets/Art/Models/_Source~/models/vehicles/dune_foil_rig.py` derives `dune_foil_rig.blend` from the hand-modelled
`dune_foil.blend`, which it **never writes to**. It dedupes 61 materials to 5 (one per colour),
builds the pivot hierarchy, subdivides the 13-poly sail planes into billowable grids, and UVs them
luff → leech.

Three things there are load-bearing:

- **Transforms are applied.** The artist built the sails by scaling planes, so they arrived with
  scales like (114, 242, 114). Anything working in object space then sees a space 100× smaller
  than the world — the cloth shader displaces by metres and threw the sails 150 m sideways.
- **Export uses `FBX_SCALE_ALL` and `use_triangles`.** `FBX_SCALE_NONE` bakes a ×100 onto every
  transform; the n-gons get rejected by Unity's importer as self-intersecting and silently dropped.
- **The model faces the wrong way.** Blender +Y maps to Unity −Z, so the builder yaws it 180°.
  That rotation is *composed* with the importer's own Z-up correction, never assigned over it.

## Why the spar swings with the sail

The obvious reading of "rotate the sail, not the post" is to leave the spar on the rake node and
rotate only the cloth. That does not survive this model. The spars bow 7.5% of their length and the
sail is laced along that curve, so rotating the cloth rigidly about any straight axis peels the luff
off the spar at about **0.09 m per degree** — measured flush at the authored pose, 1.9 m adrift at
15° of sheet, 5.1 m at 60°. The sail visibly tears off its own mast.

So `Main_Post` hangs off `Main_Yaw` and swings with the sail: the gap is then a constant 0.05 m at
every sheet angle. It is also what the geometry depicts — a single curved spar with a sail laced to
it is a yard, and a yard turns with its sail. The spar still sits *outside* the `Sail_` node, so
furling takes the cloth and battens and leaves the spar standing.

If you want a genuinely fixed vertical mast with the sail rotating against it, that needs a bone
chain along the spar with the sail skinned to it, twisting each bone about its own local tangent.
`best_spar_axis` in the rig script still computes the fitted rotation axis and would become that
rig's rest pose.

## Sailcloth shader

`Assets/Art/Shaders/SailCloth.shader` + `SailCloth.hlsl`. Billow is a vertex displacement to leeward
with the draft at 40% of the chord and the luff, foot and head pinned; flutter is two octaves of
scrolling noise weighted to the free leech. `SailSurface` writes `_Billow`, `_Luff`, `_Hoist` and
`_WindDirection` per sail through a `MaterialPropertyBlock`, so all four sails share one material.
The shadow and depth passes run the same displacement — otherwise a billowing sail casts a flat
shadow.

The displacement is converted out of object units, so a mesh that arrives with a scaled transform
cannot blow the sail up again.

## Physics, and where the rules live

`SailAerodynamics` and `FoilPhysics` are static classes of pure functions with no scene
dependency, covered by `Assets/Tests/EditMode/SailAerodynamicsTests.cs` and `FoilPhysicsTests.cs`.

Points of sail are tested by **terminal speed**, not instantaneous force. At equal apparent wind a
dead run actually makes the most force — a stalled sail is a parachute and its drag coefficient
beats any lift coefficient. A reach is faster because running away from the wind kills the
apparent wind while reaching builds it.

**Induced drag is what creates the no-go zone.** Drag grows as the square of the lift, so close to
the wind the drive is a small difference between large opposing terms and goes negative. Without
that term pointing high is free and the craft happily sails 20° off the wind, which nothing can.
`NoGoHalfAngle` is advisory, for UI only — the collapse is smooth: ~2/3 speed at 45°, ~1/3 at 25°,
stopped by 15°.

---

# Dune Ornithopter

`Assets/Prefabs/agents/vehicle/DuneOrnithopter.prefab` — a 10 m flapping-wing flyer, built from
`Assets/Art/Models/Vehicles/Ornithopter/dune_ornithopter.fbx` by
[`OrnithopterBuilder`](../../Editor/Vehicles/OrnithopterBuilder.cs) — **Tools ▸ Vehicles ▸ Build
Dune Ornithopter Prefab**.

Unlike everything else here it is **not placed in the world**. The player carries it folded as an
inventory item (`WingPackItem`, built by **Tools ▸ Vehicles ▸ Build Wing Pack Item**) and uses it in
mid-air; the craft spawns already mounted, with the rider prone in the cradle, and despawns on
landing or bail-out. `mountableByDirectInteraction` is off for that reason — there is nothing to
walk up to.

W/S pitch, A/D roll, **Space** flap, **Left Ctrl** tuck and dive, **Escape** bail out.

Flight is an energy model rather than a throttle: airspeed is bought with altitude or with
stamina-limited flapping, and the wing stalls if asked for too much. The physics and the 30-bone
wing articulation live in `SpaceGame.Vehicles.Ornithopter`; the motor is in Assembly-CSharp because
`IRiderControllable` is. **See
[`Assets/Scripts/Vehicles/Ornithopter/README.md`](Ornithopter/README.md)** — in particular the
per-side axis-sign table, which is the one part of the rig that is easy to get silently wrong.
