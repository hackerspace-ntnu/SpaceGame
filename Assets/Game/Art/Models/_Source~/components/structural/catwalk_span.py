"""components/structural/catwalk_span — exterior walkways for large structures.

The library already has `handrail`, and it is the better component *up close*:
692 triangles for a 2.24 m bay — 309 per metre — buys real stanchion detail on
a vehicle deck the player stands on. A 75 m refinery wants something like 200
running metres of walkway railed both sides, which at that rate is 120 000
triangles of railing and nothing else.

So this is the same idea at building scale: deck, stringers, brackets and rail
as one object per span. The railing itself runs about 72 triangles per metre,
roughly a quarter of `handrail`'s, and the deck and brackets come with it — a
finished 6 m railed span costs 1 628 triangles all in. Use `handrail` where the
player's hand could touch it; use this everywhere above the second storey. The
two are dimensionally compatible — 1.10 m rail height, same steel.

Authored with the origin at the start end, on the centreline, at *walking
surface* height — so an assembly places a catwalk by putting its origin at the
floor level it wants to walk on, and the structure hangs below by itself.

Runs along +X. Standard span is 6.00 m long x 1.80 m wide; `Balcony`, `Bridge`
and `Stair` break that on purpose.

    blender --background --python catwalk_span.py -- --out catwalk_span.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

from mathutils import Matrix  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",      # 0 stringers, posts, rails — the bulk
    "Mat_Metal_Steel_Dark",      # 1 brackets, bolts, cleats
    "Mat_Paint_Safety_Orange",   # 2 toe plates and hazard edges
    "Mat_Neutral_Black_Matte",   # 3 the gap seen through grating
    "Mat_Metal_Rust_Heavy",      # 4 weathering at the fixings
    "Mat_Paint_Warn_Red",        # 5 end-of-run markings
]
STEEL, DARK, ORANGE, BLACK, RUST, RED = range(6)

L, WD = 6.00, 1.80                   # standard span length, deck width
DECK = 0.10                          # deck plate thickness
RAIL_H = 1.10                        # matches components/structural/handrail
POST = 1.50                          # stanchion spacing


# ---------------------------------------------------------------------------
# Shared walkway language
# ---------------------------------------------------------------------------

def deck(p, length=L, width=WD, x0=0.0, y0=0.0):
    """Walking surface plus the two edge stringers that carry it.

    The deck reads as grating from below, which is where it is mostly seen on a
    tower, so the ribs matter more than the top face does.
    """
    p.slab((x0, y0 - width / 2, -DECK), (x0 + length, y0 + width / 2, 0.0), STEEL)
    for sy in (-1, 1):
        p.box((x0 + length / 2, y0 + sy * (width / 2 - 0.06), -0.24),
              (length, 0.12, 0.40), STEEL)
    # Cross ribs on the underside — the grating read, and the only detail on a
    # catwalk that is genuinely visible from the ground.
    n = max(2, int(length / 0.75))
    for i in range(n):
        x = x0 + length * (i + 0.5) / n
        p.box((x, y0, -0.16), (0.10, width - 0.16, 0.14), DARK)


def toe(p, length=L, width=WD, x0=0.0, y0=0.0, sides=(-1, 1)):
    """Kick plates. Orange because on a real structure they are, and because a
    long thin colour line is what makes a walkway legible against white."""
    for sy in sides:
        p.box((x0 + length / 2, y0 + sy * (width / 2 - 0.03), 0.09),
              (length, 0.10, 0.30), ORANGE)


def rail(p, length=L, y=0.0, x0=0.0, spacing=POST, end_posts=True):
    """One line of railing: stanchions, top rail, mid rail.

    Two boxes per post rather than a modelled stanchion. At the distance a
    walkway 40 m up is read, the silhouette of the gap is the whole signal.
    """
    n = max(2, int(round(length / spacing)) + 1)
    for i in range(n):
        if not end_posts and i in (0, n - 1):
            continue
        x = x0 + length * i / (n - 1)
        p.box((x, y, RAIL_H / 2), (0.09, 0.09, RAIL_H), STEEL)
        p.box((x, y, 0.02), (0.20, 0.20, 0.10), DARK)          # base cleat
    for z in (RAIL_H, RAIL_H * 0.52):
        p.box((x0 + length / 2, y, z), (length, 0.07, 0.07), STEEL)


def brackets(p, length=L, width=WD, x0=0.0, y_wall=None, count=3, drop=1.5):
    """Raking struts from the walkway's outboard stringer back to a wall.

    `y_wall` is the face the walkway is bolted to. Without these a cantilevered
    catwalk reads as floating, which is the single most common tell of a
    building modelled as boxes.
    """
    y_wall = (width / 2) if y_wall is None else y_wall
    y_out = -width / 2
    span = abs(y_wall - y_out)
    for i in range(count):
        x = x0 + length * (i + 0.5) / count
        p.box((x, (y_wall + y_out) / 2, -0.22 - drop / 2),
              (0.16, 0.20, math.hypot(span, drop)), DARK,
              rot=Matrix.Rotation(math.atan2(span, drop), 4, 'X'))
        p.box((x, y_wall - 0.12, -0.22 - drop / 2), (0.24, 0.30, drop), DARK)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def straight(coll, mats):
    """Free-standing 6 m run, railed both sides. The default tile."""
    p = Part(mats)
    deck(p)
    toe(p)
    for sy in (-1, 1):
        rail(p, y=sy * (WD / 2 - 0.10))
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Catwalk_Straight", coll)


def wall(coll, mats):
    """6 m run bolted to a wall on +Y: rail outboard only, struts inboard.

    The workhorse for wrapping a tower, because three sides of a slab want a
    walkway with nothing on the building side.
    """
    p = Part(mats)
    deck(p)
    toe(p, sides=(-1,))
    rail(p, y=-(WD / 2 - 0.10))
    brackets(p)
    # Wall cleats — where the stringer actually bolts on.
    for i in range(3):
        p.box((L * (i + 0.5) / 3, WD / 2 - 0.05, -0.12), (0.5, 0.22, 0.5), DARK)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Catwalk_Wall", coll)


def balcony(coll, mats):
    """A 6.0 x 3.6 m working platform hung off a wall on +Y.

    Wider than a walkway because things happen on it: a valve stand, a hose
    reel, two people passing. Rail on three sides, heavy raking struts.
    """
    bw = 3.60
    p = Part(mats)
    deck(p, width=bw, y0=-(bw - WD) / 2)
    y_out = -(bw - WD) / 2 - bw / 2
    y_in = -(bw - WD) / 2 + bw / 2
    toe(p, width=bw, y0=-(bw - WD) / 2, sides=(-1,))
    rail(p, y=y_out + 0.10)
    for x in (0.09, L - 0.09):                       # the two short returns
        n = 3
        for i in range(n):
            y = y_out + 0.10 + (y_in - y_out - 0.2) * i / (n - 1)
            p.box((x, y, RAIL_H / 2), (0.09, 0.09, RAIL_H), STEEL)
        p.box((x, (y_out + y_in) / 2, RAIL_H), (0.07, bw - 0.2, 0.07), STEEL)
        p.box((x, (y_out + y_in) / 2, RAIL_H * 0.52), (0.07, bw - 0.2, 0.07), STEEL)
    brackets(p, width=bw, y_wall=y_in, count=4, drop=2.6)
    # A valve stand, so the platform has a reason to be this wide.
    p.box((1.5, y_out + 1.2, 0.55), (0.7, 0.7, 1.1), DARK)
    p.cyl((1.5, y_out + 1.2, 1.28), 0.34, 0.16, 'Z', seg=10, mat=RED)
    p.box((4.4, y_out + 1.0, 0.45), (1.2, 0.9, 0.9), STEEL)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Catwalk_Balcony", coll)


def corner(coll, mats):
    """An L junction: a 1.8 m square landing with a stub each way.

    Wrapping a rectangular tower needs this or every corner shows a gap. The
    stubs overlap the neighbouring straight runs so the joint is never a seam.
    """
    s = WD
    p = Part(mats)
    deck(p, length=s + 1.2, width=s)                       # the +X stub
    deck(p, length=s + 1.2, width=s, x0=-0.0, y0=0.0)
    # The +Y leg, built as a second deck rotated into place by construction.
    p.slab((-s / 2, s / 2, -DECK), (s / 2, s / 2 + 1.2, 0.0), STEEL)
    for sx in (-1, 1):
        p.box((sx * (s / 2 - 0.06), s / 2 + 0.6, -0.24), (0.12, 1.2, 0.40), STEEL)
    toe(p, length=s + 1.2, width=s, sides=(-1,))
    p.box((-s / 2 + 0.03, s / 2 + 0.6, 0.09), (0.10, 1.2, 0.30), ORANGE)
    rail(p, length=s + 1.2, y=-(s / 2 - 0.10))
    for i in range(2):                                     # rail up the +Y leg
        y = s / 2 + 0.2 + i * 0.9
        p.box((-(s / 2 - 0.10), y, RAIL_H / 2), (0.09, 0.09, RAIL_H), STEEL)
    for z in (RAIL_H, RAIL_H * 0.52):
        p.box((-(s / 2 - 0.10), s / 2 + 0.6, z), (0.07, 1.2, 0.07), STEEL)
    p.box((-(s / 2 - 0.10), -(s / 2 - 0.10), RAIL_H), (0.07, s - 0.2, 0.07), STEEL)
    # Corner gusset under the knuckle.
    p.box((0, 0, -0.45), (s - 0.3, s - 0.3, 0.5), DARK)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Catwalk_Corner", coll)


def bridge(coll, mats):
    """A 12 m clear span with an underslung Warren truss.

    Links the tower to the outrigger deck. The truss is the point: at that
    length a flat walkway looks like a plank and the whole structure loses
    weight.
    """
    span = 12.0
    p = Part(mats)
    deck(p, length=span)
    toe(p, length=span)
    for sy in (-1, 1):
        rail(p, length=span, y=sy * (WD / 2 - 0.10))
    # Bottom chords and the zigzag web between them.
    depth = 1.60
    for sy in (-1, 1):
        y = sy * (WD / 2 - 0.10)
        p.box((span / 2, y, -depth), (span, 0.16, 0.20), STEEL)
        n = 8
        for i in range(n):
            x = span * (i + 0.5) / n
            p.box((x, y, -depth / 2 - 0.1),
                  (0.14, 0.14, math.hypot(span / n, depth) * 1.02), DARK,
                  rot=Matrix.Rotation(
                      math.radians(38 if i % 2 else -38), 4, 'Y'))
    for i in range(4):                                    # cross bracing
        x = span * (i + 0.5) / 4
        p.box((x, 0, -depth), (0.12, WD - 0.2, 0.12), DARK)
    p.box((0.3, 0, -0.55), (0.6, WD, 1.1), DARK)          # end bearings
    p.box((span - 0.3, 0, -0.55), (0.6, WD, 1.1), DARK)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Catwalk_Bridge", coll)


def stair(coll, mats):
    """A flight rising 4.50 m over a 4.50 m run, with a landing at the top.

    45 degrees — steeper than a building stair, which is correct for plant
    access and reads instantly as industrial. The treads are individual boxes
    because a ramp with a rail on it fools nobody.
    """
    rise, run, steps = 4.50, 4.50, 15
    p = Part(mats)
    for i in range(steps):
        x = run * (i + 0.5) / steps
        z = -rise + rise * (i + 0.5) / steps
        p.box((x, 0, z), (run / steps * 0.92, WD - 0.10, 0.07), STEEL)
    # Stringers, as one raked box each side.
    for sy in (-1, 1):
        p.box((run / 2, sy * (WD / 2 - 0.05), -rise / 2 - 0.16),
              (math.hypot(run, rise) * 1.02, 0.12, 0.42), STEEL,
              rot=Matrix.Rotation(math.radians(45), 4, 'Y'))
        # Raked rail: posts stepped up the flight, plus a sloped top rail.
        y = sy * (WD / 2 - 0.10)
        for i in range(4):
            t = i / 3.0
            p.box((run * t, y, -rise + rise * t + RAIL_H / 2 + 0.1),
                  (0.09, 0.09, RAIL_H), STEEL)
        p.box((run / 2, y, -rise / 2 + RAIL_H + 0.1),
              (math.hypot(run, rise), 0.07, 0.07), STEEL,
              rot=Matrix.Rotation(math.radians(45), 4, 'Y'))
    # Top landing, at the origin's level so the flight docks onto a walkway.
    deck(p, length=1.6, width=WD, x0=run - 0.1)
    toe(p, length=1.6, width=WD, x0=run - 0.1)
    for sy in (-1, 1):
        rail(p, length=1.6, x0=run - 0.1, y=sy * (WD / 2 - 0.10))
    p.box((0.2, 0, -rise - 0.35), (0.8, WD, 0.7), DARK)     # bottom bearing
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Catwalk_Stair", coll)


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Straight", straight), ("Wall", wall),
                     ("Balcony", balcony), ("Corner", corner),
                     ("Bridge", bridge), ("Stair", stair)):
        fn(collection("Coll_Catwalk_%s" % name), mats)
    report()
    save(out)


build()
