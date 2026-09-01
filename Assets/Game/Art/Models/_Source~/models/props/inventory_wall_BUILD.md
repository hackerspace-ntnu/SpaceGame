# Inventory wall — build record

Built 2026-09-01, enlarged 1.5× the same day, and **re-cut the same day again**
to fit the room it stands in. A ship's gear wall in the expedition rig's
language: **4.05 × 2.97 m** of placement grid — 30 × 22 cells — on the
PlayerShip's main-deck wall, between the sliding side door and the rear ramp.
The generator authors at 2.70 × 1.98 and every number below that is not marked
otherwise is in that **modelling frame**; see *Enlarged 1.5×* and *Re-cut to fit
the aft room* at the bottom for the frame the game actually sees.

Files:

- `models/props/inventory_wall.blend` — the model
- `models/props/inventory_wall.py` — generator (`--out`), authoring at 1×
- `models/props/inventory_wall_scale.py` — the ×1.5 similarity pass, run in place
- `models/props/inventory_wall_export.py` — read-only FBX export
- `components/props/grid_panel.blend` / `.py` — the three panel variations, and
  the builder functions the wall tiles them with

---

## Decomposition

| Part | Object | Why it is separate |
|---|---|---|
| Surround | `Mesh_Wall_Surround` | Steel posts, back plate, cross rails and feet. One object because none of it moves and it all shares two materials; splitting would give Unity extra renderers for one static fitting. |
| Bays | `Mesh_Wall_Bays` | The five grid panels, drawn by `grid_panel.py`'s builders. One object for the same reason. |
| Tray | `Mesh_Wall_Tray` | A parts tray under the grid, with dividers on the bay pitch. |
| Header | `Mesh_Wall_Header` | The cowl over the grid plus the bay tabs. |
| Lamp | `Mesh_Wall_Lamp` | **Its own object.** The one emissive surface here; a lamp sharing a mesh with the steel around it cannot be dimmed, swapped or switched off without touching everything else. |
| Surface | `SURF_WallGrid` | The empty `PackSurface` sits on. Identity-scaled and carrying no size — a scaled empty would rescale every item Unity parents under it. |

### New component: `components/props/grid_panel.blend`

Three variations, saved at a 0.54 × 0.90 m module (6 × 10 cells) that tiles a
bay of this wall **and** a locker door:

- `Coll_GridPanel_Webbed` — canvas over a frame, webbing tapes both ways, brass
  eyelets on the crossings. The rig's own language, and the default read.
- `Coll_GridPanel_Pegboard` — punched steel plate with hook rails. The
  engineered version, for machine spaces.
- `Coll_GridPanel_Netted` — an open sub-frame with a laced cord net. The cheap
  field version, and the only one you can see through.

The wall needed one; all three were built because a wall of one panel repeated
five times reads as wallpaper. They are distributed **W P W N W** — three webbed
so the rig's language leads, and no two neighbours alike.

The builders take the rectangle to fill rather than a fixed size, so the wall
tiles full-height 0.54 × 1.98 bays out of the same code that draws the module.
Two copies of a webbing field that had to stay on the same pitch is exactly the
drift this avoids.

## Materials

All from the palette; nothing added. `Mat_Fabric_Canvas_Sand` /
`Mat_Fabric_Wing_Ochre` are the rig's dressed colours and are what makes this
read as the backpack's big brother; `Mat_Metal_Steel_Worn` /
`Mat_Metal_Steel_Dark` / `Mat_Metal_Brass_Tarnished` / `Mat_Fabric_Rope_Hemp` /
`Mat_Plastic_Rubber_Black` / `Mat_Emissive_Amber` complete it.

## The two numbers that are not style choices

**Every POSITION is a whole multiple of the cell.** The grid is 30 × 22 = 660
cells; the bays are 6 cells wide; tape, eyelet and hole pitch is 2 cells,
phase-aligned to the global grid rather than to each rectangle, so neighbouring
bays continue each other's lines. Gear snaps to that grid, so decoration off the
pitch reads as a rendering fault — the item sits visibly between the lines it is
supposed to sit on. The rig learned this the hard way; its stitching is at
200/260 mm against a 90 mm cell and the two have drifted ever since.

