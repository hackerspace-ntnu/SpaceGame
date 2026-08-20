"""components/props/supply_crate — stackable field containers.

The library had no crate. Every scene that needs a settlement, a depot, a camp
or a loading bay needs one, so this is the most reused component here by a wide
margin and it is worth the six variations.

They differ by silhouette and structure, not by colour: a flat case, a tall
corner-cast box, a long thin one, an offset stack, an open one with contents,
and a strapped pallet. Placed together they read as a supply dump rather than
as one crate copied six times.

Origin at the bottom centre of the footprint, so a crate sits on whatever
surface it is placed on without arithmetic.

    blender --background --python supply_crate.py -- --out supply_crate.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

from mathutils import Matrix

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0  crate body, sun-worn
    "Mat_Paint_Roof_Green",      # 1  the older paint layer, banded lids
    "Mat_Metal_Steel_Dark",      # 2  castings, latches, banding
    "Mat_Metal_Steel_Worn",      # 3  frames, corner irons
    "Mat_Metal_Rust_Heavy",      # 4  weather streaks and rot
    "Mat_Paint_Warn_Red",        # 5  stencils and hazard marks
    "Mat_Wood_Ply_Worn",         # 6  scavenged ply lids and pallets
    "Mat_Plastic_Cream_Aged",    # 7  moulded cases
    "Mat_Neutral_Black_Matte",   # 8  seals, interior shadow
    "Mat_Fabric_Canvas_Faded",   # 9  netting, sacking, strapping
]
BODY, GREEN, DARK, STEEL, RUST, RED, PLY, PLASTIC, BLACK, CANVAS = range(10)


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 4, 'Z')


def ribs(p, w, d, h, count, mat, axis='X'):
    """Raised vertical strips down two opposing faces — the read of 'moulded'."""
    for i in range(count):
        t = (i + 0.5) / count - 0.5
        if axis == 'X':
            for sy in (-1, 1):
                p.box((t * w * 0.86, sy * d / 2, h / 2),
                      (w / count * 0.34, 0.03, h * 0.82), mat)
        else:
            for sx in (-1, 1):
                p.box((sx * w / 2, t * d * 0.86, h / 2),
                      (0.03, d / count * 0.34, h * 0.82), mat)


def corner_irons(p, w, d, h, mat, t=0.055):
    """L-section irons up the four verticals, plus top and bottom rails."""
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * (w / 2 - t / 2), sy * d / 2, h / 2), (t, t * 1.6, h), mat)
            p.box((sx * w / 2, sy * (d / 2 - t / 2), h / 2), (t * 1.6, t, h), mat)
    for z in (t / 2, h - t / 2):
        for sy in (-1, 1):
            p.box((0, sy * d / 2, z), (w, t * 1.4, t), mat)
        for sx in (-1, 1):
            p.box((sx * w / 2, 0, z), (t * 1.4, d, t), mat)


def latches(p, w, d, z, mat, count=2):
    for i in range(count):
        t = (i + 0.5) / count - 0.5
        for sy in (-1, 1):
            p.box((t * w * 0.62, sy * (d / 2 + 0.018), z), (0.16, 0.05, 0.09), mat)
            p.box((t * w * 0.62, sy * (d / 2 + 0.03), z), (0.07, 0.03, 0.14), mat)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def crate_small(mats, coll):
    """A moulded plastic flight case. Flat, wide, carried by one person."""
    p = Part(mats)
    w, d, h = 0.66, 0.46, 0.42
    p.box((0, 0, h / 2), (w, d, h), PLASTIC)
    p.box((0, 0, h - 0.045), (w + 0.03, d + 0.03, 0.09), PLASTIC)   # lid lip
    p.box((0, 0, h + 0.012), (w * 0.72, d * 0.72, 0.024), PLASTIC)  # stack boss
    p.box((0, 0, 0.022), (w * 0.74, d * 0.74, 0.044), DARK)         # skid feet
    ribs(p, w, d, h, 3, PLASTIC)
    latches(p, w, d, h - 0.09, DARK)
    p.box((0, 0, h * 0.42), (w * 0.4, d + 0.006, 0.1), RED)         # stencil band
    for sx in (-1, 1):                                              # end handles
        p.box((sx * (w / 2 + 0.03), 0, h * 0.55), (0.06, 0.16, 0.05), DARK)
    p.bevel(width=0.012)
    return p.finish("Mesh_Crate_Small", coll)


def crate_large(mats, coll):
    """The corner-cast standard: a stackable steel-framed shipping box."""
    p = Part(mats)
    w, d, h = 1.22, 0.92, 0.94
    p.box((0, 0, h / 2), (w, d, h), BODY)
    corner_irons(p, w, d, h, STEEL)
    ribs(p, w, d, h, 4, BODY)
    ribs(p, w, d, h, 3, BODY, axis='Y')
    for sx in (-1, 1):                                              # corner castings
        for sy in (-1, 1):
            for z in (0.08, h - 0.08):
                p.box((sx * (w / 2 - 0.09), sy * (d / 2 - 0.09), z),
                      (0.19, 0.19, 0.16), DARK)
    p.box((0, 0, h + 0.03), (w - 0.1, d - 0.1, 0.06), GREEN)        # lid panel
    p.box((0, 0, h + 0.075), (w * 0.5, d * 0.5, 0.03), GREEN)
    latches(p, w, d, h - 0.05, DARK, count=3)
    p.box((-w * 0.22, d / 2 + 0.004, h * 0.62), (0.34, 0.012, 0.2), RED)
    p.box((w * 0.3, -d / 2 - 0.004, h * 0.3), (0.26, 0.012, 0.26), RUST)
    p.rivets((-w / 2 + 0.1, d / 2 + 0.01, 0.16), (w / 2 - 0.1, d / 2 + 0.01, 0.16),
             8, axis='Y', mat=DARK)
    p.bevel(width=0.014)
    return p.finish("Mesh_Crate_Large", coll)


def crate_long(mats, coll):
    """Long and shallow — pipe, rod and parts stock. Reads as 'not a cube'."""
    p = Part(mats)
    w, d, h = 1.86, 0.44, 0.38
    p.box((0, 0, h / 2), (w, d, h), GREEN)
    corner_irons(p, w, d, h, STEEL, t=0.045)
    for i in range(3):                                              # banding straps
        x = (i - 1) * w * 0.31
        p.box((x, 0, h / 2), (0.06, d + 0.02, h + 0.02), DARK)
    p.box((0, 0, h + 0.028), (w - 0.06, d - 0.06, 0.056), PLY)      # ply lid
    p.box((0, 0, 0.03), (w * 0.86, d + 0.05, 0.06), STEEL)          # skids
    latches(p, w, d, h - 0.06, DARK, count=4)
    p.box((-w * 0.28, d / 2 + 0.004, h * 0.55), (0.5, 0.012, 0.12), RED)
    p.bevel(width=0.01)
    return p.finish("Mesh_Crate_Long", coll)


def crate_stack(mats, coll):
    """Three mismatched crates, offset and slightly askew.

    Modelled as one object rather than assembled from the others at placement
    time: a stack that is deliberately imperfect cannot be got by instancing a
    crate three times on a grid, which is exactly the lockstep look to avoid.
    """
    p = Part(mats)
    layers = [
        (1.24, 0.94, 0.9, 0.0, 0.0, 0.0, BODY),
        (1.02, 0.78, 0.72, 0.09, -0.06, 7.0, GREEN),
        (0.66, 0.5, 0.44, -0.14, 0.11, -13.0, PLASTIC),
    ]
    z = 0.0
    for i, (w, d, h, ox, oy, ang, mat) in enumerate(layers):
        r = rot_z(ang)
        p.box((ox, oy, z + h / 2), (w, d, h), mat, rot=r)
        p.box((ox, oy, z + h + 0.03), (w - 0.08, d - 0.08, 0.06), DARK, rot=r)
        for sx in (-1, 1):                                          # corner posts
            for sy in (-1, 1):
                p.box((ox + sx * (w / 2 - 0.05), oy + sy * (d / 2 - 0.05),
                       z + h / 2), (0.1, 0.1, h), STEEL, rot=r)
        if i == 0:
            p.box((ox, oy + d / 2 + 0.004, z + h * 0.6), (0.4, 0.012, 0.18), RED,
                  rot=r)
        z += h + 0.06
    p.box((0.02, -0.03, z + 0.05), (0.5, 0.36, 0.1), CANVAS)        # tarp scrap on top
    p.bevel(width=0.013)
    return p.finish("Mesh_Crate_Stack", coll)


def crate_open(mats, coll):
    """Lid hinged back, packing and contents showing. Reads as in-use."""
    p = Part(mats)
    w, d, h = 1.1, 0.82, 0.72
    t = 0.05
    p.box((0, 0, t / 2), (w, d, t), BODY)                           # floor
    for sy in (-1, 1):                                              # walls
        p.box((0, sy * (d / 2 - t / 2), h / 2), (w, t, h), BODY)
    for sx in (-1, 1):
        p.box((sx * (w / 2 - t / 2), 0, h / 2), (t, d - 2 * t, h), BODY)
    p.box((0, 0, h - t / 2), (w + 0.04, d + 0.04, t), STEEL)        # rim
    corner_irons(p, w, d, h, STEEL, t=0.05)
    # Lid, hinged back past vertical so it leans away from the opening.
    lid = Matrix.Translation((0, -d / 2 - 0.02, h)) @ \
        Matrix.Rotation(math.radians(-104), 4, 'X')
    p.box((0, 0, 0), (w, d, 0.06), GREEN, rot=lid)
    p.box((0, 0, 0), (w * 0.55, d * 0.6, 0.1), PLY, rot=lid)
    for sy in (-1, 1):                                              # hinge barrels
        p.cyl((sy * w * 0.32, -d / 2 - 0.02, h), 0.035, 0.14, axis='X', seg=8,
              mat=DARK)
    # Contents: packing and a few boxed items down inside.
    p.box((0, 0, 0.16), (w - 0.16, d - 0.16, 0.2), CANVAS)
    p.greeble((-w / 2 + 0.16, -d / 2 + 0.16, 0.28), (w / 2 - 0.16, d / 2 - 0.16, 0.4),
              7, seed=41, scale=(0.1, 0.26), mat=PLASTIC)
    p.box((0, 0, 0.02), (w - 2 * t, d - 2 * t, 0.02), BLACK)        # interior shadow
    p.bevel(width=0.012)
    return p.finish("Mesh_Crate_Open", coll)


def crate_pallet(mats, coll):
    """A netted pallet load — the only variation with a soft silhouette."""
    p = Part(mats)
    w, d = 1.24, 1.06
    # Pallet: three bearers under seven deck boards.
    for i in range(3):
        p.box(((i - 1) * w * 0.36, 0, 0.05), (0.11, d, 0.1), PLY)
    for i in range(7):
        p.box((0, (i / 6 - 0.5) * d * 0.9, 0.115), (w, d / 7 * 0.74, 0.03), PLY)
    # Load: sacks, deliberately uneven so the netting has something to catch on.
    z = 0.13
    for row, (n, sz, hz) in enumerate(((3, 0.36, 0.26), (3, 0.34, 0.24), (2, 0.4, 0.22))):
        for i in range(n):
            t = (i + 0.5) / n - 0.5
            ang = 6.0 * ((i % 2) * 2 - 1) + row * 4
            p.box((t * w * 0.66, (row % 2 - 0.5) * 0.16, z + hz / 2),
                  (sz * 1.5, sz, hz), CANVAS, rot=rot_z(ang))
        z += hz - 0.02
    # Netting: a lattice of thin straps over the whole load.
    for i in range(5):
        x = (i / 4 - 0.5) * w * 0.94
        p.box((x, 0, z * 0.55), (0.028, d * 0.98, z * 1.06), DARK)
    for i in range(4):
        y = (i / 3 - 0.5) * d * 0.9
        p.box((0, y, z * 0.55), (w * 0.98, 0.028, z * 1.02), DARK)
    p.box((0, 0, z + 0.01), (w * 0.9, d * 0.86, 0.03), DARK)
    for sx in (-1, 1):                                              # tie-down hooks
        p.box((sx * w * 0.5, 0, 0.14), (0.05, 0.1, 0.07), STEEL)
    p.bevel(width=0.01)
    return p.finish("Mesh_Crate_Pallet", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Small", crate_small), ("Large", crate_large),
                     ("Long", crate_long), ("Stack", crate_stack),
                     ("Open", crate_open), ("Pallet", crate_pallet)):
        fn(mats, collection("Coll_Crate_%s" % name))
    report()
    save(out)


main()
