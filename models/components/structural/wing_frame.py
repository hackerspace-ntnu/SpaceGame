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
from _buildlib import *  # noqa: E402,F403

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

# Fan geometry, shared with the assembly so the blades land on the lugs.
LUG_COUNT = 5
LUG_SPREAD = math.radians(98.0)    # total arc from first lug to last
LUG_RADIUS = 0.185


def lug_angle(i, count=LUG_COUNT, spread=LUG_SPREAD):
    """Angle of fan socket `i`, measured about the hub's +Z, 0 = straight out."""
    if count == 1:
        return 0.0
    return -spread / 2 + spread * i / (count - 1)


def hub(mats, coll):
    """Stacked disc with one knuckle lug per fan blade. Origin on the pivot."""
    p = Part(mats)

    p.cyl((0, 0, 0), 0.175, 0.070, axis='Z', seg=14, mat=4)
    p.cyl((0, 0, 0.062), 0.140, 0.060, axis='Z', seg=14, mat=0)
    p.cyl((0, 0, -0.062), 0.150, 0.060, axis='Z', seg=14, mat=0)
    p.cyl((0, 0, 0), 0.052, 0.235, axis='Z', seg=10, mat=2)     # king post

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
    for i in range(8):
        a = -LUG_SPREAD / 2 + LUG_SPREAD * i / 7
        p.box((0.245 * math.cos(a), 0.245 * math.sin(a), -0.078),
              (0.070, 0.048, 0.024), 3, rot=Matrix.Rotation(a, 4, 'Z'))

    p.bevel(width=0.007, segments=1)
    return p.finish("Mesh_WingFrame_Hub", coll, origin=(0, 0, 0))


def pylon(mats, coll):
    """Carries the shoulder off the fuselage flank. Origin at the body end,
    running out along +X."""
    p = Part(mats)

    # Tapering box beam, wide at the root where the load is.
    p.loft([(0.00, [(-0.130, -0.145), (0.130, -0.145),
                    (0.130, 0.150), (-0.130, 0.150)]),
            (0.34, [(-0.115, -0.125), (0.115, -0.125),
                    (0.115, 0.128), (-0.115, 0.128)]),
            (0.72, [(-0.095, -0.100), (0.095, -0.100),
                    (0.095, 0.105), (-0.095, 0.105)])],
           axis='X', mat=4)

    # Truss webbing on the flank, and a bolted root flange.
    for i in range(3):
        x = 0.11 + 0.24 * i
        p.box((x, 0, 0.020), (0.030, 0.245, 0.150), 7,
              rot=Matrix.Rotation(math.radians(28 if i % 2 else -28), 4, 'X'))
    p.box((0.012, 0, 0.005), (0.030, 0.320, 0.330), 0)
    p.rivets((0.012, -0.125, 0.140), (0.012, 0.125, 0.140), 4, radius=0.017,
             height=0.013, axis='X', mat=3)
    p.rivets((0.012, -0.125, -0.130), (0.012, 0.125, -0.130), 4, radius=0.017,
             height=0.013, axis='X', mat=3)

    # Shoulder saddle at the outboard end.
    p.box((0.735, 0, 0), (0.075, 0.290, 0.290), 0)
    p.cyl((0.760, 0, 0), 0.048, 0.070, axis='X', seg=10, mat=2)

    p.bevel(width=0.008, segments=1)
    return p.finish("Mesh_WingFrame_Pylon", coll, origin=(0, 0, 0))


def strut(mats, coll):
    """Turnbuckle tie-rod. Origin at the inboard fork, runs out along +X."""
    p = Part(mats)
    L = 1.05

    p.cyl((L / 2, 0, 0), 0.019, L * 0.86, axis='X', seg=8, mat=5)
    # Turnbuckle body at mid-span — the giveaway that it is hand-tensioned.
    p.box((L * 0.5, 0, 0), (0.185, 0.056, 0.056), 2)
    for sx in (-1, 1):
        p.cyl((L * 0.5 + sx * 0.105, 0, 0), 0.030, 0.040, axis='X', seg=8,
              mat=3)

    for x, flip in ((0.030, 1), (L - 0.030, -1)):
        for sz in (-1, 1):
            p.box((x + flip * 0.040, 0, sz * 0.032),
                  (0.090, 0.026, 0.028), 1)
        p.cyl((x, 0, 0), 0.026, 0.090, axis='Z', seg=6, mat=1)

    # Anti-chafe lashing where the rod crosses other structure.
    p.torus((L * 0.24, 0, 0), 0.032, 0.011, axis='X', maj_seg=8, min_seg=4,
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
