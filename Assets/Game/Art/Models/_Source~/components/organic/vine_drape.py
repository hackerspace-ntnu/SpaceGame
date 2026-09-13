"""components/organic/vine_drape — the growth that says nobody stopped it.

The rest of `components/organic/` is creature anatomy. This is the first plant
in the library, and it exists because an ancient-planet structure with clean
edges reads as recently built however rusty its panels are. Growth on the roof
and over the parapet is the cheapest possible statement that the building is
older than whoever is standing in front of it.

Foliage here is massed, not modelled. Each clump is a deterministic scatter of
small rotated slabs in two greens — a lit `Mat_Foliage_Leaf_Pale` over a
shadowed `Mat_Foliage_Moss_Deep` — which at any distance a building is actually
seen from reads as a canopy and costs a fraction of real leaves. Two tones are
the minimum: one flat green mass has no form at all.

**Origin conventions**, which differ by variation because these attach to
different things:

- `RoofMat`, `Tuft`, `Planter` — centre of the footprint, at their base (z = 0).
- `DrapeLong`, `DrapeShort` — **the anchor point at the top (z = 0), hanging
  into -Z**, so a drape is placed by putting its origin on the edge it falls
  over rather than by working out where its bottom ends up.

    blender --background --python vine_drape.py -- --out vine_drape.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

import bpy  # noqa: E402
from mathutils import Euler, Vector  # noqa: E402

MATS = [
    "Mat_Foliage_Moss_Deep",       # 0 DEEP    shadowed underside of the mass
    "Mat_Foliage_Leaf_Pale",       # 1 PALE    the lit tips
    "Mat_Wood_Ply_Worn",           # 2 STEM    woody runners and old growth
    "Mat_Wood_Timber_Silvered",    # 3 TIMBER  planter boards
    "Mat_Metal_Rust_Heavy",        # 4 RUST    planter bands, hanging brackets
    "Mat_Metal_Steel_Worn",        # 5 STEEL   wire and hooks
]
DEEP, PALE, STEM, TIMBER, RUST, STEEL = range(6)

LEAF = 0.155          # nominal leaf-slab size — the mass's grain


def emit(coll, name, mats, build, origin=(0, 0, 0), bevel=None):
    p = Part(mats)
    build(p)
    if bevel:
        p.bevel(width=bevel, segments=1)
    return p.finish(name, coll, origin)


def clump(p, centre, radii, count, seed, pale_bias=0.45, size=LEAF):
    """A deterministic ball of leaf slabs.

    `radii` is the (x, y, z) half-extent of the ellipsoid the slabs fill, so a
    clump can be squashed into a mat or stretched into a hanging strand without
    a second function. `pale_bias` is the fraction that get the lit green — put
    it high on top surfaces and low underneath and the mass gains a light
    direction for free.
    """
    rng = random.Random(seed)
    cx, cy, cz = centre
    rx, ry, rz = radii
    for _ in range(count):
        # Rejection-free ellipsoid sample: direction times a cube-rooted
        # radius, which keeps the surface denser than the middle where the
        # silhouette actually is.
        u = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1)))
        if u.length < 1e-6:
            continue
        u.normalize()
        t = rng.uniform(0.45, 1.0) ** 0.5
        pos = (cx + u.x * rx * t, cy + u.y * ry * t, cz + u.z * rz * t)
        s = size * rng.uniform(0.62, 1.35)
        rot = Euler((rng.uniform(0, math.pi), rng.uniform(0, math.pi),
                     rng.uniform(0, math.pi))).to_matrix().to_4x4()
        # Slabs, not cubes: a leaf mass is made of flat things.
        p.box(pos, (s, s * rng.uniform(0.7, 1.2), s * 0.34),
              PALE if rng.random() < pale_bias else DEEP, rot=rot)


def runner(p, a, b, sag, segments=6, radius=0.022, mat=STEM):
    """A woody stem arcing from `a` to `b`, drooping by `sag` in the middle."""
    a, b = Vector(a), Vector(b)
    prev = a
    for i in range(1, segments + 1):
        t = i / float(segments)
        pt = a.lerp(b, t) - Vector((0, 0, sag * math.sin(math.pi * t)))
        d = pt - prev
        if d.length > 1e-5:
            rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
            p.cyl((prev + pt) / 2.0, radius, d.length, 'Z', seg=4, mat=mat,
                  rot=rot.to_4x4())
        prev = pt


def strand(p, x, y, length, seed, taper=0.72, drift=(0.0, 0.0), z0=0.0):
    """One hanging strand: a stem down from the anchor with clumps threaded on
    it, thinning toward the tip so it does not read as a rope."""
    rng = random.Random(seed)
    steps = max(3, int(length / 0.42))
    pts = []
    for i in range(steps + 1):
        t = i / float(steps)
        pts.append((x + drift[0] * t ** 2 + rng.uniform(-0.05, 0.05),
                    y + drift[1] * t ** 2 + rng.uniform(-0.05, 0.05),
                    z0 - length * t))
    for a, b in zip(pts, pts[1:]):
        d = Vector(b) - Vector(a)
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
        p.cyl(((Vector(a) + Vector(b)) / 2.0), 0.020, d.length, 'Z', seg=4,
              mat=STEM, rot=rot.to_4x4())
    for i, pt in enumerate(pts):
        t = i / float(steps)
        r = 0.30 * (1.0 - taper * t) + 0.06
        clump(p, pt, (r, r, r * 1.25), int(9 * (1.0 - 0.55 * t)) + 3,
              seed * 31 + i, pale_bias=0.30 + 0.35 * t, size=LEAF * 0.85)


# ---------------------------------------------------------------------------
# V1 — Roof mat. Sized for the workshop tank's crown.
# ---------------------------------------------------------------------------

def build_roofmat(coll, mats):
    """A domed circular mat, 5.1 m across — the tank's roof, taken over."""
    tag = "VineRoofMat"
    radius, dome = 2.55, 0.62

    def base(p):
        """A solid dome under the scatter.

        Without it the gaps between clumps show the roof through, and the mat
        reads as sparse weeds rather than as a canopy. Filling those gaps with
        more leaves would cost ten times as many triangles for the same read.
        """
        rings = []
        steps = 7
        for i in range(steps):
            t = i / (steps - 1.0)
            r = radius * (1.0 - t ** 1.7) * 0.97
            rings.append((0.04 + dome * t * 0.92,
                          [(r * math.cos(2 * math.pi * k / 16),
                            r * math.sin(2 * math.pi * k / 16)) for k in range(16)]))
        p.loft(rings, axis='Z', mat=DEEP)
    emit(coll, "Mesh_%s_Base" % tag, mats, base)

    def mass(p):
        rng = random.Random(11)
        for k in range(46):
            a = 2 * math.pi * (k * 0.618034)
            rr = radius * math.sqrt(rng.uniform(0.0, 1.0))
            # Domed: thickest at the middle, thinning to the rim.
            h = dome * (1.0 - (rr / radius) ** 2) ** 0.6
            clump(p, (rr * math.cos(a), rr * math.sin(a), 0.10 + h * 0.5),
                  (0.42, 0.42, 0.10 + h * 0.55), 11, 300 + k,
                  pale_bias=0.30 + 0.40 * (1.0 - rr / radius))
    emit(coll, "Mesh_%s_Mass" % tag, mats, mass)

    def fringe(p):
        """Growth spilling over the rim. This is the part that is actually
        seen from the ground, so it gets more of the budget than the middle."""
        for k in range(14):
            a = 2 * math.pi * k / 14.0 + 0.11
            x, y = radius * math.cos(a), radius * math.sin(a)
            drop = 0.35 + 0.55 * ((k * 7) % 5) / 4.0
            clump(p, (x, y, 0.16), (0.34, 0.34, 0.30), 10, 700 + k, 0.42)
            clump(p, (x * 1.06, y * 1.06, 0.16 - drop),
                  (0.24, 0.24, drop * 0.55), 8, 760 + k, 0.24)
    emit(coll, "Mesh_%s_Fringe" % tag, mats, fringe)

    def runners(p):
        for k in range(7):
            a = 2 * math.pi * k / 7.0 + 0.4
            runner(p, (0.5 * math.cos(a), 0.5 * math.sin(a), 0.42),
                   (radius * 1.14 * math.cos(a), radius * 1.14 * math.sin(a),
                    -0.30), 0.22)
    emit(coll, "Mesh_%s_Runners" % tag, mats, runners)


