"""components/mechanical/road_wheel — the wheels hung off a walking machine.

A legged hauler still carries wheels. They ride idle most of the time and take
the load when the machine kneels or is towed, and visually they are what stops a
leg from reading as a bare linkage: a hub with a fat tyre on it says *vehicle*
in a way no amount of piping does.

Authored with the axle along X and the origin on the hub centre, so an assembly
places one by putting the origin on the axle line and mirroring in X.

    blender --background --python road_wheel.py -- --out road_wheel.blend
"""

import math
import os
import sys

from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Plastic_Rubber_Black",  # 0 tyre
    "Mat_Metal_Steel_Dark",      # 1 hub, bolts
    "Mat_Metal_Steel_Worn",      # 2 rim
    "Mat_Paint_Hull_Bleached",   # 3 painted rim face
    "Mat_Metal_Rust_Heavy",      # 4 corrosion
    "Mat_Metal_Chrome_Scuffed",  # 5 axle stub
]
RUBBER, DARK, STEEL, HULL, RUST, CHROME = range(6)

R_TYRE = 0.86
R_RIM = 0.58


def tread(p, x, width, radius=R_TYRE, count=18, mat=RUBBER):
    """Lugs around the circumference. Cheaper and blockier than a real tread
    pattern, and at the distance a walker is seen from it is all that lands."""
    for i in range(count):
        a = 2 * math.pi * i / count
        # Rotated about the axle so the lug's long side runs radially; without
        # it every lug stays axis-aligned and the tyre goes visibly out of round.
        p.box((x, math.cos(a) * (radius + 0.03), math.sin(a) * (radius + 0.03)),
              (width * 0.82, 0.16, 0.10), mat, rot=Matrix.Rotation(a, 4, 'X'))


def hub_face(p, x, sign, rim_mat=HULL):
    """One side of a wheel: dished rim, bolt circle, centre cap."""
    p.cyl((x, 0, 0), R_RIM, 0.10, 'X', 20, rim_mat)
    p.cyl((x + sign * 0.06, 0, 0), 0.34, 0.14, 'X', 16, STEEL)
    p.cyl((x + sign * 0.13, 0, 0), 0.17, 0.10, 'X', 12, DARK)
    for i in range(6):
        a = 2 * math.pi * i / 6
        p.cyl((x + sign * 0.10, math.cos(a) * 0.24, math.sin(a) * 0.24),
              0.045, 0.10, 'X', 6, DARK)


def build_twin(coll):
    """Two tyres on one hub — the load-bearing arrangement, and the widest
    silhouette of the three."""
    p = Part(PALETTE)
    for x in (-0.24, 0.24):
        p.tube((x, 0, 0), R_TYRE, R_TYRE - R_RIM, 0.42, 'X', 20, RUBBER)
        p.cyl((x, 0, 0), R_TYRE - 0.02, 0.40, 'X', 20, RUBBER)
        tread(p, x, 0.42)
    hub_face(p, -0.46, -1)
    hub_face(p, 0.46, 1)
    p.cyl((0, 0, 0), 0.30, 0.52, 'X', 14, STEEL)          # spacer between them
    p.cyl((0, 0, 0), 0.13, 0.98, 'X', 10, CHROME)         # axle through both
    p.bevel(width=0.014, segments=2)
    p.finish("Mesh_RoadWheel_Twin", coll)


def build_single(coll):
    """One wide tyre. Narrower than the twin and a hand taller, so the two do
    not read as the same wheel at a different scale."""
    p = Part(PALETTE)
    p.tube((0, 0, 0), R_TYRE + 0.06, R_TYRE - R_RIM + 0.06, 0.52, 'X', 20,
           RUBBER)
    p.cyl((0, 0, 0), R_TYRE + 0.04, 0.50, 'X', 20, RUBBER)
    tread(p, 0.0, 0.52, R_TYRE + 0.06, 20)
    hub_face(p, -0.28, -1, STEEL)
    hub_face(p, 0.28, 1, STEEL)
    p.cyl((0, 0, 0), 0.12, 0.72, 'X', 10, CHROME)
    p.bevel(width=0.014, segments=2)
    p.finish("Mesh_RoadWheel_Single", coll)


def build_hub(coll):
    """Bare hub, no tyre. Built ahead: a stripped machine, a spare on a rack,
    or a wheel station that carries a tool instead."""
    p = Part(PALETTE)
    p.cyl((0, 0, 0), R_RIM, 0.34, 'X', 20, STEEL)
    p.tube((0, 0, 0), R_RIM + 0.08, 0.12, 0.30, 'X', 20, HULL)
    for i in range(8):
        a = 2 * math.pi * i / 8
        p.box((0, math.cos(a) * 0.36, math.sin(a) * 0.36), (0.20, 0.12, 0.34),
              STEEL)
    p.cyl((0, 0, 0), 0.22, 0.46, 'X', 14, DARK)
    p.cyl((0, 0, 0), 0.11, 0.66, 'X', 10, CHROME)
    for i in range(6):
        a = 2 * math.pi * i / 6
        p.cyl((0.20, math.cos(a) * 0.30, math.sin(a) * 0.30), 0.05, 0.10, 'X',
              6, DARK)
    p.bevel(width=0.014, segments=2)
    p.finish("Mesh_RoadWheel_Hub", coll)


def build_flat(coll):
    """Run flat and shredded — sits lower and out of round. Built ahead, and the
    reason a machine can have four wheels that are not all the same wheel."""
    p = Part(PALETTE)
    p.cyl((0, 0, -0.10), R_TYRE - 0.06, 0.44, 'X', 20, RUBBER)
    p.box((0, 0.0, -0.72), (0.46, 1.10, 0.26), RUBBER)     # squashed contact
    p.box((0, 0.62, -0.44), (0.42, 0.34, 0.52), RUBBER)    # bulged sidewall
    p.box((0, -0.58, -0.40), (0.42, 0.30, 0.46), RUST)     # torn carcass
    hub_face(p, -0.24, -1, RUST)
    hub_face(p, 0.24, 1, RUST)
    p.cyl((0, 0, 0), 0.12, 0.66, 'X', 10, CHROME)
    p.bevel(width=0.014, segments=2)
    p.finish("Mesh_RoadWheel_Flat", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_twin(collection("Coll_RoadWheel_Twin"))
    build_single(collection("Coll_RoadWheel_Single"))
    build_hub(collection("Coll_RoadWheel_Hub"))
    build_flat(collection("Coll_RoadWheel_Flat"))

    report()
    save(out)


build()
