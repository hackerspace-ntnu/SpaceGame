"""Crew seating.

Four seats that share a frame language — tubular steel spine, moulded shell,
cracked ochre vinyl — but differ in silhouette: a tall harnessed pilot's chair,
a squatter copilot's chair, a fold-down bench, and a backless stool. That spread
matters because two of them stand side by side on the bridge, where identical
chairs would read as a duplicated asset rather than a cockpit.

Built facing +X (the ship's nose), seated occupant facing the same way. Origin
at deck level under the pedestal centre, which is the point that bolts down.

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

VINYL, STEEL, DARK, CHROME, CANVAS, RUBBER, AMBER, GREEN = 0, 1, 2, 3, 4, 5, 6, 7
MATS = ["Mat_Fabric_Seat_Ochre", "Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Metal_Chrome_Scuffed", "Mat_Fabric_Canvas_Faded",
        "Mat_Plastic_Rubber_Black", "Mat_Emissive_Amber",
        "Mat_Emissive_Green_CRT"]

SEAT_H = 0.46     # cushion top above the deck — standard sitting height


def pedestal(p, height=SEAT_H - 0.10, r=0.085, slide=True):
    """Column, foot and (optionally) the fore-aft slide rail under it."""
    if slide:
        p.slab((-0.30, -0.13, 0.0), (0.34, -0.09, 0.035), STEEL)
        p.slab((-0.30, 0.09, 0.0), (0.34, 0.13, 0.035), STEEL)
        for x in (-0.26, 0.30):
            p.box((x, 0.0, 0.018), (0.05, 0.30, 0.036), DARK)
    p.cyl((0, 0, 0.055), 0.20, 0.045, 'Z', 12, DARK)
    p.cyl((0, 0, height / 2 + 0.05), r, height, 'Z', 12, STEEL)
    p.cyl((0, 0, height * 0.72), r * 1.22, 0.05, 'Z', 12, DARK)
    # Height-adjust lever.
    p.cyl((0.11, -0.10, height * 0.75), 0.016, 0.16, 'X', 6, DARK)
    p.cyl((0.19, -0.10, height * 0.75), 0.026, 0.05, 'X', 8, RUBBER)


def cushion(p, x0, x1, w, z0, thick, mat=VINYL, tilt=0.0, seg=5):
    """A padded slab with a rolled front edge and stitch channels — the read
    that separates upholstery from a painted box."""
    rot = Matrix.Rotation(math.radians(tilt), 4, 'Y')
    c = ((x0 + x1) / 2, 0.0, z0 + thick / 2)
    p.box(c, (x1 - x0, w, thick), mat, rot=rot)
    for i in range(1, seg):
        y = -w / 2 + w * i / seg
        p.box(((x0 + x1) / 2, y, z0 + thick), (x1 - x0 - 0.04, 0.018, 0.02),
              mat, rot=rot)
    # Rolled edges.
    p.cyl((x1, 0, z0 + thick / 2), thick * 0.48, w, 'Y', 10, mat)
    p.cyl((x0, 0, z0 + thick / 2), thick * 0.42, w, 'Y', 10, mat)


def pilot(coll, mats):
    """High-backed harnessed pilot's chair for the left helm station."""
    p = Part(mats)
    pedestal(p)

    # Shell: one moulded piece wrapping pan and back, in bare frame material.
    p.prism([(-0.34, 0.36), (0.34, 0.36), (0.34, 0.46), (-0.26, 0.50),
             (-0.40, 1.22), (-0.50, 1.20), (-0.46, 0.44)], 0.56, 'Y', DARK)
    # Side bolsters — the wrap-around that says "acceleration couch".
    for s in (-1, 1):
        p.prism([(-0.30, 0.46), (0.30, 0.46), (0.28, 0.60), (-0.32, 0.64)],
                0.06, 'Y', DARK, offset=(0, s * 0.28, 0))
        p.prism([(-0.42, 0.62), (-0.30, 0.62), (-0.36, 1.14), (-0.46, 1.14)],
                0.06, 'Y', DARK, offset=(0, s * 0.24, 0))

    cushion(p, -0.28, 0.30, 0.50, 0.44, 0.10)
    # Backrest cushion, raked back 12 degrees.
    p.box((-0.36, 0.0, 0.84), (0.16, 0.48, 0.66), VINYL,
          rot=Matrix.Rotation(math.radians(12), 4, 'Y'))
    for i in range(4):
        z = 0.60 + i * 0.16
        p.box((-0.30 - i * 0.012, 0.0, z), (0.03, 0.44, 0.022), VINYL,
              rot=Matrix.Rotation(math.radians(12), 4, 'Y'))
    # Headrest on two posts.
    for s in (-1, 1):
        p.cyl((-0.40, s * 0.10, 1.20), 0.016, 0.14, 'Z', 8, CHROME)
    p.box((-0.42, 0.0, 1.30), (0.15, 0.34, 0.16), VINYL,
          rot=Matrix.Rotation(math.radians(12), 4, 'Y'))

    # Five-point harness: shoulder straps, lap straps, buckle.
    for s in (-1, 1):
        p.box((-0.22, s * 0.15, 0.94), (0.42, 0.075, 0.016), CANVAS,
              rot=Matrix.Rotation(math.radians(-58), 4, 'Y'))
        p.box((0.02, s * 0.24, 0.52), (0.30, 0.07, 0.016), CANVAS,
              rot=Matrix.Rotation(math.radians(-8), 4, 'Y'))
    p.box((-0.02, 0.0, 0.60), (0.11, 0.10, 0.045), CHROME)
    p.cyl((-0.02, 0.0, 0.625), 0.032, 0.02, 'Z', 10, AMBER)

    # Armrests with a thumb-pad on the right.
    for s in (-1, 1):
        p.box((-0.06, s * 0.32, 0.66), (0.44, 0.09, 0.06), DARK)
        p.box((-0.06, s * 0.32, 0.71), (0.40, 0.085, 0.05), VINYL)
        p.box((-0.30, s * 0.32, 0.56), (0.07, 0.06, 0.16), STEEL)
    p.box((0.12, 0.32, 0.75), (0.13, 0.09, 0.03), DARK)
    p.cyl((0.12, 0.32, 0.77), 0.022, 0.02, 'Z', 8, GREEN)
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_CrewSeat_Pilot", coll)


