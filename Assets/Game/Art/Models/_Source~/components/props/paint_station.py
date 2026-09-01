"""components/props/paint_station — the reason the settlement is pastel.

A white tank with pastel houses around it raises a question, and this component
answers it on the ground: somebody here paints things. Tins, a mixing trestle,
a spray rig, and a board of test patches in exactly the colours the cottages
are. It is set dressing that does narrative work, which is the cheapest kind.

`SwatchBoard` is the load-bearing one. Four painted rectangles in
`Mat_Paint_Mint_Pastel`, `_Butter_Pastel`, `_Rose_Dusty` and
`Mat_Paint_White_Arctic` tie every building on the site back to one prop, and
a player who has walked past the board reads the houses differently afterwards.

**Origin convention: centre of the footprint, at ground level (z = 0).**

    blender --background --python paint_station.py -- --out paint_station.blend

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
    "Mat_Paint_White_Arctic",      #  0 WHITE   the tank's colour, in a tin
    "Mat_Paint_Mint_Pastel",       #  1 MINT
    "Mat_Paint_Butter_Pastel",     #  2 BUTTER
    "Mat_Paint_Rose_Dusty",        #  3 ROSE
    "Mat_Metal_Steel_Worn",        #  4 STEEL   tin bodies, trolley frame
    "Mat_Metal_Steel_Dark",        #  5 DARK    fittings, handles, valves
    "Mat_Metal_Rust_Heavy",        #  6 RUST    tins that have been outside
    "Mat_Metal_Chrome_Scuffed",    #  7 CHROME  the pressure vessel, gauge
    "Mat_Wood_Timber_Silvered",    #  8 TIMBER  trestles, board frames
    "Mat_Wood_Ply_Worn",           #  9 PLY     bench tops, crates
    "Mat_Fabric_Canvas_Faded",     # 10 CANVAS  dust sheets and rags
    "Mat_Plastic_Rubber_Black",    # 11 RUBBER  hose, tyres, grips
    "Mat_Plastic_Cream_Aged",      # 12 CREAM   plastic jars and funnels
    "Mat_Neutral_Black_Matte",     # 13 BLACK   the inside of an open tin
]
(WHITE, MINT, BUTTER, ROSE, STEEL, DARK, RUST, CHROME, TIMBER, PLY, CANVAS,
 RUBBER, CREAM, BLACK) = range(14)

PASTELS = (WHITE, MINT, BUTTER, ROSE)


def emit(coll, name, mats, build, origin=(0, 0, 0), bevel=0.006):
    p = Part(mats)
    build(p)
    if bevel:
        p.bevel(width=bevel, segments=1)
    return p.finish(name, coll, origin)


def tin(p, x, y, z, r=0.16, h=0.24, mat=STEEL, fill=None, lid=True, tilt=0.0):
    """One paint tin. `fill` paints the surface of what is in it."""
    rot = Matrix.Rotation(tilt, 4, 'Y') if tilt else None
    p.cyl((x, y, z + h / 2.0), r, h, 'Z', seg=12, mat=mat, rot=rot)
    p.torus((x, y, z + h - 0.01), r - 0.008, 0.014, 'Z', 12, 6, mat=DARK)
    if fill is not None:
        p.cyl((x, y, z + h - 0.035), r - 0.022, 0.02, 'Z', seg=12, mat=fill)
    if lid:
        p.cyl((x, y, z + h + 0.012), r + 0.012, 0.022, 'Z', seg=12, mat=mat)


def drips(p, x, y, z_top, r, mat, count=5, seed=0):
    """Runs down the outside of a tin. Small, and the single thing that makes
    painted props look used rather than shopped."""
    for k in range(count):
        a = (k * 2.399 + seed) % (2 * math.pi)
        length = 0.05 + 0.06 * ((k * 7 + seed) % 5) / 4.0
        p.box((x + r * math.cos(a), y + r * math.sin(a), z_top - length / 2.0),
              (0.028, 0.028, length), mat)


def brush(p, x, y, z, length=0.26, mat=TIMBER, bristle=DARK, tilt=0.0):
    rot = Matrix.Rotation(tilt, 4, 'Y') if tilt else None
    p.box((x, y, z + length / 2.0), (0.022, 0.022, length), mat, rot=rot)
    p.box((x, y, z + length * 0.06), (0.052, 0.020, 0.09), bristle, rot=rot)


# ---------------------------------------------------------------------------
# V1 — Trestle. The mixing bench.
# ---------------------------------------------------------------------------

def build_trestle(coll, mats):
    tag = "PaintTrestle"
    top_z = 0.84

    emit(coll, "Mesh_%s_Top" % tag, mats, lambda p: (
        p.box((0, 0, top_z), (1.90, 0.72, 0.045), PLY),
        # Spilt colour on the bench top, in the order the houses got painted.
        [p.box((-0.62 + k * 0.42, -0.10 + 0.14 * (k % 2), top_z + 0.026),
               (0.30, 0.24, 0.006), PASTELS[k]) for k in range(4)]))

    for s, side in ((-1, "Left"), (1, "Right")):
        emit(coll, "Mesh_%s_Trestle%s" % (tag, side), mats,
             lambda p, s=s: (
                 [p.box((s * 0.72 + sx * 0.16 * (1 if sy > 0 else 1), sy * 0.30,
                         (top_z - 0.05) / 2.0),
                        (0.07, 0.07, top_z - 0.05), TIMBER,
                        rot=Matrix.Rotation(math.radians(-8 * sx), 4, 'Y'))
                  for sx in (-1, 1) for sy in (-1, 1)],
                 p.box((s * 0.72, 0, 0.34), (0.10, 0.72, 0.05), TIMBER),
                 p.box((s * 0.72, 0, top_z - 0.07), (0.20, 0.76, 0.08), TIMBER)),
             origin=(s * 0.72, 0, 0))

    emit(coll, "Mesh_%s_Tins" % tag, mats, lambda p: (
        tin(p, 0.58, 0.16, top_z + 0.025, 0.15, 0.22, STEEL, fill=MINT, lid=False),
        tin(p, 0.86, -0.14, top_z + 0.025, 0.12, 0.18, RUST, fill=WHITE, lid=False),
        drips(p, 0.58, 0.16, top_z + 0.24, 0.15, MINT, seed=1),
        drips(p, 0.86, -0.14, top_z + 0.20, 0.12, WHITE, seed=3),
        p.cyl((0.58, 0.16, top_z + 0.32), 0.012, 0.34, 'Z', seg=6, mat=STEEL,
              rot=Matrix.Rotation(math.radians(14), 4, 'X'))))

    emit(coll, "Mesh_%s_Jars" % tag, mats, lambda p: (
        [p.cyl((-0.82 + k * 0.15, 0.20, top_z + 0.11), 0.055, 0.18, 'Z', seg=10,
               mat=CREAM) for k in range(3)],
        [brush(p, -0.82 + k * 0.15, 0.20, top_z + 0.16, 0.24,
               tilt=math.radians(-9 + 9 * k)) for k in range(3)]))

    emit(coll, "Mesh_%s_Rags" % tag, mats, lambda p: (
        p.box((-0.34, -0.18, top_z + 0.055), (0.30, 0.24, 0.07), CANVAS,
              rot=Matrix.Rotation(math.radians(22), 4, 'Z')),
        p.box((-0.20, -0.26, top_z + 0.09), (0.20, 0.16, 0.05), CANVAS,
              rot=Matrix.Rotation(math.radians(-40), 4, 'Z'))), bevel=0.012)


# ---------------------------------------------------------------------------
# V2 — Pot stack. Tins, most of them empty.
# ---------------------------------------------------------------------------

def build_potstack(coll, mats):
    tag = "PaintPots"

    emit(coll, "Mesh_%s_StackTall" % tag, mats, lambda p: [
        tin(p, 0.0, 0.0, 0.26 * k, 0.185, 0.25, (STEEL, RUST, STEEL, RUST)[k],
            lid=(k < 3)) for k in range(4)])

    emit(coll, "Mesh_%s_StackShort" % tag, mats, lambda p: [
        tin(p, 0.46, -0.22, 0.27 * k, 0.20, 0.26, (RUST, STEEL)[k], lid=(k < 1))
        for k in range(2)], origin=(0.46, -0.22, 0))

    # The two open ones. They carry the colour, so they sit at the front.
    emit(coll, "Mesh_%s_OpenMint" % tag, mats, lambda p: (
        tin(p, -0.44, -0.30, 0.0, 0.19, 0.25, WHITE, fill=MINT, lid=False),
        drips(p, -0.44, -0.30, 0.25, 0.19, MINT, 6, seed=2),
        p.cyl((-0.44, -0.30, 0.20), 0.168, 0.04, 'Z', seg=12, mat=BLACK)),
        origin=(-0.44, -0.30, 0))

    emit(coll, "Mesh_%s_OpenRose" % tag, mats, lambda p: (
        tin(p, -0.10, -0.62, 0.0, 0.17, 0.22, WHITE, fill=ROSE, lid=False),
        drips(p, -0.10, -0.62, 0.22, 0.17, ROSE, 5, seed=5),
        # The stirring stick, left in it.
        p.box((-0.06, -0.60, 0.30), (0.030, 0.030, 0.44), TIMBER,
              rot=Matrix.Rotation(math.radians(17), 4, 'X'))),
        origin=(-0.10, -0.62, 0))

    emit(coll, "Mesh_%s_LidsLoose" % tag, mats, lambda p: [
        p.cyl((0.40 + 0.10 * k, 0.46 - 0.06 * k, 0.014 + 0.026 * k), 0.195,
              0.024, 'Z', seg=12, mat=(STEEL, RUST, STEEL)[k],
              rot=Matrix.Rotation(math.radians(4 * k), 4, 'X'))
        for k in range(3)])

    emit(coll, "Mesh_%s_Crate" % tag, mats, lambda p: (
        p.box((-0.62, 0.44, 0.16), (0.62, 0.46, 0.32), PLY),
        [p.box((-0.62, 0.44, z), (0.66, 0.50, 0.035), TIMBER)
         for z in (0.05, 0.29)]))


# ---------------------------------------------------------------------------
# V3 — Spray rig. Pressure, hose and a lance.
# ---------------------------------------------------------------------------

def build_sprayrig(coll, mats):
    tag = "PaintSpray"
    axle_z = 0.26

    emit(coll, "Mesh_%s_Vessel" % tag, mats, lambda p: (
        p.cyl((0, 0, 0.72), 0.24, 0.76, 'Z', seg=16, mat=CHROME),
        p.cyl((0, 0, 1.13), 0.24, 0.10, 'Z', seg=16, mat=CHROME, radius_top=0.14),
        p.cyl((0, 0, 0.32), 0.24, 0.10, 'Z', seg=16, mat=CHROME, radius_top=0.20),
        [p.torus((0, 0, z), 0.245, 0.018, 'Z', 16, 6, mat=DARK)
         for z in (0.52, 0.92)]))

    emit(coll, "Mesh_%s_Pump" % tag, mats, lambda p: (
        p.cyl((0, 0, 1.24), 0.075, 0.20, 'Z', seg=10, mat=DARK),
        p.cyl((0, 0, 1.42), 0.024, 0.30, 'Z', seg=8, mat=STEEL),
        p.box((0, 0, 1.58), (0.34, 0.05, 0.05), TIMBER),
        p.cyl((0.20, 0.0, 1.16), 0.070, 0.05, 'Y', seg=12, mat=CHROME),
        p.cyl((0.20, -0.03, 1.16), 0.052, 0.02, 'Y', seg=12, mat=CREAM)))

    emit(coll, "Mesh_%s_Frame" % tag, mats, lambda p: (
        [p.box((sx * 0.28, 0.0, 0.52), (0.05, 0.05, 1.00), STEEL,
               rot=Matrix.Rotation(math.radians(-4 * sx), 4, 'Y'))
         for sx in (-1, 1)],
        p.box((0, -0.28, 0.98), (0.62, 0.05, 0.05), STEEL),
        [p.box((0, 0.30, z), (0.60, 0.05, 0.05), STEEL) for z in (0.36, 1.30)],
        # Handles, so it reads as something one person wheels.
        [p.box((sx * 0.28, 0.42, 1.34), (0.05, 0.30, 0.05), STEEL)
         for sx in (-1, 1)]))

    for s, side in ((-1, "Left"), (1, "Right")):
        emit(coll, "Mesh_%s_Wheel%s" % (tag, side), mats,
             lambda p, s=s: (
                 p.cyl((s * 0.34, -0.10, axle_z), 0.26, 0.09, 'X', seg=16,
                       mat=RUBBER),
                 p.cyl((s * 0.34, -0.10, axle_z), 0.13, 0.10, 'X', seg=12,
                       mat=STEEL),
                 [p.box((s * 0.34, -0.10, axle_z), (0.05, 0.44, 0.03), STEEL,
                        rot=Matrix.Rotation(math.pi * k / 3.0, 4, 'X'))
                  for k in range(3)]),
             origin=(s * 0.34, -0.10, axle_z))

    emit(coll, "Mesh_%s_Hose" % tag, mats, lambda p: (
        [p.torus((0.0, 0.46, 0.62 + 0.055 * k), 0.22, 0.026, 'Z', 16, 6,
                 mat=RUBBER) for k in range(4)],
        p.cyl((0.16, 0.28, 1.10), 0.026, 0.42, 'Z', seg=8, mat=RUBBER,
              rot=Matrix.Rotation(math.radians(40), 4, 'X'))), bevel=None)

    emit(coll, "Mesh_%s_Lance" % tag, mats, lambda p: (
        p.cyl((-0.42, 0.34, 0.66), 0.020, 0.92, 'Z', seg=8, mat=STEEL,
              rot=Matrix.Rotation(math.radians(24), 4, 'Y')),
        p.box((-0.30, 0.34, 0.28), (0.07, 0.06, 0.16), RUBBER),
        p.cyl((-0.56, 0.34, 1.06), 0.032, 0.06, 'Z', seg=8, mat=DARK,
              rot=Matrix.Rotation(math.radians(24), 4, 'Y'))))


# ---------------------------------------------------------------------------
# V4 — Swatch board. The prop that explains the settlement.
# ---------------------------------------------------------------------------

def build_swatchboard(coll, mats):
    tag = "PaintSwatch"
    lean = math.radians(11)
    board_z = 0.94

    emit(coll, "Mesh_%s_Board" % tag, mats, lambda p: p.box(
        (0, 0, board_z), (1.42, 0.05, 1.06), PLY,
        rot=Matrix.Rotation(lean, 4, 'X')))

    # Four patches, brushed on unevenly, in the order they were tried.
    emit(coll, "Mesh_%s_Patches" % tag, mats, lambda p: [
        p.box((-0.50 + k * 0.34,
               -0.035 - math.sin(lean) * (0.22 - 0.44 * (k % 2)),
               board_z + 0.22 - 0.44 * (k % 2)),
              (0.27 - 0.02 * (k % 3), 0.012, 0.30 - 0.03 * (k % 2)), PASTELS[k],
              rot=Matrix.Rotation(lean, 4, 'X'))
        for k in range(4)], bevel=0.004)

    emit(coll, "Mesh_%s_Legs" % tag, mats, lambda p: (
        [p.box((sx * 0.62, 0.0, board_z / 2.0 + 0.10), (0.06, 0.06, board_z + 0.30),
               TIMBER, rot=Matrix.Rotation(lean, 4, 'X')) for sx in (-1, 1)],
        # Back stay, so it does not look glued to the ground.
        [p.box((sx * 0.62, 0.30, 0.62), (0.05, 0.05, 1.42), TIMBER,
               rot=Matrix.Rotation(math.radians(-26), 4, 'X')) for sx in (-1, 1)],
        p.box((0, 0.0, 0.24), (1.30, 0.06, 0.05), TIMBER,
              rot=Matrix.Rotation(lean, 4, 'X'))))

    emit(coll, "Mesh_%s_Notes" % tag, mats, lambda p: [
        p.box((-0.56 + k * 0.30, -0.06, board_z - 0.44),
              (0.14, 0.006, 0.10), WHITE, rot=Matrix.Rotation(lean, 4, 'X'))
        for k in range(4)], bevel=0.003)

    emit(coll, "Mesh_%s_Shelf" % tag, mats, lambda p: (
        p.box((0, -0.11, 0.30), (1.24, 0.20, 0.04), TIMBER),
        [tin(p, -0.40 + k * 0.40, -0.11, 0.32, 0.10, 0.14,
             (STEEL, RUST, STEEL)[k], fill=PASTELS[k + 1], lid=False)
         for k in range(3)]))


# ---------------------------------------------------------------------------
# V5 — Drip sheet. Ground cover with the job's history on it.
# ---------------------------------------------------------------------------

def build_dripsheet(coll, mats):
    tag = "PaintSheet"

    def sheet(p):
        """Draped over two crates, so it has form instead of being a rug."""
        nx, steps = 11, 9
        sections = []
        for j in range(steps):
            ty = j / (steps - 1.0)
            y = -0.95 + 1.90 * ty
            upper, lower = [], []
            for i in range(nx):
                tx = i / (nx - 1.0)
                x = -1.25 + 2.50 * tx
                lump = (0.34 * math.exp(-((x + 0.55) / 0.44) ** 2)
                        + 0.26 * math.exp(-((x - 0.62) / 0.38) ** 2))
                z = 0.02 + lump * (0.35 + 0.65 * math.sin(math.pi * ty) ** 0.5)
                upper.append((x, z))
                lower.append((x, max(0.005, z - 0.022)))
            sections.append((y, upper + list(reversed(lower))))
        p.loft(sections, axis='Y', mat=CANVAS)
    emit(coll, "Mesh_%s_Sheet" % tag, mats, sheet, bevel=None)

    emit(coll, "Mesh_%s_CrateUnder" % tag, mats, lambda p: (
        p.box((-0.55, 0.0, 0.17), (0.60, 0.60, 0.34), PLY),
        p.box((0.62, -0.06, 0.13), (0.52, 0.52, 0.26), PLY)))

    # The spatter. Flat discs on the cloth in the four house colours.
    emit(coll, "Mesh_%s_Spatter" % tag, mats, lambda p: [
        p.cyl((-1.05 + 0.23 * k, -0.72 + 0.19 * (k % 5), 0.03 + 0.006 * (k % 3)),
              0.035 + 0.030 * ((k * 3) % 4) / 3.0, 0.008, 'Z', seg=8,
              mat=PASTELS[k % 4])
        for k in range(14)], bevel=None)

    emit(coll, "Mesh_%s_Roller" % tag, mats, lambda p: (
        p.cyl((0.30, 0.62, 0.10), 0.055, 0.22, 'Y', seg=10, mat=CREAM),
        p.cyl((0.30, 0.62, 0.10), 0.020, 0.34, 'Y', seg=6, mat=STEEL),
        p.box((0.30, 0.86, 0.10), (0.030, 0.24, 0.030), STEEL,
              rot=Matrix.Rotation(math.radians(30), 4, 'X')),
        p.box((-1.02, 0.52, 0.06), (0.34, 0.26, 0.09), CANVAS,
              rot=Matrix.Rotation(math.radians(-24), 4, 'Z'))))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_trestle(collection("Coll_PaintStation_Trestle", root), mats)
    build_potstack(collection("Coll_PaintStation_PotStack", root), mats)
    build_sprayrig(collection("Coll_PaintStation_SprayRig", root), mats)
    build_swatchboard(collection("Coll_PaintStation_SwatchBoard", root), mats)
    build_dripsheet(collection("Coll_PaintStation_DripSheet", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
