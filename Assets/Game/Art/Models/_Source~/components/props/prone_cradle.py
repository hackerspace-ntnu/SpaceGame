"""Rider rig for the dune ornithopter — prone, slung under the belly.

Deliberately simple, per the brief: a board to lie on, a bar to hold, somewhere
to put your feet. Everything hangs from the fuselage belly rail, so each piece's
origin is at its *top* mounting point rather than its centre.

Rider convention: lying face-down, head toward −Y (forward).

    blender --background --python prone_cradle.py -- --out <path>/prone_cradle.blend
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", ".."))
import _buildlib  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _ornithopter import SCALE as ORNI_SCALE  # noqa: E402

# Authored at sketch units; shipped at the brief's 6 m span.
_buildlib.SCALE = ORNI_SCALE

MATS = [
    "Mat_Metal_Steel_Worn",        # 0  hanger frame
    "Mat_Metal_Steel_Dark",        # 1  fittings
    "Mat_Fabric_Seat_Ochre",       # 2  padding
    "Mat_Fabric_Canvas_Faded",     # 3  webbing straps
    "Mat_Wood_Ply_Worn",           # 4  the board itself
    "Mat_Metal_Rust_Heavy",        # 5  bolts
    "Mat_Plastic_Rubber_Black",    # 6  grips
    "Mat_Metal_Brass_Tarnished",   # 7  buckles
]


def pad(mats, coll):
    """Plywood board, ochre padding, webbing straps, two hanger arms."""
    p = Part(mats)
    DROP = 0.34          # how far below the rail the board hangs

    p.box((0, 0, -DROP), (0.40, 1.52, 0.032), 4)
    p.box((0, -0.10, -DROP + 0.036), (0.34, 1.20, 0.048), 2)
    # Chest bolster, so the rider is not lying flat on plywood.
    p.box((0, -0.50, -DROP + 0.070), (0.30, 0.34, 0.055), 2)

    # Webbing straps across the board, with brass buckles.
    for y in (-0.34, 0.10, 0.48):
        p.box((0, y, -DROP + 0.062), (0.44, 0.075, 0.014), 3)
        p.box((0.155, y, -DROP + 0.074), (0.055, 0.095, 0.022), 7)

    # Hanger arms up to the belly rail, braced fore and aft.
    for sx in (-1, 1):
        p.box((sx * 0.150, -0.30, -DROP / 2), (0.045, 0.058, DROP), 0)
        p.box((sx * 0.150, 0.42, -DROP / 2), (0.045, 0.058, DROP), 0)
        p.box((sx * 0.150, 0.06, -DROP * 0.55), (0.030, 0.78, 0.036), 0,
              rot=Matrix.Rotation(math.radians(9), 4, 'X'))
        p.cyl((sx * 0.150, -0.30, 0), 0.030, 0.055, axis='Z', seg=8, mat=1)
        p.cyl((sx * 0.150, 0.42, 0), 0.030, 0.055, axis='Z', seg=8, mat=1)

    p.rivets((-0.15, -0.62, -DROP + 0.020), (0.15, -0.62, -DROP + 0.020), 4,
             radius=0.014, height=0.010, axis='Z', mat=5)
    p.rivets((-0.15, 0.66, -DROP + 0.020), (0.15, 0.66, -DROP + 0.020), 4,
             radius=0.014, height=0.010, axis='Z', mat=5)

    p.bevel(width=0.007, segments=1)
    return p.finish("Mesh_ProneCradle_Pad", coll, origin=(0, 0, 0))


def grip_bar(mats, coll):
    """Control bar the rider steers with. Origin at the pivot on the rail."""
    p = Part(mats)

    p.cyl((0, 0, -0.30), 0.026, 0.68, axis='X', seg=8, mat=0)
    for sx in (-1, 1):
        # Swept-back grips, angled to where prone hands actually fall.
        p.cyl((sx * 0.415, 0.075, -0.30), 0.030, 0.20, axis='Y', seg=8,
              mat=6, rot=Matrix.Rotation(math.radians(sx * 16), 4, 'Z'))
        p.torus((sx * 0.30, 0, -0.30), 0.030, 0.010, axis='X', maj_seg=8,
                min_seg=4, mat=7)
        # Stalk down from the rail.
        p.box((sx * 0.11, 0, -0.16), (0.036, 0.042, 0.30), 0,
              rot=Matrix.Rotation(math.radians(sx * -12), 4, 'Y'))

    p.cyl((0, 0, -0.012), 0.040, 0.070, axis='Z', seg=8, mat=1)
    p.box((0, 0.052, -0.30), (0.10, 0.075, 0.055), 1)   # trim lever block
    p.cyl((0.03, 0.10, -0.265), 0.011, 0.135, axis='Y', seg=6, mat=7)

    p.bevel(width=0.005, segments=1)
    return p.finish("Mesh_ProneCradle_GripBar", coll, origin=(0, 0, 0))


def stirrup(mats, coll):
    """Foot rest. Origin at the rail mount; the loop hangs below and aft."""
    p = Part(mats)

    p.box((0, 0.06, -0.18), (0.034, 0.048, 0.36), 0,
          rot=Matrix.Rotation(math.radians(-11), 4, 'X'))
    p.box((0, 0.15, -0.355), (0.30, 0.135, 0.028), 0)      # tread plate
    p.box((0, 0.15, -0.338), (0.26, 0.100, 0.014), 6)      # rubber tread
    for sx in (-1, 1):
        p.box((sx * 0.135, 0.15, -0.320), (0.028, 0.135, 0.055), 0)
    p.box((0, 0.088, -0.300), (0.20, 0.030, 0.070), 3)     # heel strap
    p.cyl((0, 0.06, -0.010), 0.030, 0.052, axis='Z', seg=8, mat=1)

    p.bevel(width=0.005, segments=1)
    return p.finish("Mesh_ProneCradle_Stirrup", coll, origin=(0, 0, 0))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Coll_ProneCradle_Pad", pad),
                     ("Coll_ProneCradle_GripBar", grip_bar),
                     ("Coll_ProneCradle_Stirrup", stirrup)):
        fn(mats, collection(name))

    report()
    save(out)


main()