**Stock sizes are not**, and the distinction is what forced a separate scale
pass rather than a raised `CELL` — see *Enlarged 1.5×*.

**The grid band is z 0.54 … 3.51** (0.36 … 2.34 as modelled). Tray, plinth and
header live outside it. The astronaut is 3 m tall, so the top row is about half a
metre over its head and is reached the way every row is — `WallAimController`
aims along the `Interactor`'s 5 m look ray, not with an arm. Neither the height
nor the width is free any more: both are cut to the aft room, see *Re-cut to fit
the aft room*.

## y = 0 is the placement plane

The bay frames stand 0.045 m proud of their canvas, and **their front faces are
the plane gear rests on**. The panels are recessed behind it, which is why a bay
frame never pokes through an item lying across it. The corner bolts are sunk
into the frame for the same reason: a 16 mm bolt head at every bay corner would
poke through the first thing hung over it.

## `SURF_WallGrid` rotation is Z 180, and it is not arbitrary

`PackSurface`'s frame is local X = u, local Z = v, local Y = the outward normal.
There is **no** rotation giving u-right, v-up and a −Y normal at once — that
triple is left-handed, so one of the three has to flip. v-up is the one worth
keeping on a wall, so u runs right-to-left as seen by a player facing it.

Verify the sense **in Unity after import**, never from the .blend: FBX axis
conversion mirrors handedness on root empties, which is what made the rig's wing
folds come out inverted.

## Triangles

22 138, of which 19 888 are the bays. That is a hero interior fitting seen from
two metres; the cheapest saving if it ever matters is the brass eyelet field on
the webbed bays (one 6-segment tube per tape crossing).

## Decided here, easy to reverse

- **The tray is deliberately not a second `PackSurface`.** Two placement faces
  on one fitting would mean the player's aim decides which of them a click lands
  on, and the boundary between them is invisible from three metres away.
- **Five bays of 0.54 m.** Six-cell bays are the largest that keep the eye on the
  grid rather than on the panel; a 0.90 m bay reads as a cupboard door. When the
  wall had to shrink, the bay COUNT went and the bay WIDTH stayed — six cells is
  `grid_panel.MODULE_W`, the module a locker door reuses.

---

## Enlarged 1.5× — 2026-09-01

The whole physical inventory was scaled up uniformly by **1.5**: the cell
(`PackGrid.Cell` 0.090 → 0.135 m), every `SURF_*` rectangle, the expedition rig,
the size every item is drawn at, and this wall with them. Unity's half of the
number is `PackScale.Factor` in
`Assets/Game/Scripts/Items/Backpack/Placement/PackScale.cs`;
`InventoryWallBuilder.SurfaceSize` is `SurfaceCellsAcross * PackGrid.Cell` ×
`SurfaceCellsUp * PackGrid.Cell`, today **4.05 × 2.97 m**.

**The scale pass moves no cell count.** Bays six cells wide, pitch two cells —
exactly as before, on both sides of it. It is a similarity transform, so every
authored `PackShape` mask and every item's cell footprint are untouched. What it
changed is how much of the room the wall fills and how big the gear on it reads
from across the deck. The counts themselves were re-cut afterwards, for a
different reason — see *Re-cut to fit the aft room*.

**The model had to come along.** The decoration *is* the grid. The bay dividers
are exactly six cells apart, and the webbing tapes, pegboard bosses and net cords
are all on `PITCH` = two cells. Left at 1× under a 1.5× grid, every one of those
lines stops falling where the player is dropping gear — an item lands visibly
between the lines it is supposed to sit on — and worse, `SurfaceSize` would
describe a 4.05 × 2.97 m rectangle over a 2.70 × 1.98 m board, so a third of the
placement area would hang off the fitting into thin air.

### The pipeline is now two steps, not one

```bash
blender -b --python inventory_wall.py -- --out <new>.blend   # 1. generate, modelling frame
blender -b <new>.blend --python inventory_wall_scale.py      # 2. ×1.5, in place
```

Run the generator into `models/props/` itself and rename afterwards, not into a
scratch directory: the palette is a LINKED library at `//../../palette.blend`,
and a file generated somewhere else carries a relative path that no longer
resolves once it is copied back.