def copilot(coll, mats):
    """Lower-backed second chair. Same family, visibly not the same chair:
    squarer shell, no headrest, one armrest, a swivel instead of a slide."""
    p = Part(mats)
    pedestal(p, slide=False)
    p.cyl((0, 0, 0.40), 0.13, 0.05, 'Z', 14, CHROME)  # swivel plate

    p.prism([(-0.32, 0.38), (0.32, 0.38), (0.32, 0.46), (-0.26, 0.48),
             (-0.38, 0.96), (-0.48, 0.94), (-0.44, 0.42)], 0.54, 'Y', DARK)
    cushion(p, -0.26, 0.28, 0.48, 0.44, 0.09, seg=4)
    p.box((-0.34, 0.0, 0.72), (0.15, 0.46, 0.44), VINYL,
          rot=Matrix.Rotation(math.radians(14), 4, 'Y'))
    for i in range(3):
        p.box((-0.28 - i * 0.012, 0.0, 0.58 + i * 0.16), (0.03, 0.42, 0.022),
              VINYL, rot=Matrix.Rotation(math.radians(14), 4, 'Y'))
    # Lap belt only — the copilot is not strapped in for launch.
    p.box((0.02, 0.0, 0.51), (0.34, 0.075, 0.016), CANVAS)
    p.box((-0.02, 0.0, 0.55), (0.09, 0.09, 0.04), CHROME)
    # Single fold-up armrest on the inboard side.
    p.box((-0.06, -0.30, 0.64), (0.40, 0.085, 0.055), DARK)
    p.box((-0.06, -0.30, 0.685), (0.36, 0.08, 0.045), VINYL)
    p.box((-0.28, -0.30, 0.55), (0.065, 0.055, 0.14), STEEL)
    # Torn seam showing the foam — this is the older of the two chairs.
    p.box((0.16, 0.14, 0.545), (0.14, 0.10, 0.03), CANVAS,
          rot=Matrix.Rotation(math.radians(6), 4, 'Y'))
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_CrewSeat_Copilot", coll)


