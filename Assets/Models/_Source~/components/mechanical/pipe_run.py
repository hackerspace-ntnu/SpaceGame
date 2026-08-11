"""Conduit, plumbing and cable runs.

The single highest-value component in the library for this ship: exposed
services are what separate a lived-in working vessel from a smooth prop, and
they get reused on the hull, in the cabin ceiling, behind the workstation and
down the engine pylons.

Built on a 1.0 m module running along +X, origin at the upstream end so a run is
laid by repeated integer translation. The clamps sit against z=0, which is the
surface the run is bolted to.

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

COPPER, STEEL, DARK, RUBBER, RUST, AMBER = 0, 1, 2, 3, 4, 5
MATS = ["Mat_Metal_Copper_Oxide", "Mat_Metal_Steel_Worn",
        "Mat_Metal_Steel_Dark", "Mat_Plastic_Rubber_Black",
        "Mat_Metal_Rust_Heavy", "Mat_Emissive_Amber"]

SPAN = 1.0


def clamp(p, x, z, r, mat=DARK):
    """P-clip holding a pipe off its surface."""
    p.tube((x, 0, z), r + 0.032, 0.032, 0.055, 'X', 12, mat)
    p.box((x, 0, z / 2 - 0.01), (0.05, 0.05, z), mat)
    p.box((x, 0, 0.012), (0.09, 0.10, 0.024), STEEL)


def straight(coll, mats):
    """The workhorse: a main and a return, clamped to a surface."""
    p = Part(mats)
    p.cyl((SPAN / 2, 0.075, 0.15), 0.055, SPAN, 'X', 12, COPPER)
    p.cyl((SPAN / 2, -0.070, 0.13), 0.038, SPAN, 'X', 10, STEEL)
    for x in (0.12, 0.50, 0.88):
        p.tube((x, 0.075, 0.15), 0.085, 0.030, 0.05, 'X', 12, DARK)
        p.tube((x, -0.070, 0.13), 0.066, 0.028, 0.05, 'X', 10, DARK)
        p.box((x, 0.0, 0.055), (0.055, 0.30, 0.11), DARK)
        p.box((x, 0.0, 0.012), (0.09, 0.34, 0.024), STEEL)
        p.rivets((x, -0.15, 0.026), (x, 0.15, 0.026), 2, radius=0.018,
                 height=0.014, mat=STEEL)
    # Union collars break the run up so a long line does not read as one rod.
    p.tube((0.34, 0.075, 0.15), 0.070, 0.015, 0.07, 'X', 12, RUST)
    p.tube((0.72, -0.070, 0.13), 0.052, 0.014, 0.07, 'X', 10, RUST)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_PipeRun_Straight", coll)


def elbow(coll, mats):
    """Quarter turn from running along +X to running up +Z — corners are where
    a straight-only kit visibly fails."""
    p = Part(mats)
    r, bend = 0.055, 0.26
    p.cyl((0.30, 0.075, 0.15), r, 0.60, 'X', 12, COPPER)

    # Swept bend: short cylinders laid along the arc, each tilted onto the
    # local tangent. cx/cz trace the arc centred at (SPAN-bend, 0.15+bend).
    steps = 6
    seg_len = (bend * math.pi / 2) / steps * 1.3
    for i in range(steps):
        a = math.radians(90.0 * (i + 0.5) / steps)
        cx = SPAN - bend + bend * math.sin(a)
        cz = 0.15 + bend - bend * math.cos(a)
        p.cyl((cx, 0.075, cz), r, seg_len, 'X', 12, COPPER,
              rot=Matrix.Rotation(-a, 4, 'Y'))
    # Collar rings hide the facet joints and read as a fabricated bend.
    for i in range(steps + 1):
        a = math.radians(90.0 * i / steps)
        cx = SPAN - bend + bend * math.sin(a)
        cz = 0.15 + bend - bend * math.cos(a)
        p.torus((cx, 0.075, cz), r * 1.05, 0.014, 'Y', 12, 8, RUST)
    p.cyl((SPAN, 0.075, 0.15 + bend + 0.20), r, 0.40, 'Z', 12, COPPER)
    p.tube((SPAN, 0.075, 0.15 + bend + 0.36), 0.070, 0.015, 0.07, 'Z', 12,
           DARK)
    for x in (0.12, 0.46):
        p.box((x, 0.075, 0.075), (0.055, 0.20, 0.15), DARK)
        p.box((x, 0.075, 0.012), (0.09, 0.24, 0.024), STEEL)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_PipeRun_Elbow", coll)


def junction(coll, mats):
    """Manifold block where three lines meet — a valve wheel and a gauge give
    the cabin something a player can read as maintainable."""
    p = Part(mats)
    p.box((0.5, 0.0, 0.22), (0.34, 0.26, 0.30), DARK)
    p.rivets((0.36, -0.14, 0.37), (0.64, -0.14, 0.37), 3, radius=0.02,
             height=0.016, axis='Y', mat=STEEL)
    p.cyl((0.16, 0.075, 0.22), 0.055, 0.34, 'X', 12, COPPER)
    p.cyl((0.84, 0.075, 0.22), 0.055, 0.34, 'X', 12, COPPER)
    p.cyl((0.5, 0.0, 0.50), 0.045, 0.28, 'Z', 10, STEEL)
    # Valve wheel.
    p.cyl((0.5, 0.0, 0.62), 0.028, 0.06, 'Z', 8, DARK)
    p.torus((0.5, 0.0, 0.66), 0.115, 0.018, 'Z', 16, 8, RUST)
    for i in range(4):
        a = math.pi * i / 2
        p.box((0.5 + math.cos(a) * 0.06, math.sin(a) * 0.06, 0.66),
              (0.12, 0.022, 0.022), RUST, rot=Matrix.Rotation(a, 4, 'Z'))
    # Pressure gauge on the face.
    p.cyl((0.5, -0.14, 0.26), 0.062, 0.05, 'Y', 14, STEEL)
    p.cyl((0.5, -0.17, 0.26), 0.048, 0.012, 'Y', 14, AMBER)
    # Feet.
    for x in (0.30, 0.70):
        p.box((x, 0.0, 0.035), (0.10, 0.30, 0.07), STEEL)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_PipeRun_Junction", coll)


def cable_bundle(coll, mats):
    """Loose loomed cabling, sagging between two tie points. Its silhouette is
    the opposite of the rigid pipes, which is what keeps a wall of services
    from reading as a machine part."""
    p = Part(mats)
    n_cables, sag = 5, 0.075
    steps = 8
    for c in range(n_cables):
        y = -0.075 + c * 0.038
        r = 0.017 + (c % 3) * 0.004
        mat = RUBBER if c % 2 else DARK
        depth = sag * (0.7 + 0.3 * (c % 3))
        for i in range(steps):
            t0, t1 = i / steps, (i + 1) / steps
            z0 = 0.20 - depth * math.sin(math.pi * t0)
            z1 = 0.20 - depth * math.sin(math.pi * t1)
            p.cyl(((t0 + t1) / 2 * SPAN, y, (z0 + z1) / 2), r,
                  SPAN / steps * 1.15, 'X', 6, mat)
    # Tie wraps and the two anchor blocks.
    for x in (0.05, 0.95):
        p.box((x, 0.0, 0.10), (0.07, 0.26, 0.20), DARK)
        p.box((x, 0.0, 0.012), (0.10, 0.30, 0.024), STEEL)
    for x in (0.34, 0.66):
        p.tube((x, 0.0, 0.20 - sag * 0.85), 0.115, 0.018, 0.026, 'X', 10,
               RUBBER)
    p.bevel(width=0.004, segments=1)
    return p.finish("Mesh_PipeRun_CableBundle", coll)


def duct(coll, mats):
    """Rectangular air trunking with a flanged joint — the ceiling run through
    the cabin, and the only variation wide enough to hide a light strip."""
    p = Part(mats)
    p.slab((0.0, -0.19, 0.14), (SPAN, 0.19, 0.44), STEEL)
    for x in (0.02, 0.50, 0.98):
        p.slab((x - 0.025, -0.22, 0.11), (x + 0.025, 0.22, 0.47), DARK)
    p.rivets((0.50, -0.22, 0.47), (0.50, 0.22, 0.47), 5, radius=0.016,
             height=0.012, mat=STEEL)
    # Corrugation between the flanges.
    for i in range(6):
        x = 0.10 + i * 0.135
        if abs(x - 0.50) < 0.06:
            continue
        p.slab((x - 0.014, -0.20, 0.12), (x + 0.014, 0.20, 0.46), STEEL)
    # Hanger straps up to the ceiling.
    for x in (0.20, 0.80):
        for s in (-1, 1):
            p.box((x, s * 0.20, 0.53), (0.05, 0.02, 0.20), DARK)
    p.box((0.72, -0.20, 0.29), (0.16, 0.03, 0.16), RUST)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_PipeRun_Duct", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Straight", straight), ("Elbow", elbow),
                     ("Junction", junction), ("CableBundle", cable_bundle),
                     ("Duct", duct)):
        fn(collection("Coll_PipeRun_" + name), mats)

    report()
    save(out)


main()
