"""components/structural/cabin_module — the boxy habitat a crawler carries.

The vehicle underneath is a chassis and six legs. Everything that makes it read
as somewhere people *live* is up here: a pair of these bolted side by side, in
the same corrugated, riveted, sun-bleached language as a shipping container that
has been converted and re-converted.

Authored with the origin at the bottom centre — the face it stands on — so an
assembly sets one down by putting the origin on the deck.

Base envelope is 4.80 (X) x 7.20 (Y) x 6.40 (Z); variants differ above the
roofline and in what is cut into the walls, so their bounding boxes are not all
identical. Every variation has its own roof silhouette, because at the distance
a walking machine is seen the roofline is most of what distinguishes them.

    blender --background --python cabin_module.py -- --out cabin_module.blend
"""

import math
import os
import sys

from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0 the walls
    "Mat_Paint_Roof_Green",      # 1 roof cap, bands
    "Mat_Metal_Steel_Dark",      # 2 corner posts, fittings
    "Mat_Metal_Steel_Worn",      # 3 bare steel frames
    "Mat_Paint_Warn_Red",        # 4 roundels, stencils
    "Mat_Metal_Rust_Heavy",      # 5 streaks and repairs
    "Mat_Glass_Canopy_Tinted",   # 6 windows
    "Mat_Neutral_Black_Matte",   # 7 recesses
    "Mat_Emissive_Cabin_Warm",   # 8 lit interiors
    "Mat_Paint_Olive_Deep",      # 9 contrast panels
]
HULL, ROOF, DARK, STEEL, RED, RUST, GLASS, BLACK, WARM, OLIVE = range(10)

W, D, H = 4.80, 7.20, 6.40           # width (X), depth (Y), height (Z)
WALL = 0.14


def shell(p, w=W, d=D, h=H):
    """Four walls, a floor and a ceiling as one closed box, plus corner posts.

    Built as a solid rather than a hollow shell: nothing looks inside these
    except through a window that has its own recess behind it, and a solid box
    is a third of the triangles and has no interior faces to clean up.
    """
    p.box((0, 0, h / 2), (w, d, h), HULL)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * (w / 2 - 0.11), sy * (d / 2 - 0.11), h / 2),
                  (0.30, 0.30, h), DARK)
            # Container corner castings, top and bottom.
            for z in (0.22, h - 0.22):
                p.box((sx * (w / 2 - 0.11), sy * (d / 2 - 0.11), z),
                      (0.42, 0.42, 0.44), STEEL)


def corrugate(p, d=D, h=H, w=W, count=13, lo=0.55, hi=None, mat=HULL):
    """Vertical ribs down both long walls — the read that says 'container'."""
    hi = h - 0.55 if hi is None else hi
    for sx in (-1, 1):
        for i in range(count):
            y = -d / 2 + d * (i + 0.5) / count
            p.box((sx * (w / 2 + 0.03), y, (lo + hi) / 2),
                  (0.10, d / count * 0.55, hi - lo), mat)


def belt(p, z, d=D, h=H, w=W, mat=ROOF, thickness=0.30):
    """A band right round the module at height `z`."""
    for sx in (-1, 1):
        p.box((sx * (w / 2 + 0.04), 0, z), (0.10, d - 0.30, thickness), mat)
    for sy in (-1, 1):
        p.box((0, sy * (d / 2 + 0.04), z), (w - 0.30, 0.10, thickness), mat)


def roundel(p, x, y, z, face='X', radius=0.42):
    """The red hazard disc. Two discs, not a texture: this model has no UVs and
    the roundel is the single most recognisable marking on the machine."""
    off = 0.06 if x > 0 or y > 0 else -0.06
    if face == 'X':
        p.cyl((x + off, y, z), radius, 0.06, 'X', 18, RED)
        p.cyl((x + off * 1.6, y, z), radius * 0.52, 0.05, 'X', 18, HULL)
    else:
        p.cyl((x, y + off, z), radius, 0.06, 'Y', 18, RED)
        p.cyl((x, y + off * 1.6, z), radius * 0.52, 0.05, 'Y', 18, HULL)


def stencil(p, x, y, z, face='X', length=2.4):
    """A pale placard with a dark bar on it — reads as lettering at distance and
    costs four faces instead of a decal sheet."""
    if face == 'X':
        s = 1 if x > 0 else -1
        p.box((x + s * 0.05, y, z), (0.06, length, 0.34), OLIVE)
        p.box((x + s * 0.09, y, z), (0.05, length * 0.86, 0.12), HULL)
    else:
        s = 1 if y > 0 else -1
        p.box((x, y + s * 0.05, z), (length, 0.06, 0.34), OLIVE)
        p.box((x, y + s * 0.09, z), (length * 0.86, 0.05, 0.12), HULL)


