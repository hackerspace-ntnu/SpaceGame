"""components/props/fuel_barrel — pressure and liquid containers for a yard.

The other half of the missing clutter vocabulary. `supply_crate` covers the
boxy things; everything here is a cylinder, which matters more than it sounds:
a supply dump built only from crates reads as stacked luggage, and it is the
mix of round against square that makes it read as a working site.

Five variations spanning the silhouette range a drum can have — one upright,
a toppled group, a low frame of cans, a tall bottle rack, and a horizontal
saddled tank. Only the first is the obvious one.

Origin at the bottom centre of the footprint.

    blender --background --python fuel_barrel.py -- --out fuel_barrel.blend

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
    "Mat_Paint_Warn_Red",        # 0  the red drum and the tank
    "Mat_Paint_Roof_Green",      # 1  the green drum
    "Mat_Paint_Blue_Station",    # 2  station-issue containers
    "Mat_Metal_Steel_Worn",      # 3  bare galvanised bodies, frames
    "Mat_Metal_Steel_Dark",      # 4  bungs, valves, banding, chain
    "Mat_Metal_Rust_Heavy",      # 5  corrosion at every seam
    "Mat_Paint_Safety_Orange",   # 6  hazard bands
    "Mat_Neutral_Black_Matte",   # 7  rubber feet, recesses
    "Mat_Plastic_Cream_Aged",    # 8  moulded jerricans
    "Mat_Metal_Chrome_Scuffed",  # 9  regulator bodies and gauges
]
RED, GREEN, BLUE, STEEL, DARK, RUST, ORANGE, BLACK, PLASTIC, CHROME = range(10)

R = 0.29          # standard 200 L drum radius
HD = 0.88         # standard drum height


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 4, 'Z')


def drum_body(p, center, mat, r=R, h=HD, axis='Z', rot=None, seg=20, bands=True):
    """One drum: barrel, two rolling hoops, chimes at both ends, a bung.

    Factored out because four of the five variations are made of drums, and a
    drum whose hoops move between variations reads as a different product.
    """
    cx, cy, cz = center
    p.cyl((cx, cy, cz), r, h, axis=axis, seg=seg, mat=mat, rot=rot)
    for t in (-0.24, 0.24):                                      # rolling hoops
        off = _along(axis, t * h, rot)
        p.cyl((cx + off[0], cy + off[1], cz + off[2]), r * 1.045, h * 0.075,
              axis=axis, seg=seg, mat=RUST if bands else mat, rot=rot)
    for t in (-0.5, 0.5):                                        # chimes
        off = _along(axis, t * h, rot)
        p.cyl((cx + off[0], cy + off[1], cz + off[2]), r * 1.03, h * 0.045,
              axis=axis, seg=seg, mat=STEEL, rot=rot)
    top = _along(axis, h * 0.5, rot)                             # bung plate
    p.cyl((cx + top[0] * 1.03, cy + top[1] * 1.03, cz + top[2] * 1.03),
          r * 0.16, 0.035, axis=axis, seg=8, mat=DARK, rot=rot)


def _along(axis, dist, rot=None):
    v = {'X': (dist, 0, 0), 'Y': (0, dist, 0), 'Z': (0, 0, dist)}[axis]
    if rot is not None:
        v = tuple(rot.to_3x3() @ __import__("mathutils").Vector(v))
    return v


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def barrel_drum(mats, coll):
    """The single upright 200 L drum. The one everything else is measured off."""
    p = Part(mats)
    drum_body(p, (0, 0, HD / 2), RED)
    p.cyl((0, 0, HD + 0.012), R * 0.42, 0.024, seg=12, mat=DARK)   # closure ring
    p.box((0, R * 0.92, HD * 0.52), (0.3, 0.02, 0.16), ORANGE)     # hazard placard
    p.box((0, -R * 0.9, HD * 0.3), (0.18, 0.02, 0.22), RUST)       # streak
    p.cyl((0, 0, 0.018), R * 1.02, 0.036, seg=20, mat=RUST)        # rot at the foot
    p.bevel(width=0.008)
    return p.finish("Mesh_Barrel_Drum", coll)


def barrel_stack(mats, coll):
    """Two upright, one on its side, all dented. A dump rather than a store."""
    p = Part(mats)
    drum_body(p, (0, 0, HD / 2), GREEN)
    drum_body(p, (0.63, 0.14, HD / 2), BLUE)
    # The toppled one, lying across the front and rolled slightly off square.
    lie = Matrix.Rotation(math.radians(-16), 4, 'Z')
    drum_body(p, (0.3, -0.72, R), STEEL, axis='Y', rot=lie)
    p.cyl((0.3, -0.72, R), R * 0.34, 0.94, axis='Y', seg=12, mat=RUST, rot=lie)
    # A short plank wedged under the toppled drum so it does not float.
    p.box((0.3, -0.72, 0.03), (1.1, 0.16, 0.06), DARK, rot=lie)
    for cx, cy in ((0, 0), (0.63, 0.14)):                          # top closures
        p.cyl((cx, cy, HD + 0.012), R * 0.42, 0.024, seg=12, mat=DARK)
    p.box((0.63, 0.14 + R * 0.9, HD * 0.45), (0.26, 0.02, 0.18), ORANGE)
    p.bevel(width=0.008)
    return p.finish("Mesh_Barrel_Stack", coll)


def barrel_jerrican(mats, coll):
    """A low frame of four moulded cans — the small, portable end of the range."""
    p = Part(mats)
    w, d, h = 1.12, 0.52, 0.24
    for sx in (-1, 1):                                             # frame
        p.box((sx * (w / 2 - 0.03), 0, h / 2), (0.06, d, 0.05), STEEL)
        p.box((sx * (w / 2 - 0.03), 0, 0.025), (0.06, d, 0.05), STEEL)
    for sy in (-1, 1):
        p.box((0, sy * (d / 2 - 0.03), h - 0.025), (w, 0.06, 0.05), STEEL)
        p.box((0, sy * (d / 2 - 0.03), 0.025), (w, 0.06, 0.05), STEEL)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * (w / 2 - 0.03), sy * (d / 2 - 0.03), h / 2),
                  (0.06, 0.06, h), STEEL)
    # Four cans, alternating colour and rotated a few degrees each.
    for i in range(4):
        x = (i / 3 - 0.5) * (w - 0.34)
        mat = (PLASTIC, GREEN, PLASTIC, RED)[i]
        r = rot_z(4.0 * (i % 3 - 1))
        p.box((x, 0.0, h + 0.2), (0.24, 0.4, 0.4), mat, rot=r)
        p.box((x, 0.0, h + 0.2), (0.26, 0.3, 0.3), mat, rot=r)     # side swage
        p.cyl((x - 0.06, 0, h + 0.42), 0.045, 0.06, seg=8, mat=DARK, rot=r)
        p.box((x + 0.04, 0, h + 0.41), (0.13, 0.05, 0.05), DARK, rot=r)  # handle
    p.bevel(width=0.01)
    return p.finish("Mesh_Barrel_Jerrican", coll)


def barrel_gasbottles(mats, coll):
    """Four tall cylinders chained into a rack. The vertical accent of the set."""
    p = Part(mats)
    w, d = 1.06, 0.42
    hb, rb = 1.34, 0.115
    for sx in (-1, 1):                                             # rack uprights
        p.box((sx * w / 2, 0, 0.62), (0.06, 0.06, 1.24), STEEL)
        p.box((sx * w / 2, 0, 0.03), (0.16, d + 0.1, 0.06), DARK)
    for z in (0.36, 1.02):                                         # rails
        p.box((0, -d / 2, z), (w + 0.06, 0.05, 0.05), STEEL)
    for i in range(4):
        x = (i / 3 - 0.5) * (w - 0.28)
        y = 0.03 * ((i % 2) * 2 - 1)
        mat = (STEEL, BLUE, STEEL, GREEN)[i]
        p.cyl((x, y, hb / 2), rb, hb, seg=14, mat=mat)             # bottle
        p.cyl((x, y, hb - 0.02), rb * 0.86, 0.14, seg=14, mat=mat,
              radius_top=rb * 0.36)                                # shoulder
        p.cyl((x, y, hb + 0.09), rb * 0.3, 0.1, seg=8, mat=CHROME)  # neck
        p.cyl((x, y, hb + 0.16), rb * 0.52, 0.06, seg=10, mat=DARK)  # valve guard
        p.box((x + rb * 0.5, y, hb + 0.15), (0.12, 0.06, 0.06), CHROME)
        p.cyl((x, y, 0.05), rb * 1.02, 0.1, seg=14, mat=RUST)      # foot ring
        p.box((x, y, hb * 0.62), (rb * 1.2, 0.012, 0.14), ORANGE)  # label
    # Retaining chain across the front, sagging between the uprights.
    for i in range(9):
        t = i / 8.0
        x = (t - 0.5) * w
        sag = 0.09 * math.sin(math.pi * t)
        p.box((x, -d / 2 - 0.03, 0.86 - sag), (w / 8 * 0.9, 0.03, 0.035), DARK)
    p.bevel(width=0.008)
    return p.finish("Mesh_Barrel_GasBottles", coll)


def barrel_tank(mats, coll):
    """A saddled horizontal pressure tank. The heaviest, most fixed of the set."""
    p = Part(mats)
    rt, lt = 0.6, 1.92
    z = rt + 0.34
    p.cyl((0, 0, z), rt, lt, axis='Y', seg=24, mat=RED)
    for sy in (-1, 1):                                             # dished ends
        p.cyl((0, sy * (lt / 2 + 0.09), z), rt * 0.98, 0.18, axis='Y', seg=24,
              mat=RED, radius_top=rt * 0.62)
        p.cyl((0, sy * (lt / 2 + 0.2), z), rt * 0.62, 0.06, axis='Y', seg=20,
              mat=STEEL, radius_top=rt * 0.3)
    for sy in (-1, 1):                                             # saddles
        y = sy * lt * 0.3
        p.box((0, y, 0.17), (1.16, 0.16, 0.34), STEEL)
        p.box((0, y, 0.04), (1.34, 0.24, 0.08), DARK)
        for sx in (-1, 1):
            p.box((sx * 0.5, y, 0.42), (0.14, 0.16, 0.36), STEEL,
                  rot=Matrix.Rotation(math.radians(sx * 26), 4, 'Y'))
    for i in range(3):                                             # reinforcing hoops
        p.cyl((0, (i - 1) * lt * 0.33, z), rt * 1.03, 0.07, axis='Y', seg=24,
              mat=RUST)
    # Manway, valve cluster and a gauge on the crown.
    p.cyl((0, -lt * 0.18, z + rt), 0.24, 0.16, seg=14, mat=STEEL)
    p.cyl((0, -lt * 0.18, z + rt + 0.1), 0.27, 0.05, seg=14, mat=DARK)
    p.cyl((0, lt * 0.24, z + rt + 0.1), 0.07, 0.26, seg=10, mat=CHROME)
    p.box((0, lt * 0.24, z + rt + 0.28), (0.2, 0.14, 0.14), DARK)
    p.cyl((0.11, lt * 0.24, z + rt + 0.28), 0.07, 0.05, axis='X', seg=10,
          mat=CHROME)
    p.box((0, 0, z + rt + 0.06), (0.44, 0.9, 0.06), ORANGE)        # walk strip
    p.box((0.62, -lt * 0.1, z * 0.6), (0.02, 0.7, 0.3), ORANGE)    # hazard placard
    p.bevel(width=0.01)
    return p.finish("Mesh_Barrel_Tank", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Drum", barrel_drum), ("Stack", barrel_stack),
                     ("Jerrican", barrel_jerrican),
                     ("GasBottles", barrel_gasbottles), ("Tank", barrel_tank)):
        fn(mats, collection("Coll_Barrel_%s" % name))
    report()
    save(out)


main()
