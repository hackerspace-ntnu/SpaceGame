"""components/structural/prefab_hab — single-storey prefab blocks, ground up.

The gap between `structural/cabin_module` and `structural/tower_bay`: cabin
modules are converted containers you crane onto a deck, tower bays are 14 m
storeys that stack. Neither is a building you walk into off the sand, which is
what a desert outpost, a depot office or a field workshop actually needs.

Everything here shares one language — a graded plinth, ribbed wall panels above
a waist band, a flat roof behind a parapet — and then varies its silhouette:
long, short, mono-pitch lean-to, shuttered garage, and an L-plan with two
different roof heights. Repeated across a settlement they read as the same
prefab system delivered in different lengths, which is exactly what a prefab
system looks like.

Origin at ground level, centre of the footprint. Roof heights are round numbers
so a `station_tower` section lands on them without arithmetic.

    blender --background --python prefab_hab.py -- --out prefab_hab.blend

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
    "Mat_Paint_Blue_Station",    # 0  the hull skin
    "Mat_Paint_White_Arctic",    # 1  bands, parapet caps, roof plant
    "Mat_Metal_Steel_Worn",      # 2  frames, posts, brackets
    "Mat_Metal_Steel_Dark",      # 3  fittings, trays, bolts
    "Mat_Neutral_Slate_Dark",    # 4  the plinth and every recess
    "Mat_Metal_Rust_Heavy",      # 5  streaks below fittings, plinth rot
    "Mat_Metal_HullRust_Orange", # 6  the oxidised accent band
    "Mat_Glass_Canopy_Tinted",   # 7  windows
    "Mat_Neutral_Black_Matte",   # 8  shadow gaps behind grilles and doorways
    "Mat_Paint_Warn_Red",        # 9  stencils and hazard marks
    "Mat_Paint_Roof_Green",      # 10 the older paint layer showing through
]
BLUE, WHITE, STEEL, DARK, SLATE, RUST, ORANGE, GLASS, BLACK, RED, GREEN = range(11)

BAND = 2.42          # waist band height — constant across the family
PLINTH = 0.34


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 4, 'Z')


def plinth(p, w, d, h=PLINTH):
    """The graded base every block sits on. Dark, so the blue starts above it."""
    p.box((0, 0, h / 2), (w + 0.3, d + 0.3, h), SLATE)
    p.box((0, 0, 0.06), (w + 0.62, d + 0.62, 0.12), DARK)
    p.box((0, 0, h - 0.04), (w + 0.36, d + 0.36, 0.08), STEEL)
    for sy in (-1, 1):                                        # rot at the wet edge
        p.box((0, sy * (d + 0.3) / 2, 0.14), (w * 0.9, 0.04, 0.16), RUST)


def body(p, w, d, z0, z1, mat=BLUE):
    p.box((0, 0, (z0 + z1) / 2), (w, d, z1 - z0), mat)


def band(p, w, d, z=BAND, t=0.2, mat=WHITE, out=0.05):
    p.box((0, 0, z), (w + out, d + out, t), mat)
    p.box((0, 0, z + t / 2 + 0.03), (w + out * 1.4, d + out * 1.4, 0.06), STEEL)


def ribs(p, w, d, z0, z1, pitch=0.62, mat=BLUE, out=0.05):
    """Vertical panel ribs — the read of pressed sheet rather than a cast block."""
    nx, ny = max(2, int(w / pitch)), max(2, int(d / pitch))
    for i in range(nx):
        x = (i + 0.5) / nx * w - w / 2
        for sy in (-1, 1):
            p.box((x, sy * (d / 2 + out / 2), (z0 + z1) / 2),
                  (0.1, out, z1 - z0 - 0.1), mat)
    for i in range(ny):
        y = (i + 0.5) / ny * d - d / 2
        for sx in (-1, 1):
            p.box((sx * (w / 2 + out / 2), y, (z0 + z1) / 2),
                  (out, 0.1, z1 - z0 - 0.1), mat)


def posts(p, w, d, z0, z1, s=0.3, mat=WHITE):
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * (w / 2 - s / 2 + 0.03), sy * (d / 2 - s / 2 + 0.03),
                   (z0 + z1) / 2), (s, s, z1 - z0), mat)


def roof(p, w, d, z, thick=0.2, over=0.16, para=0.4):
    """Deck slab, overhang drip, and a parapet lip with a capping rail."""
    p.box((0, 0, z + thick / 2), (w + over, d + over, thick), WHITE)
    p.box((0, 0, z + 0.03), (w + over * 1.5, d + over * 1.5, 0.07), STEEL)
    for sy in (-1, 1):
        p.box((0, sy * (d + over) / 2, z + thick + para / 2),
              (w + over, 0.14, para), WHITE)
        p.box((0, sy * (d + over) / 2, z + thick + para), (w + over, 0.2, 0.08),
              STEEL)
    for sx in (-1, 1):
        p.box((sx * (w + over) / 2, 0, z + thick + para / 2),
              (0.14, d + over - 0.28, para), WHITE)
        p.box((sx * (w + over) / 2, 0, z + thick + para),
              (0.2, d + over - 0.28, 0.08), STEEL)
    p.box((0, 0, z + thick + 0.02), (w - 0.1, d - 0.1, 0.04), SLATE)  # deck felt


def door_bay(p, x, y_face, z0, mat_frame=STEEL, w=1.7, h=2.42, deep=0.34):
    """A recessed entrance with a threshold and a lamp over it.

    `y_face` is the outer face plane; the recess is cut inward from it. Passing
    a signed value places it on either long face without a second code path.
    Only long (Y-facing) walls — an end wall wants `window`'s normal argument.
    """
    s = 1 if y_face > 0 else -1
    p.box((x, y_face - s * deep / 2, z0 + h / 2), (w, deep, h), BLACK)
    p.box((x, y_face - s * deep, z0 + h / 2), (w - 0.2, 0.1, h - 0.16), SLATE)
    p.box((x, y_face + s * 0.04, z0 + h + 0.16), (w + 0.36, 0.12, 0.3),
          mat_frame)                                          # head
    for sx in (-1, 1):
        p.box((x + sx * (w / 2 + 0.1), y_face + s * 0.04, z0 + h / 2),
              (0.2, 0.12, h + 0.3), mat_frame)
    p.box((x, y_face - s * 0.1, z0 + 0.05), (w + 0.3, 0.5, 0.1), DARK)
    p.box((x, y_face + s * 0.2, z0 + h + 0.34), (0.44, 0.34, 0.14), DARK)
    # Stencilled bay number, painted flat on the wall beside the opening.
    p.box((x + w * 0.72, y_face + s * 0.02, z0 + h * 0.62),
          (w * 0.34, 0.02, 0.34), RED)


def window(p, px, py, z, n=(0, -1), w=1.1, h=0.86, sill=True):
    """A framed window on the wall plane through (px, py) facing outward `n`.

    The face normal is explicit because these blocks have end walls as well as
    long ones, and a helper that only ever builds -Y windows quietly places an
    end-wall one out in mid-air.
    """
    nx, ny = n
    th = math.atan2(-nx, ny)             # rotation taking local +Y onto n
    c, s = math.cos(th), math.sin(th)
    rot = Matrix.Rotation(th, 4, 'Z')

    def at(u, v, dz=0.0):
        return (px + u * c - v * s, py + u * s + v * c, z + dz)

    p.box(at(0, -0.06), (w + 0.24, 0.16, h + 0.24), STEEL, rot=rot)
    p.box(at(0, 0.02), (w, 0.06, h), GLASS, rot=rot)
    p.box(at(0, 0.02), (0.07, 0.09, h), DARK, rot=rot)        # mullion
    if sill:
        p.box(at(0, 0.06, -h / 2 - 0.12), (w + 0.34, 0.22, 0.1), STEEL, rot=rot)
        p.box(at(0, 0.02, -h / 2 - 0.5), (w * 0.7, 0.03, 0.7), RUST, rot=rot)


def roof_kit(p, w, d, z, seed=3, xlo=-0.5, xhi=0.5):
    """Plant, hatches, trays and a stack — what makes a flat roof read as used.

    All of it is confined to the X band [`xlo`, `xhi`] given as fractions of
    `w`. A block that carries a `station_tower` has to keep one half of its roof
    clear, and clutter scattered across the full width would have the tower
    plinth landing on top of a vent cowl.
    """
    def X(t):                       # t in [-0.5, 0.5] over the band, not the roof
        return (xlo + (t + 0.5) * (xhi - xlo)) * w

    bw = (xhi - xlo) * w
    p.box((X(-0.16), d * 0.1, z + 0.42), (bw * 0.34, d * 0.34, 0.72), WHITE)
    p.box((X(-0.16), d * 0.1, z + 0.82), (bw * 0.36, d * 0.36, 0.1), STEEL)
    for i in range(3):
        p.box((X(-0.16) - bw * 0.1 + i * bw * 0.1, d * 0.1 - d * 0.19,
               z + 0.42), (bw * 0.06, 0.05, 0.5), SLATE)
    p.cyl((X(0.3), d * 0.24, z + 0.34), 0.44, 0.56, seg=14, mat=STEEL)
    p.cyl((X(0.3), d * 0.24, z + 0.66), 0.5, 0.1, seg=14, mat=DARK)
    p.cyl((X(0.3), -d * 0.02, z + 0.5), 0.16, 0.9, seg=10, mat=RUST)   # stack
    p.cyl((X(0.3), -d * 0.02, z + 0.96), 0.21, 0.08, seg=10, mat=DARK)
    p.box((X(0.06), -d * 0.26, z + 0.14), (1.0, 1.0, 0.22), STEEL)     # hatch
    p.box((X(0.06), -d * 0.26, z + 0.27), (0.88, 0.88, 0.06), DARK)
    p.cyl((X(0.06) + 0.3, -d * 0.26, z + 0.32), 0.05, 0.06, seg=8, mat=DARK)
    for i in range(max(1, int(bw / 1.4))):                    # cable tray run
        x = X(-0.5) + 0.7 + i * 1.4
        p.box((x, -d * 0.4, z + 0.2), (1.3, 0.34, 0.06), DARK)
        p.box((x, -d * 0.4, z + 0.1), (0.1, 0.34, 0.2), STEEL)
    p.greeble((X(-0.44), -d * 0.12, z + 0.16), (X(0.44), d * 0.42, z + 0.34),
              6, seed=seed, scale=(0.2, 0.5), mat=SLATE)


def scupper(p, w, d, z_roof, z0=PLINTH, sx=1, sy=1):
    """A downpipe off one corner — the vertical that breaks a flat elevation."""
    x, y = sx * (w / 2 + 0.12), sy * (d / 2 + 0.12)
    p.cyl((x, y, (z0 + z_roof) / 2), 0.09, z_roof - z0, seg=8, mat=STEEL)
    for k in range(3):
        p.box((x, y, z0 + (k + 0.5) / 3 * (z_roof - z0)), (0.26, 0.26, 0.08),
              DARK)
    p.cyl((x, y, z_roof + 0.08), 0.13, 0.16, seg=8, mat=DARK)
    p.cyl((x, y, z0 + 0.1), 0.11, 0.2, seg=8, mat=RUST)
    p.box((x, y, z0 - 0.04), (0.5, 0.5, 0.1), SLATE)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def hab_long(mats, coll):
    """13 x 10 m, roof deck at exactly 4.0 — the outpost's base block."""
    p = Part(mats)
    w, d, z = 13.0, 10.0, 4.0
    plinth(p, w, d)
    body(p, w, d, PLINTH, z)
    ribs(p, w, d, BAND + 0.2, z - 0.12)
    band(p, w, d)
    p.box((0, 0, (PLINTH + BAND) / 2 + 0.1), (w + 0.03, d + 0.03,
                                              BAND - PLINTH - 0.4), BLUE)
    posts(p, w, d, PLINTH, z)
    roof(p, w, d, z)
    door_bay(p, -w * 0.2, -d / 2, PLINTH)
    window(p, w * 0.16, -d / 2, 3.1, (0, -1))
    window(p, w * 0.34, -d / 2, 3.1, (0, -1), w=0.8)
    window(p, -w * 0.4, -d / 2, 3.1, (0, -1), w=0.8)
    window(p, w / 2, -d * 0.24, 3.06, (1, 0), w=1.3)          # end wall, +X face
    # A louvred plant bay low on the far long face, backed dark.
    p.box((w * 0.2, d / 2 + 0.03, 1.5), (3.0, 0.14, 1.7), SLATE)
    for k in range(7):
        p.box((w * 0.2, d / 2 + 0.09, 0.82 + k * 0.22), (2.86, 0.08, 0.1), STEEL)
    p.box((-w * 0.26, d / 2 + 0.06, 1.3), (1.5, 0.2, 1.4), DARK)   # service panel
    p.box((-w * 0.26, d / 2 + 0.17, 1.3), (1.2, 0.03, 1.1), GREEN)
    # Accent band low down, the paint layer under the blue.
    p.box((0, 0, 0.72), (w + 0.06, d + 0.06, 0.16), ORANGE)
    # Plant confined to the -X half: this block is the one that carries a
    # tower, and the tower lands on the +X half.
    roof_kit(p, w, d, z + 0.2, xlo=-0.5, xhi=0.04)
    scupper(p, w, d, z + 0.2, sx=-1, sy=1)
    scupper(p, w, d, z + 0.2, sx=1, sy=-1)
    p.bevel(width=0.016)
    return p.finish("Mesh_PrefabHab_Long", coll)


