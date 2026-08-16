"""components/structural/mast_rig — mast, flag and stays.

The one part of a crawler that is not armour. A mast breaks the boxy silhouette,
gives the machine a top, and a flag on it is the only thing in the whole model
that is soft — which is exactly why it reads at a kilometre when nothing else
does.

Origin at the base plate, mast rising +Z, stays reaching down to the deck within
a 2.40 m radius, so a mast can be set down anywhere with that much clear roof
around it and needs nothing added at assembly time.

    blender --background --python mast_rig.py -- --out mast_rig.blend
"""

import math
import os
import sys

from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",      # 0 mast
    "Mat_Metal_Steel_Dark",      # 1 fittings, base
    "Mat_Fabric_Flag_Bleached",  # 2 flag cloth
    "Mat_Paint_Warn_Red",        # 3 flag device, mast bands
    "Mat_Metal_Rust_Heavy",      # 4 corrosion
    "Mat_Plastic_Rubber_Black",  # 5 stays, coax
    "Mat_Emissive_Amber",        # 6 obstruction lamp
    "Mat_Paint_Roof_Green",      # 7 painted collar
]
STEEL, DARK, CLOTH, RED, RUST, CORD, LAMP, GREEN = range(8)

STAY_RADIUS = 2.40


def base(p, radius=0.34):
    """Foot plate, collar and bolts. Every mast lands the same way."""
    p.box((0, 0, 0.06), (1.05, 1.05, 0.12), DARK)
    p.cyl((0, 0, 0.26), radius + 0.13, 0.34, 'Z', 12, GREEN)
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        p.cyl((math.cos(a) * 0.42, math.sin(a) * 0.42, 0.14), 0.055, 0.16, 'Z',
              6, RUST)
    for i in range(4):                                     # gusset webs
        a = i * math.pi / 2
        p.box((math.cos(a) * (radius + 0.16), math.sin(a) * (radius + 0.16),
               0.38), (0.34, 0.07, 0.62), STEEL,
              rot=Matrix.Rotation(a, 4, 'Z'))


def stays(p, top, count=4, radius=STAY_RADIUS, mat=CORD, thickness=0.035):
    """Guy wires from a point on the mast down to the deck.

    Straight cylinders: a stay under tension is straight, and at the diameter it
    is drawn at nobody reads a catenary anyway.
    """
    apex = Vector((0, 0, top))
    for i in range(count):
        a = 2 * math.pi * i / count + math.pi / 4
        foot = Vector((math.cos(a) * radius, math.sin(a) * radius, 0.10))
        span = foot - apex
        centre = (apex + foot) / 2
        rot = span.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        p.cyl(centre, thickness, span.length, 'Z', 5, mat, rot=rot)
        p.box((foot.x, foot.y, 0.08), (0.22, 0.22, 0.16), DARK)


def flag(p, x0, z0, length, height, sag=0.16, panels=7, device=True):
    """A hoisted flag, waved by displacing its free edge along Y.

    Modelled as `panels` quads rather than one: a flat rectangle reads as
    cardboard, and the whole point of putting cloth on a machine like this is
    that it is the one thing moving.
    """
    for i in range(panels):
        t0, t1 = i / panels, (i + 1) / panels
        y0 = math.sin(t0 * math.pi * 1.7) * sag * t0
        y1 = math.sin(t1 * math.pi * 1.7) * sag * t1
        drop0 = t0 * t0 * 0.22
        drop1 = t1 * t1 * 0.22
        p.prism([(x0 + length * t0, z0 - drop0),
                 (x0 + length * t1, z0 - drop1),
                 (x0 + length * t1, z0 - drop1 - height),
                 (x0 + length * t0, z0 - drop0 - height)],
                0.03, 'Y', CLOTH, offset=(0, (y0 + y1) / 2, 0))
    if device:
        p.cyl((x0 + length * 0.34, 0.0, z0 - height * 0.48), height * 0.24,
              0.05, 'Y', 12, RED)
        p.cyl((x0 + length * 0.34, 0.0, z0 - height * 0.48), height * 0.11,
              0.06, 'Y', 12, CLOTH)


def build_flag(coll):
    """The tall one: 8 m mast, spar, a big hoisted flag and four stays."""
    top = 8.0
    p = Part(PALETTE)
    base(p)
    p.cyl((0, 0, top / 2 + 0.3), 0.13, top - 0.6, 'Z', 10, STEEL)
    p.cyl((0, 0, top * 0.52), 0.16, 0.24, 'Z', 10, RED)          # day band
    p.cyl((0, 0, top - 0.15), 0.10, 0.60, 'Z', 8, STEEL)
    p.cyl((0, 0, top + 0.22), 0.11, 0.16, 'Z', 10, LAMP)         # lamp on top

    p.cyl((0.95, 0, top - 0.55), 0.07, 1.90, 'X', 8, STEEL)      # spar
    p.cyl((1.90, 0, top - 0.55), 0.10, 0.10, 'X', 8, DARK)
    flag(p, 0.16, top - 0.62, 1.85, 1.20)
    stays(p, top - 0.9)
    p.cyl((0, 0, 1.30), 0.05, 0.10, 'X', 6, DARK)                # cleat
    p.bevel(width=0.012, segments=2)
    p.finish("Mesh_MastRig_Flag", coll)


