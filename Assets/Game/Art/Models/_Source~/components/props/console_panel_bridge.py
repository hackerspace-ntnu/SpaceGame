"""Add the Bridge variation to console_panel.blend.

A wide wrap-around flight console after the Prometheus rear-console prop: a
raked central desk with one large main display, a bank of three raised screens
behind it, and two yawed wing sections carrying small panels and joystick
domes. Simpler than the Helm variation — built for the lander blockout bridge.

Pilot sits on the -X side (knee well there), nose beyond +X, origin at deck
level centred on the footprint — same conventions as the rest of the file.

Additive: opens the existing .blend, adds collection Coll_ConsolePanel_Bridge,
asserts nothing pre-existing moved, saves in place. Never re-run the original
console_panel.py over the file.

    blender --background --python console_panel_bridge.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TARGET = os.path.join(HERE, "console_panel.blend")

DARK, PANEL, STEEL, ORANGE, GREEN, AMBER, RED, BLACK = range(8)
MATS = ["Mat_Metal_Steel_Dark", "Mat_Neutral_Panel_Grey",
        "Mat_Metal_Steel_Worn", "Mat_Paint_Safety_Orange",
        "Mat_Emissive_Green_CRT", "Mat_Emissive_Amber",
        "Mat_Emissive_Red_Warn", "Mat_Neutral_Black_Matte"]


def display(p, centre, w, h, rot, mat=GREEN):
    """A lit panel in a proud dark bezel."""
    c = Vector(centre)
    p.box(c, (w + 0.05, h + 0.05, 0.045), DARK, rot=rot)
    p.box(c + rot @ Vector((0, 0, 0.026)), (w, h, 0.012), mat, rot=rot)


def bridge(coll, mats):
    p = Part(mats)
    H = 0.92                                     # desk height
    fas = Matrix.Rotation(math.radians(-42), 4, 'Y')   # fascia rake, to pilot

    # Central carcass: raked front, knee well undercut on the pilot side.
    p.prism([(-0.32, 0.30), (-0.12, 0.30), (-0.12, 0.0), (0.32, 0.0),
             (0.32, 0.58), (0.10, H), (-0.32, H)],
            1.30, 'Y', PANEL)
    p.box((0.10, 0.0, 0.06), (0.44, 1.22, 0.12), DARK)   # kick recess

    # Main display on the raked fascia, orange lip along its lower edge.
    display(p, (-0.07, 0.0, H - 0.11), 0.62, 0.30, fas)
    p.box((-0.245, 0.0, 0.70), (0.05, 1.28, 0.045), ORANGE, rot=fas)
    # Two small readouts flanking the main display.
    display(p, (-0.02, -0.50, H - 0.09), 0.24, 0.16, fas, mat=AMBER)
    display(p, (-0.02, 0.50, H - 0.09), 0.24, 0.16, fas, mat=RED)

    # Raised bank of three screens along the far edge, angled at the pilot.
    # Negative faces the pilot, the same sign the fascia rake uses: these stand
    # upright, leaning 18 degrees back, screens toward -X. Positive turns all three
    # around to face the nose and buries the lit quad inside its own support box.
    tilt = Matrix.Rotation(math.radians(-(90 - 18)), 4, 'Y')
    for i, mat in ((-1, GREEN), (0, AMBER), (1, GREEN)):
        c = Vector((0.26, i * 0.44, H + 0.24))
        # The pedestal runs all the way down to the carcass chamfer (z 0.58 at the
        # far face) rather than stopping level with the desk top, which left the
        # whole bank hanging a third of a metre clear of the console.
        p.box((0.295, i * 0.44, (0.58 + H + 0.41) / 2),
              (0.10, 0.40, H + 0.41 - 0.58), PANEL)
        display(p, c, 0.30, 0.22, tilt, mat=mat)

    # Yawed wing sections: a low cabinet with a raked desk top carrying a
    # readout, a joystick and a lamp row.
    for s in (-1, 1):
        yaw = Matrix.Rotation(s * math.radians(-28), 4, 'Z')
        wtop = yaw @ Matrix.Rotation(math.radians(-20), 4, 'Y')
        c = Vector((-0.06, s * 0.86, 0))
        WH = H - 0.24
        p.box(c + Vector((0, 0, WH / 2)), (0.56, 0.52, WH), PANEL, rot=yaw)
        p.box(c + yaw @ Vector((-0.02, 0, WH + 0.02)), (0.62, 0.52, 0.16),
              PANEL, rot=wtop)
        p.box(c + yaw @ Vector((-0.28, 0, WH + 0.065)), (0.045, 0.52, 0.04),
              ORANGE, rot=wtop)
        display(p, c + yaw @ Vector((-0.10, -s * 0.10, WH + 0.115)),
                0.22, 0.15, wtop, mat=(AMBER if s < 0 else GREEN))
        stick = c + yaw @ Vector((0.10, s * 0.13, WH + 0.13))
        p.cyl(stick, 0.055, 0.04, 'Z', 10, STEEL, rot=wtop)
        p.cyl(stick + wtop @ Vector((0, 0, 0.08)), 0.032, 0.11, 'Z', 8, DARK,
              radius_top=0.042, rot=wtop)
        for i in range(3):
            b = c + yaw @ Vector((0.16, -s * (0.02 + i * 0.10), WH + 0.11))
            p.cyl(b, 0.020, 0.025, 'Z', 8, (AMBER, GREEN, RED)[i], rot=wtop)

    p.bevel(width=0.008, segments=2)
    return p.finish("Mesh_ConsolePanel_Bridge", coll)


def main():
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    # Replace only this script's own output; everything else is untouchable.
    old = bpy.data.collections.get("Coll_ConsolePanel_Bridge")
    if old is not None:
        for o in list(old.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(old)
    before = {o.name: o.matrix_world.copy() for o in bpy.data.objects}

    mats = link_materials(MATS)
    bridge(collection("Coll_ConsolePanel_Bridge"), mats)

    for name, mw in before.items():
        assert bpy.data.objects[name].matrix_world == mw, name
    report()
    bpy.ops.wm.save_mainfile(filepath=TARGET)
    print("Added Coll_ConsolePanel_Bridge to %s" % TARGET)


main()
