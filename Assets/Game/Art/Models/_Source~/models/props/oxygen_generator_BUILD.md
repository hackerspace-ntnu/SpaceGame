# Oxygen Generator — build record

A wall-mounted plant that refills oxygen bottles and charges power cells, laid
out from a photograph of a service-module oxygen unit and rendered in the
stylised sci-fi language of the concept sheet: pale enamel, one saturated accent
per function, wide chamfers, and as few parts as the read allows.

**Final, hand-edited: 1.077 W × 0.447 D × 2.126 H**, and asymmetric — the drum
became a full-height cylinder standing off the tower's right side (x 0.49), so
the envelope runs x −0.404 … +0.673. A bottle plugged in reaches 0.86 m out from
the wall.

> **`oxygen_generator.blend` is hand-edited and is the source of truth.**
> Never re-run `oxygen_generator.py` over it. The script is historical record.

Design basis: the receptacle is the signifier for each verb the machine has
(`GDC-L1-UX-0004` — make the right action obvious and the wrong one hard), and
the two docks differ in **shape** before they differ in colour, because colour
alone cannot carry the message (`GDC-L1-UX-0003`). A round collar physically
will not accept a rectangular cell, and vice versa.

## Reused, by path

- `components/props/grid_panel.py` — `pegboard()` builds the rack panel behind
  the unit. Its builders take the rectangle to fill, which is what that file
  exists for. It gets its **own** material list: `grid_panel.MATS` is six
  entries in a different order from this model's eighteen.
- `components/mechanical/panel_control.py` — `connector_strip`, `tube_path`.
  MATS indices 0–9 matched index-for-index (the `repair_station` precedent).
- `components/mechanical/dock_cradle.py` — the Collar and the Shoe, appended and
  renamed to their roles on this machine.
- `_tracked.TrackedPart` with `restamp()` before every bevel pass.

## New components, and why each is separate

`components/mechanical/dock_cradle.blend` — the receptacles. Separate from the
generator because a receptacle is the same signifier on every machine with that
verb: a charging rack in the lander, a refuelling post at an outpost. Three
variations, each a handful of primitives under a wide bevel:

| Collection | What it accepts | Needed / ahead |
|---|---|---|
| `Coll_DockCradle_Collar` | a bottle, plugged in base-first, standing out at 90° | the request |
| `Coll_DockCradle_Shoe` | a slab power cell, lying on its back | the request |
| `Coll_DockCradle_Clamp` | a bottle standing upright against a wall | built ahead |

`components/props/oxygen_tank.blend` and `components/props/power_cell.blend`
have their own build records.

## Assembly

One collection, `Coll_OxygenGenerator`. Five blocks up the column, the
photograph's own division:

| z | Part | Role |
|---|---|---|
| 1.86 – 2.10 | `Mesh_OxyGen_ControlHead` | three yellow valve caps, connector bank, the one amber lamp |
| full height | `Mesh_OxyGen_HatchDrum` | a tall cylinder standing off the right flank (hand edit; it began as a 0.48 m drum behind the dock) |
| **1.60** | `Mesh_OxyGen_Hatch` | **the tank dock's face plate** — the bolted disc a bottle plugs into |
| 1.60 | `Cylinder` | a small plug at the dock's centre, added by hand |
| 0.90 – 1.32 | `Mesh_OxyGen_Tower` (stack) | banded process section with a louvre vent |
| **0.70** | `Mesh_OxyGen_CellDock_*` | **the cell dock** — green-lipped slot with a rectangular socket |
| 0.00 – 0.18 | `Mesh_OxyGen_BasePanel` | three capped service ports, a gauge, two connectors |
| — | `Mesh_OxyGen_Straps` | three webbing straps and brass buckles to the rack |
| — | `Mesh_OxyGen_RackPanel` | the pegboard bulkhead behind |

`Marker_OxyGen_TankDock` (0, −0.350, 1.600) and `Marker_OxyGen_CellDock`
(0, −0.284, 0.590) are 6 mm cubes whose **origins are the docked poses**, so a
Unity builder parents an item to a transform instead of re-deriving the
arithmetic from three files. Both were moved by hand and both still check out:
a real bottle's skirt engages the collar bore by **74 mm** and the hatch by
8 mm; a real slab cell sits in its slot with its port in the socket.

Materials: `Mat_Paint_White_Arctic` (shell), `Mat_Neutral_Panel_Grey`,
`Mat_Neutral_Slate_Dark` (recesses), `Mat_Paint_Safety_Orange` (tank dock),
`Mat_Paint_Cell_Green` (cell dock), `Mat_Plastic_Safety_Yellow` (service caps),
`Mat_Emissive_Amber` / `Mat_Emissive_Green_CRT`, `Mat_Fabric_Canvas_Faded` and
`Mat_Metal_Brass_Tarnished` (straps).

