"""Build components/organic/foot_splayed.blend -- broad sand-spreading feet.

Four variations for a heavy animal that has to stay on top of loose dune sand
rather than sink into it. That constraint is what makes these different from a
predator's foot: the toes fan almost side-to-side rather than pointing forward,
and the pad is wide and flat instead of compact.

Built with the ankle/wrist socket at the origin, the sole below it at -Z, and
the toes pointing +X. Symmetric about local y = 0, so one mesh serves both
sides. See `_organic.py` for the full convention.

Each toe ends in a small integral nail. The big digging claws live in
`claw_talon.blend` and get attached separately, because only front feet dig --
a rear foot with a spade claw on it looks wrong and a foot component that
forces one is not reusable.

Authored at final real-world scale for a ~5.5 m animal.

    blender --background --python foot_splayed.py -- \
        --out <lib>/components/organic/foot_splayed.blend
"""

import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from mathutils import Vector                                       # noqa: E402

from _buildlib import (Part, collection, link_materials, parse_out, report,
                       save, start)                                # noqa: E402
from _organic import (TOE_FANS, bone, heading_matrix, rounded,
                      shaped)                                      # noqa: E402

HIDE, PLATE, HORN = 0, 1, 2
MATERIALS = ["Mat_Hide_Sand_Pale", "Mat_Hide_Plate_Tan", "Mat_Hide_Claw_Horn"]


def sole(part, back, front, width, thickness, mat=HIDE, flat=0.86):
    """The fleshy pad: a domed top over a near-flat sole.

    Lofted rather than extruded from a flat outline. An extruded prism is quicker
    to write but gives the pad a hard rim all the way round, and a foot with a
    machined edge on it is the single thing that stops a creature reading as
    grown. `flat` lifts the underside almost to the pad's mid-plane, which is
    what leaves a flat sole under a rounded top.
    """
    span = front - back
    pts = [(back, width * 0.44, thickness * 0.72),
           (back + span * 0.22, width * 0.80, thickness * 1.00),
           (back + span * 0.52, width * 1.00, thickness * 0.96),
           (back + span * 0.80, width * 0.96, thickness * 0.84),
           (front, width * 0.74, thickness * 0.66)]
    part.loft(bone(shaped(rounded(pts, cap=0.32), cz=-thickness * 0.88,
                          flat_bottom=flat)),
              axis='X', mat=mat, cap=True)
    # Ankle boss: a low swelling around the socket rather than a stub cylinder,
    # so the limb meets flesh instead of appearing to be bolted on.
    part.cyl((back * 0.30, 0.0, -thickness * 0.45), width * 0.40,
             thickness * 1.35, axis='Z', seg=12, mat=mat,
             radius_top=width * 0.27)


def toe(part, origin, direction, length, radius, nail=0.030, joints=3):
    """One toe: overlapping shrinking capsules, tipped with a horn nail.

    Built as separate segments rather than a single lofted tube because the
    overlaps read as knuckles for free, and a sand-spreading toe is mostly
    knuckle.
    """
    p = Vector(origin)
    d = Vector((math.cos(direction), math.sin(direction), 0.0))
    rot = heading_matrix(d)
    seg = length / joints
    for j in range(joints):
        f = 1.0 - 0.20 * j
        centre = p + d * (seg * 0.5) - Vector((0.0, 0.0, seg * 0.05 * j))
        part.cyl(centre, radius * f, seg * 1.18, axis='Z', seg=8, mat=HIDE,
                 radius_top=radius * f * 0.84, rot=rot)
        p = p + d * seg - Vector((0.0, 0.0, seg * 0.10 * j))
    if nail:
        part.cyl(p + d * (nail * 0.45) - Vector((0.0, 0.0, nail * 0.12)),
                 radius * 0.52, nail, axis='Z', seg=6, mat=HORN,
                 radius_top=radius * 0.10, rot=rot)
    return p


def fan(part, name, nail=0.030):
    """Place the named variation's toes, evenly across +/-`spread` about +X.

    Bases sit on an ellipse rather than a circle so the outer toes start from
    the wide part of the pad instead of hanging off the front of it. The fan
    parameters live in `_organic.TOE_FANS` rather than here so that a model
    attaching separate claws can ask where the tips are without re-deriving it.
    """
    count, spread, rx, ry, z, length, radius, joints = TOE_FANS[name]
    tips = []
    for i in range(count):
        t = 0.0 if count == 1 else (i / (count - 1.0)) * 2.0 - 1.0
        a = t * spread
        base = (rx * math.cos(a), ry * math.sin(a), z)
        tips.append(toe(part, base, a, length, radius, nail=nail,
                        joints=joints))
    return tips


def manus4(mats):
    """Front foot: four toes, broad and short. The digging hand."""
    part = Part(mats)
    sole(part, -0.11, 0.15, 0.180, 0.084)
    fan(part, "Manus4", nail=0.030)
    part.bevel(width=0.005, segments=1)
    return part


def pes5(mats):
    """Rear foot: five toes, longer and wider than the front -- the rear limb
    both carries more weight and does the pushing."""
    part = Part(mats)
    sole(part, -0.13, 0.17, 0.200, 0.094)
    fan(part, "Pes5", nail=0.031)
    part.bevel(width=0.005, segments=1)
    return part


def spade(mats):
    """Toeless fused shovel with a hardened leading edge -- for a burrower, or
    a Vrescal variant that ploughs rather than walks."""
    part = Part(mats)
    sole(part, -0.12, 0.14, 0.190, 0.092)
    # Keratin blade rolled across the front of the sole, angled to bite sand.
    blade = [(0.06, 0.150, 0.020), (0.13, 0.140, 0.016), (0.20, 0.104, 0.011)]
    part.loft(bone(shaped(rounded(blade, cap=0.30), cz=-0.062,
                          flat_bottom=0.9)), axis='X', mat=PLATE, cap=True)
    part.bevel(width=0.006, segments=1)
    return part


def fringed(mats):
    """Four toes carrying lateral fringe scales -- the sandfish trick, a comb
    of keratin spines that widens the footprint without adding flesh."""
    part = Part(mats)
    sole(part, -0.11, 0.14, 0.174, 0.080)
    tips = fan(part, "Fringed", nail=0.026)
    for tip in tips:
        side = 1.0 if tip.y >= 0 else -1.0
        for k in range(4):
            f = k / 3.0
            p = Vector((tip.x - 0.030 - 0.026 * k,
                        tip.y - side * (0.024 + 0.010 * k),
                        tip.z + 0.004))
            part.cyl(p, 0.011 - 0.002 * f, 0.052 - 0.010 * k, axis='Y',
                     seg=5, mat=PLATE, radius_top=0.003)
    part.bevel(width=0.004, segments=1)
    return part


VARIANTS = [
    ("Manus4", manus4),
    ("Pes5", pes5),
    ("Spade", spade),
    ("Fringed", fringed),
]


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATERIALS)
    for name, builder in VARIANTS:
        coll = collection("Coll_FootSplayed_%s" % name)
        builder(mats).finish("Mesh_FootSplayed_%s" % name, coll)
    report()
    save(out)


main()
