"""components/structural/facade_awning — cloth on poles, hung off a building.

`awning_shade` next door already covers free-standing tarpaulins pitched over a
work area, and it is the right component for anything spanning 4 to 6 m. This
one exists because those are outpost-scale: hang a 5.5 m lean-to off a 3.4 m
`cottage_shell` and the cloth is wider than the house. Everything here is sized
to a cottage facade — 1.8 to 3.4 m of wall, projecting 1.3 to 2.6 m — and every
variation fixes to the wall rather than standing on its own.

Reuse `awning_shade` for the big spans over the yard. Reach for this one when
the cloth belongs to a particular building.

**Origin convention: the mounting face is the plane x = 0 and the awning
projects into +X**, matching `shanty_addon` so the two are interchangeable at
any mounting point. **Width runs along Y; z = 0 is ground level.** Each
variation states its fixing height. `Shop` and `Sail` carry no ground poles, so
those two can be slid up and down the wall freely; `PolePorch`, `Stall` and
`Strip` stand on the ground and are authored at the height their poles suit.

The cloth is never a flat quad. Every sheet is sampled along its fall line and
across its width, so it bellies between its fixings — a tarpaulin that does not
sag reads as a folded roof, which is the one thing this component must not
look like.

    blender --background --python facade_awning.py -- --out facade_awning.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Fabric_Canvas_Sand",      #  0 SAND    the good canvas
    "Mat_Fabric_Flag_Bleached",    #  1 BLEACH  canvas the sun has finished with
    "Mat_Fabric_Canvas_Faded",     #  2 FADED   dirty webbing, valances, straps
    "Mat_Fabric_Wing_Ochre",       #  3 OCHRE   patched-in sailcloth
    "Mat_Fabric_Tarp_Azure",       #  4 AZURE   the one loud colour on site
    "Mat_Fabric_Rope_Hemp",        #  5 ROPE    guys and lashings
    "Mat_Wood_Timber_Silvered",    #  6 TIMBER  poles, spreader bars, counters
    "Mat_Metal_Steel_Worn",        #  7 STEEL   tube arms, front bars, eyelets
    "Mat_Metal_Steel_Dark",        #  8 DARK    brackets, hooks, cleats
    "Mat_Metal_Rust_Heavy",        #  9 RUST    the fixings that have been wet
    "Mat_Wood_Ply_Worn",           # 10 PLY     stall boards and crates
]
SAND, BLEACH, FADED, OCHRE, AZURE, ROPE, TIMBER, STEEL, DARK, RUST, PLY = range(11)


def emit(coll, name, mats, build, origin=(0, 0, 0), bevel=0.008):
    p = Part(mats)
    build(p)
    if bevel:
        p.bevel(width=bevel, segments=1)
    return p.finish(name, coll, origin)


def strut(p, a, b, radius=0.035, mat=TIMBER, seg=6):
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    p.cyl((a + b) / 2.0, radius, d.length, 'Z', seg=seg, mat=mat,
          rot=rot.to_4x4())


def cloth(p, y0, y1, fall, mat, stations=9, nu=9, thick=0.022):
    """A sheet swept across its width, sampled along its fall line.

    `fall(tx, ty)` returns the (x, z) of the top surface at fraction `tx`
    along the fall and `ty` across the width. Everything about how a given
    awning droops lives in that one function, which is why each variation
    below is a handful of lines rather than a mesh.
    """
    sections = []
    for j in range(stations):
        ty = j / (stations - 1.0)
        y = y0 + (y1 - y0) * ty
        top, bot = [], []
        for i in range(nu):
            tx = i / (nu - 1.0)
            x, z = fall(tx, ty)
            top.append((x, z))
            bot.append((x, z - thick))
        sections.append((y, top + list(reversed(bot))))
    p.loft(sections, axis='Y', mat=mat)


def valance(p, y0, y1, x_at, z_top, drop=0.26, mat=FADED, scallops=5):
    """The scalloped hem hanging off a front edge.

    A straight-cut hem reads as sheet metal. Scallops cost nothing and are the
    single clearest signal that the material is cloth.
    """
    sections = []
    steps = scallops * 4 + 1
    for j in range(steps):
        ty = j / (steps - 1.0)
        y = y0 + (y1 - y0) * ty
        cut = drop * (0.55 + 0.45 * abs(math.cos(math.pi * scallops * ty)))
        sections.append((y, [(x_at, z_top), (x_at + 0.018, z_top),
                             (x_at + 0.018, z_top - cut), (x_at, z_top - cut)]))
    p.loft(sections, axis='Y', mat=mat)


def wall_plate(p, y0, y1, z, mat=DARK, thick=0.10):
    """The bracket strip the cloth is actually fixed to. Without it an awning
    floats a hand's width off the wall from every angle but head-on."""
    p.box((0.05, (y0 + y1) / 2.0, z), (thick, y1 - y0, 0.09), mat)
    for y in (y0 + 0.10, (y0 + y1) / 2.0, y1 - 0.10):
        p.box((0.06, y, z), (0.13, 0.07, 0.16), mat)


