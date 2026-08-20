"""Graviportal feet -- the columnar, weight-bearing kind.

The library's existing `foot_splayed` is a *sprawler's* foot: a thin splayed
manus on a limb that leaves the body sideways, with long separated toes that
read as gripping. This is the opposite animal. A columnar leg carries the mass
straight down a vertical strut, and the foot at the bottom of it is a
compression pad -- broad, deep, mostly heel, with short toes that barely clear
the pad they grow out of. Elephant rather than lizard.

Built to `_organic`'s foot convention, unchanged: **ankle socket at the origin,
sole below it at -Z, toes pointing +X**, y-symmetric so one mesh serves both
sides.

Two things here that `foot_splayed` does not do, both of which are what make a
heavy foot read as heavy:

- **The pad flares downward and outward.** Top ring is narrower than the sole,
  so the foot reads as spreading under load rather than as a cylinder someone
  stood the leg on. The flare is also what hides the join where the leg tube
  enters the ankle.
- **The toes sit *in* the pad, not on the end of it.** Their bases are set
  inboard of the sole rim so only the front third of each toe emerges. Toes
  that stick out cleanly are a running animal's; a graviportal foot shows
  knuckles and nails and nothing else.

Sizes are real-world metres for a very large creature -- the sole of a
`Pad_Broad3Toe` is 68 cm across. Scale the mesh data on the way in for anything
smaller.

## Variations

| Collection | Sole | Height | Toes | For |
|---|---|---|---|---|
| `Coll_Pad_Round4Toe`   | 0.60 | 0.34 | 4 | general-purpose heavy forefoot |
| `Coll_Pad_Broad3Toe`   | 0.68 | 0.30 | 3 | the widest, heaviest read -- hind foot |
| `Coll_Pad_Splayed5Toe` | 0.72 | 0.26 | 5 | flattest; spreads load on soft sand |
| `Coll_Pad_Cloven_Heavy`| 0.56 | 0.36 | 2 | two big digits, camel-like, deepest pad |

    blender --background --python foot_pad.py -- --out foot_pad.blend
"""

import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

import _buildlib as B  # noqa: E402
from mathutils import Vector  # noqa: E402

import _organic as O  # noqa: E402

MATS = ["Mat_Hide_Slate_Teal", "Mat_Hide_Claw_Horn"]
PAD, NAIL = 0, 1

RING = 14

#   (sole_rx, sole_ry, top_r, height, toes, spread, toe_r, toe_len, heel)
SPECS = {
    "Round4Toe":    (0.300, 0.290, 0.225, 0.34, 4, 0.58, 0.086, 0.165, 0.10),
    "Broad3Toe":    (0.340, 0.320, 0.250, 0.30, 3, 0.66, 0.110, 0.185, 0.12),
    "Splayed5Toe":  (0.360, 0.330, 0.215, 0.26, 5, 0.88, 0.072, 0.175, 0.08),
    "Cloven_Heavy": (0.280, 0.250, 0.230, 0.36, 2, 0.30, 0.140, 0.235, 0.14),
}


def disc(rx, ry, n=RING, cx=0.0):
    """Closed ellipse in the XY plane for `Part.loft(axis='Z')`."""
    return [(cx + rx * math.cos(2.0 * math.pi * i / n),
             ry * math.sin(2.0 * math.pi * i / n)) for i in range(n)]


def pad(part, spec):
    """The compression pad: ankle socket down to a flat sole.

    Five rings rather than two. The extra ones put the widest point at 82 % of
    the way down instead of at the sole itself, which is what gives the bulging
    weight-bearing profile -- a straight taper from ankle to sole reads as a
    traffic cone.

    The sole ring is only slightly narrower than the widest, not markedly so. An
    earlier pass pulled it in to 86 % and the resulting sharp overhanging rim
    made the whole foot read as a rubber suction cup.
    """
    srx, sry, top, h, _, _, _, _, heel = spec
    part.loft([
        (0.06,        disc(top * 0.94, top * 0.94)),
        (0.00,        disc(top, top)),
        (-h * 0.45,   disc(srx * 0.90, sry * 0.90, cx=-heel * 0.20)),
        (-h * 0.82,   disc(srx, sry, cx=-heel * 0.30)),
        (-h,          disc(srx * 0.94, sry * 0.94, cx=-heel * 0.28)),
    ], axis='Z', mat=PAD)


def toes(part, spec):
    """Short blunt digits set into the front of the pad, each capped with a nail.

    Toe bases sit at 80 % of the sole radius: far enough in that the pad still
    swallows the proximal half of each toe, far enough out that the knuckles and
    nails read from standing height. At 62 % they vanished into the pad
    entirely, which on an animal whose feet are at the player's eye level is a
    foot that looks like a stump.
    """
    srx, sry, _, h, count, spread, tr, tlen, _ = spec
    z = -h + tr * 0.86
    for i in range(count):
        t = 0.0 if count == 1 else (i / (count - 1.0)) * 2.0 - 1.0
        a = t * spread
        base = Vector((srx * 0.80 * math.cos(a), sry * 0.80 * math.sin(a), z))
        d = Vector((math.cos(a), math.sin(a), 0.0))

        # Two overlapping capsules: the overlap reads as a knuckle for free,
        # the same trick `_organic.digit` uses.
        for j, f in enumerate((1.0, 0.86)):
            c = base + d * (tlen * (0.30 + 0.44 * j))
            part.cyl(c, tr * f, tlen * 0.70, axis='Z', seg=10, mat=PAD,
                     radius_top=tr * f * 0.90, rot=O.heading_matrix(d))

        tip = base + d * (tlen * 1.02)
        part.cyl(tip, tr * 0.80, tr * 0.52, axis='Z', seg=10, mat=NAIL,
                 radius_top=tr * 0.46, rot=O.heading_matrix(d))


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)

    for name, spec in SPECS.items():
        coll = B.collection("Coll_Pad_%s" % name)
        part = B.Part(mats)
        pad(part, spec)
        toes(part, spec)
        part.bevel(width=0.010, segments=2, angle=46.0)
        part.finish("Mesh_Pad_%s" % name, coll)

    B.save(out)
    print("foot_pad: %d variations" % len(SPECS))


main()