def window(p, x, y, z, face='X', width=0.72, height=0.66, lit=True):
    """Recess, glass, frame, and a lit backing so it does not read as a hole."""
    if face == 'X':
        s = 1 if x > 0 else -1
        p.box((x - s * 0.06, y, z), (0.14, width, height), BLACK)
        if lit:
            p.box((x - s * 0.12, y, z), (0.05, width * 0.9, height * 0.9), WARM)
        p.box((x + s * 0.02, y, z), (0.08, width, height), GLASS)
        p.box((x + s * 0.05, y, z), (0.05, width + 0.16, height + 0.16), STEEL)
        p.box((x + s * 0.07, y, z), (0.05, width * 0.96, height * 0.96), BLACK)
    else:
        s = 1 if y > 0 else -1
        p.box((x, y - s * 0.06, z), (width, 0.14, height), BLACK)
        if lit:
            p.box((x, y - s * 0.12, z), (width * 0.9, 0.05, height * 0.9), WARM)
        p.box((x, y + s * 0.02, z), (width, 0.08, height), GLASS)
        p.box((x, y + s * 0.05, z), (width + 0.16, 0.05, height + 0.16), STEEL)
        p.box((x, y + s * 0.07, z), (width * 0.96, 0.05, height * 0.96), BLACK)


def rungs(p, x, y0, y1, z0, z1, count=8):
    """Climbing rungs welded up a corner — the cheapest way to say 'accessible'."""
    for i in range(count):
        t = (i + 0.5) / count
        p.cyl((x, y0 + (y1 - y0) * t, z0 + (z1 - z0) * t), 0.035, 0.44, 'Y', 6,
              STEEL)


def rust_streaks(p, w=W, d=D, h=H, seed=0, count=7):
    """Thin vertical stains below the fittings. Placed deterministically so the
    file rebuilds identically, and only on the long walls where they read."""
    import random
    rng = random.Random(seed)
    for _ in range(count):
        sx = rng.choice((-1, 1))
        y = rng.uniform(-d / 2 + 0.5, d / 2 - 0.5)
        top = rng.uniform(h * 0.45, h - 0.7)
        drop = rng.uniform(0.6, 1.9)
        p.box((sx * (w / 2 + 0.02), y, top - drop / 2),
              (0.05, rng.uniform(0.10, 0.22), drop), RUST)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def build_habitat(coll):
    """Somebody lives here: windows, a door, a pitched green cap, a stove flue."""
    p = Part(PALETTE)
    shell(p)
    corrugate(p, count=11, lo=0.6, hi=3.6)
    belt(p, 4.05)

    # Pitched roof cap, overhanging on all four sides.
    p.prism([(-W / 2 - 0.22, 0.0), (W / 2 + 0.22, 0.0),
             (W / 2 + 0.22, 0.30), (0.0, 0.86), (-W / 2 - 0.22, 0.30)],
            D + 0.44, 'Y', ROOF, offset=(0, 0, H))
    p.box((0, 0, H + 0.90), (0.30, D + 0.20, 0.14), DARK)          # ridge cap
    for sy in (-1, 1):
        p.box((0, sy * (D / 2 + 0.20), H + 0.16), (W + 0.50, 0.12, 0.34), ROOF)

    p.cyl((1.30, -2.05, H + 1.35), 0.17, 1.60, 'Z', 10, STEEL)     # flue
    p.cyl((1.30, -2.05, H + 2.20), 0.26, 0.20, 'Z', 10, DARK)

    for sx in (-1, 1):
        for i, y in enumerate((-2.05, -0.35, 1.35)):
            window(p, sx * W / 2, y, 4.85, 'X', 0.78, 0.70, lit=(i != 1))
    window(p, 0, -D / 2, 4.85, 'Y', 1.10, 0.70)

    # Door on the aft face, with a step and a lamp over it.
    p.box((0, D / 2 + 0.03, 1.35), (1.16, 0.14, 2.30), OLIVE)
    p.box((0, D / 2 + 0.10, 1.35), (0.96, 0.08, 2.10), HULL)
    p.cyl((0.36, D / 2 + 0.16, 1.30), 0.06, 0.22, 'Y', 8, STEEL)
    p.box((0, D / 2 + 0.22, 0.14), (1.30, 0.44, 0.14), STEEL)
    p.box((0, D / 2 + 0.14, 2.70), (0.42, 0.24, 0.16), DARK)

    roundel(p, W / 2, 1.90, 2.35, 'X')
    roundel(p, -W / 2, -1.90, 2.35, 'X')
    stencil(p, W / 2, -0.60, 5.75, 'X', 3.0)
    stencil(p, -W / 2, -0.60, 5.75, 'X', 3.0)
    rungs(p, W / 2 + 0.20, D / 2 - 0.55, D / 2 - 0.55, 0.6, 5.9)
    rust_streaks(p, seed=1)
    p.bevel(width=0.022, segments=2)
    p.finish("Mesh_CabinModule_Habitat", coll)