# ---------------------------------------------------------------------------
# V2 / V3 — Drapes. Anchor at the top, hanging into -Z.
# ---------------------------------------------------------------------------

def build_drape_long(coll, mats):
    tag = "VineDrapeLong"
    lengths = (3.05, 2.35, 2.75)
    for i, length in enumerate(lengths):
        x = -0.42 + i * 0.42
        emit(coll, "Mesh_%s_Strand_%d" % (tag, i), mats,
             lambda p, x=x, length=length, i=i: strand(
                 p, x, 0.0, length, 90 + i * 17, drift=(0.10 * (i - 1), 0.16)),
             origin=(x, 0.0, 0.0))

    emit(coll, "Mesh_%s_Anchor" % tag, mats, lambda p: (
        clump(p, (0.0, 0.0, -0.06), (0.62, 0.26, 0.20), 22, 55, 0.5),
        runner(p, (-0.62, 0.02, 0.0), (0.62, -0.02, -0.04), 0.06, radius=0.026)))


def build_drape_short(coll, mats):
    tag = "VineDrapeShort"
    for i, length in enumerate((0.92, 0.64, 1.10, 0.78)):
        x = -0.54 + i * 0.36
        emit(coll, "Mesh_%s_Strand_%d" % (tag, i), mats,
             lambda p, x=x, length=length, i=i: strand(
                 p, x, 0.0, length, 210 + i * 13, taper=0.55,
                 drift=(0.05 * (i - 1.5), 0.09)),
             origin=(x, 0.0, 0.0))

    emit(coll, "Mesh_%s_Anchor" % tag, mats, lambda p: clump(
        p, (0.0, 0.0, -0.05), (0.68, 0.22, 0.16), 20, 61, 0.52))