`inventory_wall_scale.py` stamps `scene["wall_scale"]` and **refuses a file that
already carries one**. A second pass leaves a 2.25× wall that no number on
Unity's side agrees with, and nothing downstream would say so.

### Why the scale is a second script and not a raised `CELL`

The obvious route is `grid_panel.CELL = 0.135`, letting every derived number
follow. It is wrong, and the audit is worth keeping so nobody re-proposes it.

Only the *positions* in these files are derived from `CELL`. The *stock* is not.
`grid_panel.py` carries **fifteen** distinct lengths that are not multiples of the
cell — `FRAME_T` 0.030, `FRAME_D` 0.045, `FACE_T` 0.018, `TAPE_W` 0.024, `TAPE_P`
0.008, the 0.011/0.016/0.008 corner bolt, the 0.014/0.005/0.020 eyelet, the 0.017
pegboard boss, the 0.007 net cord, the 0.004 bevel — and `inventory_wall.py`
carries **twenty-two** more: `STILE_W` 0.120, `DEPTH` 0.180, `TRAY_D` 0.240, the
0.024 plate thicknesses, the 0.014 rivets, the whole lamp housing, and both bevel
widths. Thirty-seven numbers a change to `CELL` does not touch.

So route (a) is not a similarity transform at all. It would wrap a 0.024 m
webbing tape and 0.030 m frame stock around a bay grown from 0.54 to 0.81 m, and
leave a 0.005 m bevel on a fitting half again as large. The wall would not be
bigger; it would be a differently-proportioned wall, and every comment in both
files relating one measurement to a neighbouring one would have quietly become
false. `grid_panel.py`'s own header used to say "keep every number here a
multiple of CELL" — it was already untrue of fifteen of its nineteen literals,
and that line has been corrected to say *position*.

Route (b) — the one taken, and the one
`components/props/expedition_rig_scale.py` takes over the rig — applies the
enlargement afterwards as what it actually is: one similarity transform of the
finished model. Rotations, parenting, material assignment, face indices, object
names and object *scales* all come out untouched.

### Control diff, run before the enlargement

`inventory_wall.blend` was proved script-reproducible first: regenerated from
`inventory_wall.py` into a scratch directory and compared against the shipped
file object by object — 6 objects, **zero** differences in name, type, parent,
location, rotation, **scale**, vertex/edge/polygon counts, local bounding box or
material assignment, and an identical SHA-256 per-vertex fingerprint on all 5
meshes. Re-run that control before any future regeneration: the answer stops
being zero the moment someone opens the file and models on it by hand.

After the enlargement the same dump was compared against the pre-enlargement
one: **every object location and every mesh bounding box exactly 1.5×, with
rotations, object scales, mesh counts and material assignment identical.**

The control was run again, unchanged, before the re-cut below — regenerate both
steps into `inventory_wall_control.blend`, diff against the shipped file, expect
**7 rows compared, 0 differences** (the six objects plus the `wall_scale` stamp).
It came back zero, which is what made overwriting the file safe. Run it every
time; the answer stops being zero the moment somebody models on the file by hand,
and at that point regenerating destroys their work silently.

### Numbers

Three states: as first modelled, after the ×1.5 pass, and after the re-cut that
made it fit. The middle column never shipped in a ship — it is here because it
is the state every save written on 2026-09-01 was in.

| | as modelled (1×) | ×1.5, 60 × 30 | shipped: ×1.5, 30 × 22 |
|---|---|---|---|
| `SURF_WallGrid` rectangle | 5.400 × 2.700 m | 8.100 × 4.050 m | 4.050 × 2.970 m |
| cell | 0.090 m | 0.135 m | 0.135 m |
| cells | 60 × 30 = 1800 | 60 × 30 = 1800 | 30 × 22 = 660 |
| bays | 10 | 10 | 5 |
| bay pitch | 0.540 m (6 cells) | 0.810 m (6 cells) | 0.810 m (6 cells) |
| grid band above the deck | z 0.360 … 3.060 | z 0.540 … 4.590 | z 0.540 … 3.510 |
| `SURF_WallGrid` height | 1.710 m | 2.565 m | 2.025 m |
| fitting W × H × D | 5.640 × 3.300 × 0.180 m | 8.460 × 4.950 × 0.270 m | 4.410 × 3.870 × 0.270 m |
| tray reach | 0.240 m | 0.360 m | 0.360 m |
| model bounds | 5.640 × 0.330 × 3.300 m | 8.460 × 0.495 × 4.950 m | 4.410 × 0.495 × 3.870 m |
| tris / objects | 41 940 / 6 | 41 940 / 6 | 22 138 / 6 |

