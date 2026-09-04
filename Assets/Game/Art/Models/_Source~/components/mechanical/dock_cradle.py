"""Dock cradle — the hardware that says 'put the thing HERE'.

Three receptacles, one per way a consumable attaches:

    Coll_DockCradle_Collar   a bolted ring on a machine's face that a canister
                             plugs STRAIGHT INTO, skirt first, so the bottle
                             stands out at ninety degrees to the wall
    Coll_DockCradle_Shoe     a shallow slot with a rectangular socket for a
                             slab power cell lying on its back
    Coll_DockCradle_Clamp    a hinged ring clamp for a canister standing up

Why this is a component and not part of the generator
-----------------------------------------------------
A receptacle is the signifier for the one verb a machine has, and it is the
same signifier on every machine that has that verb — the oxygen generator, a
charging rack in the lander, a refuelling post at an outpost. Building it into
the first machine that needed it is how the second machine ends up with a
slightly different cradle that means the same thing.

Two docks on one machine must differ in SHAPE
---------------------------------------------
The collar is a circle and the shoe is a rectangle, before either is painted.
The colour coding — orange on the collar, green on the shoe, each matching what
goes in it — is confirmation on top of a shape difference, never the message
itself (GDC-L1-UX-0003: never encode critical information in colour alone;
GDC-L1-UX-0004: a slab cell physically will not enter a round collar).

Kept deliberately plain
-----------------------
Each of these is a handful of primitives under a wide bevel and nothing else.
They are read at a glance from across a room while a player is deciding where to
walk, so the silhouette has to survive being small — detail added here costs
triangles and reads as grey noise at the only distance that matters.

The mating numbers are IMPORTED, never retyped
----------------------------------------------
`power_cell.py` and `oxygen_tank.py` own the dimensions of the things that
go in here. Two copies of one measurement is exactly how a cell ends up floating
4 mm off its contacts with nothing in either file looking wrong.

Orientation and origin
----------------------
A cradle bolts to a vertical machine face. That face lies in XZ at y = 0, the
hardware grows toward -Y (the library's forward), and the origin is the centre
of the mounting face.

    blender --background --python dock_cradle.py -- --out dock_cradle.blend

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
sys.path.insert(0, os.path.join(LIB, "components", "mechanical"))
from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from _tracked import TrackedPart  # noqa: E402
from power_cell import PORT, SLAB_H, SLAB_W  # noqa: E402
from oxygen_tank import OXY_CAP_R, OXY_SKIRT_R  # noqa: E402

(STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT,
 SHELL, GREY, ORANGE, YELLOW, GREEN, SLATE) = range(16)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Paint_Warn_Red",
        "Mat_Paint_Blue_Station", "Mat_Emissive_Amber",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Green_CRT",
        "Mat_Paint_White_Arctic", "Mat_Neutral_Panel_Grey",
        "Mat_Paint_Safety_Orange", "Mat_Plastic_Safety_Yellow",
        "Mat_Paint_Cell_Green", "Mat_Neutral_Slate_Dark"]

# Wide on purpose. The reference art carries a visible chamfer on every
# moulding; at 6 mm it survives being seen from across a room, which the 1-2 mm
# technical bevel the rest of the library uses on panel hardware does not.
BEVEL_W = 0.006

# --- collar geometry --------------------------------------------------------
# The bore swallows the bottle's base SKIRT, which is the widest thing that has
# to pass through it — not the barrel, which is 8 mm narrower.
BORE_R = OXY_SKIRT_R + 0.008
COLLAR_R = BORE_R + 0.046
COLLAR_D = 0.076               # how far the collar stands off the machine face

CELL_Y = -0.024                # the shoe's contact plane: where a cell's back
                               # sits, clear of the machine's own skin


def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.restamp()
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll, origin=origin)


# ---------------------------------------------------------------------------
# Collar
# ---------------------------------------------------------------------------

def collar(coll, mats):
    """A bolted ring a canister plugs straight into, skirt first.

    The bottle ends up standing out at ninety degrees to the wall, held by its
    own base, with nothing wrapped round its body — so the whole bottle is
    visible and the part a player reaches for is its far end. The cradle this
    replaced lay the bottle along the wall and hid two thirds of it behind its
    own ribs and yoke.

    Nothing here is a half-measure toward a cradle: a ring either accepts a
    cylinder or it does not, and that is the entire affordance
    (GDC-L1-UX-0004).
    """
    p = TrackedPart(mats)
    hard = []
    # Flange, throat, gasket — three rings at three depths, so no face of the
    # collar shares a plane with the machine it bolts to or with its neighbour.
    p.tube((0, -0.024, 0), COLLAR_R, COLLAR_R - BORE_R - 0.016, 0.048,
           axis='Y', seg=24, mat=GREY)
    p.tube((0, -0.056, 0), BORE_R + 0.024, 0.024, 0.036, axis='Y', seg=24,
           mat=ORANGE)
    p.tube((0, -0.010, 0), BORE_R + 0.008, 0.014, 0.020, axis='Y', seg=24,
           mat=RUBBER)
    for i in range(8):
        a = 2 * math.pi * i / 8 + math.pi / 8
        hard += p.box((math.cos(a) * (COLLAR_R - 0.022), -0.040,
                       math.sin(a) * (COLLAR_R - 0.022)),
                      (0.028, 0.030, 0.028), DARK,
                      rot=Matrix.Rotation(a, 4, 'Y'))
    ring = _emit(p, hard, "Mesh_DockCradle_Collar_Ring", coll)

    # Locking lever across the flange: one bar, the part a hand turns.
    p = TrackedPart(mats)
    hard = p.box((0, -COLLAR_D - 0.006, 0), (COLLAR_R * 1.62, 0.038, 0.052),
                 GREY)
    hard += p.box((0, -COLLAR_D - 0.026, 0), (COLLAR_R * 1.12, 0.026, 0.032),
                  ORANGE)
    for s in (-1, 1):
        hard += p.box((s * COLLAR_R * 0.78, -COLLAR_D - 0.014, 0),
                      (0.046, 0.058, 0.066), DARK)
    p.cyl((0, -COLLAR_D - 0.036, 0), 0.028, 0.028, 'Y', 14, CHROME)
    _emit(p, hard, "Mesh_DockCradle_Collar_Lever", coll,
          origin=(0, -COLLAR_D, 0))
    return ring


# ---------------------------------------------------------------------------
# Shoe
# ---------------------------------------------------------------------------

def shoe(coll, mats):
    """A shallow slot with a rectangular socket for a slab cell.

    Four boxes and one socket. The version this replaced had a sunk bay, a
    coloured lip, side rails, a ledge, a catch, a status lamp and two-step
    surrounds, and at the size a player actually sees it that read as a smear of
    grey detail rather than as a slot — the shape was doing none of the work and
    the parts were doing all of the noise.

    Sized from `power_cell`'s own constants with 8 mm of clearance round the
    cell, so it is visibly a slot the cell drops into.
    """
    w, h = SLAB_W + 0.016, SLAB_H + 0.016

    p = TrackedPart(mats)
    hard = p.slab((-w / 2, CELL_Y - 0.006, 0.0), (w / 2, -0.006, h), SLATE)
    hard += p.slab((-w / 2 - 0.034, CELL_Y - 0.040, -0.036),
                   (w / 2 + 0.034, CELL_Y - 0.006, 0.006), GREY)
    hard += p.slab((-w / 2 - 0.028, CELL_Y - 0.048, -0.028),
                   (w / 2 + 0.028, CELL_Y - 0.040, -0.002), GREEN)
    for s in (-1, 1):
        hard += p.box((s * (w / 2 + 0.014), CELL_Y - 0.028, h * 0.48),
                      (0.036, 0.052, h * 0.86), GREY)
    bay = _emit(p, hard, "Mesh_DockCradle_Shoe_Body", coll)

    # The socket half: one rectangular mouth that swallows the cell's port,
    # with 6 mm of lead-in all round. Rectangular against a rectangular plug is
    # the whole key — it enters one way up and no other (GDC-L1-UX-0004).
    p = TrackedPart(mats)
    hard = p.box((0, CELL_Y + PORT[1] / 2 + 0.006, h / 2),
                 (PORT[0] + 0.034, PORT[1] + 0.012, PORT[2] + 0.034), DARK)
    hard += p.box((0, CELL_Y + PORT[1] / 2 + 0.002, h / 2),
                  (PORT[0] + 0.012, PORT[1] + 0.008, PORT[2] + 0.012), BLACK)
    _emit(p, hard, "Mesh_DockCradle_Shoe_Socket", coll)
    return bay


# ---------------------------------------------------------------------------
# Clamp
# ---------------------------------------------------------------------------

def clamp(coll, mats):
    """Hinged ring clamp for a canister standing upright against a wall.

    Built ahead. A third motion again — the bottle stands on a foot plate and a
    ring closes round it, which is how a spare gets stowed on a bulkhead rather
    than plugged into a machine.
    """
    r = OXY_CAP_R + 0.018
    p = TrackedPart(mats)
    hard = p.slab((-0.150, -0.026, 0.0), (0.150, 0.0, 0.034), GREY)
    p.cyl((0, -r - 0.020, 0.024), r + 0.030, 0.030, 'Z', 20, GREY)
    p.cyl((0, -r - 0.020, 0.044), r - 0.004, 0.012, 'Z', 20, RUBBER)
    hard += p.slab((-0.060, -0.030, 0.038), (0.060, 0.0, 0.470), GREY)
    hard += p.slab((-0.042, -0.038, 0.056), (0.042, -0.030, 0.452), ORANGE)
    foot = _emit(p, hard, "Mesh_DockCradle_Clamp_Post", coll)

    p = TrackedPart(mats)
    hard = []
    for z in (0.180, 0.386):
        # Two thirds of a ring, open at the front — that gap is the way in.
        for i in range(15):
            a = math.radians(-118 + i * 16.8)
            c = (math.cos(a) * r, -r - 0.020 + math.sin(a) * r, z)
            hard += p.box(c, (0.024, 0.038, 0.034), STEEL,
                          rot=Matrix.Rotation(a, 4, 'Z'))
        for s in (-1, 1):
            hard += p.box((s * (r - 0.006), -r - 0.020 - r * 0.72, z),
                          (0.034, 0.034, 0.052), DARK)
        hard += p.box((0, -r - 0.020 - r - 0.028, z), (0.056, 0.034, 0.030),
                      ORANGE)
    _emit(p, hard, "Mesh_DockCradle_Clamp_Rings", coll)
    return foot


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    collar(collection("Coll_DockCradle_Collar"), mats)
    shoe(collection("Coll_DockCradle_Shoe"), mats)
    clamp(collection("Coll_DockCradle_Clamp"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
