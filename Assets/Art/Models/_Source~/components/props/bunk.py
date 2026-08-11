"""Crew berths.

Along with the galley, this is what makes the ship an RV rather than a shuttle:
somebody sleeps here. Three arrangements because the cabin has room for exactly
one berth, and which one it gets should be a placement decision rather than a
remodelling job.

Built against a wall at x=0, the sleeper's head toward -Y. Origin at deck level
on the wall line.

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

CANVAS, STEEL, DARK, PANEL, PLY, CREAM, WARM, RUST = range(8)
MATS = ["Mat_Fabric_Canvas_Faded", "Mat_Metal_Steel_Worn",
        "Mat_Metal_Steel_Dark", "Mat_Neutral_Panel_Grey",
        "Mat_Wood_Ply_Worn", "Mat_Plastic_Cream_Aged",
        "Mat_Emissive_Cabin_Warm", "Mat_Metal_Rust_Heavy"]

LEN = 1.95        # a berth long enough for a person
WID = 0.78


def mattress(p, z, rumple=True, seed=0):
    """Pad plus a slept-in duvet. The rumple is four overlapping boxes at
    slight angles — cheap, and it stops the berth reading as a shelf."""
    p.slab((0.04, -LEN / 2 + 0.03, z), (WID - 0.04, LEN / 2 - 0.03, z + 0.10),
           CANVAS)
    if not rumple:
        return
    import random
    rng = random.Random(seed)
    for i in range(4):
        y = -LEN / 2 + 0.30 + i * 0.42
        p.box((WID / 2 + rng.uniform(-0.04, 0.04), y, z + 0.15),
              (WID - 0.10, 0.44, 0.11), CANVAS,
              rot=Matrix.Rotation(rng.uniform(-0.10, 0.10), 4, 'Z'))
    # Pillow at the head end.
    p.box((WID / 2, -LEN / 2 + 0.22, z + 0.17), (WID - 0.20, 0.32, 0.10),
          CREAM, rot=Matrix.Rotation(math.radians(4), 4, 'X'))


def single(coll, mats):
    """One berth in an alcove with a reading light and a shelf."""
    p = Part(mats)
    Z = 0.42
    # Frame: two end panels, a back and a slatted base.
    for s in (-1, 1):
        p.slab((0.0, s * LEN / 2 - s * 0.04, 0.0), (WID, s * LEN / 2, Z + 0.62),
               PANEL)
    p.slab((0.0, -LEN / 2, 0.0), (0.05, LEN / 2, Z + 0.62), PANEL)
    p.slab((0.0, -LEN / 2, Z - 0.06), (WID, LEN / 2, Z), STEEL)
    for i in range(9):
        y = -LEN / 2 + 0.10 + i * (LEN - 0.20) / 8
        p.slab((0.05, y - 0.03, Z - 0.02), (WID, y + 0.03, Z + 0.01), PLY)
    # Legs and an under-berth storage void.
    for sy in (-1, 1):
        for sx in (0.10, WID - 0.10):
            p.box((sx, sy * (LEN / 2 - 0.10), Z / 2 - 0.03),
                  (0.06, 0.06, Z - 0.06), STEEL)
    p.slab((0.06, -LEN / 2 + 0.08, 0.02), (WID - 0.06, LEN / 2 - 0.08, 0.06),
           DARK)
    mattress(p, Z, seed=3)
    # Lee cloth: the strap that stops you falling out under thrust.
    p.box((WID - 0.01, 0.10, Z + 0.20), (0.02, LEN * 0.60, 0.26), CANVAS)
    for s in (-1, 1):
        p.cyl((WID - 0.02, 0.10 + s * LEN * 0.30, Z + 0.36), 0.012, 0.34, 'Z',
              6, STEEL)
    # Shelf, reading light and a personal photo taped to the back panel.
    p.slab((0.05, -LEN / 2 + 0.10, Z + 0.44), (WID * 0.55, LEN / 2 - 0.60,
                                               Z + 0.47), PLY)
    p.cyl((0.16, -LEN / 2 + 0.26, Z + 0.56), 0.05, 0.05, 'Z', 12, DARK)
    p.cyl((0.16, -LEN / 2 + 0.26, Z + 0.53), 0.035, 0.02, 'Z', 10, WARM)
    p.box((0.055, -LEN / 2 + 0.55, Z + 0.40), (0.012, 0.16, 0.12), CREAM)
    p.greeble((0.10, -LEN / 2 + 0.16, Z + 0.48), (WID * 0.45, LEN / 2 - 0.70,
                                                  Z + 0.52),
              4, seed=13, scale=(0.05, 0.12), mat=DARK)
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_Bunk_Single", coll)


def stacked(coll, mats):
    """Two berths one above the other with a ladder — the arrangement that uses
    the cabin's full 2.2 m of headroom."""
    p = Part(mats)
    Z0, Z1 = 0.38, 1.28
    for s in (-1, 1):
        p.slab((0.0, s * LEN / 2 - s * 0.05, 0.0), (WID, s * LEN / 2,
                                                    Z1 + 0.52), PANEL)
    p.slab((0.0, -LEN / 2, 0.0), (0.05, LEN / 2, Z1 + 0.52), PANEL)
    for z in (Z0, Z1):
        p.slab((0.0, -LEN / 2, z - 0.06), (WID, LEN / 2, z), STEEL)
        for i in range(7):
            y = -LEN / 2 + 0.12 + i * (LEN - 0.24) / 6
            p.slab((0.05, y - 0.03, z - 0.02), (WID, y + 0.03, z + 0.01), PLY)
    mattress(p, Z0, seed=5)
    # Upper berth made up flat — one made bed, one not.
    mattress(p, Z1, rumple=False)
    p.box((WID / 2, 0.10, Z1 + 0.13), (WID - 0.12, LEN * 0.68, 0.07), CANVAS)
    # Corner posts and the ladder up to the top berth.
    for sy in (-1, 1):
        p.box((WID - 0.06, sy * (LEN / 2 - 0.08), (Z1 + 0.52) / 2),
              (0.07, 0.07, Z1 + 0.52), STEEL)
    for i in range(4):
        p.cyl((WID - 0.06, LEN / 2 - 0.30, 0.30 + i * 0.30), 0.018, 0.42, 'Y',
              8, STEEL)
    # Lee cloths on both, curtain rail on the lower.
    for z in (Z0, Z1):
        p.box((WID - 0.01, 0.06, z + 0.22), (0.02, LEN * 0.58, 0.28), CANVAS)
    p.cyl((WID - 0.03, 0.0, Z1 - 0.10), 0.014, LEN - 0.14, 'Y', 8, STEEL)
    for i in range(5):
        y = -LEN / 2 + 0.24 + i * 0.36
        p.box((WID - 0.04, y, Z0 + 0.42), (0.025, 0.16, 0.70), CANVAS,
              rot=Matrix.Rotation(math.radians(3), 4, 'Y'))
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_Bunk_Stacked", coll)