def lashings(p, y0, y1, x_at, z_at, count=5, mat=ROPE, r=0.055):
    for k in range(count):
        y = y0 + (y1 - y0) * k / (count - 1.0)
        p.torus((x_at, y, z_at), r, 0.014, 'Y', 10, 5, mat=mat)


# ---------------------------------------------------------------------------
# V1 — Shop. Over a door, arms back to the wall, no legs.
# ---------------------------------------------------------------------------

def build_shop(coll, mats):
    """Fixing at 2.58 m — clears a 2.00 m door with its frame. No ground poles,
    so this one slides up the wall to suit whatever it is shading."""
    tag = "AwningShop"
    y0, y1 = -1.20, 1.20
    fix, front, proj = 2.58, 2.12, 1.36

    emit(coll, "Mesh_%s_Cloth" % tag, mats, lambda p: cloth(
        p, y0, y1,
        lambda tx, ty: (0.04 + proj * tx,
                        fix + (front - fix) * tx
                        - 0.085 * math.sin(math.pi * ty) * math.sin(math.pi * tx)),
        SAND), bevel=None)

    emit(coll, "Mesh_%s_Valance" % tag, mats, lambda p: valance(
        p, y0, y1, proj + 0.04, front + 0.01, 0.28, OCHRE), bevel=None)

    emit(coll, "Mesh_%s_FrontBar" % tag, mats, lambda p: (
        strut(p, (proj + 0.04, y0 - 0.10, front), (proj + 0.04, y1 + 0.10, front),
              0.026, STEEL, seg=8),
        [p.cyl((proj + 0.04, y, front), 0.045, 0.05, 'Y', seg=8, mat=DARK)
         for y in (y0, y1)]))

    emit(coll, "Mesh_%s_Arms" % tag, mats, lambda p: [
        (strut(p, (0.06, y, fix - 0.02), (proj + 0.04, y, front), 0.024, STEEL),
         strut(p, (0.06, y, front - 0.46), (proj * 0.62, y, front + 0.06),
               0.020, STEEL))
        for y in (y0 + 0.14, y1 - 0.14)])

    emit(coll, "Mesh_%s_WallPlate" % tag, mats,
         lambda p: wall_plate(p, y0 - 0.10, y1 + 0.10, fix))


# ---------------------------------------------------------------------------
# V2 — Pole porch. Wall at the back, two poles at the front.
# ---------------------------------------------------------------------------

