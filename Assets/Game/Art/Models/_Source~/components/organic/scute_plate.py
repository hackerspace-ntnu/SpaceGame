"""Armour scales, keels and spikes that stamp onto a creature's skin.

The library's existing organic parts are all *structural* -- a limb segment, a
foot, a haunch, each one a chunk of the animal's silhouette. This is the first
component that is pure **surface**: a single keratin scale meant to be
replicated a few hundred times across a body that is already the right shape.

That difference sets the convention, which is deliberately unlike the limb one:

**Scales are built footprint-down in the XY plane, growing along +Z.** +Z is
the outward skin normal at the point the scale gets stamped, and +X is the
direction the scale overlaps *toward* -- down the body, tailwards -- so a model
that orients +Z to the surface normal and +X to the body's rearward tangent
gets a correctly-lying scale with no further thought. They are y-symmetric, so
one mesh serves both flanks.

**The base ring sits slightly below z = 0** (`SINK`). A scale whose base is
exactly on the skin plane shows a hairline of background between plate and hide
wherever the surface curves away underneath it; sinking the footprint a few
millimetres into the body is what makes it read as growing out of the animal
rather than glued on.

Sizes are real-world metres for a large creature -- a `Cracked_Hex` is 30 cm
across, which is a scale you can see from across a dune. A model on a smaller
animal scales the mesh data on the way in.

## Variations

| Collection | Footprint | Height | For |
|---|---|---|---|
| `Coll_Scute_Cracked_Hex`   | 0.30 x 0.34 | 0.055 | the main body field -- irregular, tessellating |
| `Coll_Scute_Pebble_Round`  | 0.15 x 0.15 | 0.050 | fine gravel between the big plates, cheek and jaw |
| `Coll_Scute_Keeled_Ridge`  | 0.22 x 0.46 | 0.130 | spine and tail crest -- a blade, not a bump |
| `Coll_Scute_Shard_Angular` | 0.30 x 0.36 | 0.100 | shoulder and hump clusters, lifted trailing edge |
| `Coll_Scute_Spike_Low`     | 0.24 x 0.24 | 0.160 | broad defensive boss on hips and elbows |
| `Coll_Scute_Spike_Tall`    | 0.19 x 0.19 | 0.550 | crest and tail spikes, curving back |

`Cracked_Hex` and `Shard_Angular` take a `seed`, so a field of them is not a
field of one shape repeated -- see `SEEDS`. Six shapes at four seeds each is
what stops a scattered body reading as wallpaper.

    blender --background --python scute_plate.py -- --out scute_plate.blend
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))

import _buildlib as B  # noqa: E402

# How far the footprint sinks below the skin plane. Tuned on a 30 cm plate over
# a body whose radius of curvature is about 1 m; deeper than this and the plate
# disappears into the flank on convex regions.
SINK = 0.022

# Seeds that produce distinct-looking irregular plates. Enumerated rather than
# left to the caller so a model and this file cannot disagree about how many
# distinct shapes exist.
SEEDS = (0, 1, 2, 3)

MATS = ["Mat_Hide_Plate_Tan", "Mat_Hide_Claw_Horn"]
PLATE, HORN = 0, 1


# --------------------------------------------------------------------------
# Profiles
# --------------------------------------------------------------------------

def sym_radii(n, jitter, seed):
    """Per-vertex radius multipliers for an n-gon, mirrored across y = 0.

    Mirroring rather than jittering all n independently is what keeps the
    component y-symmetric, which is the whole reason one mesh can serve both
    flanks of an animal.
    """
    rnd = random.Random(seed)
    r = [1.0] * n
    for i in range(n // 2 + 1):
        r[i] = 1.0 + rnd.uniform(-jitter, jitter)
    for i in range(n // 2 + 1, n):
        r[i] = r[(n - i) % n]
    return r


def ngon(n, rx, ry, radii=None, shrink=1.0, dx=0.0):
    """Closed profile in the XY plane, as `Part.loft(axis='Z')` wants it.

    `shrink` scales the ring toward its own centre -- successive shrinking rings
    stacked up +Z are what makes a dome. `dx` slides a ring bodily along X,
    which leans the dome off-centre and turns a symmetric plate into an
    overlapping scale.
    """
    radii = radii or [1.0] * n
    pts = []
    for i in range(n):
        a = 2.0 * math.pi * i / n
        pts.append((dx + rx * radii[i] * shrink * math.cos(a),
                    ry * radii[i] * shrink * math.sin(a)))
    return pts


# --------------------------------------------------------------------------
# Variations
# --------------------------------------------------------------------------

def cracked_hex(part, seed):
    """Irregular tessellating plate with a raised, slightly flattened crown."""
    n = 7
    r = sym_radii(n, 0.16, seed)
    h = 0.055
    part.loft([
        (-SINK,      ngon(n, 0.150, 0.170, r, 1.00)),
        (h * 0.30,   ngon(n, 0.150, 0.170, r, 0.95)),
        (h * 0.72,   ngon(n, 0.150, 0.170, r, 0.78)),
        (h,          ngon(n, 0.150, 0.170, r, 0.42)),
    ], axis='Z', mat=PLATE)


def pebble_round(part, seed=0):
    """Smooth low dome. The filler that keeps a scale field from looking sparse."""
    n = 10
    r = sym_radii(n, 0.07, seed)
    h = 0.050
    part.loft([
        (-SINK,      ngon(n, 0.075, 0.075, r, 1.00)),
        (h * 0.42,   ngon(n, 0.075, 0.075, r, 0.88)),
        (h * 0.78,   ngon(n, 0.075, 0.075, r, 0.62)),
        (h,          ngon(n, 0.075, 0.075, r, 0.24)),
    ], axis='Z', mat=PLATE)


def keeled_ridge(part, seed=0):
    """Long blade rising to a sharp fore-aft keel -- the spine and tail crest.

    Narrows in Y far faster than in X, which is what makes it a keel rather than
    a taller version of the round pebble.
    """
    n = 10
    r = sym_radii(n, 0.05, seed)
    part.loft([
        (-SINK, ngon(n, 0.230, 0.110, r, 1.00)),
        (0.048, ngon(n, 0.216, 0.075, r, 1.00)),
        (0.094, ngon(n, 0.175, 0.038, r, 1.00)),
        (0.130, ngon(n, 0.095, 0.013, r, 1.00)),
    ], axis='Z', mat=PLATE)


def shard_angular(part, seed):
    """Flat angular scale whose mass leans back, lifting the trailing edge.

    The lean comes from sliding the upper rings along -X rather than from
    rotating the piece: a rotated scale lifts its footprint off the skin at one
    corner, and the gap that opens is exactly what `SINK` exists to prevent.
    """
    n = 5
    r = sym_radii(n, 0.20, seed)
    h = 0.100
    part.loft([
        (-SINK,     ngon(n, 0.150, 0.180, r, 1.00, dx=0.000)),
        (h * 0.34,  ngon(n, 0.150, 0.180, r, 0.90, dx=-0.028)),
        (h * 0.72,  ngon(n, 0.150, 0.180, r, 0.68, dx=-0.058)),
        (h,         ngon(n, 0.150, 0.180, r, 0.34, dx=-0.086)),
    ], axis='Z', mat=PLATE)


def spike_low(part, seed=0):
    """Broad conical boss. Two lofts so the tip can carry the horn material."""
    n = 9
    r = sym_radii(n, 0.06, seed)
    part.loft([
        (-SINK, ngon(n, 0.120, 0.120, r, 1.00)),
        (0.055, ngon(n, 0.120, 0.120, r, 0.74)),
        (0.100, ngon(n, 0.120, 0.120, r, 0.48)),
    ], axis='Z', mat=PLATE)
    part.loft([
        (0.100, ngon(n, 0.120, 0.120, r, 0.48)),
        (0.140, ngon(n, 0.120, 0.120, r, 0.24)),
        (0.160, ngon(n, 0.120, 0.120, r, 0.06)),
    ], axis='Z', mat=HORN)


def spike_tall(part, seed=0):
    """Tall horn curving back over the body.

    The curve is quadratic in height, so it leaves the skin almost vertical and
    hooks hardest near the tip -- the same shape `_organic.shaped(droop=)` gives
    a claw, which is what makes a horn read as grown rather than extruded.
    """
    n = 9
    r = sym_radii(n, 0.05, seed)
    H = 0.55
    sweep = 0.30

    def at(t, shrink):
        return (H * t, ngon(n, 0.095, 0.095, r, shrink, dx=-sweep * t * t))

    part.loft([(-SINK, ngon(n, 0.095, 0.095, r, 1.00)),
               at(0.18, 0.82), at(0.40, 0.60)], axis='Z', mat=PLATE)
    part.loft([at(0.40, 0.60), at(0.66, 0.40), at(0.86, 0.21),
               at(1.00, 0.05)], axis='Z', mat=HORN)


# Which builders take a meaningful seed. The rest are smooth enough that a
# reseeded copy is indistinguishable, and four identical files in the index is
# exactly the near-duplicate noise the library is supposed to avoid.
VARIATIONS = [
    ("Cracked_Hex",   cracked_hex,   True),
    ("Pebble_Round",  pebble_round,  False),
    ("Keeled_Ridge",  keeled_ridge,  False),
    ("Shard_Angular", shard_angular, True),
    ("Spike_Low",     spike_low,     False),
    ("Spike_Tall",    spike_tall,    False),
]


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)

    made = 0
    for name, fn, seeded in VARIATIONS:
        coll = B.collection("Coll_Scute_%s" % name)
        seeds = SEEDS if seeded else (0,)
        for s in seeds:
            part = B.Part(mats)
            fn(part, s)
            part.bevel(width=0.006, segments=1, angle=42.0)
            obj_name = ("Mesh_Scute_%s_%d" % (name, s) if seeded
                        else "Mesh_Scute_%s" % name)
            part.finish(obj_name, coll)
            made += 1

    B.save(out)
    print("scute_plate: %d meshes across %d variations"
          % (made, len(VARIATIONS)))


main()