def build_pennant(coll):
    """Shorter, with a long tapering streamer instead of a rectangle — a
    different outline against the sky, not the same flag scaled down."""
    top = 5.6
    p = Part(PALETTE)
    base(p, 0.28)
    p.cyl((0, 0, top / 2 + 0.3), 0.10, top - 0.6, 'Z', 8, STEEL)
    p.cyl((0, 0, top - 0.10), 0.13, 0.20, 'Z', 8, RUST)
    p.cyl((0, 0, top + 0.16), 0.06, 0.32, 'Z', 6, STEEL)

    # Tapering streamer: the trailing edge closes to a point.
    panels = 8
    length, height = 2.70, 0.70
    for i in range(panels):
        t0, t1 = i / panels, (i + 1) / panels
        h0, h1 = height * (1 - t0 * 0.78), height * (1 - t1 * 0.78)
        y0 = math.sin(t0 * math.pi * 2.3) * 0.24 * t0
        y1 = math.sin(t1 * math.pi * 2.3) * 0.24 * t1
        p.prism([(0.14 + length * t0, top - 0.55),
                 (0.14 + length * t1, top - 0.55),
                 (0.14 + length * t1, top - 0.55 - h1),
                 (0.14 + length * t0, top - 0.55 - h0)],
                0.03, 'Y', CLOTH, offset=(0, (y0 + y1) / 2, 0))
    p.cyl((0.14, 0, top - 0.90), 0.04, 0.76, 'Z', 6, DARK)
    stays(p, top - 0.8, 3, 1.90)
    p.bevel(width=0.012, segments=2)
    p.finish("Mesh_MastRig_Pennant", coll)


def build_antenna(coll):
    """No cloth at all: a whip with dipole crossbars and a tuner box. Reads as
    equipment where the other two read as identity."""
    top = 6.8
    p = Part(PALETTE)
    base(p, 0.26)
    p.box((0.60, 0, 0.62), (0.62, 0.46, 0.90), DARK)             # tuner
    p.box((0.60, 0, 1.10), (0.44, 0.30, 0.08), GREEN)
    p.cyl((0.60, 0.24, 0.30), 0.045, 0.36, 'Z', 6, CORD)

    p.cyl((0, 0, 2.0), 0.11, 3.4, 'Z', 8, STEEL)
    p.cyl((0, 0, 4.4), 0.07, 1.6, 'Z', 8, STEEL)
    p.cyl((0, 0, 5.9), 0.035, 1.6, 'Z', 6, STEEL)                # whip
    for z, span in ((3.30, 2.30), (4.35, 1.70), (5.15, 1.15)):
        p.cyl((0, 0, z), 0.035, span, 'X', 6, STEEL)
        for sx in (-1, 1):
            p.cyl((sx * span / 2, 0, z), 0.05, 0.10, 'X', 6, DARK)
    p.cyl((0, 0, 3.30), 0.15, 0.18, 'Z', 8, RED)
    p.cyl((0, 0, top + 0.05), 0.08, 0.12, 'Z', 8, LAMP)
    stays(p, 4.30, 3, 2.10)
    p.bevel(width=0.012, segments=2)
    p.finish("Mesh_MastRig_Antenna", coll)


def build_windvane(coll):
    """Anemometer and wind vane on a short post. Built ahead — a weather mast
    for a station or a smaller vehicle, and the only one that spins."""
    top = 3.4
    p = Part(PALETTE)
    base(p, 0.22)
    p.cyl((0, 0, top / 2 + 0.3), 0.085, top - 0.6, 'Z', 8, STEEL)
    p.box((0, 0, top - 0.30), (0.70, 0.16, 0.10), STEEL)         # crossarm

    p.cyl((-0.35, 0, top - 0.02), 0.05, 0.46, 'Z', 6, STEEL)     # cup rotor
    for i in range(3):
        a = 2 * math.pi * i / 3
        arm = Vector((math.cos(a), math.sin(a), 0)) * 0.28
        p.cyl((-0.35 + arm.x, arm.y, top + 0.18), 0.02, 0.56, 'X', 5, STEEL,
              rot=Matrix.Rotation(a + math.pi / 2, 4, 'Z'))
        p.cyl((-0.35 + arm.x * 2, arm.y * 2, top + 0.18), 0.10, 0.12, 'Z', 8,
              RED)

    p.cyl((0.35, 0, top + 0.04), 0.05, 0.50, 'Z', 6, STEEL)      # vane
    p.cyl((0.35, 0, top + 0.26), 0.04, 0.66, 'Y', 6, STEEL)
    p.prism([(0.18, -0.16), (0.44, -0.30), (0.44, 0.22), (0.18, 0.10)],
            0.03, 'X', CLOTH, offset=(0.35, 0, top + 0.26))
    p.cyl((0, 0, 1.10), 0.05, 0.16, 'X', 6, DARK)
    stays(p, 2.30, 3, 1.20)
    p.bevel(width=0.012, segments=2)
    p.finish("Mesh_MastRig_Windvane", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_flag(collection("Coll_MastRig_Flag"))
    build_pennant(collection("Coll_MastRig_Pennant"))
    build_antenna(collection("Coll_MastRig_Antenna"))
    build_windvane(collection("Coll_MastRig_Windvane"))

    report()
    save(out)


build()
