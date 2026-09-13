"""Turbine engine pods for spacecraft — four variations, one per collection.

All lie along -Y (intake) to +Y (exhaust) with the origin at the centre of
the intake face, so a pod is placed by its mouth and points aft by default.

    Turbine_Long    8.0 m x 2.2 m dia  — main lift/cruise engine, under-wing
    Turbine_Short   3.6 m x 1.8 m dia  — flank booster
    Turbine_Ducted  2.4 m x 3.2 m dia  — ring-shrouded fan, wingtip or belly
    Turbine_Stub    1.8 m x 1.2 m dia  — roof/tail auxiliary

    blender --background --python turbine.py -- --out turbine.blend
"""

import math
import os
import sys

from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
import _buildlib as B  # noqa: E402

MATS = ["Mat_Paint_Hull_Bleached", "Mat_Metal_Steel_Dark", "Mat_Metal_Steel_Worn",
        "Mat_Emissive_Amber", "Mat_Paint_Olive_Deep"]
CASING, DARK, STEEL, GLOW, RECESS = range(5)


def fan(part, y, radius, blades, hub, thickness=0.05):
    """A bladed disc in the XZ plane at station y."""
    part.cyl((0, y, 0), hub, thickness * 3, axis='Y', seg=16, mat=DARK)
    for i in range(blades):
        a = 2 * math.pi * i / blades
        rot = Matrix.Rotation(a, 4, 'Y') @ Matrix.Rotation(math.radians(35), 4, 'Z')
        length = radius - hub
        centre = Matrix.Rotation(a, 4, 'Y') @ Vector((hub + length / 2, 0, 0))
        part.box((centre.x, y, centre.z), (length, thickness, radius * 0.22), mat=STEEL, rot=rot)


def spinner(part, y, radius, length):
    part.cyl((0, y + length / 2, 0), radius, length, axis='Y', seg=16, mat=DARK, radius_top=radius * 0.15)


def casing(part, y0, y1, r_in, r_out, seg=24):
    """Open tube from y0 to y1 with a lip ring at each end."""
    part.tube((0, (y0 + y1) / 2, 0), r_out, r_out - r_in, y1 - y0, axis='Y', seg=seg, mat=CASING)
    for y in (y0 + 0.08, y1 - 0.08):
        part.tube((0, y, 0), r_out + 0.06, 0.12, 0.16, axis='Y', seg=seg, mat=STEEL)


def exhaust(part, y, r, length, glow_depth=0.3):
    part.cyl((0, y + length / 2, 0), r, length, axis='Y', seg=24, mat=DARK, radius_top=r * 0.8, cap=False)
    part.cyl((0, y + length - glow_depth / 2, 0), r * 0.78, glow_depth, axis='Y', seg=24, mat=GLOW)
    part.cyl((0, y + 0.02, 0), r * 0.35, 0.3, axis='Y', seg=12, mat=DARK, radius_top=0.05)


def pylon(part, y0, y1, r, height, width=0.5):
    part.box((0, (y0 + y1) / 2, r + height / 2), (width, y1 - y0, height), mat=CASING)


def stator(part, y, r_in, r_out, count=8):
    for i in range(count):
        a = 2 * math.pi * i / count
        rot = Matrix.Rotation(a, 4, 'Y')
        centre = rot @ Vector(((r_in + r_out) / 2, 0, 0))
        part.box((centre.x, y, centre.z), (r_out - r_in, 0.08, 0.16), mat=STEEL, rot=rot)


def turbine_long(coll):
    p = B.Part(B.link_materials(MATS))
    r = 1.1
    spinner(p, 0.2, 0.42, 0.9)
    fan(p, 0.6, r - 0.08, 11, 0.42, 0.06)
    casing(p, 0.0, 5.6, r, r + 0.1)
    stator(p, 1.6, 0.45, r - 0.05)
    p.cyl((0, 3.2, 0), 0.55, 2.4, axis='Y', seg=16, mat=DARK)           # core
    p.tube((0, 5.9, 0), r + 0.02, 0.10, 0.6, axis='Y', seg=24, mat=RECESS)  # afterburner band
    exhaust(p, 6.2, r - 0.05, 1.8)
    pylon(p, 1.2, 4.6, r + 0.1, 0.9)
    for y in (1.2, 2.6, 4.0):                                               # casing seams
        p.tube((0, y, 0), r + 0.12, 0.05, 0.06, axis='Y', seg=24, mat=STEEL)
    p.finish("Mesh_Turbine_Long", coll)


def turbine_short(coll):
    p = B.Part(B.link_materials(MATS))
    r = 0.9
    spinner(p, 0.15, 0.32, 0.6)
    fan(p, 0.45, r - 0.07, 9, 0.32, 0.05)
    casing(p, 0.0, 2.4, r, r + 0.09)
    stator(p, 1.2, 0.35, r - 0.05, 6)
    p.cyl((0, 1.7, 0), 0.45, 1.2, axis='Y', seg=16, mat=DARK)
    exhaust(p, 2.5, r - 0.05, 1.1)
    pylon(p, 0.6, 2.0, r + 0.09, 0.7)
    p.finish("Mesh_Turbine_Short", coll)


def turbine_ducted(coll):
    p = B.Part(B.link_materials(MATS))
    r = 1.5
    p.tube((0, 1.2, 0), r + 0.1, 0.22, 2.4, axis='Y', seg=32, mat=CASING)   # duct
    p.torus((0, 0.05, 0), r + 0.1, 0.14, axis='Y', maj_seg=32, min_seg=8, mat=STEEL)
    p.torus((0, 2.35, 0), r + 0.1, 0.14, axis='Y', maj_seg=32, min_seg=8, mat=STEEL)
    p.cyl((0, 1.2, 0), 0.4, 1.6, axis='Y', seg=16, mat=DARK)
    spinner(p, 0.3, 0.4, 0.5)
    fan(p, 0.9, r - 0.06, 13, 0.4, 0.05)
    stator(p, 1.9, 0.4, r - 0.05, 5)
    p.cyl((0, 2.2, 0), 0.32, 0.25, axis='Y', seg=16, mat=GLOW)
    pylon(p, 0.6, 1.8, r + 0.1, 0.5, width=0.4)
    p.finish("Mesh_Turbine_Ducted", coll)


def turbine_stub(coll):
    p = B.Part(B.link_materials(MATS))
    r = 0.6
    spinner(p, 0.1, 0.22, 0.4)
    fan(p, 0.3, r - 0.05, 7, 0.22, 0.04)
    casing(p, 0.0, 1.2, r, r + 0.07, seg=16)
    p.cyl((0, 0.9, 0), 0.3, 0.6, axis='Y', seg=12, mat=DARK)
    exhaust(p, 1.25, r - 0.04, 0.55, glow_depth=0.15)
    pylon(p, 0.3, 1.0, r + 0.07, 0.4, width=0.3)
    p.finish("Mesh_Turbine_Stub", coll)


def main():
    out = B.parse_out()
    B.start(out)
    turbine_long(B.collection("Coll_Turbine_Long"))
    turbine_short(B.collection("Coll_Turbine_Short"))
    turbine_ducted(B.collection("Coll_Turbine_Ducted"))
    turbine_stub(B.collection("Coll_Turbine_Stub"))
    B.save(out)


if __name__ == "__main__":
    main()