def build_poleporch(coll, mats):
    """Fixing at 2.74 m, poles to the ground at 2.30 m out. Ground-supported,
    so it is authored at the height its poles suit."""
    tag = "AwningPorch"
    y0, y1 = -1.40, 1.40
    fix, head, proj = 2.74, 2.26, 2.30

    emit(coll, "Mesh_%s_Cloth" % tag, mats, lambda p: cloth(
        p, y0, y1,
        lambda tx, ty: (0.04 + proj * tx,
                        fix + (head - fix) * tx
                        - 0.20 * math.sin(math.pi * ty) ** 0.8
                        * math.sin(math.pi * tx) ** 0.7),
        BLEACH, stations=11), bevel=None)

    for s, side in ((-1, "Left"), (1, "Right")):
        emit(coll, "Mesh_%s_Pole%s" % (tag, side), mats,
             lambda p, s=s: (
                 strut(p, (proj, s * 1.40, 0.0), (proj, s * 1.40, head + 0.12),
                       0.046, TIMBER, seg=8),
                 p.box((proj, s * 1.40, 0.05), (0.26, 0.26, 0.10), PLY),
                 # Knee brace back under the cloth, the way a real one is stiffened.
                 strut(p, (proj, s * 1.40, head - 0.62),
                       (proj - 0.58, s * 1.40, head + 0.04), 0.030, TIMBER)),
             origin=(proj, s * 1.40, 0.0))

    emit(coll, "Mesh_%s_HeadBar" % tag, mats, lambda p: strut(
        p, (proj, y0 - 0.16, head + 0.06), (proj, y1 + 0.16, head + 0.06),
        0.030, TIMBER, seg=8))

    emit(coll, "Mesh_%s_WallPlate" % tag, mats,
         lambda p: wall_plate(p, y0 - 0.12, y1 + 0.12, fix))

    emit(coll, "Mesh_%s_Lashings" % tag, mats, lambda p: (
        lashings(p, y0, y1, proj, head + 0.06, 5, r=0.062),
        # Two guys off the pole heads, out and down.
        [(strut(p, (proj, s * 1.40, head + 0.10), (proj + 1.05, s * 1.92, 0.0),
                0.011, ROPE, seg=4),
          p.box((proj + 1.05, s * 1.92, 0.07), (0.05, 0.05, 0.26), TIMBER,
                rot=Matrix.Rotation(math.radians(16), 4, 'X')))
         for s in (-1, 1)]), bevel=None)


# ---------------------------------------------------------------------------
# V3 — Sail. Two corners on the wall, one on a leaning pole.
# ---------------------------------------------------------------------------

def build_sail(coll, mats):
    """Fixings at 4.05 m and 2.35 m — a first-floor eaves and a ground-floor
    head. No ground pole under the cloth, so it can be re-hung anywhere."""
    tag = "AwningSail"
    y0, y1 = -1.55, 1.65
    hi, lo, proj = 4.05, 2.35, 2.60
    tip_z = 3.05

    def fall(tx, ty):
        # The wall edge runs from a high fixing to a low one; the free corner
        # is out on the pole. Twisting the sheet like this is what stops it
        # reading as a roof plane.
        z_wall = hi + (lo - hi) * ty
        z_out = tip_z - 0.42 * ty
        z = z_wall + (z_out - z_wall) * tx
        belly = 0.30 * math.sin(math.pi * tx) * math.sin(math.pi * ty) ** 0.6
        return (0.04 + proj * tx, z - belly)

    emit(coll, "Mesh_%s_Cloth" % tag, mats,
         lambda p: cloth(p, y0, y1, fall, AZURE, stations=11, nu=11), bevel=None)

    emit(coll, "Mesh_%s_Pole" % tag, mats, lambda p: (
        strut(p, (proj + 0.46, y1 + 0.30, 0.0), (proj + 0.10, y1 + 0.06, tip_z + 0.20),
              0.048, TIMBER, seg=8),
        p.box((proj + 0.46, y1 + 0.30, 0.06), (0.30, 0.30, 0.12), PLY),
        p.cyl((proj + 0.10, y1 + 0.06, tip_z + 0.24), 0.062, 0.09, 'Z', seg=8,
              mat=DARK)), origin=(proj + 0.46, y1 + 0.30, 0.0))

    emit(coll, "Mesh_%s_EdgeRopes" % tag, mats, lambda p: (
        # Bolt ropes along the two free edges — a sail's edges are always
        # heavier than its middle, and the eye reads that as tension.
        strut(p, (0.06, y0, hi), (proj + 0.06, y0 - 0.02, tip_z), 0.016, ROPE, seg=4),
        strut(p, (0.06, y1, lo), (proj + 0.06, y1 + 0.02, tip_z - 0.42), 0.016,
              ROPE, seg=4),
        strut(p, (proj + 0.06, y0 - 0.02, tip_z), (proj + 0.06, y1 + 0.02, tip_z - 0.42),
              0.016, ROPE, seg=4),
        strut(p, (proj + 0.08, y1 + 0.04, tip_z - 0.40), (proj + 0.10, y1 + 0.06, tip_z + 0.14),
              0.013, ROPE, seg=4),
        strut(p, (proj + 0.06, y0 - 0.02, tip_z), (proj + 1.30, y0 - 0.70, 0.0),
              0.012, ROPE, seg=4),
        p.box((proj + 1.30, y0 - 0.70, 0.07), (0.05, 0.05, 0.26), TIMBER,
              rot=Matrix.Rotation(math.radians(18), 4, 'X'))), bevel=None)

    emit(coll, "Mesh_%s_WallFixings" % tag, mats, lambda p: [
        (p.box((0.05, y, z), (0.10, 0.14, 0.14), RUST),
         p.torus((0.10, y, z), 0.055, 0.014, 'Y', 10, 5, mat=DARK))
        for y, z in ((y0, hi), (y1, lo))])


