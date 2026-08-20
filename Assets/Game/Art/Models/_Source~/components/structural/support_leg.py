"""components/structural/support_leg — the raked legs a tower stands on.

`walker_leg` in the mechanical folder is a machine's leg: it articulates, it has
a gait, its proportions come from stride. This is the opposite kind of leg — it
never moves, it is a load path, and its proportions come from the fact that it
has to look capable of holding 75 m of refinery off the ground. Clad boxes with
bolted joints, not a linkage.

Authored with the origin at the *top attachment* — the point where the leg bolts
to the structure it carries — because that is the fixed end. The leg descends
from there and its foot lands wherever the geometry puts it, which is exactly
how an assembly wants to think about it: pick the point on the tower, get a leg.

The hero variation `Raked` drops 19.6 m and reaches 13.8 m forward along -Y.

    blender --background --python support_leg.py -- --out support_leg.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Paint_Safety_Orange",   # 0 the leg's cladding — its whole identity
    "Mat_Metal_Steel_Dark",      # 1 joints, collars, bolt flanges
    "Mat_Metal_Steel_Worn",      # 2 bare structure, tie rods, pad steel
    "Mat_Paint_White_Arctic",    # 3 the odd panel that ties back to the tower
    "Mat_Neutral_Black_Matte",   # 4 shadow gaps and recesses
    "Mat_Metal_Rust_Heavy",      # 5 weathering where the foot meets the ground
    "Mat_Paint_Warn_Red",        # 6 hazard banding on the lower leg
]
ORANGE, DARK, STEEL, WHITE, BLACK, RUST, RED = range(7)


# ---------------------------------------------------------------------------
# Building a leg as a chain of clad segments
# ---------------------------------------------------------------------------

def along(a, b):
    """Rotation taking local +Z onto the direction a->b, with the length."""
    d = Vector(b) - Vector(a)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    return rot.to_4x4(), d.length


def segment(p, a, b, w, t, mat=ORANGE, overlap=0.0):
    """One clad section of leg running from a to b.

    `overlap` lengthens the box past both nodes so consecutive segments
    interpenetrate at a kink instead of leaving a wedge of daylight — the
    cheapest way to get a mitred joint without solving the mitre.
    """
    rot, length = along(a, b)
    mid = (Vector(a) + Vector(b)) / 2.0
    return p.box(mid, (w, t, length + overlap), mat, rot=rot)


def collar(p, node, w, t, mat=DARK, thickness=0.55, axis_from=None):
    """A bolted flange at a kink. Reads as 'fabricated in sections'."""
    if axis_from is None:
        p.box(node, (w, t, thickness), mat)
        return
    rot, _ = along(axis_from, node)
    p.box(node, (w, t, thickness), mat, rot=rot)


def ribs(p, a, b, w, t, count=5, mat=DARK, depth=0.16):
    """Transverse strengthening ribs banding a segment.

    A 20 m orange box with nothing on it has no scale. These give it one, and
    they cost six faces each.
    """
    a, b = Vector(a), Vector(b)
    rot, _ = along(a, b)
    for i in range(count):
        node = a.lerp(b, (i + 0.5) / count)
        p.box(node, (w + depth, t + depth, 0.30), mat, rot=rot)


def pad(p, node, size=5.4, thickness=1.10, bolts=8):
    """The footing: a spread pad, a tapered plinth, and its anchor bolts."""
    node = Vector(node)
    p.box(node + Vector((0, 0, -thickness / 2)), (size, size * 0.92, thickness),
          STEEL)
    p.box(node + Vector((0, 0, -thickness - 0.22)),
          (size + 0.7, size * 0.92 + 0.7, 0.44), DARK)
    for i in range(bolts):
        a = 2 * math.pi * i / bolts
        p.cyl(node + Vector((math.cos(a) * size * 0.36,
                             math.sin(a) * size * 0.34, 0.18)),
              0.16, 0.42, 'Z', seg=6, mat=DARK)
    # Corrosion where it sits in the snow and salt.
    p.box(node + Vector((0, 0, -thickness - 0.40)),
          (size + 0.75, size * 0.92 + 0.75, 0.16), RUST)


def hazard(p, a, b, w, t, bands=2, mat=RED):
    """Painted danger banding on the part of the leg people walk past."""
    a, b = Vector(a), Vector(b)
    rot, _ = along(a, b)
    for i in range(bands):
        node = a.lerp(b, 0.25 + 0.45 * i)
        p.box(node, (w + 0.04, t + 0.04, 0.55), mat, rot=rot)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

RAKED_PATH = [(0.0, 0.0), (-2.0, -5.0), (-6.2, -11.2), (-11.0, -16.6),
              (-13.6, -19.4)]
RAKED_SECT = [(3.60, 3.20), (3.80, 3.40), (3.30, 2.90), (2.70, 2.40),
              (2.40, 2.20)]


def raked(coll, mats):
    """The hero leg: a clad orange member kinked at the knee, on a spread pad.

    Modelled with a genuine kink rather than a straight rake because a straight
    diagonal reads as a prop holding something up, and a kinked one reads as a
    leg taking weight. That is the entire difference between the reference image
    and a box on a stick.
    """
    p = Part(mats)
    pts = [Vector((0.0, y, z)) for y, z in RAKED_PATH]
    for i in range(len(pts) - 1):
        w0, t0 = RAKED_SECT[i]
        w1, t1 = RAKED_SECT[i + 1]
        segment(p, pts[i], pts[i + 1], (w0 + w1) / 2, (t0 + t1) / 2,
                ORANGE, overlap=0.5)
        ribs(p, pts[i], pts[i + 1], (w0 + w1) / 2, (t0 + t1) / 2,
             count=3 if i else 2)
    for i, node in enumerate(pts[1:-1], start=1):
        w, t = RAKED_SECT[i]
        collar(p, node, w + 0.34, t + 0.34, axis_from=pts[i - 1])

    # Top attachment: a bolted shoe standing off the tower face.
    p.box((0, 0.55, -0.4), (4.6, 1.5, 2.6), DARK)
    p.rivets((-1.9, 1.15, 0.4), (1.9, 1.15, 0.4), 7, radius=0.14,
             height=0.14, axis='Y', mat=STEEL)
    p.box((0, 0.2, -2.4), (4.2, 2.4, 0.5), WHITE)

    # A tie rod from the knee back up to the attachment — the member that
    # actually stops the leg folding, and the one detail that makes the
    # structure legible as engineering.
    knee = pts[2]
    top = Vector((0, -0.2, -1.4))
    for sx in (-1, 1):
        a = top + Vector((sx * 1.5, 0, 0))
        b = knee + Vector((sx * 1.5, 1.1, 0.6))
        rot, length = along(a, b)
        p.cyl((a + b) / 2, 0.24, length, 'Z', seg=8, mat=STEEL, rot=rot)
        p.cyl(b, 0.42, 0.5, 'Z', seg=8, mat=DARK, rot=rot)

    hazard(p, pts[3], pts[4], *RAKED_SECT[4])
    pad(p, pts[4] + Vector((0, 0, 0.05)))
    # A service ladder up the inside face of the lower leg.
    for i in range(11):
        z = pts[4].z + 1.4 + i * 1.35
        t = (z - pts[4].z) / (pts[2].z - pts[4].z)
        y = pts[4].y + (pts[2].y - pts[4].y) * t
        p.box((0, y + 1.5, z), (1.0, 0.10, 0.10), STEEL)
    p.bevel(width=0.05, segments=1)
    return p.finish("Mesh_SupportLeg_Raked", coll)


def splayed(coll, mats):
    """An A-frame: two slimmer members from one shoe, splaying in X as well.

    Where `Raked` is one heavy leg, this braces against sideways load, so it
    suits a corner of the structure rather than the middle of a face. Distinct
    in silhouette, which is the whole reason it exists rather than being
    `Raked` at 80% scale.
    """
    p = Part(mats)
    p.box((0, 0.4, -0.6), (5.4, 1.6, 2.8), DARK)
    for sx in (-1, 1):
        pts = [Vector((sx * 1.4, 0.0, -1.4)),
               Vector((sx * 2.6, -3.2, -7.0)),
               Vector((sx * 4.3, -7.4, -13.2)),
               Vector((sx * 5.2, -9.4, -16.0))]
        widths = [(2.4, 2.2), (2.2, 2.0), (1.9, 1.8), (1.7, 1.6)]
        for i in range(len(pts) - 1):
            w = (widths[i][0] + widths[i + 1][0]) / 2
            t = (widths[i][1] + widths[i + 1][1]) / 2
            segment(p, pts[i], pts[i + 1], w, t, ORANGE, overlap=0.4)
            ribs(p, pts[i], pts[i + 1], w, t, count=2)
        for i, node in enumerate(pts[1:-1], start=1):
            collar(p, node, widths[i][0] + 0.3, widths[i][1] + 0.3,
                   axis_from=pts[i - 1])
        hazard(p, pts[2], pts[3], *widths[3])
        pad(p, pts[3] + Vector((0, 0, 0.05)), size=3.9, thickness=0.85, bolts=6)
    # Cross bracing between the two members — what makes it an A and not two
    # legs that happen to share a shoe.
    for zt, yt, half in ((-7.0, -3.2, 2.6), (-12.0, -6.4, 4.0)):
        p.box((0, yt, zt), (half * 2, 0.7, 0.55), STEEL)
        for sx in (-1, 1):
            p.box((sx * half * 0.5, yt, zt - 1.6), (half, 0.5, 0.4), DARK,
                  rot=Matrix.Rotation(math.radians(sx * 24), 4, 'Y'))
    p.bevel(width=0.05, segments=1)
    return p.finish("Mesh_SupportLeg_Splayed", coll)


def pier(coll, mats):
    """A plain vertical clad pier, 14 m, for holding a deck up off flat ground.

    The unglamorous one, and the one that gets used most: the outrigger deck
    stands on four of these. Origin at the top so it hangs from the deck it
    carries, like every other variation here.
    """
    p = Part(mats)
    h = 14.0
    p.box((0, 0, -h / 2), (2.8, 2.8, h), ORANGE)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * 1.3, sy * 1.3, -h / 2), (0.4, 0.4, h), DARK)
    for i in range(6):
        p.box((0, 0, -1.4 - i * 2.2), (3.1, 3.1, 0.30), DARK)
    p.box((0, 0, -0.35), (3.9, 3.9, 0.70), DARK)          # head casting
    p.box((0, 0, -h + 0.9), (3.4, 3.4, 1.0), STEEL)       # base transition
    hazard(p, (0, 0, -h + 2.0), (0, 0, -h + 5.0), 2.85, 2.85, bands=2)
    pad(p, (0, 0, -h + 0.45), size=4.6, thickness=0.9, bolts=8)
    p.bevel(width=0.05, segments=1)
    return p.finish("Mesh_SupportLeg_Pier", coll)


def strut(coll, mats):
    """A slim 12 m tie-back with clevis ends. Bracing, not support.

    Kept separate from the legs because it is used in tens rather than fours,
    and because a tie in tension should look like a rod, not a column.
    """
    p = Part(mats)
    a, b = Vector((0, 0, 0)), Vector((0, -6.0, -10.4))
    rot, length = along(a, b)
    p.cyl((a + b) / 2, 0.42, length - 1.0, 'Z', seg=10, mat=ORANGE, rot=rot)
    for i in range(4):
        p.box(a.lerp(b, 0.2 + 0.2 * i), (0.95, 0.95, 0.26), DARK, rot=rot)
    for node in (a, b):
        p.box(node, (1.0, 0.44, 1.0), DARK, rot=rot)
        p.cyl(node, 0.22, 1.3, 'X', seg=8, mat=STEEL)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_SupportLeg_Strut", coll)


def footing(coll, mats):
    """The pad on its own — anchor block, bolts, and a rusted skirt.

    Split out because everything that meets the ground on this site wants one:
    piers, legs, conveyor trestles, the derrick guys. Origin at ground level.
    """
    p = Part(mats)
    pad(p, (0, 0, 0), size=5.4, thickness=1.10, bolts=10)
    p.box((0, 0, 0.55), (3.0, 2.8, 1.1), DARK)
    p.box((0, 0, 1.18), (2.4, 2.2, 0.24), STEEL)
    for sx in (-1, 1):                          # kerb guards against vehicles
        p.box((sx * 3.3, 0, 0.35), (0.4, 4.4, 0.7), ORANGE)
    p.bevel(width=0.04, segments=1)
    return p.finish("Mesh_SupportLeg_Footing", coll)


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Raked", raked), ("Splayed", splayed), ("Pier", pier),
                     ("Strut", strut), ("Footing", footing)):
        fn(collection("Coll_SupportLeg_%s" % name), mats)
    report()
    save(out)


build()
