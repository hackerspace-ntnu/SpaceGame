"""Cabin storage.

The cargo bay's walls are the largest empty surfaces in the ship, and lockers
are what stop them reading as a corridor. Four fittings that differ in
silhouette — a full-height cabinet, a low bank, open shelving with restraint
bars, and a buckled one — so a wall carrying all four does not repeat.

Built standing against a wall at x=0, opening toward +X, origin at deck level on
the wall line.

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

PANEL, STEEL, DARK, CREAM, CANVAS, RUST, AMBER, PLY = range(8)
MATS = ["Mat_Neutral_Panel_Grey", "Mat_Metal_Steel_Worn",
        "Mat_Metal_Steel_Dark", "Mat_Plastic_Cream_Aged",
        "Mat_Fabric_Canvas_Faded", "Mat_Metal_Rust_Heavy",
        "Mat_Emissive_Amber", "Mat_Wood_Ply_Worn"]


def carcass(p, w, d, h, mat=PANEL, base=0.09):
    """Box carcass on a recessed plinth, so it does not look glued to the deck."""
    p.slab((0.04, -w / 2 + 0.05, 0.0), (d - 0.04, w / 2 - 0.05, base), DARK)
    p.slab((0.0, -w / 2, base), (d, w / 2, h), mat)
    return base


def latch(p, x, y, z, mat=STEEL):
    """Over-centre catch — the fitting that says the contents move in flight."""
    p.box((x, y, z), (0.03, 0.05, 0.09), mat)
    p.cyl((x + 0.018, y, z), 0.013, 0.05, 'Y', 8, DARK)
    p.box((x + 0.03, y, z - 0.03), (0.045, 0.035, 0.035), DARK)


def tall(coll, mats):
    """Full-height double-door cabinet, 1.90 m — the wall's vertical accent."""
    p = Part(mats)
    W, D, H = 0.90, 0.44, 1.90
    b = carcass(p, W, D, H)
    # Two doors with a centre stile, one hanging slightly open.
    for s, swing in ((-1, 0.0), (1, math.radians(-11))):
        rot = Matrix.Rotation(swing, 4, 'Z')
        cy = s * W / 4
        p.box((D + 0.015, cy, (H + b) / 2), (0.04, W / 2 - 0.02, H - b - 0.03),
              PANEL, rot=rot)
        # Recessed door panel, to break up a metre of flat steel.
        p.box((D + 0.036, cy, (H + b) / 2), (0.012, W / 2 - 0.14, H - b - 0.22),
              DARK, rot=rot)
        latch(p, D + 0.04, cy - s * (W / 4 - 0.06), 1.05)
        p.cyl((D + 0.05, cy - s * (W / 4 - 0.10), 1.20), 0.016, 0.26, 'Z', 8,
              STEEL)
    p.box((D + 0.01, 0.0, (H + b) / 2), (0.03, 0.035, H - b - 0.03), STEEL)
    # Ventilation slots high up, and a label plate.
    for i in range(4):
        p.box((D + 0.03, 0.0, H - 0.14 - i * 0.05), (0.02, 0.34, 0.018), DARK)
    p.box((D + 0.035, -W / 4, 1.52), (0.012, 0.20, 0.09), CREAM)
    # Restraint strap over the top, plus rust creeping up the plinth.
    p.box((D / 2, 0.0, H + 0.02), (D - 0.06, 0.06, 0.02), CANVAS)
    p.slab((0.0, -W / 2, 0.0), (D * 0.5, -W / 2 + 0.02, 0.30), RUST)
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_WallLocker_Tall", coll)


def bank(coll, mats):
    """Waist-height run of drawers with a worktop — the surface things get put
    down on, which every lived-in interior needs."""
    p = Part(mats)
    W, D, H = 1.40, 0.52, 0.92
    b = carcass(p, W, D, H - 0.06)
    # Ply worktop, overhanging at the front.
    p.slab((-0.03, -W / 2 - 0.02, H - 0.06), (D + 0.04, W / 2 + 0.02, H), PLY)
    p.slab((-0.03, -W / 2 - 0.02, H - 0.075), (D + 0.04, W / 2 + 0.02,
                                               H - 0.06), STEEL)
    # Three drawers side by side, the middle one pulled out.
    z0 = b + 0.03
    for y, out in ((-W / 3, 0.0), (0.0, 0.13), (W / 3, 0.0)):
        p.slab((D + out, y - W / 6 + 0.03, z0),
               (D + out + 0.035, y + W / 6 - 0.03, H - 0.12), PANEL)
        p.box((D + out + 0.055, y, (z0 + H - 0.12) / 2), (0.04, 0.30, 0.05),
              STEEL)
        p.box((D + out + 0.045, y - W / 6 + 0.09, H - 0.20), (0.014, 0.13,
                                                              0.055), CREAM)
        if out > 0:
            # Contents visible in the open drawer.
            p.slab((D - 0.02, y - W / 6 + 0.05, H - 0.30),
                   (D + out, y + W / 6 - 0.05, H - 0.26), DARK)
            p.greeble((D + 0.02, y - 0.14, H - 0.25), (D + out - 0.03,
                                                       y + 0.14, H - 0.22),
                      6, seed=41, scale=(0.03, 0.09), mat=STEEL)
    # Splashback and a cargo rail along the worktop edge.
    p.slab((-0.03, -W / 2, H), (0.03, W / 2, H + 0.22), STEEL)
    for s in (-1, 1):
        p.cyl((D + 0.02, s * (W / 2 - 0.02), H + 0.09), 0.016, 0.18, 'Z', 8,
              STEEL)
    p.cyl((D + 0.02, 0.0, H + 0.17), 0.016, W - 0.04, 'Y', 8, STEEL)
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_WallLocker_Bank", coll)