# ---------------------------------------------------------------------------
# V4 — Stall. A counter under cloth, against a wall.
# ---------------------------------------------------------------------------

def build_stall(coll, mats):
    """Fixing at 2.66 m, frame down to the ground 2.20 m out."""
    tag = "AwningStall"
    y0, y1 = -1.50, 1.50
    fix, head, proj = 2.66, 2.34, 2.20

    emit(coll, "Mesh_%s_Cloth" % tag, mats, lambda p: cloth(
        p, y0, y1,
        lambda tx, ty: (0.04 + proj * tx,
                        fix + (head - fix) * tx
                        - 0.14 * math.sin(math.pi * ty) * math.sin(math.pi * tx)),
        OCHRE, stations=11), bevel=None)

    emit(coll, "Mesh_%s_Frame" % tag, mats, lambda p: (
        [(strut(p, (proj, s * 1.50, 0.0), (proj, s * 1.50, head + 0.10), 0.042,
                TIMBER, seg=8),
          strut(p, (0.08, s * 1.50, fix - 0.06), (proj, s * 1.50, head + 0.06),
                0.034, TIMBER))
         for s in (-1, 1)],
        strut(p, (proj, y0 - 0.12, head + 0.10), (proj, y1 + 0.12, head + 0.10),
              0.032, TIMBER, seg=8)))

    emit(coll, "Mesh_%s_Lashings" % tag, mats, lambda p: (
        [p.torus((proj, s * 1.50, head + 0.08), 0.075, 0.016, 'Z', 10, 5, mat=ROPE)
         for s in (-1, 1)],
        lashings(p, y0 + 0.30, y1 - 0.30, proj, head + 0.10, 4, r=0.058)),
        bevel=None)

    emit(coll, "Mesh_%s_SideSheet" % tag, mats, lambda p: cloth(
        p, y1 - 0.02, y1 + 0.02,
        lambda tx, ty: (0.06 + (proj - 0.10) * tx, 0.05 + (head - 0.35) * (1.0 - tx)),
        FADED, stations=3, nu=7, thick=0.02), bevel=None)

    emit(coll, "Mesh_%s_Counter" % tag, mats, lambda p: (
        p.box((proj - 0.52, 0.0, 0.92), (0.72, 2.60, 0.06), PLY),
        [p.box((proj - 0.52 + sx * 0.28, sy * 1.18, 0.44), (0.08, 0.08, 0.88), TIMBER)
         for sx in (-1, 1) for sy in (-1, 1)],
        p.box((proj - 0.52, 0.0, 0.52), (0.60, 2.40, 0.04), PLY)))

    emit(coll, "Mesh_%s_Goods" % tag, mats, lambda p: (
        [p.box((proj - 0.56, -0.95 + k * 0.52, 1.03), (0.34, 0.30, 0.16), PLY,
               rot=Matrix.Rotation(math.radians(7 * k), 4, 'Z')) for k in range(4)],
        [p.cyl((proj - 0.34, 0.86 - k * 0.30, 1.04), 0.09, 0.18, 'Z', seg=10,
               mat=RUST) for k in range(2)]))

    emit(coll, "Mesh_%s_WallPlate" % tag, mats,
         lambda p: wall_plate(p, y0 - 0.10, y1 + 0.10, fix))


