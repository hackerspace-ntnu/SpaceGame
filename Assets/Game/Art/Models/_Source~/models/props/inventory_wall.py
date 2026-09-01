"""The inventory wall — a ship's gear wall in the expedition rig's language.

The rig is 0.72 m of canvas you unfold on the sand. This is the same idea given
a bulkhead: 30 x 22 cells of grid, five bays across, standing on the main deck
of the PlayerShip between the sliding side door and the rear ramp. Gear placed
on it is placed the way gear is placed on the rig — free position on the cell
grid, true size, no slot count — and the C# side reuses the rig's whole
placement layer to do it.

This file authors in the PRE-ENLARGEMENT frame
----------------------------------------------
Every metre below is in the 0.090 m cell the wall was first modelled at, and the
shipped `inventory_wall.blend` is 1.5x all of it — 4.05 x 2.97 m of grid on a
0.135 m cell, which is what `PackGrid.Cell` and `InventoryWallBuilder.SurfaceSize`
now hold. The enlargement is a second script, `inventory_wall_scale.py`, and not
a raised CELL here, because thirty-seven of the lengths in this file and in
`grid_panel.py` are *stock* rather than *pitch* — frame section, tape width, bolt
heads, plate thicknesses, bevel widths — and none of them is derived from CELL.
Raising CELL would reproportion the wall rather than enlarge it. That file's
header carries the full audit. So the pipeline is two steps:

    blender --background --python inventory_wall.py -- --out inventory_wall.blend
    blender --background inventory_wall.blend --python inventory_wall_scale.py

Read every number below as a MODELLING metre. Multiply by
`inventory_wall_scale.SCALE` for what Unity sees.

Why 30 x 22 cells
-----------------
Whole cells in both directions — 660 of them, against the rig's biggest single
face at 9 x 9. **The room is the constraint, and it is measured, not chosen.**
The wall was first drawn 60 x 30 on the 0.090 m cell, which was comfortable; the
2026-09-01 enlargement multiplied every length by 1.5 and the fitting stopped
fitting — 8.46 m wide and 4.95 m tall against a room that has neither.

The room was measured against the ship's BAKED COLLISION
(`player_ship_collision.fbx`), not against its visible meshes, because the two
are not the same shape: the hull skin `Plane.001` curves up off the deck and its
convex decomposition fills that curve, so the outboard half-metre of deck that
looks free is solid to a player. Standing off `PlayerShipBuilder`'s
`WallRibClearance` — raised to 1.00 m in the same change for exactly that reason
— the fitting's own footprint is clear from the deck up, and the headroom over
it is **4.37 m**, capped by one arch-rib buttress (`Cube.007`); the deckhead
proper is 4.79-4.87 m. Fore and aft the run ends at the cockpit dais riser and
the ramp sill, and the builder centres the fitting on the main deck's centre,
which leaves 2.91 m of run forward of that centre and 3.75 m aft.

So the counts were re-picked against those numbers rather than the height being
trimmed alone. At 1.5x, 22 cells of grid plus the 0.54 m tray band and the 0.36 m
header cowl stand 3.87 m off the deck — 89% of the 4.37 m available, i.e. a wall
that runs from the deck most of the way up and stops half a metre short of the
ribs on purpose. 30 cells of grid plus the two stiles are 4.41 m wide, which
centres in the run with 0.75 m clear forward of it and 1.54 m clear aft.

Layout (modelling frame; the shipped file is 1.5x every number here)
-------------------------------------------------------------------
                       +----- header + lamp -----+   z 2.34..2.58
                       | W | P | W | N | W |         z 0.36..2.34
                       +------- tool tray -------+   z 0.00..0.36
    x -1.47                                  x +1.47

Five six-cell bays, drawn by `components/props/grid_panel.py` — the same three
variations it saves as a module, distributed W P W N W so no two neighbours
match. The bay stayed six cells wide when the wall shrank and the bay COUNT
halved instead, because six cells is `grid_panel.MODULE_W` — the module the
component file saves and a locker door reuses — and a wall of five of those is
still the same fitting, where a wall of ten narrower ones would be a different
one. The bay frames stand 0.045 m proud of their canvas and their front faces
are the plane at y = 0 that gear rests on: the panels are recessed BEHIND the
placement surface, which is why a bay frame never pokes through an item lying
across it.

SURF_WallGrid
-------------
The placement face, carrying no size of its own — Unity's `PackSurface` holds
that, exactly as it does for the rig, because a scaled empty would rescale
every item parented under it. The rectangle is printed at build time for whoever
fills in the inspector: 2.70 x 1.98 here, and `inventory_wall_scale.py` prints
the 4.05 x 2.97 that Unity must actually carry.

Its rotation is Z 180 and that is not arbitrary. `PackSurface`'s frame is local
X = u, local Z = v, local Y = the outward normal, and there is NO rotation that
gives u-right, v-up and a -Y normal at once — that triple is left-handed, so
one of the three has to flip. v-up is the one worth keeping on a wall, so u
runs right-to-left as seen by a player facing it. Verify the sense in Unity
after import, never from this file: FBX axis conversion mirrors handedness on
root empties, which is what made the rig's wing folds come out inverted.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
sys.path.insert(0, os.path.join(LIB, "components", "props"))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402
import grid_panel  # noqa: E402

MATS = list(grid_panel.MATS) + [
    "Mat_Emissive_Amber",         # 6  the header strip lamp
    "Mat_Plastic_Rubber_Black",   # 7  tray mat, cable runs
]
SAND, OCHRE, STEEL, DARK, BRASS, CORD, AMBER, RUBBER = range(8)

# The MODELLING cell, not `PackGrid.Cell` — see the header. Everything below is
# in this frame and the shipped .blend is `inventory_wall_scale.SCALE` times it.
CELL = grid_panel.CELL

# --- the placement face ----------------------------------------------------
GRID_W = 30 * CELL            # 2.70 -> 4.05 shipped
GRID_H = 22 * CELL            # 1.98 -> 2.97 shipped
GRID_Z0 = 4 * CELL            # 0.36 above the deck — clears the tray
GRID_Z1 = GRID_Z0 + GRID_H    # 2.34 -> 3.51 shipped, half a metre under the ribs

BAYS = 5
BAY_W = GRID_W / BAYS         # 0.54 — six cells, grid_panel's module width

# W P W N W: three webbed so the rig's language leads, no two neighbours alike
# so the wall does not read as one panel copied five times.
BAY_PATTERN = ("Webbed", "Pegboard", "Webbed", "Netted", "Webbed")

# --- the surround ----------------------------------------------------------
STILE_W = 0.120               # the vertical posts either side
STILE_X = GRID_W / 2.0        # inner edge; posts run outward from here
DEPTH = 0.180                 # how far the whole fitting stands off the wall

TRAY_Z1 = GRID_Z0             # the tray fills everything under the grid
TRAY_D = 0.240                # and reaches further out than the bays do
HEADER_Z1 = GRID_Z1 + 0.240

LAMP_Z = GRID_Z1 + 0.090
LAMP_HALF = GRID_W / 2.0 - 0.180


def _bar(p, a, b, width, depth, mat):
    return grid_panel._bar(p, a, b, width, depth, mat)


PARTS = []


def part(name, bevel=0.005, seg=1):
    def wrap(fn):
        PARTS.append((name, fn, bevel, seg))
        return fn
    return wrap


# ---------------------------------------------------------------------------

@part("Mesh_Wall_Surround")
def _surround(p):
    """The steel that carries the bays and bolts to the bulkhead.

    One object rather than a post-and-rail set, because none of it moves and
    every piece shares a material — splitting it would give Unity six renderers
    for one static fitting.
    """
    x0, x1 = -STILE_X - STILE_W, STILE_X + STILE_W

    # Posts, full height, standing the whole depth off the wall.
    for sx in (-1.0, 1.0):
        p.slab((sx * STILE_X, 0.0, 0.0),
               (sx * (STILE_X + STILE_W), DEPTH, HEADER_Z1), STEEL)

    # Back plate: what the bays screw into, and what stops daylight showing
    # between them. Recessed behind the bay canvas.
    p.slab((-STILE_X, DEPTH - 0.024, GRID_Z0 - 0.024),
           (STILE_X, DEPTH, GRID_Z1 + 0.024), DARK)

    # Cross rails top and bottom of the grid band.
    for z in (GRID_Z0, GRID_Z1):
        p.slab((-STILE_X, 0.0, z - 0.024), (STILE_X, DEPTH * 0.5, z), STEEL)

    # Feet: the fitting stands on the deck rather than floating on the wall.
    for sx in (-1.0, 1.0):
        p.slab((sx * (STILE_X - 0.060), 0.0, 0.0),
               (sx * (STILE_X + STILE_W), TRAY_D, 0.048), DARK)

    p.rivets((x0 + 0.060, 0.004, 0.120), (x0 + 0.060, 0.004, HEADER_Z1 - 0.120),
             8, radius=0.014, height=0.010, axis='Y', mat=DARK)
    p.rivets((x1 - 0.060, 0.004, 0.120), (x1 - 0.060, 0.004, HEADER_Z1 - 0.120),
             8, radius=0.014, height=0.010, axis='Y', mat=DARK)


@part("Mesh_Wall_Bays")
def _bays(p):
    """The ten grid panels, from the shared component builders."""
    for i, variant in enumerate(BAY_PATTERN):
        x0 = -GRID_W / 2.0 + i * BAY_W
        dict(grid_panel.VARIANTS)[variant](p, x0, x0 + BAY_W, GRID_Z0, GRID_Z1)


@part("Mesh_Wall_Tray")
def _tray(p):
    """A parts tray under the grid — the shelf every real gear wall grows.

    Deliberately not a `PackSurface`. It is 0.36 m of clutter space for the eye,
    not a second inventory: two placement faces on one fitting would mean the
    player's aim decides which of them a click lands on, and the boundary
    between them is invisible from three metres away.
    """
    p.slab((-STILE_X, 0.0, TRAY_Z1 - 0.036), (STILE_X, TRAY_D, TRAY_Z1), STEEL)
    p.slab((-STILE_X, 0.0, TRAY_Z1 - 0.030), (STILE_X, 0.024, TRAY_Z1 + 0.048),
           STEEL)
    p.slab((-STILE_X + 0.012, TRAY_D - 0.024, TRAY_Z1 - 0.030),
           (STILE_X - 0.012, TRAY_D, TRAY_Z1 + 0.048), STEEL)

    # Rubber matting in the tray, and dividers on the bay pitch so the tray
    # reads as part of the same rack rather than a shelf that happened to land.
    p.slab((-STILE_X + 0.012, 0.024, TRAY_Z1 - 0.032),
           (STILE_X - 0.012, TRAY_D - 0.024, TRAY_Z1 - 0.026), RUBBER)
    for i in range(1, BAYS):
        x = -GRID_W / 2.0 + i * BAY_W
        p.slab((x - 0.008, 0.030, TRAY_Z1 - 0.030),
               (x + 0.008, TRAY_D - 0.030, TRAY_Z1 + 0.030), DARK)

    # The kick space below it, closed with a plinth.
    p.slab((-STILE_X, DEPTH - 0.048, 0.036), (STILE_X, DEPTH, TRAY_Z1 - 0.036),
           DARK)


@part("Mesh_Wall_Header")
def _header(p):
    """The cowl over the grid, and the housing the lamp sits in."""
    p.slab((-STILE_X, 0.0, GRID_Z1 + 0.024), (STILE_X, DEPTH, HEADER_Z1), STEEL)

    # A hood that throws the light down the face of the wall.
    p.slab((-STILE_X, -0.090, LAMP_Z + 0.054), (STILE_X, 0.0, LAMP_Z + 0.078),
           DARK)
    for sx in (-1.0, 1.0):
        p.slab((sx * STILE_X, -0.090, LAMP_Z - 0.030),
               (sx * (STILE_X - 0.024), 0.0, LAMP_Z + 0.078), DARK)

    # Stencilled bay numbers would be texture work; the physical equivalent is
    # a tab over each bay division, which also breaks up the header's long
    # straight edge (GRID_W, 2.70 here and 4.05 shipped).
    for i in range(1, BAYS):
        x = -GRID_W / 2.0 + i * BAY_W
        p.slab((x - 0.024, -0.012, GRID_Z1 + 0.024),
               (x + 0.024, 0.0, GRID_Z1 + 0.108), OCHRE)


@part("Mesh_Wall_Lamp", bevel=0.002)
def _lamp(p):
    """The strip lamp under the header hood.

    Its own object because it is the one emissive surface here, and a lamp
    sharing a mesh with the steel around it cannot be dimmed, swapped or
    switched off without touching everything else.
    """
    p.slab((-LAMP_HALF, -0.072, LAMP_Z - 0.024), (LAMP_HALF, -0.036, LAMP_Z),
           AMBER)
    for sx in (-1.0, 1.0):
        p.slab((sx * LAMP_HALF, -0.078, LAMP_Z - 0.030),
               (sx * (LAMP_HALF + 0.036), -0.030, LAMP_Z + 0.006), DARK)


def empty(name, loc, coll, rot=(0.0, 0.0, 0.0), size=0.12):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = size
    obj.location = Vector(loc)
    obj.rotation_euler = rot
    coll.objects.link(obj)
    return obj


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    coll = collection("Coll_InventoryWall")

    for name, build, bevel, seg in PARTS:
        p = Part(mats)
        build(p)
        p.bevel(width=bevel, segments=seg)
        p.finish(name, coll, origin=(0.0, 0.0, 0.0))

    # See the header: Z 180 is the only rotation that keeps v pointing up with
    # the normal out of the wall.
    empty("SURF_WallGrid", (0.0, 0.0, (GRID_Z0 + GRID_Z1) / 2.0), coll,
          rot=(0.0, 0.0, math.pi))

    report()
    # The modelling frame. `inventory_wall_scale.py` prints the same table
    # already multiplied, and THAT is the one Unity's inspector has to match.
    print("  SURF_WallGrid  size = %.3f x %.3f m  (%d x %d cells of %.3f)  "
          "[modelling frame]"
          % (GRID_W, GRID_H, round(GRID_W / CELL), round(GRID_H / CELL), CELL))
    print("  grid band z %.3f .. %.3f, fitting %.2f W x %.2f H x %.2f D"
          % (GRID_Z0, GRID_Z1, 2 * (STILE_X + STILE_W), HEADER_Z1, DEPTH))
    save(out)


if __name__ == "__main__":
    main()