def hab_short(mats, coll):
    """7.4 x 6.2 m, roof at 3.6. The same system, one delivery shorter."""
    p = Part(mats)
    w, d, z = 7.4, 6.2, 3.6
    plinth(p, w, d)
    body(p, w, d, PLINTH, z)
    ribs(p, w, d, BAND + 0.2, z - 0.12, pitch=0.56)
    band(p, w, d)
    posts(p, w, d, PLINTH, z)
    roof(p, w, d, z, para=0.34)
    door_bay(p, w * 0.24, -d / 2, PLINTH, w=1.5)
    window(p, -w * 0.22, -d / 2, 2.96, (0, -1), w=1.2)
    window(p, 0, d / 2, 2.96, (0, 1), w=1.4)
    window(p, -w / 2, d * 0.18, 2.96, (-1, 0), w=0.9)         # end wall, -X face
    p.box((-w * 0.3, d / 2 + 0.03, 1.34), (1.8, 0.14, 1.3), SLATE)
    for k in range(5):
        p.box((-w * 0.3, d / 2 + 0.09, 0.86 + k * 0.24), (1.7, 0.08, 0.1), STEEL)
    p.box((0, 0, 0.68), (w + 0.06, d + 0.06, 0.14), ORANGE)
    p.cyl((w * 0.28, d * 0.2, z + 0.5), 0.36, 0.5, seg=12, mat=STEEL)
    p.cyl((w * 0.28, d * 0.2, z + 0.78), 0.42, 0.08, seg=12, mat=DARK)
    p.box((-w * 0.2, -d * 0.16, z + 0.32), (1.6, 1.4, 0.44), WHITE)
    p.box((-w * 0.2, -d * 0.16, z + 0.55), (1.7, 1.5, 0.07), STEEL)
    scupper(p, w, d, z + 0.2, sx=1, sy=1)
    p.bevel(width=0.016)
    return p.finish("Mesh_PrefabHab_Short", coll)


