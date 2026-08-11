"""Wing-drive mechanism for the dune ornithopter: the visible gears.

The reference sketch shows the drive wheels as full circles in plan view, which
means they lie flat and turn about a vertical axis. Everything here is built in
the XY plane spinning about local +Z, with the origin on the shaft centre, so a
bone at the origin spins the part with a single-axis rotation.

    blender --background --python shoulder_gear.py -- --out <path>/shoulder_gear.blend
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", ".."))
from _buildlib import *  # noqa: E402,F403

MATS = [
    "Mat_Metal_Steel_Worn",        # 0  rim, spokes, structure
    "Mat_Metal_Steel_Dark",        # 1  shafts, fittings
    "Mat_Metal_Brass_Tarnished",   # 2  hubs, gear teeth, bushings
    "Mat_Metal_Rust_Heavy",        # 3  weathering, old bolt heads
    "Mat_Paint_Hull_Bleached",     # 4  painted housing cheeks
    "Mat_Metal_Chrome_Scuffed",    # 5  polished rod
    "Mat_Plastic_Rubber_Black",    # 6  seals, boots
    "Mat_Paint_Warn_Red",          # 7  hazard mark on the flywheel
]


def spoked(mats, coll):
    """The big open drive wheel — the machine's signature part."""
    p = Part(mats)
    R = 0.42

    p.tube((0, 0, 0), R, 0.058, 0.078, axis='Z', seg=16, mat=0)
    # Inner tension ring, so the spokes land on something rather than floating.
    p.tube((0, 0, 0), 0.135, 0.030, 0.052, axis='Z', seg=12, mat=0)

    for i in range(6):
        a = 2 * math.pi * i / 6
        rmid = (0.135 + R - 0.058) / 2
        p.box((rmid * math.cos(a), rmid * math.sin(a), 0),
              (R - 0.135 - 0.05, 0.040, 0.030), 0,
              rot=Matrix.Rotation(a, 4, 'Z'))
        # Bolted flag where each spoke meets the rim.
        p.box(((R - 0.075) * math.cos(a), (R - 0.075) * math.sin(a), 0),
              (0.070, 0.075, 0.046), 3, rot=Matrix.Rotation(a, 4, 'Z'))

    p.cyl((0, 0, 0), 0.105, 0.115, axis='Z', seg=12, mat=2)
    p.cyl((0, 0, 0.070), 0.062, 0.048, axis='Z', seg=10, mat=2)
    p.cyl((0, 0, 0), 0.034, 0.185, axis='Z', seg=8, mat=1)

    # Hazard stripe on one rim segment — reads as a timing mark.
    p.box((R - 0.030, 0, 0), (0.026, 0.090, 0.080), 7)

    for i in range(6):
        a = 2 * math.pi * i / 6 + math.pi / 6
        p.cyl(((R - 0.030) * math.cos(a), (R - 0.030) * math.sin(a), 0.040),
              0.014, 0.014, axis='Z', seg=6, mat=3)

    p.bevel(width=0.008, segments=1)
    return p.finish("Mesh_ShoulderGear_Spoked", coll, origin=(0, 0, 0))


def toothed(mats, coll):
    """A real toothed cog — meshes against the spoked wheel's inner ring."""
    p = Part(mats)
    R, teeth = 0.235, 14

    p.cyl((0, 0, 0), R - 0.028, 0.062, axis='Z', seg=16, mat=2)
    for i in range(teeth):
        a = 2 * math.pi * i / teeth
        p.box(((R - 0.014) * math.cos(a), (R - 0.014) * math.sin(a), 0),
              (0.038, 0.030, 0.060), 2, rot=Matrix.Rotation(a, 4, 'Z'))

    # Lightening holes: a homemade cog gets drilled out to save weight.
    for i in range(4):
        a = 2 * math.pi * i / 4
        p.cyl((0.125 * math.cos(a), 0.125 * math.sin(a), 0), 0.042, 0.070,
              axis='Z', seg=8, mat=1)

    p.cyl((0, 0, 0), 0.072, 0.086, axis='Z', seg=12, mat=0)
    p.cyl((0, 0, 0), 0.028, 0.150, axis='Z', seg=8, mat=1)

    p.bevel(width=0.005, segments=1)
    return p.finish("Mesh_ShoulderGear_Toothed", coll, origin=(0, 0, 0))


def bearing(mats, coll):
    """Pivot block the wing root swings in. Origin on the pivot bore."""
    p = Part(mats)

    p.box((0, 0, -0.045), (0.34, 0.30, 0.20), 4)
    p.box((0, 0, -0.155), (0.40, 0.36, 0.045), 0)      # base flange

    # Two cheeks straddling the bore, with the bore liner between them.
    for sy in (-1, 1):
        p.box((0, sy * 0.155, 0.070), (0.26, 0.048, 0.26), 0)
    p.cyl((0, 0, 0.070), 0.062, 0.34, axis='Y', seg=14, mat=2)
    p.cyl((0, 0, 0.070), 0.034, 0.40, axis='Y', seg=10, mat=1)

    # Grease boot and a couple of cap bolts.
    p.torus((0, 0.145, 0.070), 0.072, 0.020, axis='Y', maj_seg=12, min_seg=5,
            mat=6)
    p.rivets((-0.15, 0, -0.140), (0.15, 0, -0.140), 4, radius=0.020,
             height=0.018, axis='Z', mat=3)

    p.bevel(width=0.009, segments=1)
    return p.finish("Mesh_ShoulderGear_Bearing", coll, origin=(0, 0, 0.070))


def crank(mats, coll):
    """Crank arm and connecting rod — what turns wheel spin into a wing beat.

    Origin on the crank shaft; the rod runs out along +X.
    """
    p = Part(mats)

    p.cyl((0, 0, 0), 0.058, 0.10, axis='Z', seg=12, mat=2)
    p.box((0.145, 0, 0), (0.30, 0.072, 0.042), 0)     # throw arm
    p.cyl((0.275, 0, 0), 0.044, 0.088, axis='Z', seg=10, mat=2)

    # Con-rod: polished rod between two forked ends.
    p.cyl((0.275 + 0.315, 0, 0.062), 0.026, 0.63, axis='X', seg=10, mat=5)
    for x in (0.275, 0.275 + 0.63):
        for sy in (-1, 1):
            p.box((x, sy * 0.050, 0.062), (0.085, 0.026, 0.075), 1)
    p.cyl((0.275 + 0.63, 0, 0.062), 0.030, 0.135, axis='Y', seg=10, mat=1)

    p.rivets((0.06, 0, 0.030), (0.24, 0, 0.030), 3, radius=0.013,
             height=0.010, axis='Z', mat=3)

    p.bevel(width=0.006, segments=1)
    return p.finish("Mesh_ShoulderGear_Crank", coll, origin=(0, 0, 0))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Coll_ShoulderGear_Spoked", spoked),
                     ("Coll_ShoulderGear_Toothed", toothed),
                     ("Coll_ShoulderGear_Bearing", bearing),
                     ("Coll_ShoulderGear_Crank", crank)):
        fn(mats, collection(name))

    report()
    save(out)


main()
