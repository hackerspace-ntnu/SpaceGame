"""components/mechanical/drill_derrick — the machinery that crowns a rig.

A refinery tower is a stack of boxes until something on top of it is obviously a
*machine*. That is this component's whole job: a tapering lattice derrick, the
pipe it handles, the winch that moves it, and the masts that talk to the rest of
the world. Everything here is deliberately spindly, because the silhouette above
the roofline is read against open sky and mass up there kills the height the
tower spent 60 m earning.

`mast_rig` in the structural folder covers the vehicle-scale version of the last
of those — a whip antenna on a cab roof. This is the same idea at 20 m, where
guys, dishes and aircraft warning lights start to matter and a single tapered
cylinder does not read.

Origins are at the base centre for everything that stands up, so a placement is
"put it on the deck at this height".

    blender --background --python drill_derrick.py -- --out drill_derrick.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",      # 0 lattice, chords, pipe
    "Mat_Metal_Steel_Dark",      # 1 nodes, sheaves, machinery
    "Mat_Paint_Safety_Orange",   # 2 the painted crown and drawworks
    "Mat_Paint_White_Arctic",    # 3 clad housings that tie back to the tower
    "Mat_Emissive_Red_Warn",     # 4 aircraft warning lights
    "Mat_Emissive_Amber",        # 5 working lamps
    "Mat_Neutral_Black_Matte",   # 6 recesses
    "Mat_Metal_Rust_Heavy",      # 7 weathering
    "Mat_Metal_Chrome_Scuffed",  # 8 wire rope, polished rod
    "Mat_Paint_Warn_Red",        # 9 painted hazard bands
]
STEEL, DARK, ORANGE, WHITE, WARN, AMBER, BLACK, RUST, CHROME, RED = range(10)

CHORD = 0.26
LACE = 0.15


def along(a, b):
    d = Vector(b) - Vector(a)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    return rot.to_4x4(), d.length


def member(p, a, b, size=CHORD, mat=STEEL, overlap=0.0):
    rot, length = along(a, b)
    p.box((Vector(a) + Vector(b)) / 2.0, (size, size, length + overlap), mat,
          rot=rot)


def taper_legs(p, h, base, top, mat=STEEL):
    """The four raked chords of a derrick, returning their end points."""
    legs = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            a = Vector((sx * base, sy * base, 0.0))
            b = Vector((sx * top, sy * top, h))
            member(p, a, b, CHORD * 1.2, mat)
            legs.append((a, b))
    return legs


def belts(p, legs, h, levels, mat=STEEL):
    """Horizontal rings plus X bracing in each bay, between four raked legs.

    Bracing every face of every bay is what makes a derrick read as a derrick.
    Skipping it — leaving four bare raked legs — is the classic tell.
    """
    order = [0, 1, 3, 2]           # legs walked round the perimeter, not paired
    for k in range(levels + 1):
        t = k / levels
        pts = [legs[i][0].lerp(legs[i][1], t) for i in order]
        for i in range(4):
            member(p, pts[i], pts[(i + 1) % 4], LACE * 1.3, mat)
        if k:
            t0 = (k - 1) / levels
            prev = [legs[i][0].lerp(legs[i][1], t0) for i in order]
            for i in range(4):
                j = (i + 1) % 4
                member(p, prev[i], pts[j], LACE, mat, overlap=LACE)
                member(p, prev[j], pts[i], LACE, mat, overlap=LACE)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def mast(coll, mats):
    """A 16 m tapering lattice derrick with a monkey board and crown.

    Tapered rather than parallel: a parallel lattice reads as a radio tower, a
    tapered one reads as something that pulls pipe. Same triangle count, and it
    is the single decision that makes this component what it is.
    """
    h = 16.0
    p = Part(mats)
    legs = taper_legs(p, h, 3.10, 1.15)
    belts(p, legs, h, 7)
    # Base spread feet, bolting onto whatever deck it stands on.
    for a, _ in legs:
        p.box(a + Vector((0, 0, 0.35)), (1.1, 1.1, 0.7), DARK)
        p.box(a + Vector((0, 0, 0.08)), (1.5, 1.5, 0.22), RUST)
    # Monkey board — the working platform two thirds up.
    zb = h * 0.62
    rb = 3.10 + (1.15 - 3.10) * 0.62
    p.box((0, rb + 0.9, zb), (rb * 2.2, 1.9, 0.16), STEEL)
    for i in range(5):
        x = -rb + rb * 2 * i / 4
        p.box((x, rb + 1.75, zb + 0.55), (0.08, 0.08, 1.05), STEEL)
    p.box((0, rb + 1.75, zb + 1.06), (rb * 2.2, 0.07, 0.07), STEEL)
    p.box((0, rb + 1.75, zb + 0.56), (rb * 2.2, 0.07, 0.07), STEEL)
    for sx in (-1, 1):                              # board support knees
        p.box((sx * rb * 0.7, rb + 0.55, zb - 0.9), (0.16, 2.0, 1.9), DARK,
              rot=Matrix.Rotation(math.radians(42), 4, 'X'))
    # Crown: sheave block, dead line anchor, warning light.
    p.box((0, 0, h + 0.45), (3.0, 3.0, 0.9), ORANGE)
    p.box((0, 0, h + 1.15), (2.2, 2.2, 0.55), DARK)
    for i in range(3):
        p.cyl((0, -0.7 + i * 0.7, h + 1.15), 0.52, 0.22, 'Y', seg=12, mat=STEEL)
    p.cyl((0, 0, h + 1.72), 0.28, 0.62, 'Z', seg=8, mat=DARK)
    p.cyl((0, 0, h + 2.12), 0.30, 0.34, 'Z', seg=8, mat=WARN)
    # Travelling block on a wire, hanging in the derrick.
    p.cyl((0, 0, h * 0.62), 0.05, h * 0.72, 'Z', seg=6, mat=CHROME)
    p.box((0, 0, h * 0.30), (1.0, 1.0, 1.9), DARK)
    p.cyl((0, 0, h * 0.30 - 1.2), 0.34, 0.9, 'Z', seg=10, mat=ORANGE)
    for sx in (-1, 1):                              # working lamps
        p.box((sx * 2.4, -2.4, h * 0.48), (0.42, 0.42, 0.42), DARK)
        p.cyl((sx * 2.4, -2.75, h * 0.48), 0.26, 0.20, 'Y', seg=8, mat=AMBER)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Derrick_Mast", coll)


def pipe_rack(coll, mats):
    """A stand of drill pipe in a fingerboard frame, 12 m tall.

    Vertical repetition at a different rhythm from lattice, which is why it is
    worth having next to the mast rather than folded into it.
    """
    p = Part(mats)
    h, rows, cols = 12.0, 3, 9
    p.box((0, 0, 0.30), (cols * 0.52 + 1.0, rows * 0.62 + 1.0, 0.6), DARK)
    for r in range(rows):
        y = (r - (rows - 1) / 2.0) * 0.62
        for c in range(cols):
            x = (c - (cols - 1) / 2.0) * 0.52
            # Slight height jitter so the tops do not form a ruled line.
            hh = h - ((c * 7 + r * 3) % 5) * 0.34
            p.cyl((x, y, hh / 2 + 0.6), 0.115, hh, 'Z', seg=6, mat=STEEL)
            p.cyl((x, y, hh + 0.6), 0.145, 0.34, 'Z', seg=6, mat=ORANGE)
    # Fingerboard: the comb that holds the tops in place.
    for lvl in (0.42, 0.86):
        z = 0.6 + h * lvl
        p.box((0, 0, z), (cols * 0.52 + 1.2, 0.16, 0.30), STEEL)
        for r in range(rows):
            y = (r - (rows - 1) / 2.0) * 0.62
            p.box((0, y, z), (cols * 0.52 + 0.8, 0.10, 0.22), STEEL)
        p.box((0, 0, z - 0.5), (0.14, rows * 0.62 + 1.2, 1.0), DARK)
    p.box((0, rows * 0.62 / 2 + 0.9, 0.6 + h * 0.86), (cols * 0.52 + 1.2, 1.5,
                                                       0.14), STEEL)
    p.bevel(width=0.025, segments=1)
    return p.finish("Mesh_Derrick_PipeRack", coll)


def winch(coll, mats):
    """A drawworks skid: drum, brake bands, motor, control stand.

    Sits on a deck at the derrick's foot. Modelled as a skid so it can be
    dropped anywhere machinery is wanted, not only under a mast.
    """
    p = Part(mats)
    p.box((0, 0, 0.28), (5.4, 3.2, 0.56), DARK)               # skid
    for sx in (-1, 1):
        p.box((sx * 2.4, 0, 0.30), (0.5, 3.6, 0.6), STEEL)
    p.cyl((0, 0, 1.55), 1.05, 3.0, 'X', seg=14, mat=STEEL)    # drum
    for sx in (-1, 1):
        p.cyl((sx * 1.55, 0, 1.55), 1.30, 0.24, 'X', seg=14, mat=ORANGE)
        p.cyl((sx * 1.9, 0, 1.55), 0.34, 0.5, 'X', seg=10, mat=DARK)
        p.box((sx * 1.9, 0, 0.85), (0.7, 1.2, 1.5), DARK)
    for i in range(14):                                       # spooled rope
        p.cyl((-1.35 + i * 0.20, 0, 1.55), 1.12, 0.18, 'X', seg=12, mat=CHROME)
    p.box((-2.2, 1.9, 1.15), (2.0, 1.4, 1.4), WHITE)          # motor housing
    p.cyl((-2.2, 2.7, 1.9), 0.24, 1.1, 'Z', seg=8, mat=STEEL)
    p.box((2.1, -1.9, 1.20), (1.0, 0.7, 1.3), STEEL)          # control stand
    p.box((2.1, -1.9, 1.95), (1.1, 0.5, 0.5), BLACK)
    p.cyl((2.1, -2.2, 1.6), 0.14, 0.9, 'Y', seg=6, mat=RED)   # brake lever
    p.box((0, -1.8, 0.75), (4.0, 0.16, 0.9), RED)             # hazard panel
    p.bevel(width=0.025, segments=1)
    return p.finish("Mesh_Derrick_Winch", coll)


def antenna(coll, mats):
    """A 20 m guyed comms mast with dishes and warning lights.

    Building-scale sibling of `mast_rig`'s whip. Guys are what make a mast this
    thin believable; without them the eye reads it as a floating line.
    """
    p = Part(mats)
    h = 20.0
    p.cyl((0, 0, h / 2), 0.30, h, 'Z', seg=8, mat=STEEL, radius_top=0.16)
    for i in range(8):                                        # hazard banding
        if i % 2:
            z = h * (i + 0.5) / 8
            p.cyl((0, 0, z), 0.33 - 0.02 * i, h / 8 * 0.9, 'Z', seg=8, mat=RED)
    p.box((0, 0, 0.45), (2.0, 2.0, 0.9), DARK)
    p.box((0, 0, 0.10), (2.6, 2.6, 0.24), RUST)
    # Two collars of guys, anchored on a 7 m radius.
    for lvl, rad in ((0.42, 0.245), (0.78, 0.19)):
        z = h * lvl
        p.cyl((0, 0, z), rad + 0.12, 0.34, 'Z', seg=8, mat=DARK)
        for i in range(3):
            a = 2 * math.pi * i / 3 + 0.4
            anchor = Vector((math.cos(a) * 7.0, math.sin(a) * 7.0, 0.0))
            top = Vector((math.cos(a) * rad, math.sin(a) * rad, z))
            rot, length = along(anchor, top)
            p.cyl((anchor + top) / 2, 0.05, length, 'Z', seg=4, mat=CHROME,
                  rot=rot)
            p.box(anchor + Vector((0, 0, 0.3)), (0.8, 0.8, 0.6), DARK)
    for i, z in enumerate((h * 0.55, h * 0.70)):              # dishes
        s = 1 if i else -1
        p.box((s * 0.35, 0, z), (0.7, 0.5, 0.5), DARK)
        p.cyl((s * 1.15, 0, z), 0.95 - i * 0.25, 0.28, 'X', seg=14, mat=WHITE,
              radius_top=0.72 - i * 0.2)
        p.cyl((s * 1.55, 0, z), 0.10, 0.6, 'X', seg=6, mat=STEEL)
    for z in (h * 0.5, h - 0.4):                              # warning lights
        p.cyl((0, 0, z), 0.26, 0.30, 'Z', seg=8, mat=WARN)
    p.cyl((0, 0, h + 1.1), 0.05, 2.2, 'Z', seg=4, mat=CHROME)  # lightning rod
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Derrick_Antenna", coll)


def flare(coll, mats):
    """A 14 m flare stack with its ladder, windshield and pilot lines.

    Every refinery has one and its silhouette — a thin stack with a flared,
    slotted tip — is unmistakable at any distance, which makes it worth a
    variation of its own rather than a re-skinned antenna.
    """
    p = Part(mats)
    h = 14.0
    p.cyl((0, 0, h / 2), 0.62, h, 'Z', seg=10, mat=STEEL, radius_top=0.48)
    for i in range(5):
        p.cyl((0, 0, 1.4 + i * 2.6), 0.72, 0.26, 'Z', seg=10, mat=DARK)
    p.cyl((0, 0, h + 0.55), 0.78, 1.1, 'Z', seg=10, mat=RUST, radius_top=0.92)
    for i in range(10):                                       # windshield slots
        a = 2 * math.pi * i / 10
        p.box((math.cos(a) * 0.92, math.sin(a) * 0.92, h + 1.35),
              (0.16, 0.16, 0.9), DARK,
              rot=Matrix.Rotation(a, 4, 'Z'))
    p.cyl((0, 0, h + 1.85), 0.86, 0.22, 'Z', seg=10, mat=DARK)
    for i in range(2):                                        # pilot lines
        p.cyl((0.78 - i * 0.06, 0, h / 2), 0.09, h - 0.6, 'Z', seg=6, mat=STEEL)
    for i in range(11):                                       # caged ladder
        p.box((-0.78, 0, 1.0 + i * 1.2), (0.75, 0.09, 0.09), STEEL)
    for i in range(4):
        p.torus((-1.05, 0, 2.6 + i * 3.0), 0.52, 0.05, 'X', maj_seg=8,
                min_seg=4, mat=STEEL)
    p.box((0, 0, 0.42), (2.4, 2.4, 0.84), DARK)
    p.box((0, 0, 0.08), (3.0, 3.0, 0.22), RUST)
    p.bevel(width=0.025, segments=1)
    return p.finish("Mesh_Derrick_Flare", coll)


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Mast", mast), ("PipeRack", pipe_rack), ("Winch", winch),
                     ("Antenna", antenna), ("Flare", flare)):
        fn(collection("Coll_Derrick_%s" % name), mats)
    report()
    save(out)


build()
