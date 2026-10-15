"""components/structural/stair_flight — how the crew gets between decks.

Stairs are sized for the 3 m player: 0.30 m risers on a 35 degree pitch, which
is an ordinary ship's stair scaled by the body. Every flight climbs toward -Y
with its rise along +Z, and its origin is at the bottom front edge centre: the
toe of the stringers on the lower deck, so a flight is placed by putting its
origin on the edge it starts from and turning it to face the way it climbs.

    Stair_Grating  open bar-grating treads between channel stringers
    Stair_Plate    one folded plate of treads and riveted kick plates
    Stair_Landing  a free-standing grated platform at the top of either flight
    Stair_Ladder   a 70 degree ship's ladder where there is no room for a stair

    blender --background --python stair_flight.py -- --out stair_flight.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",        # 0 stringers, frames
    "Mat_Metal_Steel_Dark",        # 1 grating bars, legs
    "Mat_Metal_HullRust_Orange",   # 2 folded plate treads
    "Mat_Metal_Rust_Deep",         # 3 bolt heads, foot plates
    "Mat_Metal_Rust_Pale",         # 4 boot-worn nosings and rungs
]
STEEL, DARK, HULL, DEEP, PALE = range(5)

RISE = 2.10
STEPS = 7
RISER = RISE / STEPS                          # 0.30 m
PITCH = math.radians(35.0)
GOING = RISER / math.tan(PITCH)               # 0.428 m
RUN = GOING * STEPS                           # 3.00 m
TOE = 0.25        # the stringers reach this far ahead of the first riser
FIRST = -TOE      # y of the first riser: the stringer toe is the origin


def stringer_profile():
    """Side outline (y, z) of a stringer. Its top edge runs 0.08 above the
    nosing line, its bottom 0.37 below it, deep enough to carry the back of
    every tread. The top end is cut level and plumb so it can hang on the
    upper deck's edge beam."""
    slope = RISER / GOING

    def top(y):
        return RISER + 0.08 + slope * (FIRST - y)

    def bottom_y(z):
        return FIRST - (z - RISER + 0.37) / slope

    end = FIRST - RUN
    cap = RISE + 0.15
    bear = RISE - 0.25
    return [(0.0, 0.0), (0.0, top(0.0)),
            (FIRST - (cap - RISER - 0.08) / slope, cap), (end, cap),
            (end, bear), (bottom_y(bear), bear), (bottom_y(0.0), 0.0)]


def slope_box(p, a, b, width, thick, x, side, mat):
    """A flat bar lying along the segment a->b (in y, z). `side` picks which
    side of the line it sits on (+1 along the left-hand normal of a->b); it is
    sunk 5 mm into the line so it never shares a face with what it rests on."""
    dy, dz = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dy, dz)
    ang = math.atan2(dz, dy)                   # angle of +Y onto a->b about X
    ny, nz = -math.sin(ang), math.cos(ang)
    off = side * (thick / 2 - 0.005)
    c = ((a[0] + b[0]) / 2 + ny * off, (a[1] + b[1]) / 2 + nz * off)
    return p.box((x, c[0], c[1]), (width, length, thick), mat,
                 rot=Matrix.Rotation(ang, 4, 'X'))


def lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def bolt(p, pos, axis, r=0.024, h=0.026, mat=DEEP):
    """Square-headed bolt: four sides keep a row of them affordable."""
    return p.cyl(pos, r, h, axis, seg=4, mat=mat, radius_top=r * 0.65)


def build_grating(coll):
    """Channel stringers, bar treads. Open, so light and sand fall through."""
    p = Part(MATS)
    prof = stringer_profile()
    web = 0.015
    for s in (-1, 1):
        x_web = s * (0.72 + web / 2)
        p.prism(prof, web, 'X', STEEL, offset=(x_web, 0, 0))
        flange_x = s * 0.76
        # Flanges turn outward from the web, overlapping it by its thickness.
        # Each starts a little up its edge so its thickness stays above the
        # deck and behind the toe.
        slope_box(p, lerp(prof[1], prof[2], 0.01), prof[2], 0.08, 0.02, flange_x, -1, STEEL)
        slope_box(p, lerp(prof[6], prof[5], 0.02), prof[5], 0.08, 0.02, flange_x, 1, STEEL)
    half = 0.725                                # 5 mm into each web
    for k in range(1, STEPS + 1):
        z = RISER * k
        y_nose = FIRST - (k - 1) * GOING
        p.slab((-half, y_nose - 0.07, z - 0.06), (half, y_nose, z), PALE)
        for dy in (0.21, GOING - 0.035):
            p.slab((-half, y_nose - dy - 0.035, z - 0.06),
                   (half, y_nose - dy, z - 0.004), DARK)
    p.finish("Mesh_Stair_Grating", coll)


