"""Oxygen tank — the pressure bottle the player carries and refills.

>>> HAND-EDITED. `oxygen_tank.blend` is the source of truth and carries edits
>>> that exist nowhere else. NEVER re-run this script over it — see the build
>>> record next to it for what the hand edits were.


One model, deliberately. It began as a family of four canisters in assorted
proportions and colours; the brief is a single bottle, and a single bottle that
is instantly recognisable in a dozen contexts is worth more here than four that
have to be told apart.

    O 0.22 x 0.48, pale enamel barrel, orange cap / collar / skirt.

Simple and stylised, in the reference art's language: a pale body with ONE
saturated accent, a chunky overhanging cap, two raised ribs, a splayed foot with
legs, and a wide chamfer on every moulding. No wire bail — a swept tube handle
arcing over the cap was thin, fiddly geometry that fought the flat-shaded look
everything else here has, and it occupied exactly the space the generator's
filler head needs.

The accent is structural, not decoration
----------------------------------------
Cap, collar band and skirt all wear the orange, and it is the only saturated
thing on the object. Those are also the parts with a distinct shape, so the
bottle survives being read in shadow or by a colour-blind player
(GDC-L1-UX-0003 — never carry meaning in colour alone).

Orientation and origin
----------------------
The tank STANDS. Its axis is +Z, its face — ribs, window — looks along -Y (the
library's forward), and the origin is the centre of the base: the point a shelf,
a hand, or the generator's collar positions it by.

    blender --background --python oxygen_tank.py -- --out oxygen_tank.blend

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
from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from _tracked import TrackedPart  # noqa: E402

# Index 0 is what every bevelled edge in the file gets stamped with
# (`bmesh.ops.bevel` assigns material index 0 to the faces it creates), so it is
# structural steel and never an accent. Indices 0-9 are `panel_control.MATS`
# position for position; 10-15 are this family's, shared with `power_cell.py`
# and `dock_cradle.py` so parts from all three can carry one material list.
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

# Wide on purpose: the reference art's chamfer is a read-at-distance feature,
# not an edge break. `panel_control`'s 1.2 mm vanishes at arm's length.
BEVEL_W = 0.005
SEG = 20

# --- the shape, and the numbers other files dock against --------------------
# `components/mechanical/dock_cradle.py` and `models/props/oxygen_generator.py`
# import these rather than repeating them. A second copy of any of them is how a
# collar ends up 8 mm too tight with nothing in either file looking wrong.
OXY_R = 0.100                 # barrel radius
OXY_CAP_R = 0.110             # cap radius, the widest point on the bottle
OXY_SKIRT_R = 0.108           # base skirt radius - what a collar's bore has to
                              # swallow, and 8 mm wider than the barrel
OXY_LEN = 0.480               # base to the top of the cap
OXY_PLUG = 0.046              # how deep the base goes into a collar's throat:
                              # the whole skirt, so the barrel starts flush with
                              # the collar's mouth

Z_SKIRT = 0.046
Z_SLEEVE = 0.158
Z_BARREL = 0.396
Z_COLLAR = 0.400
Z_SHOULDER = 0.428


def _around(r, ang, out_by=0.0):
    """Frame for a detail sitting on the barrel at angle `ang` from the front.

    Returns the rotation and a centre point `out_by` metres proud of radius `r`.
    Under this rotation local **+X points radially outward**, +Y runs
    tangentially and +Z stays up — verified, not assumed:
    `Matrix.Rotation(a, 4, 'Z') @ (1,0,0) == (cos a, sin a, 0)`, which is exactly
    `out`. Reading local +Y as the radial axis is the 90-degree error this
    helper exists to stop, and it hides well: a rib turned that way still looks
    like a rib, just thin in the wrong direction.
    """
    a = -math.pi / 2 + ang
    out = Vector((math.cos(a), math.sin(a), 0.0))
    return Matrix.Rotation(a, 4, 'Z'), out * (r + out_by)


def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.restamp()
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll, origin=origin)


def skirt(coll, mats):
    """Splayed foot with six legs standing proud of it.

    The legs make the base read as moulded rather than as a painted band, and
    they are what the generator's collar lugs turn behind. Each sits at
    r - 0.002 and is 28 mm deep radially, so it protrudes 12 mm and is buried
    16 mm — a leg placed exactly on the surface would put two coplanar faces in
    one plane and flicker.
    """
    p = TrackedPart(mats)
    hard = []
    p.cyl((0, 0, Z_SKIRT / 2.0), OXY_SKIRT_R, Z_SKIRT, 'Z', SEG, ORANGE,
          radius_top=OXY_SKIRT_R - 0.016)
    # 10 mm down from the skirt's top, not on it: level with the body sleeve's
    # own bottom the two annuli sat 1 mm apart and flickered.
    p.cyl((0, 0, Z_SKIRT - 0.010), OXY_SKIRT_R - 0.020, 0.012, 'Z', SEG, GREY)
    for i in range(6):
        a = 2 * math.pi * i / 6
        hard += p.box((math.cos(a) * (OXY_SKIRT_R - 0.002),
                       math.sin(a) * (OXY_SKIRT_R - 0.002), Z_SKIRT * 0.46),
                      (0.022, 0.028, Z_SKIRT * 0.86), ORANGE,
                      rot=Matrix.Rotation(a, 4, 'Z'))
    # Rubber foot ring. Started 2 mm BELOW the skirt's own base plane: flush at
    # z = 0 the two bottom faces were coincident and flickered.
    p.cyl((0, 0, 0.003), OXY_SKIRT_R + 0.006, 0.012, 'Z', SEG, RUBBER)
    return _emit(p, hard, "Mesh_OxygenTank_Skirt", coll)


def body(coll, mats):
    """Barrel, grey sleeve, orange collar band and the shoulder cone.

    The sleeve is what stops a 0.35 m cylinder reading as a pipe: it divides the
    silhouette at a third of the height, which is where the eye wants a
    division. Every sub-part overshoots its neighbour's plane rather than
    meeting it, because a coaxial cylinder that shares a cap plane with its
    parent z-fights and nothing in the numbers looks wrong.
    """
    p = TrackedPart(mats)
    hard = []
    p.cyl((0, 0, (Z_SKIRT + Z_BARREL) / 2.0), OXY_R, Z_BARREL - Z_SKIRT, 'Z',
          SEG, SHELL)
    p.cyl((0, 0, (Z_SKIRT - 0.020 + Z_SLEEVE) / 2.0), OXY_R + 0.004,
          Z_SLEEVE - Z_SKIRT + 0.020, 'Z', SEG, GREY)
    p.cyl((0, 0, Z_COLLAR), OXY_R + 0.006, 0.024, 'Z', SEG, ORANGE)
    p.cyl((0, 0, (Z_BARREL + Z_SHOULDER) / 2.0), OXY_R, Z_SHOULDER - Z_BARREL,
          'Z', SEG, SHELL, radius_top=0.090)
    # Two sunk seams: a dark ring 3 mm UNDER the surface, so each reads as a
    # shut line between mouldings and never as a bracelet sitting on one.
    for gz in (0.240, 0.300):
        p.cyl((0, 0, gz), OXY_R - 0.003, 0.010, 'Z', SEG, BLACK)
    return _emit(p, hard, "Mesh_OxygenTank_Body", coll)


def cap(coll, mats):
    """Chunky moulded cap that overhangs the barrel, with two valve ports.

    The overhang is the strongest single cue on the object: it puts a hard
    shadow line directly under the accent colour, which is what makes this read
    as a sealed pressure vessel rather than as a painted tube.
    """
    p = TrackedPart(mats)
    hard = []
    p.cyl((0, 0, (Z_SHOULDER + OXY_LEN) / 2.0), OXY_CAP_R, OXY_LEN - Z_SHOULDER,
          'Z', SEG, ORANGE)
    p.cyl((0, 0, OXY_LEN - 0.006), OXY_CAP_R - 0.014, 0.016, 'Z', SEG, GREY)
    # Lip. Dropped 5 mm BELOW the cap's own base plane rather than starting on
    # it, for the same coplanar reason as the sleeve.
    p.cyl((0, 0, Z_SHOULDER + 0.001), OXY_CAP_R + 0.006, 0.014, 'Z', SEG,
          ORANGE)
    for i in range(2):
        a = math.pi * i + math.pi / 4
        c = (math.cos(a) * OXY_CAP_R * 0.44, math.sin(a) * OXY_CAP_R * 0.44,
             OXY_LEN - 0.004)
        p.cyl(c, 0.021, 0.024, 'Z', 12, DARK)
        p.cyl((c[0], c[1], OXY_LEN + 0.006), 0.013, 0.012, 'Z', 12, BLACK)
    return _emit(p, hard, "Mesh_OxygenTank_Cap", coll,
                 origin=(0, 0, Z_SHOULDER))


def ribs(coll, mats):
    """Two raised ribs down the flanks, one carrying a latch clip.

    Placed at +-0.44 pi from the front rather than on the back, where the first
    build put them: a rib on the far side of a cylinder is invisible from every
    angle a player sees the bottle from, and it may as well not exist.
    """
    p = TrackedPart(mats)
    hard = []
    for ang, latch in ((math.pi * 0.44, True), (math.pi * 1.56, False)):
        # Stops 6 mm short of the barrel's own top plane; level with it, the
        # rib's top face and the barrel's cap were coplanar.
        zc = (Z_SLEEVE + Z_BARREL - 0.006) / 2.0
        h = Z_BARREL - Z_SLEEVE - 0.006
        rot, base = _around(OXY_R, ang, 0.004)
        hard += p.box((base.x, base.y, zc), (0.040, 0.076, h), GREY, rot=rot)
        _, inlay = _around(OXY_R, ang, 0.012)
        hard += p.box((inlay.x, inlay.y, zc), (0.030, 0.048, h * 0.82), SHELL,
                      rot=rot)
        if latch:
            _, clip = _around(OXY_R, ang, 0.030)
            hard += p.box((clip.x, clip.y, Z_BARREL - 0.036),
                          (0.044, 0.056, 0.056), DARK, rot=rot)
            hard += p.box((clip.x, clip.y, Z_BARREL - 0.036),
                          (0.056, 0.030, 0.028), ORANGE, rot=rot)
    return _emit(p, hard, "Mesh_OxygenTank_Ribs", coll)


def gauge(coll, mats):
    """Contents window on the front — a dark plate with a lit strip.

    The one emissive on the bottle, deliberately small: it says 'this has a
    level in it' without competing with the cap for attention.
    """
    p = TrackedPart(mats)
    y = -(OXY_R - 0.006)
    z = 0.268
    hard = p.box((0, y, z), (0.090, 0.026, 0.066), BLACK)
    hard += p.box((0, y - 0.009, z), (0.064, 0.016, 0.044), SLATE)
    p.box((0, y - 0.017, z - 0.006), (0.050, 0.006, 0.022), CRT)
    for i in range(3):
        p.box((0.038, y - 0.016, z + 0.008 + i * 0.012), (0.014, 0.006, 0.005),
              ORANGE)
    return _emit(p, hard, "Mesh_OxygenTank_Gauge", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_OxygenTank")
    skirt(coll, mats)
    body(coll, mats)
    cap(coll, mats)
    ribs(coll, mats)
    gauge(coll, mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
