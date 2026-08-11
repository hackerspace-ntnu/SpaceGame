"""components/mechanical/collector_lever — lift arm and deck mount for a bucket.

The two pieces a front loader needs that are not the bucket or the cylinder: a
lift arm that carries the bucket, and the bracket that pins it to the hull. The
rams come from `cutter_drum.blend`, which already has a barrel-and-rod pair
built to be parented to two different bones so they extend rather than stretch.

Kept plain on purpose — a box beam with a boss at each end and one gusset. It is
a lever, not a set piece.

Conventions: the arm runs along **+Y** with its origin on the **hull pivot** and
its far boss at +Y * ARM_LEN, so it drops onto a bone head-to-tail. The mount's
origin is on the same pivot, so arm and bracket share one placement.

    blender --background --python collector_lever.py -- --out collector_lever.blend
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
    "Mat_Metal_Chrome_Scuffed",  # 4
    "Mat_Paint_Warn_Red",        # 5
]
HULL, DARK, STEEL, OLIVE, CHROME, RED = range(6)

ARM_LEN = 3.20


def build_arm(coll):
    p = Part(PALETTE)
    beam = p.loft(
        [(0.16, [(-0.26, -0.34), (0.26, -0.34), (0.26, 0.34), (-0.26, 0.34)]),
         (ARM_LEN * 0.5, [(-0.22, -0.42), (0.22, -0.42), (0.22, 0.42),
                          (-0.22, 0.42)]),
         (ARM_LEN - 0.16, [(-0.20, -0.30), (0.20, -0.30), (0.20, 0.30),
                           (-0.20, 0.30)])], axis='Y', mat=HULL)
    p.shade(beam, smooth=False)          # or the box beam reads as a tube
    for y in (0.0, ARM_LEN):
        p.cyl((0, y, 0), 0.34, 0.44, 'X', 14, STEEL)
        p.cyl((0, y, 0), 0.14, 0.62, 'X', 12, CHROME)
    # Gusset where the tilt ram pushes, and one hazard band.
    p.box((0, 0.62, 0.44), (0.44, 0.70, 0.22), OLIVE)
    p.box((0, ARM_LEN - 0.70, 0.40), (0.34, 0.44, 0.10), RED)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_CollectorArm", coll)


def build_mount(coll):
    """Deck bracket. Origin on the pivot, so it shares the arm's placement."""
    p = Part(PALETTE)
    for sx in (-1, 1):
        p.box((sx * 0.46, 0.0, -0.24), (0.16, 0.86, 0.90), OLIVE)
        p.cyl((sx * 0.46, 0.0, 0.0), 0.30, 0.18, 'X', 14, STEEL)
    p.box((0, 0.10, -0.74), (1.40, 1.30, 0.22), STEEL)
    p.rivets((-0.50, -0.36, -0.86), (0.50, -0.36, -0.86), 4, 0.05, 0.04, 'Z',
             CHROME)
    p.rivets((-0.50, 0.56, -0.86), (0.50, 0.56, -0.86), 4, 0.05, 0.04, 'Z',
             CHROME)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_CollectorMount", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)
    build_arm(collection("Coll_CollectorLever_Arm"))
    build_mount(collection("Coll_CollectorLever_Mount"))
    print("\nCollector lever parts:")
    report()
    save(out)


build()
