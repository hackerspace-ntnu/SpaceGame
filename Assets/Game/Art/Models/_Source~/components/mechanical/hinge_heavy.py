"""Hinges and rams for the ship's six moving panels.

The ship has four clamshell wall panels, a cargo ramp and a bulkhead door, and
until now every one of them pivoted on nothing visible. A door that swings on an
invisible axis is the single clearest tell that a model was assembled rather
than built, so these exist to sit on the pivot line and be seen.

Built with the pivot axis along Y and the origin *on the axis*, at (0, 0, 0).
That is the whole point: dropping one of these at a hinge position needs no
offset arithmetic, and it matches the axis the ArticulatedPart rotates about.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix  # noqa: E402

STEEL, DARK, RUST, CHROME, RUBBER = 0, 1, 2, 3, 4
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Metal_Rust_Heavy", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Rubber_Black"]


def barrel(coll, mats):
    """Three-knuckle butt hinge — the cabin wall panels and the bulkhead door.
    Big enough to read from across the bay."""
    p = Part(mats)
    width, knuckle_r = 0.40, 0.055

    # Knuckles alternate between the two leaves along the axis.
    for y in (-0.135, 0.0, 0.135):
        p.tube((0, y, 0), knuckle_r, 0.018, 0.12, 'Y', 14, STEEL)
    p.cyl((0, 0, 0), knuckle_r * 0.62, width + 0.06, 'Y', 10, CHROME)
    # Pin heads, so the axis has an end.
    for s in (-1, 1):
        p.cyl((0, s * (width / 2 + 0.03), 0), knuckle_r * 0.80, 0.03, 'Y', 10,
              DARK)

    # Two leaves, one to the fixed frame, one to the swinging panel.
    for x_out, ys in ((-0.19, (-0.135, 0.135)), (0.19, (0.0,))):
        for y in ys:
            p.slab((min(0, x_out), y - 0.058, -0.048),
                   (max(0, x_out), y + 0.058, 0.048), STEEL)
        plate_x = (-0.19, -0.05) if x_out < 0 else (0.05, 0.19)
        p.slab((plate_x[0], -width / 2, -0.115), (plate_x[1], width / 2, 0.115),
               STEEL)
        cx = sum(plate_x) / 2
        p.rivets((cx, -width / 2 + 0.06, 0.115), (cx, width / 2 - 0.06, 0.115),
                 3, radius=0.022, height=0.018, mat=DARK)
    # Grease nipple and rust weep — the maintained-but-not-well read.
    p.cyl((knuckle_r, 0.20, 0), 0.016, 0.05, 'Z', 6, DARK)
    p.tube((0, 0.068, 0), knuckle_r * 1.05, 0.012, 0.03, 'Y', 14, RUST)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_Hinge_Barrel", coll)


def piston(coll, mats):
    """Hydraulic ram that throws a panel open. Modelled at half extension so it
    reads correctly whether the door is shut or standing open."""
    p = Part(mats)
    # Anchor eyes at both ends, body angled between them.
    a = Vector((-0.42, 0.0, -0.30))
    b = Vector((0.34, 0.0, 0.46))
    d = (b - a)
    length = d.length
    ang = math.atan2(d.z, d.x)
    rot = Matrix.Rotation(ang, 4, 'Y')

    # Cylinder body over the lower two-thirds, polished rod above it.
    p.cyl(a.lerp(b, 0.33), 0.062, length * 0.62, 'X', 14, DARK, rot=rot)
    p.cyl(a.lerp(b, 0.62), 0.030, length * 0.42, 'X', 12, CHROME, rot=rot)
    # End caps and the gland nut where the rod enters.
    p.cyl(a.lerp(b, 0.05), 0.072, 0.06, 'X', 14, STEEL, rot=rot)
    p.cyl(a.lerp(b, 0.64), 0.052, 0.05, 'X', 12, STEEL, rot=rot)
    # Eyes.
    for end, r in ((a, 0.055), (b, 0.048)):
        p.tube(end, r, 0.022, 0.07, 'Y', 12, STEEL)
        p.cyl(end, r * 0.55, 0.10, 'Y', 10, DARK)
    # Hydraulic line looping back along the body.
    p.cyl(a.lerp(b, 0.20) + Vector((0, 0.085, 0)), 0.014, length * 0.5, 'X',
          6, RUBBER, rot=rot)
    p.cyl(a.lerp(b, 0.06) + Vector((0, 0.05, 0)), 0.018, 0.09, 'Y', 6, DARK)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_Hinge_Piston", coll)


def strap(coll, mats):
    """Long strap hinge bolted across the face of a panel — what you weld on
    when the proper hinge broke. Carries the cargo ramp."""
    p = Part(mats)
    width = 0.34
    p.tube((0, 0, 0), 0.062, 0.020, width, 'Y', 14, STEEL)
    p.cyl((0, 0, 0), 0.038, width + 0.10, 'Y', 10, CHROME)
    for s in (-1, 1):
        p.cyl((0, s * (width / 2 + 0.05), 0), 0.052, 0.035, 'Y', 10, DARK)

    # Tapering strap running out over the panel, in two steps.
    p.prism([(0.05, -0.10), (0.62, -0.045), (0.62, 0.045), (0.05, 0.10)],
            0.20, 'Y', STEEL)
    p.prism([(0.05, -0.10), (0.40, -0.06), (0.40, 0.06), (0.05, 0.10)],
            0.30, 'Y', STEEL)
    for x in (0.20, 0.36, 0.52):
        p.cyl((x, 0, 0.055), 0.030, 0.036, 'Z', 8, DARK)
        p.cyl((x, 0, -0.055), 0.030, 0.036, 'Z', 8, DARK)
    # Backing plate on the fixed side, plus a welded-on repair gusset.
    p.slab((-0.24, -width / 2, -0.13), (-0.06, width / 2, 0.13), STEEL)
    p.rivets((-0.15, -width / 2 + 0.05, 0.13), (-0.15, width / 2 - 0.05, 0.13),
             3, radius=0.024, height=0.02, mat=DARK)
    p.prism([(-0.06, 0.0), (-0.06, 0.22), (0.16, 0.0)], 0.05, 'Y', RUST,
            offset=(0, 0.12, 0))
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_Hinge_Strap", coll)


def slide_rail(coll, mats):
    """Linear rail and carriage — for anything that slides rather than swings.
    Not used on the ship as built; here because a sliding hatch is the obvious
    next moving part and a rail is the one thing it would need."""
    p = Part(mats)
    length = 1.20
    p.prism([(-0.05, -0.035), (0.05, -0.035), (0.05, 0.01), (0.028, 0.035),
             (-0.028, 0.035), (-0.05, 0.01)], length, 'Y', STEEL)
    for i in range(5):
        y = -length / 2 + 0.12 + i * (length - 0.24) / 4
        p.box((0, y, -0.048), (0.13, 0.06, 0.03), DARK)
        p.cyl((0, y, -0.048), 0.018, 0.05, 'Z', 6, DARK)
    # Carriage riding the rail.
    p.slab((-0.085, -0.11, 0.02), (0.085, 0.11, 0.09), DARK)
    for s in (-1, 1):
        for t in (-1, 1):
            p.cyl((s * 0.055, t * 0.07, 0.012), 0.026, 0.03, 'Z', 8, CHROME)
    p.box((0, 0, 0.115), (0.14, 0.18, 0.05), STEEL)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_Hinge_SlideRail", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Barrel", barrel), ("Piston", piston),
                     ("Strap", strap), ("SlideRail", slide_rail)):
        fn(collection("Coll_Hinge_" + name), mats)

    report()
    save(out)


main()
