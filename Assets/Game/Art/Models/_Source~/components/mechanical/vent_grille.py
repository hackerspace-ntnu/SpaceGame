"""Grilles, intakes and extractor fans.

Separated from `hull_plate.blend`'s vented variation because these are the
interior-facing ones: they sit in cabin walls and ceiling ducts where the player
is a metre away, and they need a real recess behind the slats rather than a
suggestion of one.

Built facing +Y in a 0.60 x 0.60 m opening, origin at the centre of the mounting
face, so a grille drops onto a wall with one translation and a yaw.

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

STEEL, DARK, BLACK, RUST, PANEL, AMBER = 0, 1, 2, 3, 4, 5
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Neutral_Black_Matte", "Mat_Metal_Rust_Heavy",
        "Mat_Neutral_Panel_Grey", "Mat_Emissive_Amber"]

W = 0.60          # opening width and height
FRAME = 0.055     # frame border
DEPTH = 0.045     # how far the frame stands proud of the wall


def frame(p, mat=STEEL, half=W / 2):
    """Bezel around the opening, plus the dark void behind it."""
    o = half
    i = half - FRAME
    p.slab((-o, -DEPTH, -o), (o, 0.0, -i), mat)
    p.slab((-o, -DEPTH, i), (o, 0.0, o), mat)
    p.slab((-o, -DEPTH, -i), (-i, 0.0, i), mat)
    p.slab((i, -DEPTH, -i), (o, 0.0, i), mat)
    # Recess: without this the slats read as stripes painted on a wall.
    p.slab((-i, 0.16, -i), (i, 0.20, i), BLACK)
    for s in (-1, 1):
        p.slab((s * i, 0.0, -i), (s * (i + 0.012), 0.16, i), BLACK)
        p.slab((-i, 0.0, s * i), (i, 0.16, s * (i + 0.012)), BLACK)
    return i


def louvre(coll, mats):
    """Fixed angled slats — cabin extract, and the one that reads best from
    below because the angle catches the light."""
    p = Part(mats)
    i = frame(p)
    for n in range(7):
        z = -i + (n + 0.5) * (2 * i / 7)
        p.box((0, -0.012, z), (2 * i, 0.075, (2 * i / 7) * 0.92), STEEL,
              rot=Matrix.Rotation(math.radians(-34), 4, 'X'))
    p.rivets((-i - FRAME / 2, -DEPTH, -i - FRAME / 2),
             (i + FRAME / 2, -DEPTH, -i - FRAME / 2), 4, radius=0.016,
             height=0.014, axis='Y', mat=DARK)
    p.rivets((-i - FRAME / 2, -DEPTH, i + FRAME / 2),
             (i + FRAME / 2, -DEPTH, i + FRAME / 2), 4, radius=0.016,
             height=0.014, axis='Y', mat=DARK)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_Vent_Louvre", coll)


def mesh_screen(coll, mats):
    """Woven guard over an intake — a crosshatch rather than slats, so a wall
    carrying both does not look like it was stamped from one die."""
    p = Part(mats)
    i = frame(p)
    bars = 9
    for n in range(bars):
        t = -i + (n + 0.5) * (2 * i / bars)
        p.box((t, 0.005, 0), (0.016, 0.028, 2 * i), DARK)
        p.box((0, 0.033, t), (2 * i, 0.028, 0.016), DARK)
    # Diagonal stiffener across the screen, and a torn corner.
    p.box((0, 0.055, 0), (2 * i * 1.30, 0.02, 0.022), RUST,
          rot=Matrix.Rotation(math.radians(45), 4, 'Y'))
    p.box((i * 0.62, 0.02, -i * 0.66), (0.16, 0.05, 0.12), RUST,
          rot=Matrix.Rotation(math.radians(18), 4, 'Y'))
    p.bevel(width=0.004, segments=1)
    return p.finish("Mesh_Vent_MeshScreen", coll)


def fan(coll, mats):
    """Powered extractor: a shrouded impeller in a deep housing. The one
    variation with a moving-looking part, for the workstation wall."""
    p = Part(mats)
    i = frame(p)
    r = i * 0.92
    p.tube((0, 0.10, 0), r, 0.05, 0.22, 'Y', 24, DARK)
    # Motor pod on a three-legged spider.
    p.cyl((0, 0.14, 0), r * 0.30, 0.16, 'Y', 14, DARK)
    for n in range(3):
        a = 2 * math.pi * n / 3
        p.box((math.cos(a) * r * 0.60, 0.19, math.sin(a) * r * 0.60),
              (r * 0.75, 0.035, 0.035), STEEL, rot=Matrix.Rotation(a, 4, 'Y'))
    # Blades, swept and tilted.
    for n in range(7):
        a = 2 * math.pi * n / 7
        rot = (Matrix.Rotation(a, 4, 'Y')
               @ Matrix.Rotation(math.radians(28), 4, 'X'))
        p.box((math.cos(a) * r * 0.52, 0.08, math.sin(a) * r * 0.52),
              (r * 0.24, 0.022, r * 0.80), STEEL, rot=rot)
    p.cyl((0, 0.05, 0), r * 0.22, 0.09, 'Y', 12, STEEL)
    # Guard bars across the front and a running lamp on the bezel.
    for n in range(4):
        t = -i + (n + 0.5) * (2 * i / 4)
        p.box((0, -0.022, t), (2 * i, 0.02, 0.018), STEEL)
    p.box((0, -DEPTH - 0.01, -i - FRAME / 2), (0.11, 0.02, 0.028), AMBER)
    p.bevel(width=0.004, segments=1)
    return p.finish("Mesh_Vent_Fan", coll)


def scoop(coll, mats):
    """External ram-air scoop standing proud of the hull — the only variation
    with a silhouette, which is what it is for."""
    p = Part(mats)
    h = W * 0.42
    # Tapered hood: tall at the mouth, flush at the back.
    p.loft([(-0.02, [(-h, 0.0), (h, 0.0), (h, 0.02), (-h, 0.02)]),
            (0.30, [(-h * 0.94, 0.0), (h * 0.94, 0.0), (h * 0.80, 0.20),
                    (-h * 0.80, 0.20)]),
            (0.62, [(-h * 0.72, 0.0), (h * 0.72, 0.0), (h * 0.60, 0.26),
                    (-h * 0.60, 0.26)])], 'Y', STEEL, cap=True)
    # Mouth opening and its shadow.
    p.slab((-h * 0.62, 0.60, 0.03), (h * 0.62, 0.66, 0.23), BLACK)
    for n in range(3):
        x = -h * 0.4 + n * h * 0.4
        p.box((x, 0.58, 0.13), (0.022, 0.10, 0.20), DARK)
    # Base flange bolted to the skin.
    p.slab((-h - 0.05, -0.06, -0.02), (h + 0.05, 0.70, 0.025), STEEL)
    p.rivets((-h, -0.02, 0.025), (-h, 0.64, 0.025), 5, radius=0.017,
             height=0.013, mat=DARK)
    p.rivets((h, -0.02, 0.025), (h, 0.64, 0.025), 5, radius=0.017,
             height=0.013, mat=DARK)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_Vent_Scoop", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Louvre", louvre), ("MeshScreen", mesh_screen),
                     ("Fan", fan), ("Scoop", scoop)):
        fn(collection("Coll_Vent_" + name), mats)

    report()
    save(out)


main()