def bench(coll, mats):
    """Two-place fold-down jump bench for the cargo bay wall. Wall-mounted, so
    its origin is on the wall line at deck level, not under a pedestal."""
    p = Part(mats)
    # Wall rail it hangs from.
    p.slab((-0.10, -0.72, 0.40), (-0.04, 0.72, 0.52), STEEL)
    for y in (-0.62, 0.0, 0.62):
        p.box((-0.07, y, 0.46), (0.10, 0.09, 0.20), DARK)
        p.rivets((-0.10, y - 0.03, 0.55), (-0.10, y + 0.03, 0.55), 2,
                 radius=0.017, height=0.013, mat=DARK)
    # Seat pan, hinged down, held by two diagonal stays.
    p.slab((-0.05, -0.70, 0.42), (0.42, 0.70, 0.47), DARK)
    p.slab((-0.02, -0.68, 0.47), (0.40, 0.68, 0.52), CANVAS)
    for i in range(6):
        y = -0.60 + i * 0.24
        p.box((0.19, y, 0.525), (0.38, 0.02, 0.014), CANVAS)
    for s in (-1, 1):
        p.cyl((0.18, s * 0.66, 0.30), 0.016, 0.42, 'X', 8, STEEL,
              rot=Matrix.Rotation(math.radians(-52), 4, 'Y'))
    # Canvas backrest slung between two uprights.
    for s in (-1, 1):
        p.cyl((-0.06, s * 0.68, 0.78), 0.022, 0.56, 'Z', 8, STEEL)
    p.box((-0.03, 0.0, 0.92), (0.03, 1.34, 0.30), CANVAS)
    p.box((-0.03, 0.0, 1.02), (0.045, 1.30, 0.035), STEEL)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_CrewSeat_Bench", coll)


def stool(coll, mats):
    """Backless workshop stool for the repair bench — the seat that is not a
    chair, so a bay containing both does not look furnished from one kit."""
    p = Part(mats)
    p.cyl((0, 0, 0.02), 0.024, 0.04, 'Z', 12, DARK)
    # Four-star base on castors.
    for i in range(4):
        a = math.pi / 2 * i + math.pi / 4
        p.box((math.cos(a) * 0.14, math.sin(a) * 0.14, 0.055),
              (0.26, 0.045, 0.04), STEEL, rot=Matrix.Rotation(a, 4, 'Z'))
        p.cyl((math.cos(a) * 0.26, math.sin(a) * 0.26, 0.032), 0.032, 0.026,
              'Y', 10, RUBBER)
    p.cyl((0, 0, 0.26), 0.05, 0.38, 'Z', 12, CHROME)
    p.cyl((0, 0, 0.20), 0.062, 0.06, 'Z', 12, DARK)
    # Foot ring.
    p.torus((0, 0, 0.19), 0.16, 0.014, 'Z', 16, 8, STEEL)
    for i in range(3):
        a = 2 * math.pi * i / 3
        p.box((math.cos(a) * 0.11, math.sin(a) * 0.11, 0.19),
              (0.12, 0.016, 0.016), STEEL, rot=Matrix.Rotation(a, 4, 'Z'))
    # Round pad, worn through at the front.
    p.cyl((0, 0, 0.44), 0.20, 0.035, 'Z', 20, DARK)
    p.cyl((0, 0, 0.475), 0.19, 0.05, 'Z', 20, VINYL)
    p.torus((0, 0, 0.472), 0.185, 0.026, 'Z', 20, 8, VINYL)
    p.box((0.13, 0.0, 0.498), (0.10, 0.13, 0.02), CANVAS)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_CrewSeat_Stool", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Pilot", pilot), ("Copilot", copilot),
                     ("Bench", bench), ("Stool", stool)):
        fn(collection("Coll_CrewSeat_" + name), mats)

    report()
    save(out)


main()
