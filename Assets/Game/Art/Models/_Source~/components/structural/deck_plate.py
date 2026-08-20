"""Interior floor plating for the RV ship's cargo bay and cockpit.

Separate from the hull plating because the player walks on these: they are seen
from 1.7 m away looking down, which is a completely different detail budget from
a hull panel seen at 20 m. Tread pattern and slot depth matter here; rivet rows
do not.

1.0 m grid, thickness along +Z, origin at the bottom-left corner so laying a
deck is integer translation.

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

SIZE = 1.0
T = 0.06

STEEL, DARK, RUST, BLACK, AMBER = 0, 1, 2, 3, 4
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Metal_Rust_Heavy", "Mat_Neutral_Black_Matte",
        "Mat_Emissive_Amber"]


def slab(p, mat=STEEL, inset=0.015):
    return p.slab((inset, inset, 0.0), (SIZE - inset, SIZE - inset, T), mat)


def solid(coll, mats):
    """Diamond-tread plate — the standard walking surface."""
    p = Part(mats)
    slab(p)
    # Raised diamonds in a staggered lattice. Coarse on purpose: fine tread
    # disappears at play distance and costs thousands of triangles.
    for row in range(6):
        y = 0.11 + row * 0.155
        offset = 0.077 if row % 2 else 0.0
        for col in range(6):
            x = 0.11 + col * 0.155 + offset
            if x > SIZE - 0.08:
                continue
            p.box((x, y, T + 0.008), (0.075, 0.075, 0.016), STEEL,
                  rot=Matrix.Rotation(math.radians(45), 4, 'Z'))
    p.bevel(width=0.005, segments=1)
    return p.finish("Mesh_DeckPlate_Solid", coll)


def grate(coll, mats):
    """Slotted grating over the under-deck run. Sees the bilge machinery
    through it, which is most of why the cargo bay reads as a ship."""
    p = Part(mats)
    # Perimeter frame plus longitudinal bearer bars.
    p.slab((0.015, 0.015, 0.0), (SIZE - 0.015, 0.08, T), STEEL)
    p.slab((0.015, SIZE - 0.08, 0.0), (SIZE - 0.015, SIZE - 0.015, T), STEEL)
    p.slab((0.015, 0.08, 0.0), (0.08, SIZE - 0.08, T), STEEL)
    p.slab((SIZE - 0.08, 0.08, 0.0), (SIZE - 0.015, SIZE - 0.08, T), STEEL)
    for i in range(9):
        x = 0.115 + i * 0.096
        p.slab((x, 0.08, T - 0.045), (x + 0.048, SIZE - 0.08, T), STEEL)
    # Two cross-ties, so the bars do not read as loose slats.
    for y in (0.34, 0.66):
        p.slab((0.08, y, T - 0.045), (SIZE - 0.08, y + 0.028, T - 0.022), DARK)
    p.bevel(width=0.004, segments=1)
    return p.finish("Mesh_DeckPlate_Grate", coll)


def hatch(coll, mats):
    """Recessed access hatch with a flush ring pull — under-deck storage and
    the engineering crawl."""
    p = Part(mats)
    slab(p)
    # Recessed lid sunk into the plate, leaving a visible shadow line.
    p.slab((0.13, 0.13, T - 0.012), (SIZE - 0.13, SIZE - 0.13, T + 0.014),
           DARK)
    p.rivets((0.20, 0.20, T + 0.014), (SIZE - 0.20, 0.20, T + 0.014), 4,
             radius=0.02, height=0.014, mat=STEEL)
    p.rivets((0.20, SIZE - 0.20, T + 0.014), (SIZE - 0.20, SIZE - 0.20,
                                              T + 0.014), 4,
             radius=0.02, height=0.014, mat=STEEL)
    # Flush ring pull in a countersunk well.
    p.cyl((0.5, 0.5, T + 0.004), 0.115, 0.022, 'Z', 20, BLACK)
    p.torus((0.5, 0.5, T + 0.016), 0.082, 0.016, 'Z', 20, 8, STEEL)
    # Hinge knuckles along one edge, so it is obvious which way it lifts.
    for y in (0.30, 0.70):
        p.cyl((0.145, y, T + 0.016), 0.028, 0.12, 'Y', 8, STEEL)
    p.bevel(width=0.005, segments=1)
    return p.finish("Mesh_DeckPlate_Hatch", coll)


def worn(coll, mats):
    """Buckled and patched — laid where the cargo ramp lands and boots land
    hardest. Its silhouette is genuinely different: it is not flat."""
    p = Part(mats)
    # Base plate lifted at one corner, so the floor visibly does not sit true.
    p.box((0.5, 0.5, T / 2), (SIZE - 0.03, SIZE - 0.03, T), STEEL,
          rot=Matrix.Rotation(math.radians(1.6), 4, 'Y'))
    # Bare patch riveted over a worn-through section.
    p.box((0.36, 0.60, T + 0.016), (0.46, 0.42, 0.03), RUST,
          rot=Matrix.Rotation(math.radians(-5), 4, 'Z'))
    p.rivets((0.17, 0.44, T + 0.03), (0.55, 0.42, T + 0.03), 4,
             radius=0.021, height=0.018, mat=STEEL)
    p.rivets((0.19, 0.78, T + 0.03), (0.57, 0.76, T + 0.03), 4,
             radius=0.021, height=0.018, mat=STEEL)
    # Remaining tread survives only where feet do not fall.
    for row in range(3):
        y = 0.13 + row * 0.155
        for col in range(4):
            x = 0.60 + col * 0.10
            p.box((x, y, T + 0.008), (0.06, 0.06, 0.014), STEEL,
                  rot=Matrix.Rotation(math.radians(45), 4, 'Z'))
    # Scuffed-through hole showing the dark under-deck.
    p.cyl((0.80, 0.72, T - 0.01), 0.075, 0.03, 'Z', 10, BLACK)
    p.bevel(width=0.005, segments=1)
    return p.finish("Mesh_DeckPlate_Worn", coll)


def edge_strip(coll, mats):
    """Threshold strip with an inset marker lamp — runs along the cargo
    doorway and the cockpit step, where the deck ends."""
    p = Part(mats)
    p.slab((0.0, 0.0, 0.0), (SIZE, 0.22, T), DARK)
    # Hazard chevrons, cut as separate raised wedges rather than painted.
    for i in range(7):
        x = 0.05 + i * 0.135
        p.box((x, 0.11, T + 0.008), (0.075, 0.17, 0.016), RUST,
              rot=Matrix.Rotation(math.radians(35), 4, 'Z'))
    p.box((0.5, 0.035, T - 0.006), (0.34, 0.03, 0.02), AMBER)
    p.bevel(width=0.004, segments=1)
    return p.finish("Mesh_DeckPlate_EdgeStrip", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Solid", solid), ("Grate", grate), ("Hatch", hatch),
                     ("Worn", worn), ("EdgeStrip", edge_strip)):
        fn(collection("Coll_DeckPlate_" + name), mats)

    report()
    save(out)


main()
