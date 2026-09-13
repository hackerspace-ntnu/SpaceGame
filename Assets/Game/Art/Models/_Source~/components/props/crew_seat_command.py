"""Add the Command variation to crew_seat.blend.

A clean sci-fi command chair for the lander bridge, after the TRTCH_SFT
concept: angular pale shell, orange bolster rails, dark cushions, a headrest
display module, and fold-forward leg guards ending in a footplate. Deliberately
simpler than the ochre-vinyl family — the lander cockpit is blockout-grade.

Additive: opens the existing .blend, adds collection Coll_CrewSeat_Command,
asserts nothing pre-existing moved, saves in place. Never re-run the original
crew_seat.py over the file.

    blender --background --python crew_seat_command.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TARGET = os.path.join(HERE, "crew_seat.blend")

SHELL, ORANGE, CUSHION, DARK, STEEL, RUBBER, GREEN = range(7)
MATS = ["Mat_Paint_White_Arctic", "Mat_Paint_Safety_Orange",
        "Mat_Neutral_Black_Matte", "Mat_Metal_Steel_Dark",
        "Mat_Metal_Steel_Worn", "Mat_Plastic_Rubber_Black",
        "Mat_Emissive_Green_CRT"]


def command(coll, mats):
    """Facing +X like the rest of the family, origin at deck level under the
    pedestal centre."""
    p = Part(mats)
    # Seats face +X, and a rotation about +Y carries +Z toward +X — so a rearward
    # rake is negative. The shell's own back leans 10 degrees back; the cushions and
    # headrest that ride on it have to use the same sign or they splay off it.
    back = Matrix.Rotation(math.radians(-10), 4, 'Y')

    # Pedestal block with a steel kick plate.
    p.box((0.0, 0.0, 0.03), (0.62, 0.62, 0.06), STEEL)
    p.box((0.0, 0.0, 0.20), (0.48, 0.46, 0.30), DARK)

    # One-piece angular shell: pan and tall reclined back, side silhouette.
    p.prism([(0.34, 0.34), (0.38, 0.46), (-0.20, 0.52), (-0.34, 0.48),
             (-0.50, 1.34), (-0.66, 1.32), (-0.46, 0.40), (-0.34, 0.30)],
            0.64, 'Y', SHELL)

    # Orange bolster rails up the sides of the back and around the pan.
    for s in (-1, 1):
        p.prism([(-0.36, 0.50), (-0.28, 0.50), (-0.44, 1.30), (-0.54, 1.28)],
                0.07, 'Y', ORANGE, offset=(0, s * 0.30, 0))
        p.prism([(0.26, 0.42), (0.36, 0.44), (0.34, 0.56), (0.22, 0.54)],
                0.07, 'Y', ORANGE, offset=(0, s * 0.30, 0))

    # Cushions: pan and back pads, panelled in two steps.
    p.box((0.02, 0.0, 0.50), (0.52, 0.54, 0.10), CUSHION)
    p.box((-0.36, 0.0, 0.86), (0.12, 0.50, 0.56), CUSHION, rot=back)
    p.box((-0.44, 0.0, 1.16), (0.12, 0.42, 0.24), CUSHION, rot=back)

    # Headrest module with a rear status display.
    p.box((-0.52, 0.0, 1.44), (0.20, 0.36, 0.22), SHELL, rot=back)
    p.box((-0.60, 0.0, 1.45), (0.05, 0.26, 0.13), DARK, rot=back)
    p.box((-0.625, 0.0, 1.45), (0.02, 0.20, 0.09), GREEN, rot=back)

    # Armrests on angled struts.
    for s in (-1, 1):
        p.box((-0.02, s * 0.36, 0.68), (0.42, 0.09, 0.07), DARK)
        p.box((-0.02, s * 0.36, 0.735), (0.36, 0.085, 0.05), CUSHION)
        p.box((-0.22, s * 0.36, 0.58), (0.07, 0.06, 0.16), STEEL)

    # Fold-forward leg guards, orange outer plates, down at 50 degrees.
    # Positive drops the outboard end: the guard runs from the hinge drum at the pan
    # lip down to the footplate. Negative swings it up into the air off both.
    drop = Matrix.Rotation(math.radians(50), 4, 'Y')
    for s in (-1, 1):
        p.box((0.42, s * 0.20, 0.28), (0.46, 0.12, 0.07), SHELL, rot=drop)
        p.box((0.42, s * 0.20, 0.325), (0.34, 0.13, 0.04), ORANGE, rot=drop)
        # Hinge drum at the pan lip.
        p.cyl((0.30, s * 0.20, 0.40), 0.045, 0.16, 'Y', 10, DARK)

    # Footplate with a rubber tread.
    p.box((0.58, 0.0, 0.10), (0.26, 0.52, 0.05), STEEL)
    p.box((0.58, 0.0, 0.13), (0.22, 0.46, 0.02), RUBBER)

    p.bevel(width=0.008, segments=2)
    return p.finish("Mesh_CrewSeat_Command", coll)


def main():
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    if "Coll_CrewSeat_Command" in bpy.data.collections:
        raise SystemExit("Coll_CrewSeat_Command already exists — refusing.")
    before = {o.name: o.matrix_world.copy() for o in bpy.data.objects}

    mats = link_materials(MATS)
    command(collection("Coll_CrewSeat_Command"), mats)

    for name, mw in before.items():
        assert bpy.data.objects[name].matrix_world == mw, name
    report()
    bpy.ops.wm.save_mainfile(filepath=TARGET)
    print("Added Coll_CrewSeat_Command to %s" % TARGET)


main()
