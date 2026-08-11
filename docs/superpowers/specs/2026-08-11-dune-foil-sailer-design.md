# Dune Foil Sailer — design

A wind-driven hydrofoil sand craft built from `Assets/Models/_Source~/models/vehicles/dune_foil.blend`. The player
walks the deck in normal first person and sails it by working the rigging: hoisting and furling
sails, sheeting them in and out, and raking the mast. There is no rudder and no cockpit — sail
balance is the entire control scheme.

## Source model

`Assets/Models/_Source~/models/vehicles/dune_foil.blend` — 78 objects, flat hierarchy, no parenting, no pivots.

| Part | Objects | Extent (Blender, Z-up) |
|---|---|---|
| Hull | `Sphere.001` | 18.2 m fore-aft, 5.5 m beam, z 11.3 → 14.9 |
| Foil strut + wing | `Cube`, `Cylinder.005–.008` | z −1.9 → 12 (13.9 m) |
| Main sail | `Plane`, `Plane.001` | z 16.3 → 34.6, swung out to port |
| Jib | `Plane.003`, `Plane.002` | forward, z 13.6 → 21.1 |
| Wing sails (port/stbd pair) | `Plane.004/.005`, `Plane.008/.009` | low, outboard, z 13.6 → 17.8 |
| Battens | `Cube.012–.051` | one per sail panel |
| Rigging | 10 × `BézierCurve*` | static curves, cannot follow a moving sail |

61 materials are in use across those objects, but they carry only **5 distinct base colours**.

## Decisions taken

| Question | Decision |
|---|---|
| Sand line | Near the foil. At full speed the deck flies ~13 m up with the whole strut exposed. |
| Steering | Sail balance only. No rudder, no tiller. |
| Sim depth | Real sailing rules, forgiving numbers. |
| Blend file | Derive `dune_foil_rig.blend` by script. The hand-modelled original is never written to. |

## 1. Model rework

`Assets/Models/_Source~/models/vehicles/dune_foil_rig.py` builds `dune_foil_rig.blend` from the original, so the rig
is reproducible and the original stays authoritative for geometry.

### Material dedupe, 61 → 5

One material per colour, remapped across every object; the 24 unused materials are dropped.

| New name | Hex | Linear base colour | Replaces |
|---|---|---|---|
| `Mat_Foil_Wood_Dark` | `#41311F` | 0.050, 0.027, 0.010 | 44 duplicates |
| `Mat_Foil_Sailcloth` | `#E6B575` | 0.800, 0.473, 0.180 | 8 duplicates |
| `Mat_Foil_Wood_Mid` | `#6C4429` | 0.150, 0.054, 0.018 | 5 duplicates |
| `Mat_Foil_Metal_Slate` | `#41464C` | 0.050, 0.059, 0.069 | 3 duplicates |
| `Mat_Foil_Metal_Grey` | `#565656` | 0.092, 0.092, 0.092 | 1 |

### Hierarchy

Pivots are empties, so Unity drives plain transforms — no armature, no skinning.

```
FOIL_Root                     origin at the foil tip; this is the sand contact point
├─ Hull                       hull shell and deck fittings
├─ Mast_Main_Rake             pivot at the mast foot, rotates fore/aft  → mast angle
│  ├─ Mast_Main_Post          the post: always visible, never furled
│  └─ Mast_Main_Yaw           pivot on the mast axis                    → sheeting angle
│     └─ Sail_Main            fabric + battens: hidden when furled
├─ Jib_Rake → Jib_Post + Jib_Yaw → Sail_Jib
├─ WingSail_P_Rake → … → Sail_WingP
├─ WingSail_S_Rake → … → Sail_WingS
├─ Foil_Strut
│  └─ Foil_Wing               pivot for angle of attack
└─ Anchors                    empties at every rope endpoint and sail clew
```

Furling hides the `Sail_*` node — fabric and battens go, post and spar stay.

### Two geometry fixes

The sails are **13 polys each**. A vertex shader cannot belly out a quad, so each sail is
subdivided to a ~12×12 grid and UV'd with `U=0` at the luff (the edge pinned to the mast) and
`U=1` at the leech (the free edge). Vertex colour marks pinned edges so the shader leaves them
alone. Without this, no amount of shader work produces a curved sail.

The 10 Bézier curves are authored for the sail's current pose and cannot follow it once the sail
rotates. They are replaced by **anchor empties at their endpoints**, and Unity redraws every rope
at runtime (§3). Standing rigging then looks static because its anchors are static; running
rigging follows the sail because one of its anchors is the sail's clew.

Export: `Assets/Models/Vehicles/DuneFoil/dune_foil_rig.fbx`, Y-up, transforms applied.

## 2. Sailing model

`SailAerodynamics` — a static class of pure functions, so the rules are readable in one place and
testable without a scene.

- **Apparent wind** = true wind − craft velocity. Everything downstream uses apparent wind, which
  is what makes speed change the felt wind angle and makes tacking behave.