# ---------------------------------------------------------------------------
# V4 — Tuft. One clump for a ledge or a gutter.
# ---------------------------------------------------------------------------

def build_tuft(coll, mats):
    tag = "VineTuft"

    emit(coll, "Mesh_%s_Body" % tag, mats, lambda p: (
        clump(p, (0.0, 0.0, 0.26), (0.40, 0.34, 0.26), 20, 401, 0.48),
        clump(p, (0.18, -0.12, 0.08), (0.26, 0.22, 0.12), 9, 402, 0.22)))

    emit(coll, "Mesh_%s_Shoots" % tag, mats, lambda p: (
        [runner(p, (0.0, 0.0, 0.20),
                (0.62 * math.cos(2 * math.pi * k / 5.0),
                 0.62 * math.sin(2 * math.pi * k / 5.0), 0.44), 0.10,
                radius=0.016) for k in range(5)],
        [clump(p, (0.66 * math.cos(2 * math.pi * k / 5.0),
                   0.66 * math.sin(2 * math.pi * k / 5.0), 0.40),
               (0.14, 0.14, 0.12), 6, 410 + k, 0.62) for k in range(5)]))


# ---------------------------------------------------------------------------
# V5 — Planter. Growth somebody put there on purpose.
# ---------------------------------------------------------------------------

def build_planter(coll, mats):
    """For balcony rails and scaffold decks. The one variation that is
    cultivated rather than invading, which is why it has a box around it."""
    tag = "VinePlanter"
    w, d, h = 0.92, 0.38, 0.34

    emit(coll, "Mesh_%s_Box" % tag, mats, lambda p: (
        [p.box((0, s * d / 2.0, h / 2.0), (w, 0.035, h), TIMBER) for s in (-1, 1)],
        [p.box((s * w / 2.0, 0, h / 2.0), (0.035, d, h), TIMBER) for s in (-1, 1)],
        p.box((0, 0, 0.022), (w, d, 0.035), TIMBER),
        [p.box((s * (w / 2.0 - 0.06), 0, h / 2.0), (0.05, d + 0.03, h + 0.02),
               RUST) for s in (-1, 1)]), bevel=0.006)

    emit(coll, "Mesh_%s_Soil" % tag, mats, lambda p: p.box(
        (0, 0, h - 0.05), (w - 0.09, d - 0.09, 0.07), DEEP))

    emit(coll, "Mesh_%s_Growth" % tag, mats, lambda p: (
        [clump(p, (-0.30 + k * 0.30, 0.0, h + 0.14), (0.20, 0.16, 0.15), 12,
               520 + k, 0.55) for k in range(3)],
        # Two trailers over the front edge — the reason to put one of these on
        # a rail rather than on the ground.
        strand(p, -0.22, -d / 2.0 - 0.02, 0.62, 531, taper=0.5, z0=h + 0.02),
        strand(p, 0.26, -d / 2.0 - 0.02, 0.44, 537, taper=0.5, z0=h + 0.02)))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_roofmat(collection("Coll_Vine_RoofMat", root), mats)
    build_drape_long(collection("Coll_Vine_DrapeLong", root), mats)
    build_drape_short(collection("Coll_Vine_DrapeShort", root), mats)
    build_tuft(collection("Coll_Vine_Tuft", root), mats)
    build_planter(collection("Coll_Vine_Planter", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