`grid_panel.blend` was **not** rescaled and does not need to be: nothing ships
it, it is a reference module, and the wall does not instance it — it calls
`grid_panel.py`'s builder functions with its own rectangle, so the bays are drawn
at the wall's size and then scaled with everything else.

---

## Re-cut to fit the aft room — 2026-09-01

The ×1.5 fitting was **8.46 × 4.95 m** and the lander's aft room has neither
dimension. The grid was re-cut to **30 × 22 cells**, which is a **4.41 × 3.87 m**
fitting, and `PlayerShipBuilder.WallRibClearance` was raised from 0.70 to 1.00 m
in the same change. Capacity went 1800 → **660** cells.

### Measure against the baked collision, not against the ribs you can see

This is the trap, and it cost the first pass of this measurement. The room looks
like it has 3.8–4.6 m of headroom right out to the deck edge when you probe
`ship_lander_blockout.blend`'s visible meshes. It does not. `player_ship.fbx` is
not what the player walks into — `player_ship_collision.fbx` is, and it is a
**convex decomposition**: the hull skin `Plane.001` curves up off the deck, and
the hulls that approximate it fill the curve. For the first ~0.36 m above the
deck, the outboard half-metre of floor is solid to a player with nothing drawn
there.

So at the old `WallRibClearance` 0.70 the fitting's feet, plinth and tray stood
**inside the hull collision** down the whole length of the room — and no probe
caught it, because every check on this wall asks about the placement FACE and
the face starts 0.54 m up. The footprint comes clear at 0.95 m of clearance;
1.00 m is that with a round margin.

### What the room actually offers

Measured in the PlayerShip prefab's own frame, over the fitting's own footprint,
at `WallRibClearance` 1.00 and `WallDepth` 0.36:

| | |
|---|---|
| headroom over the footprint | **4.37 m**, capped by one arch-rib buttress (`COL_Cube.007_3`) |
| deckhead proper | 4.79 – 4.87 m (`COL_Cube.010_4`) |
| run, centred on the main deck's centre | 2.91 m forward (cockpit dais riser), 3.75 m aft (ramp sill) |
| deck top, deck half-width | y 2.986, x half-width 3.589 |

### The counts that fall out of it

| | |
|---|---|
| 30 cells across = 5 six-cell bays | fitting **4.41 m** wide → **0.75 m** clear forward, **1.54 m** clear aft |
| 22 cells up | fitting **3.87 m** tall → **0.50 m** of air above the header cowl |
| | 89 % of the available height: reads as running from the deck most of the way up and stopping on purpose, which is what was asked for |

Across must stay a multiple of **6** or a part-bay is left at one end; up is free
in whole cells. 22 also keeps the pegboard's two hook rails: `grid_panel.py`
derives their spacing from the panel height, and at 18 cells the arithmetic
yields only one.

The three numbers that have to agree, and where each is written:

| number | written in | value |
|---|---|---|
| face, in cells | `InventoryWallBuilder.SurfaceCellsAcross` / `Up` | 30 × 22 |
| face, in the model | `inventory_wall.py` `GRID_W` / `GRID_H` | 30 × `CELL` / 22 × `CELL` |
| `PlayerShipBuilder.WallGridCentreHeight` | printed by `inventory_wall_scale.py` | 2.025 m |
| `PlayerShipBuilder.WallDepth` | `TRAY_D` × `SCALE` | 0.360 m |

Guarded by `PlayerShipTests.PlayerShip_InventoryWallStopsShortOfTheOverhead`,
which measures headroom **from the deck up** rather than from the top of the
fitting — read the other way it would share its input with the thing it checks,
and a wall already through the roof would start its rays above the roof and pass.