def build_cargo(coll):
    """No windows, corrugated top to bottom, a shutter across one end and a flat
    roof with lifting eyes. The lowest silhouette of the four."""
    p = Part(PALETTE)
    shell(p, h=H - 0.5)
    h = H - 0.5
    corrugate(p, h=h, count=15, lo=0.35, hi=h - 0.35)

    # Flat roof with a raised kerb and four lifting eyes.
    p.box((0, 0, h + 0.12), (W + 0.24, D + 0.24, 0.24), ROOF)
    for sx in (-1, 1):
        p.box((sx * (W / 2 + 0.10), 0, h + 0.34), (0.16, D + 0.20, 0.24), DARK)
    for sy in (-1, 1):
        p.box((0, sy * (D / 2 + 0.10), h + 0.34), (W + 0.20, 0.16, 0.24), DARK)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.torus((sx * (W / 2 - 0.5), sy * (D / 2 - 0.5), h + 0.46), 0.20,
                    0.05, 'X', 12, 6, STEEL)

    # Roller shutter across the forward end.
    p.box((0, -D / 2 - 0.05, 2.15), (W - 0.55, 0.14, 3.90), DARK)
    for i in range(12):
        p.box((0, -D / 2 - 0.13, 0.35 + i * 0.32), (W - 0.65, 0.10, 0.22), STEEL)
    p.box((0, -D / 2 - 0.16, 4.25), (W - 0.45, 0.22, 0.34), OLIVE)
    p.box((0, -D / 2 - 0.14, 0.16), (W - 0.45, 0.18, 0.20), DARK)

    for sx in (-1, 1):                                    # lashing rings
        for y in (-2.2, 0.0, 2.2):
            p.torus((sx * (W / 2 + 0.10), y, 0.62), 0.13, 0.035, 'X', 10, 5,
                    STEEL)
    roundel(p, W / 2, 0.0, 4.30, 'X', 0.50)
    roundel(p, -W / 2, 0.0, 4.30, 'X', 0.50)
    stencil(p, W / 2, -1.8, 2.55, 'X', 2.6)
    stencil(p, -W / 2, 1.8, 2.55, 'X', 2.6)
    rust_streaks(p, h=h, seed=2, count=9)
    p.bevel(width=0.022, segments=2)
    p.finish("Mesh_CabinModule_Cargo", coll)


