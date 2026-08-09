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
  in `Assets/Models/Vehicles/RV/`. Only `ship_model 1.blend` is used. Worth consolidating — if the
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
