"""components/props/field_bench — the kit that turns a patch of shade into work.

An awning over bare sand is a tent. An awning over a bench with a vice, a
running genset and a half-unwound cable reel is a repair bay, and that reading
is entirely carried by these five props.

`props/console_panel` and `props/wall_locker` already cover the indoor,
bolted-to-a-bulkhead end of this. Everything here is free-standing, dragged
outside, and stands on its own legs — which is why it is a separate component
rather than more variations on those.

Origin at the bottom centre of the footprint.

    blender --background --python field_bench.py -- --out field_bench.blend

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
    "Mat_Metal_Steel_Worn",      # 0  frames, legs, trestles
    "Mat_Metal_Steel_Dark",      # 1  tool bodies, brackets, fasteners
    "Mat_Wood_Ply_Worn",         # 2  bench tops and reel cheeks
    "Mat_Metal_Rust_Heavy",      # 3  weathering at joints and feet
    "Mat_Paint_Roof_Green",      # 4  the genset shell
    "Mat_Paint_Safety_Orange",   # 5  hazard marking, cable jacket
    "Mat_Plastic_Rubber_Black",  # 6  hose, grips, cable
    "Mat_Metal_Chrome_Scuffed",  # 7  vice screw, bright tooling
    "Mat_Neutral_Black_Matte",   # 8  shadow gaps, vents
    "Mat_Plastic_Cream_Aged",    # 9  moulded cases and handles
    "Mat_Emissive_Amber",        # 10 the genset's running lamp
]
STEEL, DARK, PLY, RUST, GREEN, ORANGE, RUBBER, CHROME, BLACK, PLASTIC, AMBER = range(11)


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 4, 'Z')


def tube_leg(p, x, y, h, mat=STEEL, r=0.028):
    p.cyl((x, y, h / 2), r, h, seg=8, mat=mat)
    p.cyl((x, y, 0.012), r * 1.5, 0.024, seg=8, mat=RUST)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def bench_table(mats, coll):
    """The workbench: ply top on a steel frame, a vice, tools left out."""
    p = Part(mats)
    w, d, h = 2.04, 0.78, 0.9
    p.box((0, 0, h - 0.03), (w, d, 0.06), PLY)                      # top
    p.box((0, 0, h - 0.075), (w - 0.06, d - 0.06, 0.03), STEEL)     # top frame
    p.box((0, -d / 2 + 0.03, h + 0.09), (w, 0.04, 0.18), PLY)       # backboard
    for sx in (-1, 1):                                              # A-frame legs
        for sy in (-1, 1):
            tube_leg(p, sx * (w / 2 - 0.1), sy * (d / 2 - 0.09), h - 0.06)
        p.box((sx * (w / 2 - 0.1), 0, 0.24), (0.04, d - 0.18, 0.04), STEEL)
    p.box((0, 0, 0.26), (w - 0.2, 0.05, 0.05), STEEL)               # stretcher
    p.box((0, 0.06, 0.34), (w * 0.62, d * 0.62, 0.04), PLY)         # under-shelf
    # Vice, bolted to the near-left corner and standing proud of the edge.
    vx, vy = -w * 0.34, d * 0.34
    p.box((vx, vy, h + 0.06), (0.2, 0.16, 0.09), DARK)
    p.box((vx, vy - 0.11, h + 0.08), (0.2, 0.06, 0.13), DARK)
    p.cyl((vx, vy - 0.24, h + 0.08), 0.022, 0.3, axis='Y', seg=8, mat=CHROME)
    p.cyl((vx, vy - 0.4, h + 0.08), 0.05, 0.03, axis='Y', seg=8, mat=DARK)
    # Tools and offcuts scattered across the top.
    p.box((w * 0.16, -0.06, h + 0.03), (0.34, 0.1, 0.03), DARK)
    p.box((w * 0.3, 0.14, h + 0.04), (0.12, 0.26, 0.04), CHROME)
    p.cyl((w * 0.36, -0.16, h + 0.05), 0.055, 0.09, seg=10, mat=PLASTIC)
    p.greeble((-w * 0.12, -d * 0.3, h + 0.03), (w * 0.44, d * 0.3, h + 0.07),
              6, seed=17, scale=(0.05, 0.14), mat=DARK)
    # A hose coiled over the backboard hook.
    for i in range(10):
        a = 2 * math.pi * i / 10
        p.cyl((-w * 0.42 + 0.11 * math.cos(a), -d / 2 + 0.09,
               h + 0.14 + 0.11 * math.sin(a)), 0.018, 0.07, axis='Y', seg=6,
              mat=RUBBER)
    p.bevel(width=0.01)
    return p.finish("Mesh_FieldBench_Table", coll)


def bench_toolrack(mats, coll):
    """Tall and thin — a pegboard on a stand. Reads at a distance as vertical."""
    p = Part(mats)
    w, h = 1.16, 1.72
    for sx in (-1, 1):
        p.box((sx * w / 2, 0, h / 2), (0.05, 0.05, h), STEEL)
        p.box((sx * w / 2, 0, 0.03), (0.24, 0.44, 0.06), DARK)      # foot plate
        p.box((sx * (w / 2 - 0.06), 0.2, 0.5), (0.04, 0.4, 0.04), STEEL,
              rot=Matrix.Rotation(math.radians(38), 4, 'X'))        # rear brace
    p.box((0, 0.02, h * 0.62), (w, 0.03, h * 0.66), PLY)            # board
    p.box((0, 0, h - 0.03), (w + 0.06, 0.06, 0.06), STEEL)          # top rail
    p.box((0, 0, 0.34), (w - 0.1, 0.05, 0.05), STEEL)               # low rail
    p.box((0, 0.06, 0.5), (w - 0.14, 0.34, 0.04), PLY)              # tray
    # Hanging tools: bars, a coil, a saw. Different lengths, staggered.
    for i, (ln, rad, mat) in enumerate(((0.42, 0.02, CHROME), (0.3, 0.026, DARK),
                                        (0.5, 0.018, STEEL), (0.36, 0.03, DARK),
                                        (0.26, 0.022, CHROME))):
        x = (i / 4 - 0.5) * (w - 0.2)
        p.cyl((x, -0.03, h * 0.9 - ln / 2), rad, ln, seg=6, mat=mat)
        p.cyl((x, -0.03, h * 0.9), rad * 1.6, 0.03, seg=6, mat=DARK)
    p.box((w * 0.3, -0.05, h * 0.52), (0.36, 0.02, 0.16), CHROME)   # saw blade
    p.box((w * 0.3 - 0.2, -0.05, h * 0.52), (0.1, 0.04, 0.08), PLASTIC)
    for i in range(8):                                              # coiled rope
        a = 2 * math.pi * i / 8
        p.cyl((-w * 0.3 + 0.1 * math.cos(a), -0.05,
               h * 0.44 + 0.1 * math.sin(a)), 0.018, 0.06, axis='Y', seg=6,
              mat=RUBBER)
    p.box((0, 0.05, 0.52), (0.3, 0.2, 0.1), PLASTIC)                # a case on the tray
    p.bevel(width=0.009)
    return p.finish("Mesh_FieldBench_ToolRack", coll)


def bench_generator(mats, coll):
    """A portable genset on a skid — squat, loud, and the reason there is power."""
    p = Part(mats)
    w, d, h = 1.06, 0.62, 0.76
    p.box((0, 0, 0.05), (w + 0.12, d + 0.1, 0.1), STEEL)            # skid
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * (w / 2 + 0.02), sy * (d / 2 + 0.02), 0.03),
                  (0.1, 0.1, 0.06), RUBBER)                         # anti-vib feet
    p.box((0, 0, 0.1 + (h - 0.1) / 2), (w, d, h - 0.1), GREEN)      # shell
    p.box((0, 0, h - 0.03), (w - 0.1, d - 0.1, 0.06), GREEN)        # crown
    # Roll cage — what makes it read as portable rather than installed.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cyl((sx * (w / 2 + 0.05), sy * (d / 2 + 0.05), h / 2 + 0.1),
                  0.022, h, seg=6, mat=STEEL)
    for sy in (-1, 1):
        p.cyl((0, sy * (d / 2 + 0.05), h + 0.08), 0.022, w + 0.1, axis='X',
              seg=6, mat=STEEL)
    for sx in (-1, 1):
        p.cyl((sx * (w / 2 + 0.05), 0, h + 0.08), 0.022, d + 0.1, axis='Y',
              seg=6, mat=STEEL)
    p.louvres((-w * 0.34, d / 2 - 0.02, 0.26), (w * 0.34, d / 2 + 0.01, 0.62),
              6, mat=BLACK)                                         # cooling face
    p.box((-w * 0.42, -d / 2 - 0.012, 0.5), (0.22, 0.03, 0.3), DARK)  # panel
    p.cyl((-w * 0.42, -d / 2 - 0.03, 0.56), 0.035, 0.03, axis='Y', seg=10,
          mat=CHROME)                                               # gauge
    p.cyl((-w * 0.42, -d / 2 - 0.03, 0.44), 0.018, 0.03, axis='Y', seg=8,
          mat=AMBER)                                                # running lamp
    p.box((w * 0.34, -d / 2 - 0.012, 0.42), (0.2, 0.03, 0.16), BLACK)  # sockets
    # Exhaust: up the back and elbowed over, so it breaks the box silhouette.
    p.cyl((w * 0.3, d * 0.28, h + 0.16), 0.05, 0.34, seg=10, mat=RUST)
    p.cyl((w * 0.3, d * 0.05, h + 0.31), 0.05, 0.5, axis='Y', seg=10, mat=RUST)
    p.cyl((w * 0.3, d * 0.05 - 0.28, h + 0.31), 0.062, 0.08, axis='Y', seg=10,
          mat=DARK)
    p.box((0, 0, h + 0.14), (0.3, 0.24, 0.1), DARK)                 # fuel cap boss
    p.cyl((0, 0, h + 0.2), 0.06, 0.04, seg=10, mat=CHROME)
    # A pull-start handle and a fuel can leaning on the skid.
    p.cyl((-w / 2 - 0.09, 0, 0.42), 0.05, 0.06, axis='X', seg=10, mat=DARK)
    p.box((w * 0.52, -d * 0.62, 0.22), (0.22, 0.34, 0.36), PLASTIC, rot=rot_z(14))
    p.bevel(width=0.01)
    return p.finish("Mesh_FieldBench_Generator", coll)


def bench_reel(mats, coll):
    """A cable drum on an A-frame, half unwound — the one round silhouette."""
    p = Part(mats)
    rr, wr = 0.46, 0.46
    z = rr + 0.2
    p.cyl((0, 0, z), rr, 0.05, axis='Y', seg=22, mat=PLY)           # cheeks
    p.cyl((0, 0, z), rr, 0.05, axis='Y', seg=22, mat=PLY,
          rot=None)
    for sy in (-1, 1):
        p.cyl((0, sy * wr / 2, z), rr, 0.055, axis='Y', seg=22, mat=PLY)
        for i in range(6):                                          # cheek ribs
            a = 2 * math.pi * i / 6
            p.box((rr * 0.55 * math.cos(a), sy * (wr / 2 + 0.035),
                   z + rr * 0.55 * math.sin(a)), (0.06, 0.03, rr * 0.8),
                  DARK, rot=Matrix.Rotation(a - math.pi / 2, 4, 'Y'))
    p.cyl((0, 0, z), rr * 0.34, wr, axis='Y', seg=16, mat=STEEL)    # hub
    p.cyl((0, 0, z), rr * 0.82, wr - 0.1, axis='Y', seg=20, mat=RUBBER)  # cable wound
    p.cyl((0, 0, z), 0.035, wr + 0.5, axis='Y', seg=10, mat=STEEL)  # axle
    for sy in (-1, 1):                                              # A-frame stand
        y = sy * (wr / 2 + 0.16)
        for sx in (-1, 1):
            p.box((sx * 0.3, y, z / 2), (0.05, 0.05, z * 1.12), STEEL,
                  rot=Matrix.Rotation(math.radians(-sx * 24), 4, 'Y'))
        p.box((0, y, 0.03), (0.78, 0.1, 0.06), DARK)
        p.box((0, y, z * 0.42), (0.5, 0.05, 0.05), STEEL)
    # The unwound run: cable off the drum, curving down onto the ground.
    n = 9
    for i in range(n):
        t = i / (n - 1.0)
        x = -rr - 0.1 - t * 1.15
        cz = z - rr * 0.1 - (z - 0.03) * (t ** 1.7)
        p.cyl((x, wr * 0.1, max(cz, 0.03)), 0.028, 0.2, axis='X', seg=6,
              mat=ORANGE if i % 2 else RUBBER)
    p.box((-rr - 1.34, wr * 0.1, 0.05), (0.16, 0.12, 0.1), BLACK)   # plug end
    p.bevel(width=0.01)
    return p.finish("Mesh_FieldBench_Reel", coll)


def bench_sawhorse(mats, coll):
    """A trestle with stock laid across it. Low, open, mostly negative space."""
    p = Part(mats)
    w, d, h = 1.32, 0.56, 0.72
    for sy in (-1, 1):                                              # splayed legs
        for sx in (-1, 1):
            p.box((sx * (w / 2 - 0.12), sy * (d / 2 - 0.05), h / 2),
                  (0.06, 0.06, h * 1.04), STEEL,
                  rot=Matrix.Rotation(math.radians(-sx * 11), 4, 'Y'))
        p.box((0, sy * (d / 2 - 0.05), 0.28), (w * 0.62, 0.04, 0.04), STEEL)
    p.box((0, 0, h - 0.04), (w, 0.1, 0.08), PLY)                    # top rail
    p.box((0, 0, h - 0.09), (w - 0.16, 0.16, 0.05), STEEL)
    for sx in (-1, 1):                                              # feet
        for sy in (-1, 1):
            p.box((sx * (w / 2 - 0.05), sy * (d / 2 - 0.05), 0.02),
                  (0.12, 0.12, 0.04), RUST)
    # Stock across the top: two pipes and a plate, none of them square to it.
    p.cyl((0.1, -0.14, h + 0.09), 0.075, 1.86, axis='X', seg=12, mat=RUST,
          rot=rot_z(6))
    p.cyl((0.02, 0.1, h + 0.09), 0.055, 1.62, axis='X', seg=12, mat=STEEL,
          rot=rot_z(-4))
    p.box((-0.24, 0.22, h + 0.05), (0.9, 0.22, 0.03), CHROME, rot=rot_z(9))
    p.box((0.5, -0.02, h + 0.19), (0.14, 0.4, 0.06), ORANGE)        # a clamp
    p.bevel(width=0.01)
    return p.finish("Mesh_FieldBench_Sawhorse", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Table", bench_table), ("ToolRack", bench_toolrack),
                     ("Generator", bench_generator), ("Reel", bench_reel),
                     ("Sawhorse", bench_sawhorse)):
        fn(mats, collection("Coll_FieldBench_%s" % name))
    report()
    save(out)


main()
