"""components/structural/awning_shade — tarpaulins pitched over a work area.

Shade is the first thing anyone builds in a desert and the last thing a model
library gets around to, so this fills the gap generally rather than just for one
outpost: free-standing, wall-hung, half-collapsed, torn, and struck.

The cloth is not a flat plane. A tarp pinned at four corners bellies in the
middle *and* along its unsupported edges, and that double sag is most of what
separates a tarpaulin from a table. `cloth()` builds it from an additive
bump — zero droop at the pinned corners, half at the mid-edges, full at the
centre — which is close enough to a real membrane at this scale and costs one
loft.

Origin at ground level, centre of the footprint. `LeanTo` is the exception: its
origin sits on the wall face it hangs off, since that is its connection point.

    blender --background --python awning_shade.py -- --out awning_shade.blend

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
    "Mat_Fabric_Tarp_Azure",     # 0  the bright shade sail
    "Mat_Fabric_Flag_Bleached",  # 1  the sun-killed one
    "Mat_Fabric_Canvas_Faded",   # 2  dirty army canvas, and all the strapping
    "Mat_Metal_Steel_Worn",      # 3  poles and rails
    "Mat_Metal_Steel_Dark",      # 4  feet, clamps, eyelets
    "Mat_Metal_Rust_Heavy",      # 5  the bottom of every pole
    "Mat_Plastic_Rubber_Black",  # 6  guy lines
    "Mat_Paint_Safety_Orange",   # 7  a marker band so guys are visible
]
AZURE, BLEACH, CANVAS, STEEL, DARK, RUST, ROPE, ORANGE = range(8)

NX, NY = 9, 7          # cloth resolution; enough for the sag to read as curved


def cloth(p, w, d, corner_z, sag, mat, thickness=0.022, center=(0, 0),
          tilt=0.0, skew=0.0, umax=0.5):
    """A rectangular membrane pinned at its corners, sagging under its own weight.

    Built as one closed loft: each station across `d` contributes a thin ribbon
    profile that runs out along `w` on the top surface and back along the
    underside, so the sheet comes out solid and watertight rather than as a
    zero-thickness plane Unity would light from one side only.
    """
    cx, cy = center

    def z_at(u, v):
        droop = sag * ((1.0 - (2 * u) ** 2) + (1.0 - (2 * v) ** 2)) * 0.5
        return corner_z - droop + tilt * v + skew * u

    sections = []
    for j in range(NY):
        v = j / (NY - 1.0) - 0.5
        top, bot = [], []
        for i in range(NX):
            u = (i / (NX - 1.0) - 0.5) * (umax / 0.5)
            x = cx + u * w
            z = z_at(u, v)
            top.append((x, z))
            bot.append((x, z - thickness))
        sections.append((cy + v * d, top + list(reversed(bot))))
    return p.loft(sections, axis='Y', mat=mat)


def pole(p, x, y, h, lean=(0.0, 0.0), r=0.036, foot=True):
    """A shade pole. `lean` tips it off vertical, which is what stops four of
    them reading as a printed grid."""
    rot = (Matrix.Rotation(math.radians(lean[0]), 4, 'Y')
           @ Matrix.Rotation(math.radians(lean[1]), 4, 'X'))
    dx = math.sin(math.radians(lean[0])) * h / 2
    dy = -math.sin(math.radians(lean[1])) * h / 2
    p.cyl((x + dx, y + dy, h / 2), r, h, seg=8, mat=STEEL, rot=rot)
    p.cyl((x + dx * 2, y + dy * 2, h - 0.04), r * 1.5, 0.08, seg=8, mat=DARK)
    if foot:
        p.cyl((x, y, 0.03), r * 2.6, 0.06, seg=8, mat=RUST)
        p.cyl((x, y, 0.16), r * 1.3, 0.26, seg=8, mat=RUST)


def guy(p, top, peg, mat=ROPE, r=0.014, n=5):
    """A taut line from a pole head to a ground peg, plus the peg."""
    for i in range(n):
        t0, t1 = i / float(n), (i + 1) / float(n)
        a = [top[k] + (peg[k] - top[k]) * t0 for k in range(3)]
        b = [top[k] + (peg[k] - top[k]) * t1 for k in range(3)]
        mid = [(a[k] + b[k]) / 2 for k in range(3)]
        d = [b[k] - a[k] for k in range(3)]
        ln = math.sqrt(sum(v * v for v in d)) or 1.0
        yaw = math.atan2(d[1], d[0])
        pitch = math.asin(max(-1.0, min(1.0, d[2] / ln)))
        rot = (Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Rotation(-pitch, 4, 'Y'))
        p.cyl(mid, r, ln * 1.02, axis='X', seg=5,
              mat=ORANGE if i == n - 2 else mat, rot=rot)
    p.box((peg[0], peg[1], 0.06), (0.05, 0.05, 0.16), DARK)


def eyelets(p, w, d, z, mat=DARK, n=4, center=(0, 0)):
    """Reinforcing rings around the cloth edge.

    `center` must match the `center` the cloth was built on — a lean-to's sheet
    hangs off the origin rather than around it, and eyelets left on the centred
    rectangle end up inside the wall.
    """
    cx, cy = center
    for sx in (-1, 1):
        for i in range(n):
            t = (i + 0.5) / n - 0.5
            p.cyl((cx + sx * w / 2, cy + t * d, z), 0.026, 0.02, seg=6, mat=mat)
    for sy in (-1, 1):
        for i in range(n):
            t = (i + 0.5) / n - 0.5
            p.cyl((cx + t * w, cy + sy * d / 2, z), 0.026, 0.02, seg=6, mat=mat)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def awning_square(mats, coll):
    """Four poles, one taut azure sail. The clean, freshly-pitched version."""
    p = Part(mats)
    w, d, h = 4.2, 3.5, 2.62
    cloth(p, w, d, h - 0.06, 0.3, AZURE, tilt=-0.1)
    eyelets(p, w, d, h - 0.3)
    leans = ((2.5, 1.5), (-2.0, 2.0), (2.0, -1.5), (-2.5, -2.0))
    i = 0
    for sx in (-1, 1):
        for sy in (-1, 1):
            pole(p, sx * w / 2, sy * d / 2, h, lean=leans[i])
            i += 1
    # Ridge cord across the long axis, which is what holds the centre up at all.
    for k in range(7):
        t = k / 6.0 - 0.5
        p.cyl((t * w, 0, h - 0.03 - 0.06 * (1 - (2 * t) ** 2)), 0.012, w / 6 * 1.02,
              axis='X', seg=5, mat=ROPE)
    guy(p, (-w / 2, -d / 2, h), (-w / 2 - 1.1, -d / 2 - 0.9, 0.1))
    guy(p, (w / 2, d / 2, h), (w / 2 + 1.0, d / 2 + 1.0, 0.1))
    p.bevel(width=0.008)
    return p.finish("Mesh_Awning_Square", coll)


def awning_leanto(mats, coll):
    """Hung off a wall on a bracket rail, sloping down to two front poles.

    Origin sits on the wall face at ground level: this is the one variation
    whose connection point is not its own centre, and placing it anywhere else
    would mean solving for the overhang at every wall it is ever hung on.
    """
    p = Part(mats)
    w, d = 3.7, 3.2
    z_wall, z_front = 2.98, 2.24
    tilt = (z_wall - z_front)          # cloth() reads +v as the wall side
    cloth(p, w, d, (z_wall + z_front) / 2, 0.24, BLEACH,
          center=(0, -d / 2), tilt=tilt)
    # Wall rail and its brackets — the anchor that makes it a lean-to.
    p.box((0, -0.06, z_wall + 0.02), (w + 0.24, 0.09, 0.09), STEEL)
    for i in range(4):
        x = (i / 3.0 - 0.5) * w
        p.box((x, -0.05, z_wall - 0.09), (0.1, 0.14, 0.2), DARK)
        p.box((x, -0.02, z_wall - 0.2), (0.16, 0.06, 0.06), DARK)
        p.cyl((x, -0.12, z_wall + 0.02), 0.03, 0.05, axis='Z', seg=6, mat=DARK)
    for sx in (-1, 1):                                        # front poles, raked out
        pole(p, sx * w / 2, -d, z_front, lean=(sx * 3.0, -4.0))
        guy(p, (sx * w / 2, -d, z_front), (sx * (w / 2 + 0.9), -d - 1.0, 0.1))
        # Diagonal stay back to the wall, so the poles are not doing it alone.
        p.cyl((sx * w / 2 * 0.98, -d * 0.5, (z_front + 0.4) / 2 + 0.3), 0.022,
              math.hypot(d, z_front - 0.5), axis='Y', seg=6, mat=STEEL,
              rot=Matrix.Rotation(math.radians(-30), 4, 'X'))
    eyelets(p, w, d, z_front + 0.1, n=3, center=(0, -d / 2))
    p.bevel(width=0.008)
    return p.finish("Mesh_Awning_LeanTo", coll)


def awning_sagging(mats, coll):
    """Three poles holding four corners. The fourth corner is pegged to the
    ground, so the whole sheet twists — the silhouette event of the set."""
    p = Part(mats)
    w, d, h = 3.6, 3.0, 2.45
    # Skew drags one side down; the corner it belongs to is staked, not poled.
    cloth(p, w, d, h - 0.36, 0.52, CANVAS, tilt=-0.34, skew=-0.62)
    eyelets(p, w, d, h - 0.8, n=3)
    pole(p, -w / 2, -d / 2, h, lean=(6.0, 3.0))
    pole(p, w / 2, -d / 2, h - 0.14, lean=(-4.0, 5.0))
    pole(p, w / 2, d / 2, h - 0.28, lean=(-9.0, -3.0))
    # The staked corner: cloth pulled down to a low peg, with the line showing.
    low = (-w / 2, d / 2, h - 0.36 - 0.62 * -0.5 - 0.34 * 0.5)
    guy(p, low, (-w / 2 - 0.5, d / 2 + 0.55, 0.08), n=4)
    guy(p, (-w / 2, -d / 2, h), (-w / 2 - 1.2, -d / 2 - 0.7, 0.1))
    # A patch and a repair strap across the worst of the sag.
    p.box((0.2, -0.3, h - 0.86), (0.7, 0.6, 0.03), BLEACH)
    for k in range(6):
        t = k / 5.0 - 0.5
        p.cyl((t * w * 0.9, -0.3, h - 0.9 - 0.05 * (1 - (2 * t) ** 2)), 0.014,
              w / 5 * 0.95, axis='X', seg=5, mat=ROPE)
    p.bevel(width=0.008)
    return p.finish("Mesh_Awning_Sagging", coll)


def awning_torn(mats, coll):
    """One corner ripped clean off, the loose edge hanging. Damage, not decay."""
    p = Part(mats)
    w, d, h = 4.0, 3.3, 2.55
    # Main sheet stops short of the fourth pole — the tear line.
    cloth(p, w, d, h - 0.1, 0.34, AZURE, umax=0.34, tilt=-0.12)
    # The freed flap, hanging down off the surviving edge in three folds.
    for k in range(3):
        t = k / 2.0
        ang = 62 + k * 14
        p.box((w * 0.32 + 0.26 + k * 0.2, -d * 0.18 + k * 0.12,
               h - 0.42 - k * 0.52),
              (0.62, 1.5 - k * 0.3, 0.024), AZURE,
              rot=(Matrix.Rotation(math.radians(ang), 4, 'Y')
                   @ Matrix.Rotation(math.radians(9 * k), 4, 'X')))
    eyelets(p, w * 0.68, d, h - 0.4, n=3)
    pole(p, -w / 2, -d / 2, h, lean=(3.0, 2.0))
    pole(p, -w / 2, d / 2, h - 0.08, lean=(2.0, -3.0))
    pole(p, w / 2, -d / 2, h - 0.2, lean=(-7.0, 4.0))
    # The fourth pole survived the tear but now holds nothing — it leans hard.
    pole(p, w / 2, d / 2, h - 0.3, lean=(-17.0, -8.0))
    p.cyl((w * 0.36, d * 0.42, h - 0.5), 0.012, 0.9, axis='X', seg=5, mat=ROPE,
          rot=Matrix.Rotation(math.radians(-28), 4, 'Y'))     # snapped line
    guy(p, (-w / 2, d / 2, h), (-w / 2 - 1.1, d / 2 + 0.8, 0.1))
    p.bevel(width=0.008)
    return p.finish("Mesh_Awning_Torn", coll)


def awning_frame(mats, coll):
    """Struck: the frame is up, the cloth is rolled and lashed to the ridge.

    Useful on its own as scaffolding, and the only variation with no membrane —
    which is what makes a row of these read as a camp being packed up.
    """
    p = Part(mats)
    w, d, h = 4.0, 3.3, 2.6
    for sx in (-1, 1):
        for sy in (-1, 1):
            pole(p, sx * w / 2, sy * d / 2, h, lean=(sx * -1.5, sy * -1.5))
    for sy in (-1, 1):                                        # eaves rails
        p.cyl((0, sy * d / 2, h - 0.02), 0.03, w, axis='X', seg=8, mat=STEEL)
    for sx in (-1, 1):
        p.cyl((sx * w / 2, 0, h - 0.02), 0.03, d, axis='Y', seg=8, mat=STEEL)
    p.cyl((0, 0, h + 0.06), 0.032, w, axis='X', seg=8, mat=STEEL)   # ridge
    for sy in (-1, 1):                                        # knee braces
        for sx in (-1, 1):
            p.box((sx * (w / 2 - 0.26), sy * (d / 2 - 0.02), h - 0.3),
                  (0.5, 0.04, 0.04), STEEL,
                  rot=Matrix.Rotation(math.radians(-sx * 42), 4, 'Y'))
    # The rolled tarp, lashed along the ridge with three ties.
    p.cyl((0.1, 0, h + 0.2), 0.17, w * 0.82, axis='X', seg=12, mat=AZURE)
    p.cyl((0.1, 0, h + 0.2), 0.13, w * 0.86, axis='X', seg=10, mat=BLEACH)
    for i in range(3):
        x = (i - 1) * w * 0.28 + 0.1
        p.torus((x, 0, h + 0.17), 0.19, 0.018, axis='X', maj_seg=12, min_seg=5,
                mat=CANVAS)
    for sx in (-1, 1):                                        # slack lines
        for k in range(4):
            t = k / 3.0
            p.cyl((sx * w / 2, d / 2 * (1 - t * 0.4), h - 0.1 - t * 0.9), 0.012,
                  0.34, axis='Z', seg=5, mat=ROPE,
                  rot=Matrix.Rotation(math.radians(24), 4, 'X'))
    p.bevel(width=0.008)
    return p.finish("Mesh_Awning_Frame", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Square", awning_square), ("LeanTo", awning_leanto),
                     ("Sagging", awning_sagging), ("Torn", awning_torn),
                     ("Frame", awning_frame)):
        fn(mats, collection("Coll_Awning_%s" % name))
    report()
    save(out)


main()
