"""components/mechanical/conveyor_ramp — enclosed material handling galleries.

The long orange diagonal running off the base of an industrial tower to the
ground is doing something specific: it is a covered belt taking ore out of the
building. It is worth a component because it is the one element on a structure
like this that is allowed to be a strong diagonal, and a diagonal is what stops
a pile of orthogonal boxes reading as a pile of orthogonal boxes.

`Ramp` is authored **inclined at 23 degrees** rather than flat-and-rotated-later.
An inclined gallery has a horizontal floor at each end, vertical trestles under
a raked chord, and a roof that is not parallel to anything — none of which
survive being modelled flat and rotated, and all of which are the reason it
looks built. Every part is placed through `cpos`/`CROT` so the incline is stated
once.

Origins are at the low end, on the belt centreline, at belt level — so an
assembly puts the origin where the material lands and the gallery climbs away.

    blender --background --python conveyor_ramp.py -- --out conveyor_ramp.blend

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
    "Mat_Paint_Safety_Orange",   # 0 the gallery cladding — the diagonal itself
    "Mat_Metal_Steel_Worn",      # 1 truss, walkway, rollers
    "Mat_Metal_Steel_Dark",      # 2 frames, pulleys, drive machinery
    "Mat_Paint_White_Arctic",    # 3 the head house, tying it to the tower
    "Mat_Neutral_Black_Matte",   # 4 the belt, and openings
    "Mat_Metal_Rust_Heavy",      # 5 spillage staining and worn chutes
    "Mat_Plastic_Rubber_Black",  # 6 belt skirting and lagging
    "Mat_Emissive_Amber",        # 7 running lamps
    "Mat_Paint_Warn_Red",        # 8 hazard marking at the pinch points
]
ORANGE, STEEL, DARK, WHITE, BLACK, RUST, RUBBER, AMBER, RED = range(9)

ANG = math.radians(23.0)
CROT = Matrix.Rotation(-ANG, 4, 'Y')     # +X -> up the slope, +Z -> gallery up
GW, GH = 4.20, 3.40                      # gallery width and internal height
LEN = 26.0                               # slope length of the standard ramp


def cpos(s, t, u):
    """Conveyor-local (along-slope, lateral, perpendicular-up) -> world."""
    return CROT @ Vector((s, t, u))


def cbox(p, s, t, u, size, mat, flat=False):
    """A box in conveyor-local coordinates, aligned to the slope."""
    return p.box(cpos(s, t, u), size, mat, rot=None if flat else CROT)


# ---------------------------------------------------------------------------
# Gallery construction, shared by the inclined and flat runs
# ---------------------------------------------------------------------------

def gallery(p, length, s0=0.0, roof=True):
    """The clad tube: two side walls, a shallow gable roof, a floor pan."""
    sc = s0 + length / 2
    for st in (-1, 1):
        cbox(p, sc, st * GW / 2, GH / 2, (length, 0.16, GH), ORANGE)
    cbox(p, sc, 0, -0.55, (length, GW + 0.2, 0.20), DARK)          # floor pan
    if roof:
        for st in (-1, 1):
            cbox(p, sc, st * GW / 4, GH + 0.18,
                 (length, GW / 2 + 0.25, 0.16), ORANGE,)
        cbox(p, sc, 0, GH + 0.42, (length, 0.5, 0.30), DARK)       # ridge
    # Rib frames at 2 m — a 26 m clad tube needs a rhythm or it is a plank.
    n = max(2, int(length / 2.0))
    for i in range(n):
        s = s0 + length * (i + 0.5) / n
        for st in (-1, 1):
            cbox(p, s, st * (GW / 2 + 0.10), GH / 2, (0.30, 0.14, GH), DARK)
        cbox(p, s, 0, GH + 0.30, (0.30, GW + 0.3, 0.14), DARK)
    # Window slots high on one flank, where a gallery really has them.
    for i in range(max(1, int(length / 4.0))):
        s = s0 + 1.6 + i * 4.0
        if s > s0 + length - 1.2:
            break
        cbox(p, s, GW / 2 + 0.06, GH * 0.74, (2.2, 0.12, 0.70), BLACK)


def belt(p, length, s0=0.0):
    """Belt, idlers and the return strand — seen through the ends and slots."""
    sc = s0 + length / 2
    cbox(p, sc, 0, 0.10, (length, GW - 1.1, 0.10), RUBBER)
    cbox(p, sc, 0, -0.30, (length, GW - 1.4, 0.08), RUBBER)
    n = max(2, int(length / 1.5))
    for i in range(n):
        s = s0 + length * (i + 0.5) / n
        p.cyl(cpos(s, 0, 0.02), 0.16, GW - 1.2, 'Y', seg=6, mat=STEEL)
    for st in (-1, 1):                                   # skirt boards
        cbox(p, sc, st * (GW / 2 - 0.55), 0.32, (length, 0.10, 0.44), RUBBER)


def walkway(p, length, s0=0.0):
    """The inspection walk down one side, outside the cladding."""
    sc = s0 + length / 2
    cbox(p, sc, -(GW / 2 + 0.85), 0.0, (length, 1.5, 0.12), STEEL)
    n = max(2, int(length / 2.5))
    for i in range(n + 1):
        s = s0 + length * i / n
        cbox(p, s, -(GW / 2 + 1.55), 0.58, (0.09, 0.09, 1.10), STEEL)
        cbox(p, s, -(GW / 2 + 0.20), -0.35, (0.12, 1.5, 0.60), DARK)
    for u in (1.10, 0.56):
        cbox(p, sc, -(GW / 2 + 1.55), u, (length, 0.07, 0.07), STEEL)
    cbox(p, sc, -(GW / 2 + 1.52), 0.20, (length, 0.10, 0.28), RED)


def under_truss(p, length, s0=0.0, depth=1.70):
    """The chord and web the gallery rides on."""
    sc = s0 + length / 2
    for st in (-1, 1):
        cbox(p, sc, st * (GW / 2 - 0.15), -depth, (length, 0.22, 0.24), STEEL)
    n = max(2, int(length / 2.2))
    for i in range(n):
        s = s0 + length * (i + 0.5) / n
        for st in (-1, 1):
            d = math.hypot(length / n, depth)
            p.box(cpos(s, st * (GW / 2 - 0.15), -depth / 2 + 0.1),
                  (0.16, 0.16, d * 1.05), DARK,
                  rot=CROT @ Matrix.Rotation(
                      math.radians(40 if i % 2 else -40), 4, 'Y'))
        cbox(p, s, 0, -depth, (0.14, GW - 0.3, 0.14), DARK)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def ramp(coll, mats):
    """The hero: a 26 m gallery climbing 10.2 m at 23 degrees, on two trestles.

    23 degrees is the practical limit for a smooth belt before material rolls
    back, so the angle is not arbitrary and it is steep enough to read clearly
    against a vertical tower.
    """
    p = Part(mats)
    gallery(p, LEN)
    belt(p, LEN)
    walkway(p, LEN)
    under_truss(p, LEN)
    # Two trestles down to ground, vertical rather than raked with the gallery.
    for s in (LEN * 0.34, LEN * 0.72):
        top = cpos(s, 0, -1.70)
        for st in (-1, 1):
            for sy in (-1, 1):
                x = top.x + sy * 1.5
                y = st * (GW / 2 - 0.15)
                p.box((x, y, top.z / 2), (0.30, 0.30, top.z), STEEL)
            p.box((top.x, st * (GW / 2 - 0.15), top.z * 0.5),
                  (3.4, 0.18, 0.22), DARK)
            p.box((top.x, st * (GW / 2 - 0.15), top.z * 0.5),
                  (math.hypot(3.0, top.z), 0.16, 0.16), DARK,
                  rot=Matrix.Rotation(math.atan2(top.z, 3.0), 4, 'Y'))
        p.box((top.x, 0, 0.30), (4.4, GW + 0.6, 0.60), DARK)
        p.box((top.x, 0, 0.08), (5.0, GW + 1.2, 0.22), RUST)
    # Bottom end: an open mouth with a spillage apron, so it goes somewhere.
    cbox(p, -0.25, 0, GH / 2 - 0.3, (0.5, GW - 0.3, GH - 0.6), BLACK)
    p.box((cpos(0, 0, -1.9).x - 1.2, 0, 0.55), (4.0, GW + 1.0, 1.1), DARK)
    p.box((cpos(0, 0, -1.9).x - 1.2, 0, 0.10), (5.2, GW + 2.2, 0.24), RUST)
    for st in (-1, 1):
        p.cyl(cpos(1.2, st * (GW / 2 + 0.3), GH * 0.9), 0.22, 0.26, 'Y', seg=8,
              mat=AMBER)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Conveyor_Ramp", coll)


def flat(coll, mats):
    """A 14 m horizontal gallery — the same tube with the slope taken out.

    Built as its own variation rather than as `Ramp` rotated flat, because a
    level run has level trestles and a different roof drainage, and because an
    assembly should not have to know the incline to place a horizontal run.
    """
    global CROT
    saved = CROT
    CROT = Matrix.Identity(4)
    try:
        p = Part(mats)
        length = 14.0
        gallery(p, length)
        belt(p, length)
        walkway(p, length)
        under_truss(p, length, depth=1.40)
        for s in (2.6, length - 2.6):
            for st in (-1, 1):
                p.box((s, st * (GW / 2 - 0.15), -3.6), (0.34, 0.34, 4.4), STEEL)
            p.box((s, 0, -5.6), (1.4, GW + 0.6, 0.5), DARK)
        for st in (-1, 1):
            p.box((length / 2, st * (GW / 2 + 0.12), GH + 0.6),
                  (length, 0.14, 0.34), RED)
        p.bevel(width=0.03, segments=1)
        return p.finish("Mesh_Conveyor_Flat", coll)
    finally:
        CROT = saved


def head(coll, mats):
    """The drive house at the top: head pulley, motor, discharge chute.

    White-clad rather than orange, so the diagonal visibly terminates in a
    building rather than just stopping. Origin at the gallery's belt line where
    it enters, so it docks straight onto the end of a `Ramp`.
    """
    p = Part(mats)
    p.box((2.6, 0, 2.2), (7.0, GW + 1.6, 6.6), WHITE)
    p.box((2.6, 0, 5.7), (7.4, GW + 2.0, 0.5), DARK)
    for st in (-1, 1):
        for i in range(3):
            p.box((0.4 + i * 2.2, st * (GW / 2 + 0.85), 2.2),
                  (0.30, 0.14, 6.2), WHITE)
        p.box((3.6, st * (GW / 2 + 0.9), 4.4), (3.0, 0.12, 1.2), BLACK)
    p.box((-1.0, 0, 0.0), (2.4, GW, GH), BLACK)              # gallery opening
    # Head pulley and drive.
    p.cyl((1.4, 0, 0.15), 0.92, GW - 1.0, 'Y', seg=14, mat=RUBBER)
    p.cyl((1.4, 0, 0.15), 0.62, GW + 0.6, 'Y', seg=10, mat=STEEL)
    for st in (-1, 1):
        p.box((1.4, st * (GW / 2 + 0.35), 0.15), (1.6, 0.5, 1.6), DARK)
    p.box((4.6, 1.9, 0.55), (2.2, 1.5, 1.5), DARK)           # motor
    p.cyl((3.2, 1.9, 0.55), 0.34, 1.4, 'X', seg=8, mat=STEEL)
    p.box((4.6, -1.9, 0.75), (1.6, 1.2, 1.9), STEEL)         # gearbox
    # Discharge chute out of the bottom.
    p.loft([(-1.6, [(0.4, -1.5), (2.6, -1.5), (2.6, 1.5), (0.4, 1.5)]),
            (-3.9, [(0.9, -1.0), (2.2, -1.0), (2.2, 1.0), (0.9, 1.0)])],
           axis='Z', mat=DARK)
    p.box((1.55, 0, -4.15), (2.0, 2.4, 0.4), RUST)
    p.box((2.6, 0, 6.15), (3.0, 2.4, 0.6), STEEL)            # roof hoist beam
    p.cyl((2.6, 0, 5.85), 0.18, 2.6, 'Y', seg=6, mat=DARK)
    for st in (-1, 1):
        p.cyl((-0.6, st * (GW / 2 + 0.9), 3.4), 0.24, 0.28, 'Y', seg=8,
              mat=AMBER)
    p.box((2.6, 0, -0.7), (7.6, GW + 2.2, 0.7), DARK)        # support plinth
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Conveyor_Head", coll)


def hopper(coll, mats):
    """A receiving bin: grizzly grid on top, tapered box, gate at the bottom.

    Where a hauler tips its load. Ground-level and player-adjacent, so it gets
    more detail per square metre than the gallery does.
    """
    p = Part(mats)
    p.loft([(4.8, [(-3.6, -3.2), (3.6, -3.2), (3.6, 3.2), (-3.6, 3.2)]),
            (1.6, [(-1.3, -1.1), (1.3, -1.1), (1.3, 1.1), (-1.3, 1.1)])],
           axis='Z', mat=ORANGE)
    p.box((0, 0, 5.0), (7.6, 6.8, 0.4), DARK)                # rim
    for i in range(7):                                        # grizzly bars
        x = -3.0 + i * 1.0
        p.box((x, 0, 5.2), (0.18, 6.4, 0.26), STEEL)
    for i in range(4):
        y = -2.4 + i * 1.6
        p.box((0, y, 5.05), (7.0, 0.16, 0.20), STEEL)
    p.box((0, 0, 1.1), (3.2, 2.8, 0.9), DARK)                # gate frame
    p.box((0, -1.5, 1.1), (2.6, 0.3, 1.4), STEEL)            # slide gate
    p.cyl((0, -2.2, 1.1), 0.16, 1.4, 'Y', seg=6, mat=STEEL)
    for sx in (-1, 1):                                        # legs
        for sy in (-1, 1):
            p.box((sx * 2.9, sy * 2.6, 2.4), (0.42, 0.42, 4.8), STEEL)
            p.box((sx * 2.9, sy * 2.6, 0.20), (1.1, 1.1, 0.40), RUST)
        p.box((sx * 2.9, 0, 3.4), (0.30, 5.2, 0.24), DARK)
    for sy in (-1, 1):                                        # tip kerbs
        p.box((0, sy * 3.9, 5.3), (7.8, 0.5, 0.9), RED)
    p.box((0, 0, 4.86), (6.6, 5.8, 0.10), RUST)               # spillage
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Conveyor_Hopper", coll)


def trestle(coll, mats):
    """A standalone 9 m support bent. Origin at the top bearing.

    Split out because galleries need them at whatever spacing the ground
    dictates, and because everything else raised on this site — pipe racks,
    cable trays — wants the same bent.
    """
    p = Part(mats)
    h = 9.0
    for st in (-1, 1):
        for sy in (-1, 1):
            p.box((sy * 1.6, st * (GW / 2 - 0.15), -h / 2),
                  (0.34, 0.34, h), STEEL,
                  rot=Matrix.Rotation(math.radians(-sy * 5), 4, 'Y'))
        for k in range(3):
            z = -1.9 - k * 2.6
            p.box((0, st * (GW / 2 - 0.15), z), (3.4, 0.20, 0.22), DARK)
            p.box((0, st * (GW / 2 - 0.15), z - 1.3),
                  (math.hypot(3.2, 2.6), 0.16, 0.16), DARK,
                  rot=Matrix.Rotation(math.radians(39 if k % 2 else -39), 4, 'Y'))
    for sy in (-1, 1):
        p.box((sy * 1.6, 0, -h + 0.45), (1.5, GW + 0.5, 0.9), DARK)
        p.box((sy * 1.6, 0, -h + 0.10), (2.1, GW + 1.1, 0.24), RUST)
    p.box((0, 0, -0.25), (4.2, GW + 0.7, 0.5), DARK)
    p.box((0, 0, -1.1), (0.9, GW - 0.4, 1.4), STEEL)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Conveyor_Trestle", coll)


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Ramp", ramp), ("Flat", flat), ("Head", head),
                     ("Hopper", hopper), ("Trestle", trestle)):
        fn(collection("Coll_Conveyor_%s" % name), mats)
    report()
    save(out)


build()
