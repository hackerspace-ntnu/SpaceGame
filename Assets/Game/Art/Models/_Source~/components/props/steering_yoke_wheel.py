"""Add the Wheel variation to steering_yoke.blend.

A clean butterfly wheel after the racing-yoke concept: two moulded side grips,
an open top, a flat centre pad with paired buttons, and a lower cross bar —
plus a short column and pedestal foot so it can stand on a blockout deck.

Authoring frame matches the family: wheel face along +Z, column down local -Y,
origin at the hub centre. The foot plate lies in the local XZ plane, so with
the column placed vertical the foot sits flat on the deck.

Additive: opens the existing .blend, adds collection Coll_SteeringYoke_Wheel,
asserts nothing pre-existing moved, saves in place. Never re-run the original
steering_yoke.py over the file.

    blender --background --python steering_yoke_wheel.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TARGET = os.path.join(HERE, "steering_yoke.blend")

DARK, CHROME, RUBBER, STEEL, GREEN, AMBER = range(6)
MATS = ["Mat_Metal_Steel_Dark", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Steel_Worn",
        "Mat_Emissive_Green_CRT", "Mat_Emissive_Amber"]


def wheel(coll, mats):
    p = Part(mats)

    # Centre pad with a small status lamp and paired buttons each side.
    p.box((0.0, 0.0, 0.035), (0.30, 0.22, 0.09), DARK)
    p.cyl((0.0, 0.02, 0.085), 0.035, 0.02, 'Z', 10, AMBER)
    for s in (-1, 1):
        for dz in (0.03, -0.03):
            p.box((s * 0.105, dz + 0.02, 0.082), (0.05, 0.045, 0.025), CHROME)

    # Butterfly grips: moulded pads angled out at the top, chrome spine trim.
    for s in (-1, 1):
        lean = Matrix.Rotation(s * math.radians(-14), 4, 'Z')
        p.box((s * 0.335, 0.05, 0.02), (0.13, 0.40, 0.11), RUBBER, rot=lean)
        p.box((s * 0.40, 0.06, 0.005), (0.035, 0.42, 0.075), CHROME, rot=lean)
        # Spoke joining grip to the hub.
        p.box((s * 0.155, 0.02, 0.02), (0.17, 0.10, 0.07), DARK)

    # Lower cross bar closing the wheel under the pad.
    p.box((0.0, -0.17, 0.02), (0.44, 0.10, 0.075), DARK)

    # Column with a collar, ending in a pedestal foot flat in local XZ.
    p.cyl((0.0, -0.30, 0.0), 0.045, 0.30, 'Y', 12, STEEL)
    p.cyl((0.0, -0.145, 0.0), 0.06, 0.07, 'Y', 12, DARK)
    p.cyl((0.0, -0.58, 0.0), 0.055, 0.28, 'Y', 12, DARK)
    p.cyl((0.0, -0.70, 0.0), 0.12, 0.05, 'Y', 12, STEEL, radius_top=0.075)
    p.box((0.0, -0.735, 0.0), (0.30, 0.03, 0.30), DARK)

    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_SteeringYoke_Wheel", coll)


def main():
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    # Replace only this script's own output; everything else is untouchable.
    old = bpy.data.collections.get("Coll_SteeringYoke_Wheel")
    if old is not None:
        for o in list(old.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(old)
    before = {o.name: o.matrix_world.copy() for o in bpy.data.objects}

    mats = link_materials(MATS)
    wheel(collection("Coll_SteeringYoke_Wheel"), mats)

    for name, mw in before.items():
        assert bpy.data.objects[name].matrix_world == mw, name
    report()
    bpy.ops.wm.save_mainfile(filepath=TARGET)
    print("Added Coll_SteeringYoke_Wheel to %s" % TARGET)


main()