def open_shelf(coll, mats):
    """Open shelving with restraint bars and netting — the variation that shows
    its contents, so the bay has something to look into."""
    p = Part(mats)
    W, D, H = 1.10, 0.36, 1.55
    # Uprights only; no carcass, which is the whole point.
    for s in (-1, 1):
        p.slab((0.0, s * W / 2 - s * 0.04, 0.0), (D, s * W / 2, H), STEEL)
    p.slab((0.0, -W / 2, H - 0.05), (D, W / 2, H), STEEL)
    for i, z in enumerate((0.34, 0.72, 1.10)):
        p.slab((0.02, -W / 2 + 0.04, z), (D, W / 2 - 0.04, z + 0.035), PLY)
        p.slab((0.02, -W / 2 + 0.04, z - 0.02), (D, W / 2 - 0.04, z), STEEL)
        # Restraint bar across the front of each shelf.
        p.cyl((D - 0.015, 0.0, z + 0.16), 0.014, W - 0.10, 'Y', 8, STEEL)
        for s in (-1, 1):
            p.box((D - 0.015, s * (W / 2 - 0.05), z + 0.10), (0.03, 0.03,
                                                              0.14), DARK)
        # Something stowed on each shelf, different every time.
        p.greeble((0.06, -W / 2 + 0.12, z + 0.06), (D - 0.08, W / 2 - 0.12,
                                                    z + 0.14),
                  5 + i, seed=53 + i * 7, scale=(0.08, 0.20),
                  mat=(DARK, RUST, CREAM)[i])
    # Netting slung over the lowest bay.
    for i in range(7):
        y = -W / 2 + 0.08 + i * (W - 0.16) / 6
        p.box((D - 0.02, y, 0.17), (0.012, 0.012, 0.30), CANVAS)
    for i in range(4):
        z = 0.05 + i * 0.09
        p.box((D - 0.02, 0.0, z), (0.012, W - 0.14, 0.012), CANVAS)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_WallLocker_OpenShelf", coll)


def dented(coll, mats):
    """A cabinet that has been hit. Door sprung, one corner stove in, held shut
    with a strap — silhouette-level damage rather than a rust texture."""
    p = Part(mats)
    W, D, H = 0.80, 0.42, 1.25
    b = carcass(p, W, D, H, mat=RUST)
    # Buckled top: two wedges instead of a flat lid.
    p.prism([(0.0, 0.0), (D, 0.0), (D, -0.10), (0.0, -0.04)], W * 0.5, 'Y',
            RUST, offset=(0, -W * 0.25, H))
    p.prism([(0.0, 0.0), (D, 0.0), (D, -0.03), (0.0, -0.02)], W * 0.5, 'Y',
            RUST, offset=(0, W * 0.25, H))
    # Door hanging open on one hinge, twisted.
    rot = (Matrix.Rotation(math.radians(-34), 4, 'Z')
           @ Matrix.Rotation(math.radians(6), 4, 'X'))
    p.box((D + 0.16, -W / 3, (H + b) / 2), (0.035, W - 0.06, H - b - 0.10),
          PANEL, rot=rot)
    p.box((D + 0.02, W / 2 - 0.04, (H + b) / 2), (0.06, 0.07, 0.16), STEEL)
    p.box((D + 0.02, W / 2 - 0.04, H - 0.30), (0.06, 0.07, 0.16), DARK)
    # Ratchet strap holding the whole thing shut.
    p.box((D / 2, 0.0, 0.80), (D + 0.10, 0.05, 0.018), CANVAS)
    p.box((D + 0.06, 0.0, 0.80), (0.05, 0.09, 0.05), STEEL)
    # Dark interior visible through the gap, with contents spilled forward.
    p.slab((0.03, -W / 2 + 0.03, b), (D - 0.03, W / 2 - 0.03, H - 0.08), DARK)
    p.greeble((0.08, -W / 2 + 0.10, b + 0.05), (D - 0.10, W / 2 - 0.10, 0.55),
              7, seed=67, scale=(0.06, 0.16), mat=STEEL)
    p.box((D + 0.03, -W / 4, H - 0.16), (0.014, 0.16, 0.07), AMBER)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_WallLocker_Dented", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Tall", tall), ("Bank", bank),
                     ("OpenShelf", open_shelf), ("Dented", dented)):
        fn(collection("Coll_WallLocker_" + name), mats)

    report()
    save(out)


main()
