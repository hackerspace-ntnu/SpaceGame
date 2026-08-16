"""components/props/hull_stencil — painted markings for any plated surface.

The cheapest way to make a large blank hull read as *equipment somebody
operated* rather than as a grey box. Every industrial model in this library
wants these, which is why they are one component rather than geometry buried in
whichever model needed them first.

Authored as thin proud plates (12-16 mm) on the plane y = 0 facing **-Y**,
origin at the centre of the marking. Proud geometry rather than a texture,
because nothing in this library is UV-unwrapped or textured — the whole set
gets its look from flat palette materials on real geometry, and a stencil that
needed a texture atlas would be the only asset here that could not just be
dropped into a scene.

Slanted stripes are built as **parallelogram prisms**, not rotated boxes: a
rotated box overshoots the band it is supposed to sit in, and there are no
booleans here to trim it. The parallelogram gets horizontal top and bottom
edges for free, which is exactly what a hazard stripe is.

    blender --background --python hull_stencil.py -- --out hull_stencil.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Paint_Hull_Bleached",     # 0 PALE  the light half of every marking
    "Mat_Paint_Warn_Red",          # 1 RED   hazard stripes, roundels
    "Mat_Neutral_Black_Matte",     # 2 BLACK backing, engraved lines
    "Mat_Metal_Steel_Dark",        # 3 DARK  placard bodies
    "Mat_Metal_Chrome_Scuffed",    # 4 CHROME fixing screws
    "Mat_Metal_Rust_Heavy",        # 5 RUST  corrosion eating into the paint
]
PALE, RED, BLACK, DARK, CHROME, RUST = range(6)

T = 0.014          # paint thickness — proud enough to catch a low sun


def stripe(p, x0, z0, w, h, slant, mat, thick=T):
    """One slanted stripe with horizontal ends, as a parallelogram prism.

    `slant` is the horizontal shift from the bottom edge to the top edge.
    """
    prof = [(x0, z0), (x0 + w, z0), (x0 + w + slant, z0 + h), (x0 + slant, z0 + h)]
    return p.prism(prof, thick, axis='Y', mat=mat, offset=(0, -thick / 2.0, 0))


def bar(p, center, size, mat, ang=0.0):
    return p.box((center[0], -T / 2.0, center[1]), (size[0], T, size[1]), mat,
                 rot=Matrix.Rotation(math.radians(ang), 4, 'Y'))


def erosion(p, lo, hi, count, seed, mat=RUST):
    """Paint failing from the edges inward.

    Anchoring matters more than quantity here. Patches scattered uniformly over
    a marking read as confetti — the eye sees noise, not damage. Paint actually
    lifts where it has an exposed edge to lift from, so every patch is seated on
    one of the four boundaries and eats *inward*, and each is a flake (wide and
    shallow, or tall and narrow) rather than a square blob.
    """
    import random
    rng = random.Random(seed)
    w, h = hi[0] - lo[0], hi[1] - lo[1]
    for i in range(count):
        edge = i % 4
        if edge < 2:                                   # top / bottom edge
            reach = rng.uniform(0.06, 0.20) * h
            x = rng.uniform(lo[0] + 0.05 * w, hi[0] - 0.05 * w)
            z = (hi[1] if edge == 0 else lo[1]) + (-1 if edge == 0 else 1) * reach / 2.0
            size = (rng.uniform(0.10, 0.34) * w, T * 0.7, reach)
        else:                                          # left / right edge
            reach = rng.uniform(0.05, 0.16) * w
            z = rng.uniform(lo[1] + 0.05 * h, hi[1] - 0.05 * h)
            x = (hi[0] if edge == 2 else lo[0]) + (-1 if edge == 2 else 1) * reach / 2.0
            size = (reach, T * 0.7, rng.uniform(0.12, 0.40) * h)
        p.box((x, -T * 0.85, z), size, mat)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def build_chevron(mats, parent):
    """Nested V chevrons — the classic "something moves here" marking."""
    coll = collection("Coll_HullStencil_Chevron", parent)
    p = Part(mats)

    w, h = 2.4, 1.5
    bar(p, (0, 0), (w + 0.12, h + 0.12), BLACK)
    arm, rise, n = w / 2.0, 0.34, 4
    for i in range(n):
        z = -h / 2.0 + 0.12 + i * (h - 0.34) / n
        m = RED if i % 2 == 0 else PALE
        for sx in (-1, 1):
            prof = [(0.0, z), (0.0, z + rise),
                    (sx * arm, z + rise + arm * 0.55),
                    (sx * arm, z + arm * 0.55)]
            p.prism(prof, T, axis='Y', mat=m, offset=(0, -T / 2.0, 0))
    erosion(p, (-w / 2.0, -h / 2.0), (w / 2.0, h / 2.0), 7, seed=3)

    p.finish("Mesh_HullStencil_Chevron", coll)
    return coll


def build_dangerband(mats, parent):
    """A long striped band — for wrapping a hazard edge or a moving part."""
    coll = collection("Coll_HullStencil_DangerBand", parent)
    p = Part(mats)

    w, h = 6.0, 0.52
    bar(p, (0, 0), (w, h), PALE)
    sw, slant = 0.30, h * 0.75
    n = int((w - slant) / (sw * 2))
    for i in range(n):
        x = -w / 2.0 + i * sw * 2
        stripe(p, x, -h / 2.0, sw, h, slant, RED, thick=T * 1.4)
    erosion(p, (-w / 2.0, -h / 2.0), (w / 2.0, h / 2.0), 9, seed=5)

    p.finish("Mesh_HullStencil_DangerBand", coll)
    return coll


def build_arrow(mats, parent):
    """A directional arrow with a stop bar — flow, or a tow point."""
    coll = collection("Coll_HullStencil_Arrow", parent)
    p = Part(mats)

    bar(p, (-0.35, 0), (1.7, 0.34), PALE)
    p.prism([(0.50, -0.62), (1.42, 0.0), (0.50, 0.62)], T, axis='Y', mat=PALE,
            offset=(0, -T / 2.0, 0))
    bar(p, (-1.34, 0), (0.20, 1.0), PALE)
    erosion(p, (-1.4, -0.6), (1.4, 0.6), 6, seed=7)

    p.finish("Mesh_HullStencil_Arrow", coll)
    return coll


def build_roundel(mats, parent):
    """A unit roundel with a slash — an identity mark, half painted out."""
    coll = collection("Coll_HullStencil_Roundel", parent)
    p = Part(mats)

    p.cyl((0, -T / 2.0, 0), 0.86, T, axis='Y', seg=28, mat=PALE)
    p.tube((0, -T * 1.2, 0), 0.80, 0.15, T, axis='Y', seg=28, mat=RED)
    p.cyl((0, -T * 1.2, 0), 0.40, T, axis='Y', seg=24, mat=RED)
    stripe(p, -0.95, -0.14, 1.9, 0.28, 0.34, BLACK, thick=T * 1.8)
    erosion(p, (-0.8, -0.8), (0.8, 0.8), 8, seed=11)

    p.finish("Mesh_HullStencil_Roundel", coll)
    return coll


def build_placard(mats, parent):
    """A bolted-on data plate — the small-scale marking the others lack.

    Built ahead of any request for it. The other four variations all read from
    fifty metres; this one only reads from three, and a hull with nothing on it
    at close range is as flat as a hull with nothing on it at distance.
    """
    coll = collection("Coll_HullStencil_Placard", parent)
    p = Part(mats)

    w, h = 0.62, 0.44
    p.box((0, -0.012, 0), (w, 0.024, h), DARK)
    p.box((0, -0.026, h / 2.0 - 0.09), (w - 0.10, 0.006, 0.09), PALE)
    for i in range(4):
        p.box((-0.04, -0.026, h / 2.0 - 0.20 - i * 0.065),
              (w - 0.20 - (i % 2) * 0.14, 0.006, 0.026), PALE)
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.cyl((sx * (w / 2.0 - 0.045), -0.030, sz * (h / 2.0 - 0.045)),
                  0.022, 0.014, axis='Y', seg=6, mat=CHROME)

    p.finish("Mesh_HullStencil_Placard", coll)
    return coll


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_chevron(mats, root)
    build_dangerband(mats, root)
    build_arrow(mats, root)
    build_roundel(mats, root)
    build_placard(mats, root)

    report()
    save(out)


if __name__ == "__main__":
    main()