**One material added to the palette:** `Mat_Paint_Cell_Green` (`#5C9440`). The
painted-hull family's three greens are all desaturated — `Roof_Green` is a faded
military topcoat that goes grey beside safety orange, `Mint_Pastel` is a cottage
wall, `Olive_Deep` is a shadow tone — and none can hold its own as a peer accent
against `Safety_Orange` and `Safety_Yellow`. `palette.py check` confirmed nothing
within range before adding.

## Hand edits on top of the generated version

The file was taken into Blender and finished by hand. Measured against the
generated build it came from:

- `Mesh_OxyGen_Filler` — the arm that reached down onto a docked bottle's cap —
  **deleted**, and a small `Cylinder` added at the dock centre in its place.
- `Mesh_OxyGen_HatchDrum` restaged: scale (0.711, 0.711, **−3.652**) and moved
  to x 0.492, turning a 0.48 m drum behind the dock into a full-height cylinder
  beside the tower.
- `Mesh_OxyGen_Tower` widened, scale (1.393, 1, 1) → 0.808 m across.
- `Mesh_OxyGen_RackPanel` slightly rescaled and pulled 36 mm forward.
- Cell dock and its marker lowered ~80 mm.

Two things to know about that state, neither of which is a reason to change it:

- **The drum carries a negative scale**, so its transform inverts handedness
  (determinant −1.846, world signed volume −0.190). Blender draws it correctly;
  an FBX carries the negative scale straight through and Unity renders it
  inside-out. `oxygen_generator_export.py` passes `fix_inverted=True`, which
  bakes the transform and recalculates normals **in memory at export time** —
  the .blend is never written to. Verified by re-importing the shipped FBX:
  14 meshes, 0 inverted.
- **`Cylinder` is an auto-generic name.** `_buildlib.save()` would have rejected
  it and the library's naming convention forbids it, but it is hand-authored
  geometry so it has been left exactly as it is. It ships into the FBX under
  that name.

## What was cut in the second pass, and why

The first build lay the bottle **along** the wall in a saddle cradle, with pipe
runs up both flanks, a pump housing, a slatted equipment panel and a fascia
carrying six kinds of switchgear. Two thirds of the bottle disappeared behind
cradle ribs and a filling yoke, and the machine read as grey texture rather than
as a shape. All of it is gone. The bottle now plugs straight into the hatch and
is the most legible thing on the wall — correct, because it is the only part of
the machine a player ever touches.

## Gotchas this build produced

- **Everything ended flush on the machine's back plane.** Plinth, corner posts,
  process stack, bands, drum shoulders and fascia all stopped at y = 0, the same
  plane as the tower's own back: 15 clashing pairs, 0.353 m². Fixed by a stepped
  `BACK` table — only the tower reaches the mounting plane, every block bolted
  to it stops 6–24 mm short. Nothing back there is ever seen.
- **The 10 mm stylised bevel destroys panel hardware.** On a 14 mm valve slot or
  a 22 mm vent slat it eats the shape *and* swells it into neighbours that were
  clear. `_emit` now takes a `fine` list bevelled at 3 mm, applied **before** the
  coarse pass.
- **The valve caps were three floating discs.** Grey bezel, yellow cap and dark
  slot were each stacked in front of the previous one with a 3–4 mm gap, so the
  cap did not sit in its bezel — it hovered. Each now bites into the one behind.
- **A coaxial fill coupling cannot meet the bottle end-on.** The bottle's cap is
  its fattest part and the filler has to clear it; that is why the arm comes down
  from above. (In the first pass this was also blocked by a wire bail handle,
  since removed.)

`_zverify.py` on the generated build: **1 clashing pair, 0.002 m²** — two
opposed end-caps of a single connector pin inside
`panel_control.connector_strip`, which cannot occlude each other. Down from
15 pairs / 0.353 m².

On the **final hand-edited file: 5 pairs, 0.007 m²**. Four are new and all the
same cause — widening the tower put `Mesh_OxyGen_Straps`' side segments exactly
on the tower's new side plane, at 0.000 mm separation, near (±0.3, −0.3, 0.4)
and (±0.3, −0.3, 1.1). A scale applied to one part of an assembly moves its
faces onto planes that other parts were clear of; the fix is to nudge the strap
ends 3 mm past the tower rather than to un-widen it.

## Shipping

`oxygen_generator_export.py` → `Assets/Game/Art/Models/Props/oxygen_generator.fbx`
(14 meshes, 11 572 tris, 21 materials localised, `fix_inverted=True`).
No Unity builder or gameplay code exists yet — there is no oxygen or battery
system in `Assets/Game/Scripts` to wire it to.
