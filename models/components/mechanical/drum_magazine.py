"""components/mechanical/drum_magazine — a revolver magazine for salvage.

A rotating cell drum in a bolted cradle, fed from above through a funnel the
claw drops into. The drum indexes one cell per load, so the machine visibly
fills up: cells at the top are open and empty, cells that have come round are
capped and packed. That is the whole reason to build it this way rather than as
a cargo box — a box tells you nothing, a magazine tells you how full it is from
across the valley.

**No booleans.** The cells are open-ended tubes threaded between a hub and a
rim, not holes drilled out of a solid. A cylinder minus nine bores is fragile to
generate and heavy to carry; a rim, a hub, radial webs and nine tubes read as
exactly the same object and stay clean geometry.

Authoring frame: the drum's rotation **axis lies along X**, origin on the axis.
That points the cell mouths outboard on a vehicle, so the honeycomb is what you
see walking past it. Every object in a variation shares that origin except the
gate, whose origin sits on its own hinge so its bone swings it properly.

Each variation is several objects — cradle, drum, hopper, gate — because the
drum turns and the gate opens.

    Nine     nine cells, the standard fit
    Six      six deeper cells for bulkier finds, heavier rim
    Sorter   nine cells under an inspection arch, with a reject chute

    blender --background --python drum_magazine.py -- --out drum_magazine.blend
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0
    "Mat_Metal_Steel_Dark",      # 1
    "Mat_Metal_Steel_Worn",      # 2
    "Mat_Paint_Olive_Deep",      # 3
    "Mat_Paint_Roof_Green",      # 4
    "Mat_Metal_Rust_Heavy",      # 5
    "Mat_Paint_Warn_Red",        # 6
    "Mat_Emissive_Amber",        # 7
    "Mat_Neutral_Black_Matte",   # 8
    "Mat_Metal_Chrome_Scuffed",  # 9
    "Mat_Plastic_Rubber_Black",  # 10
    "Mat_Metal_Copper_Oxide",    # 11
    "Mat_Emissive_Green_CRT",    # 12
]
(HULL, DARK, STEEL, OLIVE, GREEN, RUST, RED, AMBER, BLACK, CHROME, RUBBER,
 COPPER, CRT) = range(13)

R_RIM = 1.50      # outer radius of the drum
W = 1.60          # drum width along X
R_CELL_RING = 0.95  # radius the cell centres sit on


def polar(r, a_deg):
    """A point on the drum face — X is the axis, so this returns (y, z)."""
    a = math.radians(a_deg)
    return r * math.cos(a), r * math.sin(a)


# ---------------------------------------------------------------------------
# The drum itself
# ---------------------------------------------------------------------------

def build_drum(coll, name, cells, r_cell, filled, rim_thick=0.14):
    p = Part(PALETTE)
    # Rim and hub.
    p.tube((0, 0, 0), R_RIM, rim_thick, W, 'X', 28, HULL)
    p.tube((0, 0, 0), R_RIM - rim_thick - 0.02, 0.06, W * 0.96, 'X', 28, OLIVE)
    p.cyl((0, 0, 0), 0.34, W * 1.10, 'X', 16, DARK)
    p.cyl((0, 0, 0), 0.20, W * 1.34, 'X', 12, CHROME)   # the axle stub

    for k in range(cells):
        a = 360.0 * k / cells
        cy, cz = polar(R_CELL_RING, a)
        # The cell: an open tube, so you can see straight through an empty one.
        p.tube((0, cy, cz), r_cell, 0.055, W * 0.98, 'X', 16, STEEL)
        # Radial webs tying the cell into hub and rim.
        for side in (-1, 1):
            wy, wz = polar(R_CELL_RING, a + side * (180.0 / cells))
            p.seam((0, wy * 0.30, wz * 0.30), (0, wy * 1.52, wz * 1.52),
                   width=0.09, depth=W * 0.90, axis='X', mat=DARK)
        # Numbered plate on the rim above each cell.
        ry, rz = polar(R_RIM * 1.005, a)
        p.box((W * 0.30, ry, rz), (0.30, 0.30, 0.30), OLIVE,
              rot=Matrix.Rotation(math.radians(a), 4, 'X'))

        if k in filled:
            # A packed cell: capped on the outboard face, salvage inside.
            p.cyl((W * 0.47, cy, cz), r_cell * 0.98, 0.07, 'X', 16, RUST)
            for sx in (-1, 1):
                p.cyl((sx * W * 0.40, cy, cz), r_cell * 0.62, 0.09, 'X', 12,
                      CHROME)
            p.greeble((-W * 0.30, cy - r_cell * 0.5, cz - r_cell * 0.5),
                      (W * 0.30, cy + r_cell * 0.5, cz + r_cell * 0.5),
                      5, seed=k * 7 + 3, scale=(0.10, 0.26), mat=STEEL)
        else:
            # An empty cell still has its retaining fingers.
            for j in range(3):
                fy, fz = polar(r_cell * 0.86, a + 120 * j)
                p.box((W * 0.44, cy + fy, cz + fz), (0.06, 0.16, 0.16), CHROME)

    # Indexing ratchet on the inboard face — one tooth per cell.
    for k in range(cells):
        a = 360.0 * k / cells + 180.0 / cells
        ty, tz = polar(1.22, a)
        p.box((-W * 0.54, ty, tz), (0.16, 0.20, 0.34), STEEL,
              rot=Matrix.Rotation(math.radians(a), 4, 'X'))
    p.tube((-W * 0.54, 0, 0), 1.24, 0.10, 0.14, 'X', 24, DARK)

    p.bevel(width=0.014, segments=2)
    return p.finish(name, coll)


# ---------------------------------------------------------------------------
# Cradle, hopper, gate
# ---------------------------------------------------------------------------

def build_cradle(coll, name, arch=False):
    p = Part(PALETTE)
    for sx in (-1, 1):
        # An open yoke, not a plate. A solid cheek here would completely mask
        # the cell mouths, and a magazine you cannot see into is just a box —
        # the whole read of this component is how full it looks.
        x = sx * (W * 0.62)
        p.tube((x, 0, 0), R_RIM + 0.30, 0.22, 0.24, 'X', 30, OLIVE)
        p.tube((x, 0, 0), 0.64, 0.18, 0.22, 'X', 18, STEEL)
        for k in range(3):
            ay, az = polar(1.0, 120 * k + 90)
            p.seam((x, ay * 0.52, az * 0.52), (x, ay * 1.72, az * 1.72),
                   width=0.26, depth=0.22, axis='X', mat=OLIVE)
        p.cyl((x, 0, 0), 0.46, 0.30, 'X', 18, STEEL)
        p.cyl((sx * (W * 0.62 + 0.14), 0, 0), 0.22, 0.16, 'X', 12, CHROME)
        for k in range(6):
            by, bz = polar(0.66, k * 60)
            p.cyl((sx * (W * 0.62 + 0.10), by, bz), 0.055, 0.10, 'X', 6, CHROME)
        # Bolt flanges where the hoop meets the bed.
        for sy in (-1, 1):
            hy, hz = polar(R_RIM + 0.30, 200 if sy < 0 else 340)
            p.box((x, hy, hz), (0.30, 0.34, 0.20), STEEL)
    # Bed the whole thing sits on, and the feet that bolt it down.
    p.box((0, 0, -R_RIM - 0.32), (W * 1.55, R_RIM * 2.00, 0.26), STEEL)
    for sy in (-1, 1):
        for sx in (-1, 1):
            p.box((sx * W * 0.58, sy * R_RIM * 0.86, -R_RIM - 0.52),
                  (0.36, 0.40, 0.30), DARK)
            p.rivets((sx * W * 0.58 - 0.12, sy * R_RIM * 0.86, -R_RIM - 0.66),
                     (sx * W * 0.58 + 0.12, sy * R_RIM * 0.86, -R_RIM - 0.66),
                     2, 0.05, 0.04, 'Z', CHROME)
    # Back plate, drive motor and chain case.
    p.box((0, R_RIM * 0.94, -0.40), (W * 1.30, 0.16, R_RIM * 1.30), GREEN)
    p.cyl((-W * 0.86, R_RIM * 0.62, -0.20), 0.30, 0.72, 'X', 14, DARK)
    p.cyl((-W * 1.16, R_RIM * 0.62, -0.20), 0.12, 0.24, 'X', 10, CHROME)
    p.box((-W * 0.74, R_RIM * 0.30, -0.10), (0.16, 0.90, 0.90), BLACK)
    # Pawl arm riding the ratchet.
    p.box((-W * 0.56, 0.90, 0.82), (0.14, 0.70, 0.20), STEEL,
          rot=Matrix.Rotation(math.radians(-28), 4, 'X'))
    p.cyl((-W * 0.56, 1.16, 1.02), 0.09, 0.22, 'X', 10, CHROME)
    # Hydraulic and power runs down the outside of the cheek.
    for dz, mat in ((-0.90, COPPER), (-1.12, RUBBER)):
        p.cyl((W * 0.74, 0.0, dz), 0.07, R_RIM * 1.80, 'Y', 8, mat)
    # Status lamp and a warning stencil where a crew member would read them.
    p.cyl((W * 0.72, -R_RIM * 0.80, 0.62), 0.10, 0.14, 'X', 10, BLACK)
    p.cyl((W * 0.78, -R_RIM * 0.80, 0.62), 0.07, 0.10, 'X', 10, AMBER)
    p.box((W * 0.72, -R_RIM * 0.40, 0.62), (0.06, 0.50, 0.30), RED)

    if arch:
        # Inspection arch — the Sorter's distinguishing silhouette.
        for sy in (-1, 1):
            p.box((0, sy * 0.86, R_RIM + 0.72), (W * 1.20, 0.18, 1.10), OLIVE)
        p.box((0, 0, R_RIM + 1.30), (W * 1.20, 1.90, 0.20), STEEL)
        p.box((0, 0, R_RIM + 1.16), (W * 0.70, 0.60, 0.16), BLACK)
        p.cyl((0, 0, R_RIM + 1.06), 0.22, 0.10, 'Z', 14, CRT)
        for sx in (-1, 1):
            p.cyl((sx * W * 0.44, 0, R_RIM + 1.08), 0.08, 0.10, 'Z', 8, AMBER)
        # Reject chute falling away off one side.
        p.loft([(0.0, [(-0.50, -0.50), (0.50, -0.50), (0.50, 0.50), (-0.50, 0.50)]),
                (1.30, [(-0.34, -0.34), (0.34, -0.34), (0.34, 0.34), (-0.34, 0.34)])],
               axis='Y', mat=RUST)
    p.bevel(width=0.014, segments=2)
    obj = p.finish(name, coll)
    return obj


def build_hopper(coll, name, mouth=1.45):
    """The funnel the claw drops salvage into, sitting over the top cell."""
    p = Part(PALETTE)
    z0 = R_RIM + 0.10
    # Funnel: square mouth narrowing to the cell bore. Profiles are (x, y).
    p.loft([(z0 + 1.55, [(-mouth, -mouth), (mouth, -mouth),
                         (mouth, mouth), (-mouth, mouth)]),
            (z0 + 0.70, [(-0.86, -0.86), (0.86, -0.86), (0.86, 0.86),
                         (-0.86, 0.86)]),
            (z0 + 0.05, [(-0.52, -0.52), (0.52, -0.52), (0.52, 0.52),
                         (-0.52, 0.52)])], axis='Z', mat=OLIVE, cap=False)
    # Flared lip, striped so the claw has something to aim at.
    for sy in (-1, 1):
        p.box((0, sy * mouth, z0 + 1.58), (mouth * 2.1, 0.16, 0.22), STEEL)
    for sx in (-1, 1):
        p.box((sx * mouth, 0, z0 + 1.58), (0.16, mouth * 2.1, 0.22), STEEL)
    n = 7
    for i in range(0, n, 2):
        x = -mouth + 2 * mouth * (i + 0.5) / n
        p.box((x, -mouth, z0 + 1.70), (2 * mouth / n, 0.20, 0.10), RED)
        p.box((x, mouth, z0 + 1.70), (2 * mouth / n, 0.20, 0.10), RED)
    # Guide vanes inside, and the ram that shakes a jam loose.
    for sx in (-1, 1):
        p.box((sx * 0.70, 0, z0 + 0.90), (0.08, 1.30, 0.80), STEEL,
              rot=Matrix.Rotation(math.radians(sx * 18), 4, 'Y'))
    p.cyl((0, mouth * 0.80, z0 + 0.60), 0.13, 0.70, 'Z', 12, DARK)
    p.cyl((0, mouth * 0.80, z0 + 0.25), 0.07, 0.50, 'Z', 10, CHROME)
    # Feed lamp on the lip.
    p.cyl((0, -mouth * 0.96, z0 + 1.80), 0.10, 0.14, 'Z', 10, BLACK)
    p.cyl((0, -mouth * 0.96, z0 + 1.88), 0.07, 0.08, 'Z', 10, AMBER)
    p.bevel(width=0.013, segments=2)
    return p.finish(name, coll)


def build_gate(coll, name):
    """Discharge door over the lower outboard quadrant. Origin on its hinge."""
    p = Part(PALETTE)
    hinge = (0.0, -R_RIM - 0.18, 0.10)
    n = 9
    outer, inner = R_RIM + 0.20, R_RIM + 0.06
    prof_o = [polar(outer, -8 + 84 * i / (n - 1)) for i in range(n)]
    prof_i = [polar(inner, -8 + 84 * i / (n - 1)) for i in range(n - 1, -1, -1)]
    prof = [(y - hinge[1], z - hinge[2]) for y, z in prof_o + prof_i]
    p.loft([(-W * 0.50, prof), (W * 0.50, prof)], axis='X', mat=HULL)
    # Ribs, a handle and the latch.
    for sx in (-0.32, 0.32):
        rib = [(y * 1.03 - hinge[1], z * 1.03 - hinge[2]) for y, z in prof_o]
        rib += [(y - hinge[1], z - hinge[2]) for y, z in prof_i]
        p.loft([(sx * W - 0.05, rib), (sx * W + 0.05, rib)], axis='X', mat=GREEN)
    p.cyl((0, 0, 0), 0.16, W * 1.02, 'X', 12, STEEL)
    p.cyl((0, 0, 0), 0.08, W * 1.16, 'X', 10, CHROME)
    gy, gz = polar(outer + 0.10, 66)
    p.box((0, gy - hinge[1], gz - hinge[2]), (W * 0.60, 0.30, 0.14), RUST)
    p.bevel(width=0.013, segments=2)
    obj = p.finish(name, coll)
    obj.location = hinge
    return obj


# ---------------------------------------------------------------------------

def variant(name, cells, r_cell, filled, arch=False, rim_thick=0.14):
    coll = collection("Coll_DrumMag_%s" % name)
    build_cradle(coll, "Mesh_DrumMag_%s_Cradle" % name, arch=arch)
    build_drum(coll, "Mesh_DrumMag_%s_Drum" % name, cells, r_cell, filled,
               rim_thick)
    build_hopper(coll, "Mesh_DrumMag_%s_Hopper" % name)
    build_gate(coll, "Mesh_DrumMag_%s_Gate" % name)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    variant("Nine", 9, 0.40, filled={0, 2, 3, 6})
    variant("Six", 6, 0.56, filled={1, 4}, rim_thick=0.20)
    variant("Sorter", 9, 0.40, filled={0, 1, 4, 5, 7}, arch=True)

    print("\nDrum magazine variations:")
    report()
    save(out)


build()
