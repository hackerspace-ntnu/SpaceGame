"""Galley fittings.

The other half of the RV read. A ship with a bunk and no way to make a hot drink
is a barracks; a hob, a sink and a scavenged plywood counter is a home someone
flies.

Built against a wall at x=0, facing +X, origin at deck level on the wall line —
the same convention as the lockers, so galley and storage can be laid along one
wall without per-item offsets.

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

PLY, STEEL, DARK, CREAM, CHROME, AMBER, RUST, CANVAS, COPPER = range(9)
MATS = ["Mat_Wood_Ply_Worn", "Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Cream_Aged", "Mat_Metal_Chrome_Scuffed",
        "Mat_Emissive_Amber", "Mat_Metal_Rust_Heavy",
        "Mat_Fabric_Canvas_Faded", "Mat_Metal_Copper_Oxide"]

CT = 0.92         # counter height
D = 0.56          # counter depth


def counter(p, w, h=CT, splash=0.24):
    """Ply worktop on a steel carcass, with a splashback."""
    p.slab((0.05, -w / 2 + 0.04, 0.0), (D - 0.06, w / 2 - 0.04, 0.10), DARK)
    p.slab((0.0, -w / 2, 0.10), (D, w / 2, h - 0.05), STEEL)
    p.slab((-0.02, -w / 2 - 0.02, h - 0.05), (D + 0.03, w / 2 + 0.02, h), PLY)
    p.slab((-0.02, -w / 2 - 0.02, h - 0.062), (D + 0.03, w / 2 + 0.02,
                                               h - 0.05), CHROME)
    p.slab((0.0, -w / 2, h), (0.03, w / 2, h + splash), CHROME)
    return h


def cabinet_doors(p, w, y0, count, h0=0.12, h1=CT - 0.08):
    """Run of cupboard doors under a counter."""
    for i in range(count):
        y = y0 + (i + 0.5) * w / count
        p.slab((D + 0.005, y - w / count / 2 + 0.015, h0),
               (D + 0.035, y + w / count / 2 - 0.015, h1), CREAM)
        p.box((D + 0.05, y, (h0 + h1) / 2 + 0.16), (0.035, 0.10, 0.03), CHROME)


def sink(coll, mats):
    """Deep sink with a folding tap and a drying rack over it."""
    p = Part(mats)
    W = 1.00
    counter(p, W)
    # Basin sunk into the worktop: rim, walls, floor, drain.
    p.slab((0.10, -0.22, CT - 0.02), (D - 0.08, 0.22, CT + 0.01), CHROME)
    p.slab((0.13, -0.19, CT - 0.26), (D - 0.11, 0.19, CT - 0.02), DARK)
    p.slab((0.14, -0.18, CT - 0.27), (D - 0.12, 0.18, CT - 0.24), CHROME)
    p.cyl((D / 2 - 0.02, 0.0, CT - 0.25), 0.045, 0.02, 'Z', 12, DARK)
    # Folding tap on a swan neck.
    p.cyl((0.09, -0.28, CT + 0.10), 0.028, 0.20, 'Z', 10, CHROME)
    for i in range(5):
        a = math.radians(18 * (i + 1))
        p.cyl((0.09 + math.sin(a) * 0.11, -0.28, CT + 0.20 + math.cos(a) * 0.05),
              0.020, 0.06, 'Z', 8, CHROME, rot=Matrix.Rotation(a, 4, 'Y'))
    p.cyl((0.29, -0.28, CT + 0.20), 0.018, 0.05, 'Z', 8, CHROME)
    for s in (-1, 1):
        p.cyl((0.06, -0.28 + s * 0.09, CT + 0.03), 0.022, 0.06, 'Z', 8, DARK)
        p.box((0.06, -0.28 + s * 0.09, CT + 0.07), (0.075, 0.02, 0.02), CHROME)
    # Drying rack on the splashback, with two mugs and a pan hooked on.
    p.cyl((0.05, 0.18, CT + 0.30), 0.012, 0.44, 'Y', 8, CHROME)
    for i in range(6):
        y = 0.02 + i * 0.07
        p.cyl((0.09, y, CT + 0.30), 0.008, 0.10, 'X', 6, CHROME)
    for i, y in enumerate((0.06, 0.20)):
        p.cyl((0.13, y, CT + 0.38), 0.045, 0.10, 'Z', 12, (CREAM, RUST)[i])
        p.torus((0.18, y, CT + 0.38), 0.030, 0.008, 'Y', 10, 6,
                (CREAM, RUST)[i])
    p.cyl((0.11, -0.36, CT + 0.36), 0.085, 0.05, 'X', 14, COPPER)
    p.cyl((0.11, -0.36, CT + 0.24), 0.012, 0.20, 'Z', 6, DARK)
    cabinet_doors(p, W, -W / 2, 2)
    # Waste pipe and a damp-stained patch under the basin.
    p.cyl((D / 2 - 0.02, 0.0, CT - 0.40), 0.035, 0.28, 'Z', 8, DARK)
    p.slab((0.02, -0.12, 0.10), (0.05, 0.12, 0.45), RUST)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_Galley_Sink", coll)


def hob(coll, mats):
    """Two-ring induction hob, an oven under it and a spice rail above."""
    p = Part(mats)
    W = 0.90
    counter(p, W)
    # Hob top and two rings.
    p.slab((0.08, -0.30, CT), (D - 0.06, 0.30, CT + 0.025), DARK)
    for y in (-0.15, 0.15):
        p.cyl((D / 2 - 0.02, y, CT + 0.03), 0.115, 0.02, 'Z', 16, DARK)
        p.torus((D / 2 - 0.02, y, CT + 0.035), 0.095, 0.008, 'Z', 16, 6, AMBER)
    # Control knobs on the counter edge.
    for y in (-0.15, 0.15):
        p.cyl((D - 0.02, y, CT + 0.05), 0.032, 0.045, 'X', 10, CREAM)
        p.box((D + 0.005, y, CT + 0.05), (0.014, 0.008, 0.045), DARK)
    # Oven door with a grimy window, below the counter.
    p.slab((D + 0.005, -W / 2 + 0.06, 0.14), (D + 0.045, W / 2 - 0.06,
                                              CT - 0.16), DARK)
    p.slab((D + 0.045, -W / 2 + 0.14, 0.30), (D + 0.055, W / 2 - 0.14,
                                              CT - 0.30), RUST)
    p.cyl((D + 0.09, 0.0, CT - 0.10), 0.020, W - 0.20, 'Y', 8, CHROME)
    for s in (-1, 1):
        p.box((D + 0.06, s * (W / 2 - 0.12), CT - 0.10), (0.06, 0.04, 0.04),
              CHROME)
    # Extractor hood over the hob.
    p.prism([(0.0, 0.0), (D - 0.02, 0.16), (D - 0.02, 0.26), (0.0, 0.26)],
            W - 0.06, 'Y', STEEL, offset=(0, 0, CT + 0.62))
    p.slab((0.02, -W / 2 + 0.05, CT + 0.60), (D - 0.06, W / 2 - 0.05,
                                              CT + 0.63), DARK)
    p.cyl((0.16, 0.0, CT + 0.62), 0.05, 0.16, 'Z', 12, AMBER)
    p.cyl((0.14, 0.0, CT + 0.96), 0.09, 0.20, 'Z', 12, STEEL)
    # Spice rail on the splashback.
    p.cyl((0.05, 0.0, CT + 0.34), 0.010, W - 0.14, 'Y', 6, CHROME)
    for i in range(5):
        y = -0.28 + i * 0.14
        p.cyl((0.10, y, CT + 0.30), 0.028, 0.11, 'Z', 8,
              (CREAM, RUST, CREAM, COPPER, CREAM)[i])
    # No cupboard doors: the oven door covers this unit's whole under-counter.
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_Galley_Hob", coll)


def compact(coll, mats):
    """A whole galley in 0.7 m — hob, basin and a cupboard in one salvaged
    unit, with a fold-down leaf. For when the cabin only has one wall left."""
    p = Part(mats)
    W = 0.70
    counter(p, W, splash=0.40)
    # Half the top is basin, half is a single ring.
    p.slab((0.10, -0.30, CT - 0.02), (D - 0.08, -0.04, CT + 0.01), CHROME)
    p.slab((0.13, -0.27, CT - 0.20), (D - 0.11, -0.07, CT - 0.02), DARK)
    p.cyl((D / 2, 0.18, CT + 0.02), 0.10, 0.02, 'Z', 14, DARK)
    p.torus((D / 2, 0.18, CT + 0.028), 0.082, 0.008, 'Z', 14, 6, AMBER)
    p.cyl((0.08, -0.16, CT + 0.10), 0.024, 0.20, 'Z', 10, CHROME)
    p.cyl((0.15, -0.16, CT + 0.19), 0.018, 0.15, 'X', 8, CHROME)
    # Fold-down leaf doubling the worktop, held by a folding stay.
    p.slab((D + 0.03, -W / 2, CT - 0.055), (D + 0.42, W / 2, CT - 0.02), PLY)
    p.cyl((D + 0.03, 0.0, CT - 0.04), 0.018, W - 0.06, 'Y', 8, CHROME)
    for s in (-1, 1):
        p.cyl((D + 0.22, s * (W / 2 - 0.08), CT - 0.24), 0.014, 0.40, 'Z', 6,
              STEEL, rot=Matrix.Rotation(math.radians(-28), 4, 'Y'))
    # Upper cupboard on the splashback, one door ajar.
    p.slab((0.03, -W / 2, CT + 0.42), (0.38, W / 2, CT + 0.86), CREAM)
    rot = Matrix.Rotation(math.radians(-24), 4, 'Z')
    p.box((0.40, -W / 4, CT + 0.64), (0.03, W / 2 - 0.02, 0.42), CREAM,
          rot=rot)
    p.box((0.42, W / 4, CT + 0.64), (0.03, W / 2 - 0.02, 0.42), CREAM)
    p.cyl((0.44, W / 4 + 0.10, CT + 0.64), 0.014, 0.16, 'Z', 6, CHROME)
    p.slab((0.06, -W / 2 + 0.03, CT + 0.44), (0.36, 0.0, CT + 0.84), DARK)
    p.greeble((0.10, -W / 2 + 0.08, CT + 0.48), (0.32, -0.04, CT + 0.62), 5,
              seed=71, scale=(0.05, 0.13), mat=RUST)
    # Kettle strapped down so it does not fly about under thrust.
    p.cyl((0.30, 0.18, CT + 0.11), 0.075, 0.18, 'Z', 12, COPPER)
    p.cyl((0.30, 0.18, CT + 0.21), 0.045, 0.03, 'Z', 10, DARK)
    p.cyl((0.38, 0.18, CT + 0.14), 0.016, 0.09, 'Z', 6, DARK,
          rot=Matrix.Rotation(math.radians(40), 4, 'Y'))
    p.box((0.30, 0.18, CT + 0.13), (0.18, 0.18, 0.03), CANVAS)
    cabinet_doors(p, W, -W / 2, 1)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_Galley_Compact", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Sink", sink), ("Hob", hob), ("Compact", compact)):
        fn(collection("Coll_Galley_" + name), mats)

    report()
    save(out)


main()
