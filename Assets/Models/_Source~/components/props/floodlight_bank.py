"""components/props/floodlight_bank — exterior work lamps on a mounting frame.

Distinct from `props/light_fixture`, which is interior fittings seen from two
metres away. These are weather-proof floods bolted to the outside of a machine,
seen at fifty metres, and what matters about them is the glare disc and the
guard cage rather than the fitting's detail.

Authored pointing −Y (the library's forward), origin on the mounting face, so
placing one is a matter of putting the origin on the panel it bolts to.

    blender --background --python floodlight_bank.py -- --out floodlight_bank.blend
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Dark",      # 0 housing, frame
    "Mat_Emissive_Amber",        # 1 the glare disc
    "Mat_Metal_Steel_Worn",      # 2 brackets
    "Mat_Paint_Hull_Bleached",   # 3 painted back plate
    "Mat_Plastic_Rubber_Black",  # 4 cable, seals
    "Mat_Metal_Chrome_Scuffed",  # 5 reflector lip
    "Mat_Metal_Rust_Heavy",      # 6 corrosion
]
DARK, GLOW, STEEL, HULL, RUBBER, CHROME, RUST = range(7)


def lamp(p, centre, radius=0.30, guard=True):
    """One flood: housing, reflector lip, emissive disc, and a wire guard.

    The lens sits proud of the housing and the guard proud of that, so the lamp
    still reads as a lamp when it is off and unlit.
    """
    x, y, z = centre
    p.cyl((x, y + 0.16, z), radius * 0.92, 0.30, 'Y', 14, DARK)
    p.cyl((x, y + 0.32, z), radius * 0.60, 0.14, 'Y', 10, STEEL)     # back boss
    p.cyl((x, y - 0.02, z), radius, 0.10, 'Y', 16, CHROME)           # rim
    p.cyl((x, y - 0.07, z), radius * 0.86, 0.04, 'Y', 16, GLOW)      # lens
    if guard:
        for i in range(3):
            a = math.radians(30 + i * 60)
            p.box((x + math.cos(a) * radius * 0.62, y - 0.12,
                   z + math.sin(a) * radius * 0.62),
                  (radius * 1.5, 0.05, 0.05), STEEL,
                  rot=_spin(a))
        p.torus((x, y - 0.13, z), radius * 0.96, 0.035, 'Y', 14, 6, STEEL)


def _spin(angle):
    from mathutils import Matrix
    return Matrix.Rotation(angle, 4, 'Y')


def frame(p, half_x, half_z, depth=0.16):
    """Back plate and the two brackets that stand the bank off its panel."""
    p.box((0, depth / 2 + 0.30, 0), (half_x * 2, depth, half_z * 2), HULL)
    for sx in (-1, 1):
        p.box((sx * (half_x - 0.10), 0.44, 0), (0.14, 0.32, half_z * 2 + 0.10),
              STEEL)
    p.cyl((0, 0.50, -half_z - 0.06), 0.05, 0.44, 'X', 8, RUBBER)     # loom


def build_quad(coll):
    """Two-by-two: the main forward bank. Widest and brightest of the set."""
    p = Part(PALETTE)
    frame(p, 0.76, 0.48)
    for sx in (-1, 1):
        for sz in (-1, 1):
            lamp(p, (sx * 0.36, 0.0, sz * 0.23))
    p.box((0, 0.30, 0.56), (1.52, 0.14, 0.14), DARK)
    p.rivets((-0.62, 0.24, 0.56), (0.62, 0.24, 0.56), 6, 0.035, 0.03, 'Y', RUST)
    p.bevel(width=0.014, segments=2)
    p.finish("Mesh_FloodlightBank_Quad", coll)


def build_twin(coll):
    """Two lamps on a spreader bar — a wider, flatter silhouette than the quad
    rather than the same thing with two lamps deleted."""
    p = Part(PALETTE)
    p.box((0, 0.38, 0), (1.72, 0.16, 0.22), HULL)
    for sx in (-1, 1):
        p.box((sx * 0.72, 0.30, 0), (0.16, 0.28, 0.30), STEEL)
        lamp(p, (sx * 0.48, 0.0, 0.0), 0.28)
    p.cyl((0, 0.42, 0), 0.09, 0.36, 'Y', 10, STEEL)
    p.cyl((0, 0.52, -0.14), 0.05, 0.30, 'X', 8, RUBBER)
    p.bevel(width=0.014, segments=2)
    p.finish("Mesh_FloodlightBank_Twin", coll)


def build_single(coll):
    """One big lamp in a yoke. Taller than it is wide, and the only one whose
    lamp is bigger than the frame around it."""
    p = Part(PALETTE)
    p.box((0, 0.44, -0.44), (0.46, 0.20, 0.20), HULL)
    p.cyl((0, 0.40, -0.20), 0.08, 0.44, 'Z', 10, STEEL)              # stalk
    for sx in (-1, 1):                                               # yoke arms
        p.box((sx * 0.40, 0.22, 0.0), (0.10, 0.44, 0.34), STEEL)
        p.cyl((sx * 0.40, 0.06, 0.0), 0.10, 0.14, 'X', 10, DARK)
    lamp(p, (0, 0.0, 0.0), 0.40)
    p.bevel(width=0.014, segments=2)
    p.finish("Mesh_FloodlightBank_Single", coll)


def build_sweep(coll):
    """A searchlight on a turntable, guarded and hooded. Built ahead — a machine
    that hunts wants one, and it is the only variant that could be animated."""
    p = Part(PALETTE)
    p.cyl((0, 0.34, -0.46), 0.34, 0.20, 'Z', 16, HULL)               # turntable
    p.cyl((0, 0.34, -0.30), 0.22, 0.22, 'Z', 12, STEEL)
    p.box((0, 0.34, -0.10), (0.62, 0.34, 0.24), DARK)
    lamp(p, (0, 0.0, 0.12), 0.44, guard=False)
    # Hood: three overlapping plates rather than a swept surface, which keeps it
    # in the same faceted language as the rest of the machine.
    p.box((0, -0.10, 0.52), (0.92, 0.46, 0.10), HULL)
    for sx in (-1, 1):
        p.box((sx * 0.46, -0.06, 0.30), (0.10, 0.40, 0.46), HULL)
    p.torus((0, -0.20, 0.12), 0.44, 0.04, 'Y', 16, 6, STEEL)
    p.cyl((0, 0.46, -0.40), 0.05, 0.34, 'X', 8, RUBBER)
    p.bevel(width=0.014, segments=2)
    p.finish("Mesh_FloodlightBank_Sweep", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_quad(collection("Coll_FloodlightBank_Quad"))
    build_twin(collection("Coll_FloodlightBank_Twin"))
    build_single(collection("Coll_FloodlightBank_Single"))
    build_sweep(collection("Coll_FloodlightBank_Sweep"))

    report()
    save(out)


build()
