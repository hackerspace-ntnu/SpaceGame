"""components/mechanical/cutter_drum — a transverse digging drum and its boom.

A trencher head: a toothed drum carried on two side arms that drop from the
front of a machine, with a deflector hood over the top and a ram to pitch it.
Deliberately plain — big simple forms, teeth, three weld bands, and nothing
else. The teeth are what say "this digs"; rivet rows and greeble would only
muddy a part that is read at twenty metres.

Wide rather than slender on purpose. A single boom reaching down from a hull
reads as an arm; two arms and a full-width drum read as an attachment.

Conventions: the drum's axis lies along **X** with its origin on the axis, so it
spins about its own local Y once bone-parented. The arm and the ram run along
**+Y** with their origin at the pivot end, the same as every other chained part
in this library. Arm nominal length is 8.00 m; place it at a uniform scale to
suit.

    blender --background --python cutter_drum.py -- --out cutter_drum.blend
"""

import math
import os
import sys

import bpy
from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0
    "Mat_Metal_Steel_Dark",      # 1
    "Mat_Metal_Steel_Worn",      # 2
    "Mat_Paint_Olive_Deep",      # 3
    "Mat_Metal_Rust_Heavy",      # 4
    "Mat_Paint_Warn_Red",        # 5
    "Mat_Metal_Chrome_Scuffed",  # 6
    "Mat_Neutral_Black_Matte",   # 7
]
HULL, DARK, STEEL, OLIVE, RUST, RED, CHROME, BLACK = range(8)

R_BARREL = 1.55
R_TEETH = 1.90
HALF_W = 3.20
ARM_LEN = 8.00


def build_drum(coll):
    p = Part(PALETTE)
    p.cyl((0, 0, 0), R_BARREL, HALF_W * 2, 'X', 24, HULL)
    for sx in (-1, 1):
        p.cyl((sx * (HALF_W - 0.12), 0, 0), 1.74, 0.26, 'X', 24, OLIVE)
    p.cyl((0, 0, 0), 0.34, HALF_W * 2 + 0.90, 'X', 14, DARK)
    for x in (-1.60, 0.0, 1.60):
        p.tube((x, 0, 0), R_BARREL + 0.05, 0.09, 0.20, 'X', 24, STEEL)

    # Four rows of eight, each row clocked half a tooth off the last so the
    # drum bites continuously instead of all eight landing at once.
    h = R_TEETH - R_BARREL + 0.22
    for row, x in enumerate((-2.45, -0.85, 0.85, 2.45)):
        for k in range(8):
            a = math.radians(k * 45.0 + row * 22.5)
            r = R_BARREL + h * 0.5 - 0.10
            p.cyl((x, r * math.cos(a), r * math.sin(a)), 0.20, h, 'Z', 8, STEEL,
                  radius_top=0.055,
                  rot=Matrix.Rotation(a - math.radians(90), 4, 'X'))
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_CutterDrum", coll)


def build_arm(coll):
    """One side arm, pivot at the origin, drum end at +Y * ARM_LEN."""
    p = Part(PALETTE)
    # Flat-shaded: `loft` smooth-shades everything that curves around its axis,
    # which turns a four-sided box beam into something that reads as a tube.
    beam = p.loft(
        [(0.20, [(-0.42, -0.52), (0.42, -0.52), (0.42, 0.52), (-0.42, 0.52)]),
         (ARM_LEN * 0.55, [(-0.36, -0.62), (0.36, -0.62), (0.36, 0.62),
                           (-0.36, 0.62)]),
         (ARM_LEN - 0.20, [(-0.32, -0.46), (0.32, -0.46), (0.32, 0.46),
                           (-0.32, 0.46)])], axis='Y', mat=HULL)
    p.shade(beam, smooth=False)
    for y in (0.0, ARM_LEN):
        p.cyl((0, y, 0), 0.52, 0.62, 'X', 16, STEEL)
        p.cyl((0, y, 0), 0.22, 0.86, 'X', 12, CHROME)
    # One hazard band near the pivot, and nothing else.
    p.box((0, 1.10, 0.64), (0.86, 0.60, 0.10), RED)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_CutterArm", coll)


def arc_band(r_out, r_in, a0, a1, n=13):
    """Closed annular-sector profile in (y, z) for an X-axis loft."""
    pts = []
    for i in range(n):
        a = math.radians(a0 + (a1 - a0) * i / (n - 1))
        pts.append((r_out * math.cos(a), r_out * math.sin(a)))
    for i in range(n - 1, -1, -1):
        a = math.radians(a0 + (a1 - a0) * i / (n - 1))
        pts.append((r_in * math.cos(a), r_in * math.sin(a)))
    return pts


def build_hood(coll):
    """Deflector over the top and the machine-facing side of the drum.

    0 degrees is toward the machine, 90 is up, 180 is the cutting face — so the
    band stops at 150 and leaves the drum's leading edge open. End plates follow
    the same arc: square cheeks here read as a slab from the side and hide the
    drum completely, which is the one thing this part must not do.
    """
    p = Part(PALETTE)
    a0, a1 = 5.0, 150.0
    ro, ri = R_TEETH + 0.34, R_TEETH + 0.20
    p.loft([(-HALF_W - 0.10, arc_band(ro, ri, a0, a1)),
            (HALF_W + 0.10, arc_band(ro, ri, a0, a1))], axis='X', mat=OLIVE)
    for sx in (-1, 1):
        p.loft([(sx * (HALF_W + 0.10), arc_band(ro + 0.16, ri - 0.30, a0, a1)),
                (sx * (HALF_W + 0.22), arc_band(ro + 0.16, ri - 0.30, a0, a1))],
               axis='X', mat=STEEL)
    p.box((0, 0.0, R_TEETH + 0.42), (HALF_W * 2, 0.42, 0.16), STEEL)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_CutterHood", coll)


def build_ram(coll):
    """Barrel and rod as separate objects — the barrel stays on the hull and the
    rod travels with the boom, so the pair extends instead of stretching."""
    p = Part(PALETTE)
    p.cyl((0, 1.30, 0), 0.30, 2.60, 'Y', 14, DARK)
    p.cyl((0, 0, 0), 0.20, 0.42, 'X', 12, STEEL)
    p.bevel(width=0.02, segments=1)
    barrel = p.finish("Mesh_CutterRamBarrel", coll)

    q = Part(PALETTE)
    q.cyl((0, -1.20, 0), 0.145, 2.60, 'Y', 12, CHROME)
    q.cyl((0, 0, 0), 0.20, 0.42, 'X', 12, STEEL)
    q.bevel(width=0.02, segments=1)
    rod = q.finish("Mesh_CutterRamRod", coll)
    return barrel, rod


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_drum(collection("Coll_CutterDrum_Drum"))
    build_arm(collection("Coll_CutterDrum_Arm"))
    build_hood(collection("Coll_CutterDrum_Hood"))
    build_ram(collection("Coll_CutterDrum_Ram"))

    print("\nCutter drum parts:")
    report()
    save(out)


build()