# ---------------------------------------------------------------------------
# V5 — Strip. Cloth slung between two buildings.
# ---------------------------------------------------------------------------

def build_strip(coll, mats):
    """A long sagging strip from this wall to a pole 4.2 m out — the piece that
    fills the gap between two cottages. Fixing at 3.30 m."""
    tag = "AwningStrip"
    y0, y1 = -0.62, 0.62
    fix, far, span = 3.30, 3.05, 4.20

    emit(coll, "Mesh_%s_Cloth" % tag, mats, lambda p: cloth(
        p, y0, y1,
        lambda tx, ty: (0.06 + span * tx,
                        fix + (far - fix) * tx
                        # Deep catenary: a strip this long with a shallow sag
                        # looks like a plank, and it has to look slack.
                        - 0.62 * math.sin(math.pi * tx) ** 0.85
                        - 0.07 * math.sin(math.pi * ty)),
        BLEACH, stations=7, nu=13), bevel=None)

    emit(coll, "Mesh_%s_Pole" % tag, mats, lambda p: (
        strut(p, (span + 0.16, 0.0, 0.0), (span + 0.06, 0.0, far + 0.26), 0.050,
              TIMBER, seg=8),
        p.box((span + 0.16, 0.0, 0.06), (0.32, 0.32, 0.12), PLY),
        strut(p, (span + 0.10, y0 - 0.22, far + 0.02),
              (span + 0.10, y1 + 0.22, far + 0.02), 0.028, TIMBER)),
        origin=(span + 0.16, 0.0, 0.0))

    emit(coll, "Mesh_%s_Guys" % tag, mats, lambda p: [
        (strut(p, (span + 0.06, 0.0, far + 0.22),
               (span + 1.35, s * 1.15, 0.0), 0.012, ROPE, seg=4),
         p.box((span + 1.35, s * 1.15, 0.07), (0.05, 0.05, 0.26), TIMBER,
               rot=Matrix.Rotation(math.radians(16), 4, 'X')))
        for s in (-1, 1)], bevel=None)

    emit(coll, "Mesh_%s_Lashings" % tag, mats, lambda p: (
        lashings(p, y0, y1, 0.10, fix, 3, r=0.05),
        lashings(p, y0, y1, span + 0.10, far + 0.02, 3, r=0.05)), bevel=None)

    emit(coll, "Mesh_%s_WallPlate" % tag, mats,
         lambda p: wall_plate(p, y0 - 0.14, y1 + 0.14, fix))

    # Pennants along the low point, so the strip has something to read against
    # the sky besides its own sag.
    emit(coll, "Mesh_%s_Pennants" % tag, mats, lambda p: [
        p.prism([(0.0, 0.0), (0.15, 0.0), (0.075, -0.20)], 0.01, axis='Y',
                mat=(OCHRE, AZURE, SAND, FADED)[k % 4],
                offset=(0.80 + k * 0.36, y1 + 0.03,
                        fix + (far - fix) * ((0.80 + k * 0.36) / span)
                        - 0.62 * math.sin(math.pi * (0.80 + k * 0.36) / span) ** 0.85))
        for k in range(8)], bevel=None)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_shop(collection("Coll_FacadeAwning_Shop", root), mats)
    build_poleporch(collection("Coll_FacadeAwning_PolePorch", root), mats)
    build_sail(collection("Coll_FacadeAwning_Sail", root), mats)
    build_stall(collection("Coll_FacadeAwning_Stall", root), mats)
    build_strip(collection("Coll_FacadeAwning_Strip", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