def folded(coll, mats):
    """Wall berth folded up and strapped flat — the cabin gets its floor back.
    Its silhouette is a panel, not a bed, which is the point."""
    p = Part(mats)
    H = 0.80
    # Wall frame the berth stows into.
    p.slab((0.0, -LEN / 2, H), (0.08, LEN / 2, H + WID + 0.10), PANEL)
    for s in (-1, 1):
        p.slab((0.0, s * LEN / 2 - s * 0.05, H - 0.10),
               (0.30, s * LEN / 2, H + WID + 0.10), PANEL)
    # Berth base swung up against the wall.
    p.slab((0.08, -LEN / 2 + 0.04, H + 0.04), (0.15, LEN / 2 - 0.04,
                                               H + WID + 0.02), STEEL)
    for i in range(8):
        z = H + 0.10 + i * (WID - 0.14) / 7
        p.slab((0.15, -LEN / 2 + 0.06, z - 0.02), (0.18, LEN / 2 - 0.06,
                                                   z + 0.02), PLY)
    # Mattress folded and compressed behind the retaining straps.
    p.slab((0.18, -LEN / 2 + 0.08, H + 0.10), (0.30, LEN / 2 - 0.08,
                                               H + WID - 0.06), CANVAS)
    for y in (-LEN / 4, LEN / 4):
        p.box((0.24, y, H + WID / 2), (0.16, 0.05, WID), CANVAS)
        p.box((0.32, y, H + WID / 2), (0.05, 0.09, 0.09), STEEL)
    # Hinge line along the bottom and the two stowed support legs.
    p.cyl((0.10, 0.0, H), 0.028, LEN - 0.12, 'Y', 10, STEEL)
    for y in (-LEN / 2 + 0.28, LEN / 2 - 0.28):
        p.cyl((0.22, y, H + WID * 0.55), 0.018, WID * 0.80, 'Z', 8, RUST,
              rot=Matrix.Rotation(math.radians(6), 4, 'Y'))
    # Latch hooks at the top and a stencilled label.
    for y in (-LEN / 3, LEN / 3):
        p.box((0.14, y, H + WID + 0.06), (0.10, 0.07, 0.07), DARK)
        p.cyl((0.20, y, H + WID + 0.06), 0.014, 0.06, 'Z', 6, STEEL)
    p.box((0.085, LEN / 2 - 0.30, H + WID * 0.5), (0.012, 0.22, 0.10), CREAM)
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_Bunk_Folded", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Single", single), ("Stacked", stacked),
                     ("Folded", folded)):
        fn(collection("Coll_Bunk_" + name), mats)

    report()
    save(out)


main()