def build_plate(coll):
    """Treads and kick plates folded from one sheet, bolted along each kick."""
    p = Part(MATS)
    for s in (-1, 1):
        p.prism(stringer_profile(), 0.04, 'X', STEEL, offset=(s * 0.78, 0, 0))
    th = 0.025
    top, under = [(FIRST, 0.0)], [(FIRST - th, 0.0)]
    for k in range(1, STEPS + 1):
        y_nose = FIRST - (k - 1) * GOING
        z = RISER * k
        top += [(y_nose, z), (y_nose - GOING, z)]
        under += [(y_nose - th, z - th),
                  (y_nose - GOING - (th if k < STEPS else 0.0), z - th)]
    p.prism(top + list(reversed(under)), 1.53, 'X', HULL)
    for k in range(1, STEPS + 1):
        y_nose = FIRST - (k - 1) * GOING
        for x in (-0.45, 0.45):
            bolt(p, (x, y_nose + 0.004, RISER * k - RISER * 0.5), 'Y')
    p.finish("Mesh_Stair_Plate", coll)


def build_landing(coll):
    """1.8 x 1.8 grated landing, 0.15 deep, on four 2.1 m legs — the top of a
    flight. Origin at the centre of its walking surface."""
    p = Part(MATS)
    h, e, th = 0.90, 0.06, 0.15
    for s in (-1, 1):
        p.slab((s * h, -h, -th), (s * (h - e), h, 0.0), STEEL)
        # End members sit 3 mm low so their tops never share the side members' plane.
        p.slab((-h + e - 0.005, s * h, -th), (h - e + 0.005, s * (h - e), -0.003), STEEL)
    for k in range(8):
        y = -h + e + (2 * (h - e)) * (k + 0.5) / 8
        p.slab((-h + e - 0.005, y - 0.015, -0.11), (h - e + 0.005, y + 0.015, -0.004), DARK)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.slab((sx * 0.76, sy * 0.76, -RISE + 0.01), (sx * 0.88, sy * 0.88, -th + 0.01), DARK)
            p.slab((sx * 0.67, sy * 0.67, -RISE), (sx * 0.97, sy * 0.97, -RISE + 0.025), DEEP)
    p.finish("Mesh_Stair_Landing", coll)


def build_ladder(coll):
    """Steep ship's ladder: 3.0 m rise at 70 degrees, 0.9 wide, round rungs.
    The rails run on 0.9 m past the top as hand-holds for stepping off."""
    p = Part(MATS)
    rise, lean = 3.0, math.radians(70.0)
    cot = 1.0 / math.tan(lean)
    half_d = 0.08 / math.sin(lean)             # rail depth measured along Y
    top = rise + 0.9
    foot = 0.005                               # rails stand 5 mm into the foot plates
    rail = [(-foot * cot, foot), (-2 * half_d - foot * cot, foot),
            (-2 * half_d - top * cot, top), (-top * cot, top)]
    for s in (-1, 1):
        p.prism(rail, 0.05, 'X', STEEL, offset=(s * 0.425, 0, 0))
        p.slab((s * 0.35, 0.05, 0.0), (s * 0.48, -2 * half_d - 0.05, 0.02), DEEP)
        # Hook plate lying on the upper deck, 2 mm clear of its surface.
        p.slab((s * 0.405, -half_d - rise * cot, rise + 0.002),
               (s * 0.445, -half_d - rise * cot - 0.30, rise + 0.10), STEEL)
    for k in range(1, 8):
        z = 0.375 * k
        p.cyl((0, -half_d - z * cot, z), 0.035, 0.81, 'X', 6, PALE)
    p.finish("Mesh_Stair_Ladder", coll)


def build():
    out = parse_out()
    start(out)
    global MATS
    MATS = link_materials(MATS)

    build_grating(collection("Coll_Stair_Grating"))
    build_plate(collection("Coll_Stair_Plate"))
    build_landing(collection("Coll_Stair_Landing"))
    build_ladder(collection("Coll_Stair_Ladder"))

    report()
    save(out)


build()
