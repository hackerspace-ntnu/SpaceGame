# Inventory wall — build record

Built 2026-09-01. A ship's gear wall in the expedition rig's language: 5.40 ×
2.70 m of placement grid on the PlayerShip's starboard main-deck wall, between
the sliding side door and the rear ramp.

Files:

- `models/props/inventory_wall.blend` — the model
- `models/props/inventory_wall.py` — generator (`--out`)
- `models/props/inventory_wall_export.py` — read-only FBX export
- `components/props/grid_panel.blend` / `.py` — the three panel variations, and
  the builder functions the wall tiles them with

---

## Decomposition

| Part | Object | Why it is separate |
|---|---|---|
| Surround | `Mesh_Wall_Surround` | Steel posts, back plate, cross rails and feet. One object because none of it moves and it all shares two materials; splitting would give Unity extra renderers for one static fitting. |
| Bays | `Mesh_Wall_Bays` | The ten grid panels, drawn by `grid_panel.py`'s builders. One object for the same reason. |
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
ten times reads as wallpaper. They are distributed **W P W N W P W N W P** — five
webbed so the rig's language leads, and no two neighbours alike.

The builders take the rectangle to fill rather than a fixed size, so the wall
tiles full-height 0.54 × 2.70 bays out of the same code that draws the module.
Two copies of a webbing field that had to stay on the same pitch is exactly the
drift this avoids.

## Materials

All from the palette; nothing added. `Mat_Fabric_Canvas_Sand` /
`Mat_Fabric_Wing_Ochre` are the rig's dressed colours and are what makes this
read as the backpack's big brother; `Mat_Metal_Steel_Worn` /
`Mat_Metal_Steel_Dark` / `Mat_Metal_Brass_Tarnished` / `Mat_Fabric_Rope_Hemp` /
`Mat_Plastic_Rubber_Black` / `Mat_Emissive_Amber` complete it.

## The two numbers that are not style choices

**Every dimension is a whole multiple of `PackGrid.Cell` = 0.090 m.** The grid
is 60 × 30 = 1800 cells; the bays are 6 cells wide; tape, eyelet and hole pitch
is 0.180 m = 2 cells, phase-aligned to the global grid rather than to each
rectangle, so neighbouring bays continue each other's lines. Gear snaps to that
grid, so decoration off the pitch reads as a rendering fault — the item sits
visibly between the lines it is supposed to sit on. The rig learned this the
hard way; its stitching is at 200/260 mm against a 90 mm cell and the two have
drifted ever since.

**The grid band is z 0.36 … 3.06.** The astronaut is 3 m tall, so the top row
has to stay inside its reach. Tray, plinth and header live outside that band.
Widening the wall is free — the bulkhead it mounts on is 7.9 m long — but
heightening it is not.

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

41 940, of which 39 250 are the bays. That is a hero interior fitting seen from
two metres; the cheapest saving if it ever matters is the brass eyelet field on
the webbed bays (one 6-segment tube per tape crossing).

## Decided here, easy to reverse

- **The tray is deliberately not a second `PackSurface`.** Two placement faces
  on one fitting would mean the player's aim decides which of them a click lands
  on, and the boundary between them is invisible from three metres away.
- **Ten bays of 0.54 m.** Six-cell bays are the largest that keep the eye on the
  grid rather than on the panel; a 0.90 m bay reads as a cupboard door.