def hab_annex(mats, coll):
    """A 5.2 x 4.0 lean-to with a mono-pitch roof — the only sloped silhouette.

    Built as a wedge rather than a box with a tilted lid: the wall ribs, the
    parapet and the gutter all have to follow the fall, and none of that
    survives being modelled flat and rotated.
    """
    p = Part(mats)
    w, d = 5.2, 4.0
    z_hi, z_lo = 3.06, 2.24
    plinth(p, w, d, 0.26)
    # Wedge body: a trapezoid extruded along X.
    prof = [(-d / 2, 0.26), (d / 2, 0.26), (d / 2, z_hi), (-d / 2, z_lo)]
    p.shade(p.prism(prof, w, axis='X', mat=BLUE), False)
    for sy, zz in ((-1, z_lo), (1, z_hi)):                    # eaves and ribs
        p.box((0, sy * (d / 2 + 0.02), (0.26 + zz) / 2), (w + 0.04, 0.05,
                                                          zz - 0.4), BLUE)
    for i in range(8):
        x = (i + 0.5) / 8 * w - w / 2
        for sy in (-1, 1):
            p.box((x, sy * (d / 2 + 0.03), (0.26 + (z_lo if sy < 0 else z_hi)) / 2),
                  (0.09, 0.05, (z_lo if sy < 0 else z_hi) - 0.5), BLUE)
    # Sloped roof slab, pitched to match the wedge.
    pitch = math.atan2(z_hi - z_lo, d)
    p.box((0, 0, (z_hi + z_lo) / 2 + 0.09), (w + 0.3, d / math.cos(pitch) + 0.2,
                                             0.16), WHITE,
          rot=Matrix.Rotation(-pitch, 4, 'X'))
    p.box((0, -d / 2 - 0.12, z_lo + 0.02), (w + 0.3, 0.2, 0.16), STEEL)  # gutter
    p.cyl((w / 2 - 0.2, -d / 2 - 0.12, (0.26 + z_lo) / 2), 0.07, z_lo - 0.26,
          seg=8, mat=STEEL)
    p.box((0, d / 2 + 0.1, z_hi + 0.24), (w + 0.3, 0.14, 0.34), WHITE)   # upstand
    posts(p, w, d, 0.26, z_lo)
    door_bay(p, -w * 0.16, -d / 2, 0.26, w=1.4, h=2.0, deep=0.24)
    window(p, w * 0.26, -d / 2, 1.6, (0, -1), w=0.9, h=0.7)
    p.box((0, 0, 0.56), (w + 0.06, d + 0.06, 0.12), ORANGE)
    p.box((w * 0.3, d * 0.1, z_hi + 0.34), (0.9, 0.8, 0.3), SLATE,
          rot=Matrix.Rotation(-pitch, 4, 'X'))
    p.cyl((-w * 0.32, d * 0.16, z_hi + 0.5), 0.13, 0.7, seg=8, mat=RUST)
    p.bevel(width=0.014)
    return p.finish("Mesh_PrefabHab_Annex", coll)