def build_workshop(coll):
    """Half-height shutter with a bench under it, external racking, a roof-top
    condenser and a stack. The busiest outline of the four."""
    p = Part(PALETTE)
    shell(p, h=H - 0.2)
    h = H - 0.2
    corrugate(p, h=h, count=9, lo=3.3, hi=h - 0.5)
    belt(p, 3.05, h=h, mat=OLIVE, thickness=0.36)

    # Shed roof: single slope, high on +X.
    p.prism([(-W / 2 - 0.20, 0.0), (W / 2 + 0.20, 0.42),
             (W / 2 + 0.20, 0.82), (-W / 2 - 0.20, 0.40)],
            D + 0.40, 'Y', ROOF, offset=(0, 0, h))
    p.box((W / 2 + 0.10, 0, h + 0.95), (0.90, D - 0.6, 0.66), STEEL)   # plant
    p.cyl((W / 2 + 0.10, 1.6, h + 1.50), 0.44, 0.44, 'Z', 12, DARK)
    p.cyl((-1.5, -2.3, h + 1.35), 0.20, 1.70, 'Z', 10, RUST)           # stack
    p.cyl((-1.5, -2.3, h + 2.25), 0.30, 0.18, 'Z', 10, DARK)

    # Shutter, rolled up, with the bench it opens onto.
    p.box((0, -D / 2 - 0.05, 1.60), (W - 0.70, 0.14, 2.30), BLACK)
    p.box((0, -D / 2 - 0.11, 1.35), (W - 0.80, 0.06, 1.70), WARM)
    p.cyl((0, -D / 2 - 0.18, 2.92), 0.26, W - 0.70, 'X', 12, STEEL)
    p.box((0, -D / 2 - 0.30, 0.86), (W - 0.60, 0.52, 0.16), STEEL)
    for sx in (-1, 1):
        p.box((sx * (W / 2 - 0.55), -D / 2 - 0.50, 0.42), (0.12, 0.12, 0.84),
              DARK)

    # External racking down one long wall.
    for i, z in enumerate((1.25, 2.15)):
        p.box((W / 2 + 0.40, 1.1, z), (0.80, 3.0, 0.10), STEEL)
        for y in (-0.3, 2.5):
            p.box((W / 2 + 0.74, y, z - 0.42), (0.10, 0.10, 0.84), STEEL)
    for i in range(4):
        p.cyl((W / 2 + 0.40, 0.0 + i * 0.66, 2.44), 0.20, 0.66, 'Z', 10, RUST)

    window(p, -W / 2, -1.4, 4.55, 'X', 0.86, 0.74)
    window(p, -W / 2, 0.8, 4.55, 'X', 0.86, 0.74)
    roundel(p, -W / 2, 2.6, 4.55, 'X', 0.38)
    stencil(p, -W / 2, -0.4, 5.55, 'X', 2.8)
    rungs(p, -W / 2 - 0.20, -D / 2 + 0.55, -D / 2 + 0.55, 0.6, h - 0.4)
    rust_streaks(p, h=h, seed=3, count=8)
    p.bevel(width=0.022, segments=2)
    p.finish("Mesh_CabinModule_Workshop", coll)


def build_comms(coll):
    """Antenna farm on the roof, armoured slit ports instead of windows. Built
    ahead: a convoy wants one of these and this crawler does not need it."""
    p = Part(PALETTE)
    shell(p, h=H - 0.9)
    h = H - 0.9
    corrugate(p, h=h, count=7, lo=0.5, hi=2.4)
    belt(p, 2.85, h=h, mat=OLIVE)

    p.box((0, 0, h + 0.14), (W + 0.30, D + 0.30, 0.28), ROOF)
    # A mast bed rather than a mast: the mast itself is components/mast_rig.
    for sy in (-1, 1):
        p.box((0, sy * 2.1, h + 0.42), (W - 0.6, 0.44, 0.28), STEEL)
        for sx in (-1, 1):
            p.cyl((sx * 1.5, sy * 2.1, h + 0.62), 0.16, 0.40, 'Z', 10, DARK)
    p.cyl((0, 0, h + 0.90), 0.34, 1.20, 'Z', 12, STEEL)
    p.cyl((0, 0, h + 1.62), 0.86, 0.20, 'Z', 20, DARK)               # dish base
    for i in range(6):
        a = 2 * math.pi * i / 6
        p.box((math.cos(a) * 0.62, math.sin(a) * 0.62, h + 1.86),
              (0.10, 0.10, 0.52), STEEL, rot=Matrix.Rotation(a, 4, 'Z'))
    p.cyl((0, 0, h + 2.10), 0.94, 0.12, 'Z', 20, HULL)

    for sx in (-1, 1):                                    # armoured slits
        for y in (-1.8, 0.0, 1.8):
            p.box((sx * (W / 2 - 0.03), y, 4.05), (0.12, 1.10, 0.26), BLACK)
            p.box((sx * (W / 2 + 0.06), y, 4.05), (0.10, 1.30, 0.42), STEEL)
            p.box((sx * (W / 2 + 0.10), y, 4.26), (0.14, 1.34, 0.10), OLIVE)
    p.box((0, D / 2 + 0.03, 1.35), (1.10, 0.14, 2.30), OLIVE)
    p.box((0, D / 2 + 0.10, 1.35), (0.90, 0.08, 2.10), HULL)
    roundel(p, W / 2, 1.5, 1.95, 'X', 0.40)
    stencil(p, -W / 2, 0.0, 1.95, 'X', 2.4)
    rust_streaks(p, h=h, seed=4, count=6)
    p.bevel(width=0.022, segments=2)
    p.finish("Mesh_CabinModule_Comms", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_habitat(collection("Coll_CabinModule_Habitat"))
    build_cargo(collection("Coll_CabinModule_Cargo"))
    build_workshop(collection("Coll_CabinModule_Workshop"))
    build_comms(collection("Coll_CabinModule_Comms"))

    report()
    save(out)


build()
