"""components/structural/shanty_addon — habitation welded onto somebody else's machine.

This is the component that turns a derelict into a settlement, and it is the
only one in the library whose job is to look *unauthorised*. `cabin_module` and
`hab_capsule` are both manufactured: square corners, consistent plating, made in
a factory and bolted on by the people who designed the thing. These were made on
site out of whatever was to hand, by people who did not build the hulk and do
not own it. So nothing here lines up with anything: roofs slope at angles the
host structure does not use, walls are visibly offcuts of three different
plates, and every one of them hangs off brackets that were welded on afterwards.

That contrast is the entire point. A rusted hulk with nothing added reads as
abandoned; the same hulk with five of these hanging off it reads as *occupied*,
and the read comes almost entirely from the mismatch rather than from any single
shanty being good.

**Origin convention: the mounting face is the plane x = 0 and the shanty
projects into +X.** To hang one on a wall whose outward normal is -Y, place it
on the wall and rotate -90 degrees about Z. Every variation obeys this, so they
are interchangeable at any mounting point without re-measuring.

The lit windows are `Mat_Emissive_Cabin_Warm` and they are not decoration: one
warm window on a 60 m rust pile is the cheapest possible signal that somebody is
home, and it survives being viewed as a silhouette at distance, which is how
this asset will usually be seen.

    blender --background --python shanty_addon.py -- --out shanty_addon.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Metal_HullRust_Orange",   # 0 HULL   salvaged plate, the main walls
    "Mat_Metal_Rust_Heavy",        # 1 RUST   the worst of the offcuts
    "Mat_Metal_Steel_Worn",        # 2 STEEL  brackets, frames, ladders
    "Mat_Metal_Steel_Dark",        # 3 DARK   fittings, flues, bolt clusters
    "Mat_Neutral_Black_Matte",     # 4 BLACK  doorways, shadow gaps
    "Mat_Fabric_Canvas_Faded",     # 5 CANVAS door curtains, lashings
    "Mat_Fabric_Flag_Bleached",    # 6 SHADE  sun-bleached awning cloth
    "Mat_Wood_Ply_Worn",           # 7 PLY    scavenged boards, decking
    "Mat_Emissive_Cabin_Warm",     # 8 WARM   a lit window — somebody is home
    "Mat_Plastic_Rubber_Black",    # 9 RUBBER hose, cable, gutter downpipe
]
HULL, RUST, STEEL, DARK, BLACK, CANVAS, SHADE, PLY, WARM, RUBBER = range(10)


# ---------------------------------------------------------------------------
# Local geometry helpers
# ---------------------------------------------------------------------------

def along(a, b):
    d = Vector(b) - Vector(a)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    return rot.to_4x4(), d.length


def member(p, a, b, size=0.10, mat=STEEL, overlap=0.0):
    rot, length = along(a, b)
    p.box((Vector(a) + Vector(b)) / 2.0, (size, size, length + overlap), mat,
          rot=rot)


def rod(p, a, b, radius=0.035, mat=STEEL, seg=6):
    rot, length = along(a, b)
    p.cyl((Vector(a) + Vector(b)) / 2.0, radius, length, 'Z', seg=seg, mat=mat,
          rot=rot)


def wall_pad(p, y0, y1, z0, z1, mat=STEEL):
    """The weld pad where the shanty meets the host structure.

    Every variation has one. It is what stops the addon reading as *floating
    near* the wall rather than *attached to* it, and it costs one box.
    """
    p.box((0.06, (y0 + y1) / 2.0, (z0 + z1) / 2.0),
          (0.12, y1 - y0, z1 - z0), mat)


def bracket(p, at, reach, drop, mat=STEEL):
    """A diagonal bracket from the wall out to a cantilevered floor."""
    at = Vector(at)
    member(p, at, at + Vector((reach, 0, 0)), 0.10, mat)
    member(p, at + Vector((0, 0, -drop)), at + Vector((reach, 0, 0)), 0.09, mat)


def patchwork(p, x, y0, y1, z0, z1, seed, thick=0.06, normal='X'):
    """A wall made of mismatched offcuts rather than one panel.

    Three or four plates at slightly different depths, two of them a different
    material, with visible seams between. This is the single most load-bearing
    detail in the component: one flat plate reads as manufactured, and the same
    area split into four unequal pieces reads as scavenged.
    """
    rng = random.Random(seed)
    n = rng.randint(3, 4)
    cuts = sorted(rng.uniform(z0 + 0.25, z1 - 0.25) for _ in range(n - 1))
    edges = [z0] + cuts + [z1]
    for i in range(n):
        a, b = edges[i], edges[i + 1]
        mat = RUST if rng.random() < 0.35 else HULL
        d = thick + rng.uniform(0.0, 0.035)
        if normal == 'X':
            p.box((x + d / 2.0, (y0 + y1) / 2.0, (a + b) / 2.0),
                  (d, y1 - y0, b - a - 0.02), mat)
        else:
            p.box(((y0 + y1) / 2.0, x + d / 2.0, (a + b) / 2.0),
                  (y1 - y0, d, b - a - 0.02), mat)
        if i:                                   # the seam strip over the joint
            if normal == 'X':
                p.box((x + d + 0.02, (y0 + y1) / 2.0, a), (0.05, y1 - y0, 0.09),
                      DARK)
            else:
                p.box(((y0 + y1) / 2.0, x + d + 0.02, a), (y1 - y0, 0.05, 0.09),
                      DARK)


def flue(p, base, height, mat=DARK):
    """A stove pipe with a rain cap. Says 'somebody cooks in here'."""
    base = Vector(base)
    p.cyl(base + Vector((0, 0, height / 2.0)), 0.09, height, 'Z', seg=8,
          mat=mat)
    p.cyl(base + Vector((0, 0, height + 0.06)), 0.17, 0.06, 'Z', seg=8, mat=mat)
    p.cyl(base + Vector((0, 0, height + 0.20)), 0.14, 0.05, 'Z', seg=8,
          mat=STEEL)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def build_leanto(coll, mats):
    """A sloped scrap roof leaning against the wall. 3.4 m out, 3.2 m tall.

    The lowest-effort dwelling there is: one roof plane, two end walls, and the
    host structure doing the fourth side for free. It is the variation that
    should appear most often, because a settlement made only of tidy boxes
    reads as planned.
    """
    p = Part(mats)
    reach, wide, high, low = 3.4, 4.2, 3.15, 1.85
    wall_pad(p, -wide / 2, wide / 2, 0, high)

    # The roof plane, made of overlapping corrugated sheets.
    ang = math.atan2(high - low, reach)
    for i in range(5):
        y = -wide / 2 + 0.25 + i * (wide - 0.5) / 4.0
        p.box((reach / 2.0, y, (high + low) / 2.0 + 0.06),
              (reach / math.cos(ang), (wide - 0.5) / 4.0 + 0.06, 0.07),
              RUST if i % 2 else HULL,
              rot=Matrix.Rotation(-ang, 4, 'Y'))
    for i in range(9):                          # corrugation ribs
        t = (i + 0.5) / 9.0
        p.box((reach * t, 0, high - (high - low) * t + 0.12),
              (0.06, wide - 0.4, 0.05), DARK,
              rot=Matrix.Rotation(-ang, 4, 'Y'))

    for s in (-1, 1):                           # end walls, cut to the slope
        patchwork(p, 0.10, s * (wide / 2 - 0.09), s * (wide / 2), 0.0, low,
                  seed=7 + s)
        p.prism([(0.10, 0.0), (reach, 0.0), (reach, low), (0.10, high)],
                0.10, axis='Y', mat=HULL,
                offset=(0, s * (wide / 2 - 0.05), 0))
    # Front wall with a curtained doorway.
    patchwork(p, reach - 0.10, -wide / 2, -0.45, 0.0, low, seed=19)
    patchwork(p, reach - 0.10, 0.95, wide / 2, 0.0, low, seed=23)
    p.box((reach - 0.02, 0.25, low / 2.0), (0.10, 1.40, low), BLACK)
    for i in range(5):                          # the curtain, hanging in folds
        y = -0.35 + i * 0.32
        p.box((reach + 0.04, y, low / 2.0 + 0.08), (0.05, 0.30, low - 0.16),
              CANVAS, rot=Matrix.Rotation(math.radians(6 - i * 3), 4, 'Y'))

    member(p, (0.1, -wide / 2 + 0.2, high), (reach, -wide / 2 + 0.2, low), 0.09)
    member(p, (0.1, wide / 2 - 0.2, high), (reach, wide / 2 - 0.2, low), 0.09)
    for s in (-1, 1):                           # props under the low edge
        member(p, (reach - 0.15, s * (wide / 2 - 0.3), low),
               (reach - 0.15, s * (wide / 2 - 0.3), 0.0), 0.09)

    flue(p, (1.5, wide / 2 - 0.9, high - 0.55), 1.5)
    p.box((reach - 0.25, -wide / 2 + 0.5, 0.35), (0.5, 0.7, 0.7), PLY)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Shanty_LeanTo", coll)


def build_box(coll, mats):
    """A room cantilevered off the wall on brackets. 3.6 m out, 3.0 m tall.

    Hung clear of the ground entirely, which is what you do when the ground is
    somebody else's roof. The lit window is the whole reason this variation
    exists — it is the one that gets placed where the player will see it.
    """
    p = Part(mats)
    reach, wide, high = 3.6, 3.8, 3.0
    z0 = 0.0
    wall_pad(p, -wide / 2, wide / 2, z0 - 0.3, z0 + high)

    for s in (-1, 1):                           # the brackets it hangs on
        bracket(p, (0.1, s * (wide / 2 - 0.25), z0), reach - 0.2, 1.25)
    bracket(p, (0.1, 0, z0), reach - 0.2, 1.25)

    p.box((reach / 2.0, 0, z0 - 0.06), (reach, wide, 0.13), STEEL)     # floor
    p.box((reach / 2.0, 0, z0 + high), (reach + 0.22, wide + 0.22, 0.14), RUST)

    patchwork(p, reach - 0.12, -wide / 2, wide / 2, z0 + 0.05, z0 + high - 0.05,
              seed=31)
    for s in (-1, 1):
        patchwork(p, s * (wide / 2) - (0.12 if s > 0 else 0.0),
                  0.15, reach - 0.15, z0 + 0.05, z0 + high - 0.05, seed=41 + s,
                  normal='Y')

    # The window: recessed frame, glazing bars, warm light behind.
    wx, wz = reach + 0.02, z0 + 1.65
    p.box((wx, -0.35, wz), (0.10, 1.30, 1.05), DARK)
    p.box((wx + 0.05, -0.35, wz), (0.06, 1.10, 0.85), WARM)
    for i in range(2):
        p.box((wx + 0.09, -0.35, wz - 0.42 + i * 0.84), (0.05, 1.25, 0.07),
              DARK)
    p.box((wx + 0.09, -0.35, wz), (0.05, 0.07, 1.00), DARK)

    p.box((reach - 0.06, 1.25, z0 + 1.05), (0.12, 0.95, 2.0), DARK)    # door
    p.box((reach + 0.03, 1.25, z0 + 1.05), (0.06, 0.80, 1.8), BLACK)
    p.cyl((reach + 0.10, 0.92, z0 + 1.0), 0.05, 0.16, 'X', seg=6, mat=STEEL)

    # A scrap balcony outside the door, which is where the washing goes.
    p.box((reach + 0.55, 1.25, z0 - 0.02), (1.1, 1.5, 0.09), PLY)
    for s in (-1, 1):
        member(p, (reach + 1.05, 1.25 + s * 0.72, z0),
               (reach + 1.05, 1.25 + s * 0.72, z0 + 0.95), 0.06)
    p.box((reach + 1.05, 1.25, z0 + 0.92), (0.06, 1.5, 0.06), STEEL)
    member(p, (reach + 0.1, 1.25, z0 - 0.05), (reach + 1.05, 1.25, z0 - 0.55),
           0.07)
    for i in range(3):
        p.box((reach + 1.02, 0.75 + i * 0.5, z0 + 0.62), (0.03, 0.34, 0.55),
              CANVAS)

    flue(p, (reach - 0.9, -wide / 2 + 0.55, z0 + high + 0.07), 1.35)
    p.cyl((0.55, wide / 2 + 0.02, z0 + 0.35), 0.05, 1.1, 'X', seg=6,
          mat=RUBBER)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Shanty_Box", coll)


def build_stack(coll, mats):
    """Two rooms stacked, with an external ladder. 3.2 m out, 6.4 m tall.

    The vertical variation. A row of single-storey addons all at one height
    reads as a shelf; one of these breaks the band and gives the flank a
    silhouette event. The upper room is deliberately offset from the lower one,
    because nobody who built this had a plan.
    """
    p = Part(mats)
    reach, wide = 3.2, 3.4
    wall_pad(p, -wide / 2 - 0.3, wide / 2 + 0.3, 0, 6.4)

    for z0, dy, h, seed in ((0.15, -0.35, 2.85, 61), (3.25, 0.45, 2.75, 67)):
        for s in (-1, 1):
            bracket(p, (0.1, dy + s * (wide / 2 - 0.3), z0), reach - 0.2, 1.1)
        p.box((reach / 2.0, dy, z0 - 0.07), (reach, wide, 0.14), STEEL)
        p.box((reach / 2.0, dy, z0 + h), (reach + 0.2, wide + 0.2, 0.13), RUST)
        patchwork(p, reach - 0.12, dy - wide / 2, dy + wide / 2, z0 + 0.05,
                  z0 + h - 0.05, seed=seed)
        for s in (-1, 1):
            patchwork(p, dy + s * (wide / 2) - (0.12 if s > 0 else 0.0),
                      0.15, reach - 0.15, z0 + 0.05, z0 + h - 0.05,
                      seed=seed + s, normal='Y')
        p.box((reach - 0.04, dy + 0.75, z0 + 1.0), (0.12, 0.85, 1.9), DARK)
        p.box((reach + 0.04, dy + 0.75, z0 + 1.0), (0.06, 0.70, 1.7), BLACK)
        p.box((reach + 0.02, dy - 0.85, z0 + 1.75), (0.10, 0.80, 0.70), DARK)
        p.box((reach + 0.07, dy - 0.85, z0 + 1.75), (0.05, 0.62, 0.52),
              WARM if z0 > 2 else BLACK)

    # The landing between them, and the ladder serving both.
    p.box((reach * 0.55, 0.05, 3.12), (reach * 1.1, 1.5, 0.10), STEEL)
    lx = reach + 0.35
    for s in (-1, 1):
        member(p, (lx, s * 0.42, 0.1), (lx, s * 0.42, 6.35), 0.07)
    for i in range(13):
        member(p, (lx, -0.42, 0.45 + i * 0.47), (lx, 0.42, 0.45 + i * 0.47),
               0.05)
    for h in (0.95, 1.45):                      # rail round the landing
        p.box((reach * 0.55, 0.80, 3.12 + h), (reach * 1.1, 0.05, 0.05), STEEL)

    flue(p, (reach - 0.7, 0.95, 6.02), 1.7)
    for i in range(3):                          # cable run down the front
        rod(p, (reach + 0.1, -1.1, 6.0 - i * 2.0), (reach + 0.16, -1.1,
            4.0 - i * 2.0), 0.03, RUBBER)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Shanty_Stack", coll)


def build_awning(coll, mats):
    """A shade deck: cloth over a scrap platform. 4.4 m out, 2.9 m tall.

    The only variation with no walls, and the only one whose silhouette is soft.
    A settlement of nothing but hard rusted boxes has no place anybody would
    actually sit, and the cloth is also the one thing here that moves in a
    stiff breeze, which the eye reads as inhabited even in a still render.
    """
    p = Part(mats)
    reach, wide = 4.4, 4.6
    wall_pad(p, -wide / 2, wide / 2, 0, 2.9)

    for s in (-1, 1):                           # the deck
        bracket(p, (0.1, s * (wide / 2 - 0.3), 0.0), reach - 0.6, 1.3)
    for i in range(7):                          # board by board, not one slab
        y = -wide / 2 + 0.3 + i * (wide - 0.6) / 6.0
        p.box(((reach - 0.4) / 2.0 + 0.2, y, -0.05),
              (reach - 0.4, (wide - 0.6) / 6.0 - 0.05, 0.09),
              PLY if i % 3 else RUST)

    # Four poles at unequal heights, so the cloth is not a flat rectangle.
    posts = ((reach - 0.5, -wide / 2 + 0.45, 2.55),
             (reach - 0.5, wide / 2 - 0.45, 2.32),
             (reach - 2.3, -wide / 2 + 0.45, 2.72),
             (reach - 2.3, wide / 2 - 0.45, 2.62))
    for x, y, h in posts:
        member(p, (x, y, 0.0), (x, y, h), 0.09)
    for x, y, h in posts:
        p.box((x, y, h + 0.04), (0.20, 0.20, 0.08), DARK)

    # The cloth: four quads sagging between the posts and the wall.
    hi_w, lo_w = 2.88, 2.62
    for i in range(4):
        t0, t1 = i / 4.0, (i + 1) / 4.0
        y0 = -wide / 2 + 0.45 + (wide - 0.9) * t0
        y1 = -wide / 2 + 0.45 + (wide - 0.9) * t1
        sag = 0.16 * math.sin(math.pi * (t0 + t1) / 2.0)
        p.prism([(0.12, hi_w - sag * 0.4), (reach - 0.5, lo_w - sag),
                 (reach - 0.5, lo_w - sag - 0.05),
                 (0.12, hi_w - sag * 0.4 - 0.05)],
                y1 - y0, axis='Y', mat=SHADE, offset=(0, (y0 + y1) / 2.0, 0))
    for s in (-1, 1):                           # guy lines to the deck edge
        rod(p, (reach - 0.5, s * (wide / 2 - 0.45), 2.45),
            (reach + 0.9, s * (wide / 2 - 0.1), -0.05), 0.022, CANVAS)

    # What the shade is for.
    p.box((reach - 1.6, -0.5, 0.38), (1.5, 0.65, 0.75), PLY)
    p.box((reach - 1.6, 0.75, 0.22), (1.2, 0.40, 0.42), PLY)
    p.box((reach - 2.6, 1.5, 0.30), (0.55, 0.55, 0.60), RUST)
    p.cyl((0.7, -wide / 2 + 0.7, 0.42), 0.30, 0.85, 'Z', seg=10, mat=DARK)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Shanty_Awning", coll)


def build_water(coll, mats):
    """A water tank on a welded frame, with gutter and downpipe. 2.6 m out.

    Not a dwelling, and that is why it is here: a settlement is not only rooms.
    The gutter running off the tank top is the detail that explains where the
    water comes from on a planet like this, and it costs four boxes.
    """
    p = Part(mats)
    reach, wide = 2.6, 2.8
    wall_pad(p, -wide / 2, wide / 2, 0, 4.6)

    # The frame: four legs off the wall brackets, cross-braced.
    legs = ((0.35, -wide / 2 + 0.35), (0.35, wide / 2 - 0.35),
            (reach - 0.35, -wide / 2 + 0.35), (reach - 0.35, wide / 2 - 0.35))
    for x, y in legs:
        member(p, (x, y, 0.0), (x, y, 2.55), 0.12)
    for a, b in ((0, 1), (2, 3), (0, 2), (1, 3)):
        (ax, ay), (bx, by) = legs[a], legs[b]
        member(p, (ax, ay, 0.15), (bx, by, 2.35), 0.08, overlap=0.08)
        member(p, (ax, ay, 2.45), (bx, by, 2.45), 0.09)
        member(p, (ax, ay, 0.10), (bx, by, 0.10), 0.09)
    for x, y in legs[:2]:
        member(p, (0.1, y, 1.3), (x, y, 1.3), 0.09)
        member(p, (0.1, y, 2.45), (x, y, 2.45), 0.09)

    # The tank. A cylinder on its side, because that is what a salvaged
    # pressure vessel is, and it reads differently from every box around it.
    p.cyl((reach / 2.0, 0, 3.32), 0.92, wide - 0.35, 'Y', seg=16, mat=HULL)
    for s in (-1, 1):
        p.cyl((reach / 2.0, s * (wide - 0.35) / 2.0, 3.32), 0.96, 0.10, 'Y',
              seg=16, mat=RUST)
    for i in range(3):                          # banding straps
        p.torus((reach / 2.0, -0.7 + i * 0.7, 3.32), 0.94, 0.045, 'Y', 16, 6,
                mat=STEEL)
    p.box((reach / 2.0, 0, 2.62), (1.7, wide - 0.5, 0.14), STEEL)   # cradle
    p.cyl((reach / 2.0, 0, 4.26), 0.30, 0.30, 'Z', seg=10, mat=DARK)  # hatch
    p.cyl((reach / 2.0, 0, 4.44), 0.33, 0.07, 'Z', seg=10, mat=STEEL)

    # Gutter along the wall above, feeding a downpipe into the tank.
    p.box((0.28, 0, 4.55), (0.34, wide + 0.6, 0.10), RUST)
    p.box((0.46, 0, 4.62), (0.06, wide + 0.6, 0.22), RUST)
    p.cyl((0.28, wide / 2 + 0.15, 4.0), 0.09, 1.1, 'Z', seg=8, mat=RUBBER)
    p.cyl((reach / 2.0 - 0.35, wide / 2 + 0.15, 4.42), 0.09, 1.1, 'X', seg=8,
          mat=RUBBER)

    # The tap, and the bucket under it.
    p.cyl((reach / 2.0, -wide / 2 + 0.3, 2.45), 0.06, 0.55, 'Z', seg=6,
          mat=STEEL)
    p.cyl((reach / 2.0, -wide / 2 + 0.3, 2.18), 0.10, 0.16, 'Z', seg=8,
          mat=DARK)
    p.cyl((reach / 2.0, -wide / 2 + 0.3, 0.20), 0.24, 0.40, 'Z', seg=10,
          mat=STEEL, radius_top=0.27)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_Shanty_Water", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_leanto(collection("Coll_Shanty_LeanTo", root), mats)
    build_box(collection("Coll_Shanty_Box", root), mats)
    build_stack(collection("Coll_Shanty_Stack", root), mats)
    build_awning(collection("Coll_Shanty_Awning", root), mats)
    build_water(collection("Coll_Shanty_Water", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