def hab_garage(mats, coll):
    """9.0 x 7.4, roof at 4.5, with a shuttered vehicle bay filling one end."""
    p = Part(mats)
    w, d, z = 9.0, 7.4, 4.5
    plinth(p, w, d)
    body(p, w, d, PLINTH, z)
    ribs(p, w, d, BAND + 0.2, z - 0.12, pitch=0.66)
    band(p, w, d)
    posts(p, w, d, PLINTH, z)
    roof(p, w, d, z, para=0.46)
    # Roller shutter across the -X end: recess, guides, slats, a raised lintel.
    bw, bh = d * 0.72, 3.44
    p.box((-w / 2 + 0.16, 0, PLINTH + bh / 2), (0.36, bw, bh), BLACK)
    for k in range(14):
        p.box((-w / 2 + 0.34, 0, PLINTH + 0.12 + k * (bh - 0.24) / 13),
              (0.1, bw - 0.16, (bh - 0.24) / 13 * 0.82), STEEL)
    for sy in (-1, 1):
        p.box((-w / 2 + 0.04, sy * bw / 2, PLINTH + bh / 2), (0.3, 0.24, bh + 0.3),
              WHITE)
    p.box((-w / 2 + 0.04, 0, PLINTH + bh + 0.34), (0.34, bw + 0.5, 0.62), WHITE)
    p.box((-w / 2 - 0.1, 0, PLINTH + bh + 0.34), (0.12, bw + 0.2, 0.4), ORANGE)
    p.box((-w / 2 + 0.2, 0, PLINTH + 0.04), (1.4, bw + 0.5, 0.1), DARK)  # apron
    for sy in (-1, 1):                                        # bollards
        p.cyl((-w / 2 - 0.8, sy * (bw / 2 + 0.4), PLINTH + 0.42), 0.13, 0.84,
              seg=8, mat=ORANGE)
        p.cyl((-w / 2 - 0.8, sy * (bw / 2 + 0.4), PLINTH + 0.8), 0.15, 0.1,
              seg=8, mat=DARK)
    door_bay(p, w * 0.3, -d / 2, PLINTH, w=1.4)
    window(p, w * 0.06, -d / 2, 3.3, (0, -1), w=1.0)
    window(p, w * 0.22, d / 2, 3.3, (0, 1), w=1.6)
    window(p, w / 2, d * 0.2, 3.3, (1, 0), w=1.1)             # end wall, +X face
    p.box((-w * 0.1, d / 2 + 0.03, 1.5), (2.4, 0.14, 1.6), SLATE)
    for k in range(6):
        p.box((-w * 0.1, d / 2 + 0.09, 0.9 + k * 0.24), (2.28, 0.08, 0.1), STEEL)
    p.box((0, 0, 0.74), (w + 0.06, d + 0.06, 0.16), ORANGE)
    roof_kit(p, w, d, z + 0.2, seed=11)
    scupper(p, w, d, z + 0.2, sx=1, sy=1)
    p.bevel(width=0.016)
    return p.finish("Mesh_PrefabHab_Garage", coll)


