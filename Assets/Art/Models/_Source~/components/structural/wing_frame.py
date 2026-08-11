"""Structure carrying the ornithopter's wings: fan hub, shoulder pylon, strut.

Split from `shoulder_gear` because these are structure rather than mechanism —
a pylon or a turnbuckle strut belongs on anything in the desert fleet, whereas
a crank only belongs on something that flaps.

The hub is the important one: its knuckle lugs are the sockets the blade root
clevises pin into, and their angular spacing sets the fan's fully-open spread.

    blender --background --python wing_frame.py -- --out <path>/wing_frame.blend
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", ".."))
import _buildlib  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _ornithopter import (SCALE as ORNI_SCALE, LUG_COUNT, LUG_SPREAD,  # noqa: E402
                          LUG_RADIUS, lug_angle)

# Authored at sketch units; shipped at the brief's 6 m span.
_buildlib.SCALE = ORNI_SCALE

MATS = [
    "Mat_Metal_Steel_Worn",        # 0
    "Mat_Metal_Steel_Dark",        # 1
    "Mat_Metal_Brass_Tarnished",   # 2
    "Mat_Metal_Rust_Heavy",        # 3
    "Mat_Paint_Hull_Bleached",     # 4
    "Mat_Metal_Chrome_Scuffed",    # 5
    "Mat_Fabric_Canvas_Faded",     # 6  lashed webbing
    "Mat_Paint_Olive_Deep",        # 7  shadow panels
]

def hub(mats, coll):
    """Stacked disc with one knuckle lug per fan blade. Origin on the pivot."""
    p = Part(mats)

    p.cyl((0, 0, 0), 0.175, 0.070, axis='Z', seg=12, mat=4)
    p.cyl((0, 0, 0.062), 0.140, 0.060, axis='Z', seg=12, mat=0)
    p.cyl((0, 0, -0.062), 0.150, 0.060, axis='Z', seg=12, mat=0)
    p.cyl((0, 0, 0), 0.052, 0.235, axis='Z', seg=8, mat=2)      # king post

    for i in range(LUG_COUNT):
        a = lug_angle(i)
        ca, sa = math.cos(a), math.sin(a)
        rot = Matrix.Rotation(a, 4, 'Z')
        # Knuckle: a pair of cheeks with a pin, sized to take a blade clevis.
        p.box((LUG_RADIUS * ca, LUG_RADIUS * sa, 0),
              (0.150, 0.105, 0.088), 0, rot=rot)
        p.cyl(((LUG_RADIUS + 0.045) * ca, (LUG_RADIUS + 0.045) * sa, 0),
              0.030, 0.125, axis='Z', seg=6, mat=2)

    # Splay control quadrant — the arc the blade roots are cabled to.
    for i in range(6):
        a = -LUG_SPREAD / 2 + LUG_SPREAD * i / 5
        p.box((0.245 * math.cos(a), 0.245 * math.sin(a), -0.078),
              (0.078, 0.052, 0.024), 3, rot=Matrix.Rotation(a, 4, 'Z'))

    p.bevel(width=0.007, segments=1)
    return p.finish("Mesh_WingFrame_Hub", coll, origin=(0, 0, 0))


def pylon(mats, coll):
    """Carries the shoulder off the fuselage flank. Origin at the body end,
    running out along +X.

    An open four-longeron truss rather than the boxed beam this used to be —
    the load path is the same and you can see straight through it, which is
    what stops the shoulder reading as a lump.
    """
    p = Part(mats)
    L = 0.755

    # Four thin longerons converging slightly toward the shoulder.
    corners = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    for cy, cz in corners:
        for i in range(2):
            x0, x1 = i * L / 2, (i + 1) * L / 2
            y0, z0 = cy * 0.105, cz * 0.112
            y1, z1 = cy * 0.078, cz * 0.082
            ya = y0 + (y1 - y0) * (i / 2.0)
            za = z0 + (z1 - z0) * (i / 2.0)
            yb = y0 + (y1 - y0) * ((i + 1) / 2.0)
            zb = z0 + (z1 - z0) * ((i + 1) / 2.0)
            p.seam((x0, ya, za), (x1, yb, zb), width=0.024, depth=0.024,
                   axis='Z', mat=0)

    # Diagonal web members, alternating, on the two vertical faces.
    for i in range(4):
        x = 0.085 + 0.185 * i
        f = 1.0 - 0.36 * (x / L)
        for cy in (-1, 1):
            p.seam((x, cy * 0.100 * f, -0.108 * f),
                   (x + 0.170, cy * 0.100 * f, 0.108 * f),
                   width=0.017, depth=0.017, axis='X', mat=7)

    # Bolted root flange onto the fuselage rib.
    p.box((0.010, 0, 0.005), (0.022, 0.245, 0.255), 0)
    p.rivets((0.010, -0.092, 0.104), (0.010, 0.092, 0.104), 3, radius=0.014,
             height=0.010, axis='X', mat=3)
    p.rivets((0.010, -0.092, -0.096), (0.010, 0.092, -0.096), 3, radius=0.014,
             height=0.010, axis='X', mat=3)

    # Shoulder saddle at the outboard end.
    p.box((L + 0.014, 0, 0), (0.040, 0.190, 0.195), 0)
    p.cyl((L + 0.032, 0, 0), 0.032, 0.048, axis='X', seg=10, mat=2)

    p.bevel(width=0.005, segments=1)
    return p.finish("Mesh_WingFrame_Pylon", coll, origin=(0, 0, 0))


def strut(mats, coll):
    """Turnbuckle tie-rod. Origin at the inboard fork, runs out along +X."""
    p = Part(mats)
    L = 1.05

    p.cyl((L / 2, 0, 0), 0.012, L * 0.86, axis='X', seg=8, mat=5)
    # Turnbuckle body at mid-span — the giveaway that it is hand-tensioned.
    p.box((L * 0.5, 0, 0), (0.130, 0.034, 0.034), 2)
    for sx in (-1, 1):
        p.cyl((L * 0.5 + sx * 0.074, 0, 0), 0.019, 0.026, axis='X', seg=8,
              mat=3)

    for x, flip in ((0.024, 1), (L - 0.024, -1)):
        for sz in (-1, 1):
            p.box((x + flip * 0.030, 0, sz * 0.021),
                  (0.066, 0.017, 0.018), 1)
        p.cyl((x, 0, 0), 0.017, 0.058, axis='Z', seg=6, mat=1)

    # Anti-chafe lashing where the rod crosses other structure.
    p.torus((L * 0.24, 0, 0), 0.021, 0.008, axis='X', maj_seg=8, min_seg=4,
            mat=6)

    p.bevel(width=0.005, segments=1)
    return p.finish("Mesh_WingFrame_Strut", coll, origin=(0, 0, 0))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Coll_WingFrame_Hub", hub),
                     ("Coll_WingFrame_Pylon", pylon),
                     ("Coll_WingFrame_Strut", strut)):
        fn(mats, collection(name))

    report()
    save(out)


main()
