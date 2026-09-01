"""Knuckle blocks — the business end of a powered fist or ram.

The striking head that a ram carriage drives into something. Three heads that
bolt onto the same carriage face, so the same slide mechanism can be a demolition
fist, a breaching ram or a piling head without rebuilding the machine.

## Axes and origin

The library builds **−Y forward, +Z up**. A head strikes along −Y, so its face is
at the most negative Y in the file and nothing here may be built along +Y.

Origin is at the **rear mounting face**, `(0, 0, 0)` — the plane that bolts to a
carriage. That is the one connection point that matters: an assembly positions a
head by where it attaches, not by where it hits, and the strike face then sits at
a known −`DEPTH` in front of it.

## Sizing

0.13 m across × 0.115 m tall × 0.075 m deep — a head that covers a closed human
fist with a little proud of it. Big enough to read in silhouette at the distance
a first-person item is actually seen from, which is roughly arm's length.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402

from mathutils import Matrix  # noqa: E402

# Index 0 is STEEL because `bmesh.ops.bevel` stamps every face it creates with
# material index 0 — an accent colour there paints every chamfer in the file.
STEEL, DARK, CHROME, BRASS, RUBBER, RED = range(6)
MATS = ["Mat_Metal_Steel_Worn",       # frames, backing plates, the body
        "Mat_Metal_Steel_Dark",       # hardened striking surfaces
        "Mat_Metal_Chrome_Scuffed",   # bolt heads, pins
        "Mat_Metal_Brass_Tarnished",  # bushings and shim collars
        "Mat_Plastic_Rubber_Black",   # shock pads behind the face
        "Mat_Paint_Warn_Red"]         # one danger band per head

# 1.2 mm. Everything here is 8–30 mm thick and the library's 12 mm default would
# exceed half the thickness of the segment plates, at which point `finish()`'s
# remove_doubles welds the over-bevelled edges into a lump. See
# project_buildlib_traps.
BEVEL_W = 0.0012

WIDTH = 0.130
HEIGHT = 0.115
DEPTH = 0.075


def _f(d):
    """Forward distance `d` (metres in front of the mounting face) as a Y.

    Every dimension in this file is written as a forward distance, because that
    is how the thing is measured — "the face stands 75 mm proud of the mount" —
    and the sign flip is the single easiest way to ship a head that strikes
    backwards. Doing it in one place means it is done once.
    """
    return -d


def backing(p, thickness=0.012, bolts=True):
    """The plate every head bolts through. Shared by all three variations."""
    hard = p.slab((-WIDTH / 2, _f(thickness), -HEIGHT / 2),
                  (WIDTH / 2, 0.0, HEIGHT / 2), STEEL)

    # A recessed rubber shock pad: the head is driven into rock, and a hard
    # steel-on-steel mount is the detail whose absence reads as "toy".
    hard += p.slab((-WIDTH / 2 + 0.010, _f(thickness + 0.005), -HEIGHT / 2 + 0.010),
                   (WIDTH / 2 - 0.010, _f(thickness), HEIGHT / 2 - 0.010), RUBBER)

    if bolts:
        for sx in (-1, 1):
            for sz in (-1, 1):
                p.cyl((sx * (WIDTH / 2 - 0.013), _f(thickness + 0.004),
                       sz * (HEIGHT / 2 - 0.013)),
                      0.0055, 0.008, 'Y', 6, CHROME, radius_top=0.0042)
    return hard


def segmented(coll, mats):
    """Four stacked segment bars — the hero head.

    Four separate bars rather than one grooved block: the gaps are real geometry,
    so they hold a shadow line from any angle and in silhouette. A block with
    grooves cut into a flat face reads as a flat face from the side, which is
    exactly the angle a first-person item is seen from as the arm swings.
    """
    p = TrackedPart(mats)
    hard = backing(p)

    rows = 4
    gap = 0.004
    bar_h = (HEIGHT - gap * (rows - 1)) / rows

    for i in range(rows):
        z0 = -HEIGHT / 2 + i * (bar_h + gap)
        z1 = z0 + bar_h

        # Each bar's strike face is stepped back at the top and bottom row, so
        # the block's profile is a shallow convex curve rather than a slab —
        # a fist, not a brick.
        edge = 0.006 if i in (0, rows - 1) else 0.0
        front = _f(DEPTH - edge)

        hard += p.slab((-WIDTH / 2, front, z0), (WIDTH / 2, _f(0.012), z1), STEEL)

        # The hardened cap: the few millimetres that actually touch anything.
        # Stands 0.8 mm proud rather than flush. Flush puts its face on exactly
        # the same plane as the bar's, and two coplanar faces z-fight into a
        # hatched mess the moment the camera moves.
        hard += p.slab((-WIDTH / 2 + 0.003, front - 0.0008, z0 + 0.002),
                       (WIDTH / 2 - 0.003, front + 0.010, z1 - 0.002), DARK)

        # A cross pin through each bar, visible at both ends.
        for sx in (-1, 1):
            p.cyl((sx * (WIDTH / 2 - 0.004), _f(DEPTH * 0.55), (z0 + z1) / 2),
                  0.0055, 0.012, 'X', 8, BRASS)

    # Danger band across the top of the block, not across its face: the face is
    # the part that hits things, so a stencil there would be gone after a day —
    # and it is the one surface the wearer never sees. The top is in view every
    # time the arm comes up.
    hard += p.slab((-WIDTH / 2 + 0.018, _f(DEPTH - 0.014), HEIGHT / 2),
                   (WIDTH / 2 - 0.018, _f(DEPTH - 0.030), HEIGHT / 2 + 0.0018),
                   RED)

    p.restamp("segmented")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_KnuckleBlock_Segmented", coll)


def slab_head(coll, mats):
    """One solid ram head braced by a cross rib — the breaching variant."""
    p = TrackedPart(mats)
    hard = backing(p)

    hard += p.slab((-WIDTH / 2, _f(DEPTH - 0.010), -HEIGHT / 2),
                   (WIDTH / 2, _f(0.012), HEIGHT / 2), STEEL)

    # The face proper, inset so the body reads as a frame around a wear plate
    # that could be unbolted and replaced.
    hard += p.slab((-WIDTH / 2 + 0.008, _f(DEPTH), -HEIGHT / 2 + 0.008),
                   (WIDTH / 2 - 0.008, _f(DEPTH - 0.010), HEIGHT / 2 - 0.008),
                   DARK)

    # Diagonal cross rib, standing proud of the wear plate. Length is the
    # block's own diagonal, not a round number — a rib that overhangs the body
    # is geometry ending in mid-air, which reads as a modelling slip rather than
    # as a brace.
    diag = math.hypot(WIDTH, HEIGHT) - 0.012
    for sign in (1, -1):
        rot = Matrix.Rotation(math.radians(sign * 38), 4, 'Y')
        hard += p.box((0.0, _f(DEPTH + 0.004), 0.0), (diag, 0.012, 0.020),
                      STEEL, rot=rot)

    hard += p.slab((-WIDTH / 2 + 0.014, _f(DEPTH - 0.001), -HEIGHT / 2 + 0.014),
                   (-WIDTH / 2 + 0.030, _f(DEPTH - 0.004), HEIGHT / 2 - 0.014),
                   RED)

    p.restamp("slab")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_KnuckleBlock_Slab", coll)


def studded(coll, mats):
    """A breaker head: fewer, blunter contact points that concentrate the load."""
    p = TrackedPart(mats)
    hard = backing(p)

    hard += p.slab((-WIDTH / 2, _f(DEPTH - 0.026), -HEIGHT / 2),
                   (WIDTH / 2, _f(0.012), HEIGHT / 2), STEEL)

    # 3 x 3 truncated cones. Truncated rather than pointed: a point shatters and
    # a flat crown is what a real breaker tool wears down into anyway.
    for ix in range(3):
        for iz in range(3):
            x = (ix - 1) * 0.042
            z = (iz - 1) * 0.038
            p.cyl((x, _f(DEPTH - 0.013), z), 0.015, 0.026, 'Y', 8, DARK,
                  radius_top=0.0085)

    # Retaining ring around the stud field.
    hard += p.slab((-WIDTH / 2, _f(DEPTH - 0.020), -HEIGHT / 2),
                   (-WIDTH / 2 + 0.009, _f(DEPTH - 0.026), HEIGHT / 2), RED)
    hard += p.slab((WIDTH / 2 - 0.009, _f(DEPTH - 0.020), -HEIGHT / 2),
                   (WIDTH / 2, _f(DEPTH - 0.026), HEIGHT / 2), RED)

    p.rivets((-WIDTH / 2 + 0.020, _f(DEPTH - 0.028), HEIGHT / 2 - 0.006),
             (WIDTH / 2 - 0.020, _f(DEPTH - 0.028), HEIGHT / 2 - 0.006),
             5, radius=0.0035, height=0.005, axis='Y', mat=CHROME)

    p.restamp("studded")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_KnuckleBlock_Studded", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    segmented(collection("Coll_KnuckleBlock_Segmented"), mats)
    slab_head(collection("Coll_KnuckleBlock_Slab"), mats)
    studded(collection("Coll_KnuckleBlock_Studded"), mats)

    save(out)
    report()


if __name__ == "__main__":
    main()