- **Angle of attack** = apparent wind angle − the sail's own angle. Lift and drag come from
  coefficient curves: lift peaks around 15–20°, the sail **luffs** below ~5° (flapping, near-zero
  force) and **stalls** past ~35° (drag-dominated, a slow downwind push only).
- **No-go zone**, ±45° off the true wind: no trim produces drive there, so the player has to tack
  through it.
- Best speed on a **beam reach**. A dead run is slower than reaching.
- **Steering is sail balance.** Yaw torque is the sum over sails of (lateral force × lever arm
  measured from the foil, which is the centre of lateral resistance). The main sits aft of the
  foil, so sheeting it in luffs the bow up into the wind; the jib sits forward, so sheeting it in
  bears the bow away. Furling one sail is a hard turn. The rope stations are the steering wheel.
- **Heel**: lateral force over righting moment rolls the hull, clamped and damped, plus a little
  pitch from acceleration.

## 3. Unity components

`Assets/Scripts/Vehicles/DuneFoil/`, with its own `SpaceGame.Vehicles.DuneFoil` asmdef.

| Script | Job |
|---|---|
| `WindField` | The wind. Base direction and strength, slow drift, a Perlin gust layer. Exposes `Direction`, `Speed`, `SampleAt(pos)`. Single source of truth: the shader and the physics read the same values. |
| `SailSurface` | One sail. Owns hoist state, sheet angle, rake and rope length; asks `SailAerodynamics` for its force each frame and pushes its own shader parameters. |
| `SailRig` | Holds the four `SailSurface`s and sums force and yaw torque. Knows nothing about locomotion. |
| `DuneFoilLocomotion` | Sole owner of the hull transform. Sail force + foil lift + sand drag → velocity, heading, heel. |
| `FoilLift` | Ride height only. Lift ∝ v², sand surface found by a downward raycast. |
| `RiggingStation` | `IInteractable` deck control. **E pays out rope / raises a sail**, **left click hauls in / lowers**. One instance per function, each with its own collider. |
| `RiggingRope` | Draws one rope as a `LineRenderer` between two anchors, with catenary sag from slack. |
| `WalkerPlatformCarrier` | Reused unchanged. It already carries a player standing on a transform-driven deck. |

Ownership rule, matching `SpiderWalkerLocomotion`: `DuneFoilLocomotion` is the only thing that
writes the hull transform. `SailRig` reports forces and never moves anything.

No mount and no camera takeover — the player walks the deck in normal first person and works the
rigging by looking at it. `Interactor` already resolves `IInteractable` by `GetComponent` then
`GetComponentInParent`, and `PlayerInputManager` already exposes both `OnInteractPressed` and
`OnUsePressed`, so the two-button rope control needs no input changes.

`Assets/Editor/Vehicles/DuneFoilBuilder.cs` assembles the prefab from the FBX under
**Tools ▸ Vehicles ▸ Build Dune Foil Prefab**, following the existing `ShipRVBuilder` pattern:
every pivot, collider and station position is measured off the meshes at build time, so
re-exporting the model and re-running lands everything in the right place.

## 4. Ride height

Ride height does double duty: it is the foiling behaviour and it is the answer to boarding a craft
whose deck flies 13 m up.

- **At rest** the foil makes no lift, the craft settles, and the hull bottom rests on the sand —
  deck about 3 m up, reachable by a short boarding ladder the builder assembles at the hull side.
- **As speed builds**, lift ∝ v² raises the hull up the strut until at full speed the foil tip is
  at the sand line and the whole strut is exposed.
- Ride height is critically damped and tracks the dune surface, so it rises and falls over terrain
  instead of snapping.

## 5. Sail shader

`Assets/Shaders/SailCloth.shader`, handwritten URP lit — matching `StylizedTerrain.shader` rather
than a Shader Graph, because it version-controls and diffs.

- **Billow**: vertex displacement along the sail normal, pushed leeward by the wind direction, with
  amplitude `_Billow` set per-sail by `SailSurface` from its actual aerodynamic load. A UV mask
  keeps the luff and foot pinned to their spars so only the belly inflates.
- **Flutter**: two octaves of scrolling noise, amplitude driven by `_Luff` — near zero when the
  sail is drawing, violent when it luffs or furls.
- Double-sided, with normals flipped on the back face so a backed sail lights correctly.

## 6. Verification

EditMode tests in `Assets/Tests/EditMode/SailAerodynamicsTests.cs`:

- inside the no-go zone, no trim produces forward drive
- a beam reach beats a dead run at equal wind
- lift falls off both below the luffing threshold and past the stall angle
- main and jib produce yaw torque of opposite sign
- ride height rises monotonically with speed and settles to hull-on-sand at rest

Then a Play-mode check over the unity-mcp bridge with screenshots, confirming the rig articulates
and the sails inflate.