def hab_corner(mats, coll):
    """An L-plan in two wings at different roof heights — 10.4 x 9.2 overall.

    The only variation with a re-entrant corner, which is what a settlement
    needs to make a courtyard out of two blocks instead of a corridor.
    """
    p = Part(mats)
    aw, ad, az = 10.4, 4.6, 4.0                       # long wing, along X
    bw, bd, bz = 4.4, 9.2, 3.4                        # short wing, along Y
    ax, ay = 0.0, 9.2 / 2 - ad / 2                    # wing centres
    bx, by = -10.4 / 2 + bw / 2, 0.0
    for (w, d, z, cx, cy) in ((aw, ad, az, ax, ay), (bw, bd, bz, bx, by)):
        p.box((cx, cy, PLINTH / 2), (w + 0.3, d + 0.3, PLINTH), SLATE)
        p.box((cx, cy, 0.06), (w + 0.6, d + 0.6, 0.12), DARK)
        p.box((cx, cy, (PLINTH + z) / 2), (w, d, z - PLINTH), BLUE)
        p.box((cx, cy, BAND), (w + 0.05, d + 0.05, 0.2), WHITE)
        p.box((cx, cy, BAND + 0.13), (w + 0.07, d + 0.07, 0.06), STEEL)
        p.box((cx, cy, 0.7), (w + 0.06, d + 0.06, 0.15), ORANGE)
        nx = max(2, int(w / 0.62))
        for i in range(nx):
            x = cx + (i + 0.5) / nx * w - w / 2
            for sy in (-1, 1):
                p.box((x, cy + sy * (d / 2 + 0.025), (BAND + 0.2 + z - 0.12) / 2),
                      (0.1, 0.05, z - BAND - 0.42), BLUE)
        ny = max(2, int(d / 0.62))
        for i in range(ny):
            y = cy + (i + 0.5) / ny * d - d / 2
            for sx in (-1, 1):
                p.box((cx + sx * (w / 2 + 0.025), y, (BAND + 0.2 + z - 0.12) / 2),
                      (0.05, 0.1, z - BAND - 0.42), BLUE)
        p.box((cx, cy, z + 0.1), (w + 0.16, d + 0.16, 0.2), WHITE)
        p.box((cx, cy, z + 0.03), (w + 0.24, d + 0.24, 0.07), STEEL)
        for sy in (-1, 1):
            p.box((cx, cy + sy * (d + 0.16) / 2, z + 0.4), (w + 0.16, 0.14, 0.4),
                  WHITE)
        for sx in (-1, 1):
            p.box((cx + sx * (w + 0.16) / 2, cy, z + 0.4), (0.14, d + 0.16, 0.4),
                  WHITE)
    # The re-entrant corner: a glazed link and a canopy over the inner angle.
    p.box((bx + bw / 2 + 1.4, ay - ad / 2 - 0.1, PLINTH + 1.5), (2.6, 0.2, 2.4),
          GLASS)
    p.box((bx + bw / 2 + 1.4, ay - ad / 2 - 0.14, PLINTH + 1.5), (0.12, 0.3, 2.4),
          DARK)
    p.box((bx + bw / 2 + 1.4, ay - ad / 2 - 0.9, PLINTH + 3.0), (3.2, 1.7, 0.16),
          STEEL)
    for sx in (-1, 1):
        p.cyl((bx + bw / 2 + 1.4 + sx * 1.4, ay - ad / 2 - 1.6,
               PLINTH + 1.5), 0.06, 3.0, seg=8, mat=STEEL)
    door_bay(p, aw * 0.28, ay - ad / 2, PLINTH, w=1.5)
    window(p, aw * 0.06, ay + ad / 2, 3.06, (0, 1), w=1.4)
    window(p, aw / 2, ay, 3.06, (1, 0), w=1.0)                # end wall, +X face
    window(p, bx - bw / 2, -bd * 0.34, 2.7, (-1, 0), w=1.2)   # short wing flank
    p.box((bx - bw / 2 - 0.03, -bd * 0.2, 1.5), (0.14, 2.2, 1.5), SLATE)
    for k in range(6):
        p.box((bx - bw / 2 - 0.09, -bd * 0.2, 0.94 + k * 0.22), (0.08, 2.06, 0.1),
              STEEL)
    p.cyl((ax + aw * 0.3, ay, az + 0.6), 0.4, 0.6, seg=12, mat=STEEL)
    p.cyl((ax + aw * 0.3, ay, az + 0.92), 0.46, 0.09, seg=12, mat=DARK)
    p.box((bx, by - bd * 0.24, bz + 0.42), (2.0, 1.8, 0.44), WHITE)
    p.cyl((bx, by + bd * 0.3, bz + 0.7), 0.14, 1.0, seg=8, mat=RUST)
    p.bevel(width=0.016)
    return p.finish("Mesh_PrefabHab_Corner", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Long", hab_long), ("Short", hab_short),
                     ("Annex", hab_annex), ("Garage", hab_garage),
                     ("Corner", hab_corner)):
        fn(mats, collection("Coll_PrefabHab_%s" % name))
    report()
    save(out)


main()
