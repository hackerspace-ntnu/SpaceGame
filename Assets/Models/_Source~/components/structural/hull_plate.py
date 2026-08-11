"""Hull plating overlays for the RV ship's exterior skin.

The ship's shell is one continuous lofted surface; these are the panels laid
*over* it that give it a plated read. Keeping them separate from the shell is
what lets the same plating language appear on the nacelles, the tail and any
future hull without re-cutting the shell geometry.

Built in the XY plane on a 1.0 m structural grid, thickness along +Z, origin at
the bottom-left corner so tiling is integer translation. Place by rotating the
plate onto the surface normal.

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

SIZE = 1.0      # footprint, on the 1.0 m structural grid
T = 0.045       # plate thickness

HULL, RUST, STEEL, DARK, BLACK = 0, 1, 2, 3, 4
MATS = ["Mat_Metal_HullRust_Orange", "Mat_Metal_Rust_Heavy",
        "Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Neutral_Black_Matte"]


def base(p, inset=0.02, mat=HULL):
    """The plate itself, inset from its cell so neighbours leave a shadow gap."""
    return p.slab((inset, inset, 0.0), (SIZE - inset, SIZE - inset, T), mat)


def flat(coll, mats):
    """Plain plate with a chamfered edge — the default skin, used everywhere."""
    p = Part(mats)
    p.base_faces = base(p)
    # A single shallow score across the face keeps a large run of plain plates
    # from reading as one flat sheet.
    p.seam((0.06, 0.5, T), (SIZE - 0.06, 0.5, T), width=0.02, depth=0.012,
           mat=RUST)
    p.bevel(width=0.01, segments=2)
    return p.finish("Mesh_HullPlate_Flat", coll)


def ribbed(coll, mats):
    """Stiffening ribs — used where the hull spans unsupported, and on the
    nacelle shrouds."""
    p = Part(mats)
    base(p)
    for i in range(4):
        x = 0.14 + i * 0.24
        p.box((x, SIZE / 2, T + 0.025), (0.07, SIZE - 0.12, 0.05), STEEL)
    p.box((SIZE / 2, 0.08, T + 0.02), (SIZE - 0.1, 0.06, 0.04), STEEL)
    p.box((SIZE / 2, SIZE - 0.08, T + 0.02), (SIZE - 0.1, 0.06, 0.04), STEEL)
    p.bevel(width=0.008, segments=2)
    return p.finish("Mesh_HullPlate_Ribbed", coll)


def riveted(coll, mats):
    """Fastener rows on all four edges — the bolted-on-later look, and the
    plate that carries most of the ship's close-up detail."""
    p = Part(mats)
    base(p)
    m = 0.075
    for a, b in (((m, m, T), (SIZE - m, m, T)),
                 ((m, SIZE - m, T), (SIZE - m, SIZE - m, T)),
                 ((m, m, T), (m, SIZE - m, T)),
                 ((SIZE - m, m, T), (SIZE - m, SIZE - m, T))):
        p.rivets(a, b, 7, radius=0.022, height=0.02, mat=STEEL)
    # Recessed access hatch, off-centre so a run of these does not pulse.
    p.box((0.62, 0.42, T + 0.012), (0.30, 0.26, 0.024), DARK)
    p.rivets((0.50, 0.31, T + 0.024), (0.74, 0.31, T + 0.024), 3,
             radius=0.015, height=0.014, mat=STEEL)
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_HullPlate_Riveted", coll)


def patched(coll, mats):
    """A scrap plate welded over a hole. This is the variation that sells
    'rundown' — the hull is not damaged, it has been *repaired badly*."""
    p = Part(mats)
    base(p)
    # Off-square patch, tilted, standing proud of the skin.
    tilt = Matrix.Rotation(math.radians(7), 4, 'Z')
    p.box((0.46, 0.54, T + 0.03), (0.62, 0.50, 0.05), RUST, rot=tilt)
    # Weld bead: overlapping blobs tracing the patch border.
    for i in range(11):
        t = i / 10.0
        p.cyl((0.15 + t * 0.62, 0.29, T + 0.045), 0.028, 0.03, 'Z', 6, STEEL)
        p.cyl((0.15 + t * 0.62, 0.79, T + 0.045), 0.028, 0.03, 'Z', 6, STEEL)
    for i in range(9):
        t = i / 8.0
        p.cyl((0.16, 0.29 + t * 0.50, T + 0.045), 0.028, 0.03, 'Z', 6, STEEL)
        p.cyl((0.77, 0.29 + t * 0.50, T + 0.045), 0.028, 0.03, 'Z', 6, STEEL)
    # Two through-bolts holding the patch on.
    for x, y in ((0.24, 0.38), (0.70, 0.70)):
        p.cyl((x, y, T + 0.062), 0.032, 0.026, 'Z', 6, DARK)
    p.bevel(width=0.006, segments=1)
    return p.finish("Mesh_HullPlate_Patched", coll)


def vented(coll, mats):
    """Louvred plate for engine bays and heat-exchanger faces."""
    p = Part(mats)
    # Frame around a cut-out rather than a solid plate.
    p.slab((0.02, 0.02, 0.0), (SIZE - 0.02, 0.16, T), HULL)
    p.slab((0.02, SIZE - 0.16, 0.0), (SIZE - 0.02, SIZE - 0.02, T), HULL)
    p.slab((0.02, 0.16, 0.0), (0.16, SIZE - 0.16, T), HULL)
    p.slab((SIZE - 0.16, 0.16, 0.0), (SIZE - 0.02, SIZE - 0.16, T), HULL)
    # Dark recess behind the slats, so the opening reads as depth not paint.
    p.slab((0.16, 0.16, -0.06), (SIZE - 0.16, SIZE - 0.16, -0.02), BLACK)
    for i in range(6):
        y = 0.20 + i * 0.115
        p.box((SIZE / 2, y, T * 0.5), (SIZE - 0.34, 0.075, 0.05), STEEL,
              rot=Matrix.Rotation(math.radians(38), 4, 'X'))
    p.rivets((0.09, 0.09, T), (0.91, 0.09, T), 5, radius=0.02, height=0.018,
             mat=STEEL)
    p.rivets((0.09, 0.91, T), (0.91, 0.91, T), 5, radius=0.02, height=0.018,
             mat=STEEL)
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_HullPlate_Vented", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Flat", flat), ("Ribbed", ribbed), ("Riveted", riveted),
                     ("Patched", patched), ("Vented", vented)):
        fn(collection("Coll_HullPlate_" + name), mats)

    report()
    save(out)


main()
