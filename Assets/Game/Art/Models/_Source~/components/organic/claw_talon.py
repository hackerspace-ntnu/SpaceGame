"""Build components/organic/claw_talon.blend -- keratin claws.

Four variations, separate from `foot_splayed.blend` on purpose: which claw a
foot wears is a property of what the animal does with that limb, not of the
foot. The Vrescal digs with its front feet and only walks on its rear ones, so
the same `Manus4`/`Pes5` pads take a heavy digging claw at the front and keep
their small integral nails at the back.

Built with the base -- where the claw leaves the toe -- at the origin, growing
along +X and hooking down toward -Z. The base ring is capped flat so it seats
against a toe tip without a visible seam. See `_organic.py` for the convention.

The curve falls as t squared rather than as an arc: a claw is nearly straight
where it leaves the toe and hooks hardest at the tip, and an even arc reads as
a bent rod.

Authored at final real-world scale for a ~5.5 m animal.

    blender --background --python claw_talon.py -- \
        --out <lib>/components/organic/claw_talon.blend
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from _buildlib import (Part, collection, link_materials, parse_out, report,
                       save, start)                                # noqa: E402
from _organic import bone, shaped                                  # noqa: E402

HIDE, PLATE, HORN = 0, 1, 2
MATERIALS = ["Mat_Hide_Sand_Pale", "Mat_Hide_Plate_Tan", "Mat_Hide_Claw_Horn"]


def talon(mats, points, droop, ridge=0.0):
    """Loft a claw and cap its base. The tip station is small but not zero --
    a zero ring collapses to coincident vertices and pinches the shading."""
    part = Part(mats)
    part.loft(bone(shaped(points, droop=droop, ridge=ridge)), axis='X',
              mat=HORN, cap=True)
    part.bevel(width=0.003, segments=1)
    return part


def digging(mats):
    """Broad and flattened, with a blunt chisel tip -- built for moving sand,
    not for holding prey. The front-limb claw."""
    return talon(mats, [(0.000, 0.034, 0.026),
                        (0.030, 0.036, 0.024),
                        (0.070, 0.030, 0.018),
                        (0.105, 0.019, 0.011),
                        (0.124, 0.007, 0.004)], droop=0.026)


def hooked(mats):
    """Narrow and deep in section, hooking hard -- a gripping claw. Taller than
    it is wide, the opposite of `Digging`."""
    return talon(mats, [(0.000, 0.022, 0.030),
                        (0.028, 0.021, 0.031),
                        (0.062, 0.016, 0.024),
                        (0.092, 0.010, 0.014),
                        (0.110, 0.004, 0.005)], droop=0.052, ridge=0.004)


def blunt(mats):
    """Worn down to a stub -- an old animal, or one that walks on rock. Keeps a
    usable radius at the tip instead of tapering to a point."""
    return talon(mats, [(0.000, 0.030, 0.026),
                        (0.026, 0.029, 0.024),
                        (0.052, 0.024, 0.019),
                        (0.070, 0.019, 0.015)], droop=0.010)


def dewclaw(mats):
    """The small raised claw that never touches the ground."""
    return talon(mats, [(0.000, 0.015, 0.017),
                        (0.018, 0.014, 0.016),
                        (0.038, 0.009, 0.010),
                        (0.050, 0.003, 0.004)], droop=0.018)


VARIANTS = [
    ("Digging", digging),
    ("Hooked", hooked),
    ("Blunt", blunt),
    ("Dewclaw", dewclaw),
]


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATERIALS)
    for name, builder in VARIANTS:
        coll = collection("Coll_ClawTalon_%s" % name)
        builder(mats).finish("Mesh_ClawTalon_%s" % name, coll)
    report()
    save(out)


main()
