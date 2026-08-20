"""components/structural/outpost_block — pressure hulls bolted onto a tower.

Three components in this library are already storey-sized boxes and this is a
fourth, which needs justifying. `tower_bay` is clad refinery frame. `slab_block`
is a rusted mining hulk: a field of bolted plates with structure showing through
the gaps. `cabin_module` is a converted 5 m shipping container. `prefab_hab` is a
single-storey unit you walk into off the sand.

This is the fourth thing: a **sealed hull hung 10 to 30 m up a mast**, on a world
that is trying to get in. Nothing about it is architecture. Its language is a
battered, chamfered mass with no vertical wall to catch the wind; a skin of
mismatched salvage plate re-welded over decades; external ribs because the
pressure load is outward; and — the point of the whole component — **almost no
openings**. What openings exist are a single long armoured slit and two or three
tiny ports, not a rank of windows, because every aperture is a hole in a
pressure vessel and you buy as few as you can live with.

The first version of this file put a regular grid of punched windows on every
face, a parapet capping band round every roof and a porched door on the front.
Those are civic-architecture tells — an office block, a municipal roofline, a
domestic threshold — and they are all gone.

## Everything measures itself against the batter

The walls lean in about 6 degrees as they rise, and that single decision drives
the shape of every helper below. A rib, a patch plate, a viewport or a radiator
placed at a fixed half-width will hang off the face by 200-800 mm somewhere up
its height, which reads as detail floating beside the building rather than
bolted to it. So nothing here takes `w, d`. Everything takes a **hull tuple**,
`(z0, z1, w0, d0, w1, d1)`, and asks `hull_at()` for the half-width at the exact
height it is being placed — and ribs and slits are built between two such
queries, so they lean with the wall they are on.

Envelope and origin are unchanged from the first version — base centre, and
`Station` still occupies 15.0 x 11.0 x 7.40 — so the assemblies that place these
did not have to move.

    blender --background --python outpost_block.py -- --out outpost_block.blend

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

from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Paint_Coral_Faded",     # 0 CORAL  what is left of the original skin
    "Mat_Metal_HullRust_Orange", # 1 HULL   oxidised plate — most of the hull now
    "Mat_Metal_Rust_Heavy",      # 2 RUST   deep corrosion, weld-on repair plate
    "Mat_Neutral_Slate_Dark",    # 3 SLATE  armour bands, ribs, hoods, roof deck
    "Mat_Metal_Steel_Worn",      # 4 STEEL  exposed frame, brackets, grating
    "Mat_Metal_Steel_Dark",      # 5 DARK   fittings, hatches, machinery cases
    "Mat_Neutral_Black_Matte",   # 6 BLACK  the dark inside every recess
    "Mat_Emissive_Amber",        # 7 AMBER  the few panes that are lit
    "Mat_Paint_Hull_Bleached",   # 8 BLEACH sun-killed plate, the older repairs
    "Mat_Metal_Copper_Oxide",    # 9 COPPER verdigris pipework — the odd note
    "Mat_Paint_Warn_Red",        # 10 RED   hazard marking around the openings
    "Mat_Glass_Canopy_Tinted",   # 11 GLASS the armoured pane itself
]
(CORAL, HULL, RUST, SLATE, STEEL, DARK, BLACK, AMBER, BLEACH, COPPER, RED,
 GLASS) = range(12)

# The four ages of plate on one hull, weighted rather than uniform. An even mix
# puts as much bleached and coral on the skin as oxide, and the hull comes out
# looking speckled and pale instead of rusted — the pale plates read as stickers
# at distance. Oxide is what the hull mostly is now; the original coral is the
# rarest thing on it.
SKINS = (HULL, RUST, HULL, RUST, HULL, BLEACH, CORAL)


# ---------------------------------------------------------------------------
# The hull, and the batter every detail has to follow
# ---------------------------------------------------------------------------

def chamfer_plan(w, d, cut):
    """A rectangle with its corners cut off — the plan every hull here uses.

    A square plan gives four 90 degree arrises, and a 90 degree arris is what
    the eye reads as *building*. Cutting them turns the same footprint into
    something that looks designed to shed weather and take a knock, which is
    most of the difference between a hab block and a hut.
    """
    hw, hd = w / 2.0, d / 2.0
    return [(-hw + cut, -hd), (hw - cut, -hd), (hw, -hd + cut), (hw, hd - cut),
            (hw - cut, hd), (-hw + cut, hd), (-hw, hd - cut), (-hw, -hd + cut)]


def hull_at(hull, z):
    """Half-width and half-depth of the battered hull at height z."""
    z0, z1, w0, d0, w1, d1 = hull
    t = max(0.0, min(1.0, (z - z0) / (z1 - z0)))
    return (w0 + (w1 - w0) * t) / 2.0, (d0 + (d1 - d0) * t) / 2.0


def face(hull, z, side):
    """The outer surface coordinate of one face at height z."""
    hw, hd = hull_at(hull, z)
    return {'-Y': -hd, '+Y': hd, '-X': -hw, '+X': hw}[side]


def batter_rot(hull, side):
    """The lean of one face, as a rotation for anything laid flat against it."""
    z0, z1, w0, d0, w1, d1 = hull
    run = (d0 - d1) / 2.0 if side[1] == 'Y' else (w0 - w1) / 2.0
    ang = math.atan2(run, z1 - z0)
    sgn = -1 if side[0] == '-' else 1
    axis = 'X' if side[1] == 'Y' else 'Y'
    return Matrix.Rotation(sgn * ang * (1 if axis == 'X' else -1), 4, axis)


def mass(p, hull, cut0, cut1, mat=HULL, at=(0.0, 0.0)):
    """A battered, chamfered hull section. Flat-shaded — it is a faceted solid.

    `at` shifts the section in plan. `Part.loft` has no offset argument, so the
    displacement is baked into the profile, which is also what lets a hull be
    assembled from two masses that do not share a centreline.
    """
    z0, z1, w0, d0, w1, d1 = hull
    dx, dy = at

    def prof(w, dd, cut):
        return [(u + dx, v + dy) for u, v in chamfer_plan(w, dd, cut)]

    faces = p.loft([(z0, prof(w0, d0, cut0)), (z1, prof(w1, d1, cut1))],
                   axis='Z', mat=mat)
    return p.shade(faces, False)


def patch_skin(p, hull, seed=0, count=14, sides=('-Y', '+Y', '-X', '+X')):
    """Mismatched plate welded over the hull in four states of oxidation.

    The hull is not one colour, because it has not been one hull. Sizes,
    positions and materials are all random: a *regular* patch field is just a
    window grid wearing rust.
    """
    z0, z1 = hull[0], hull[1]
    rng = random.Random(seed)
    for _ in range(count):
        side = rng.choice(sides)
        mat = rng.choice(SKINS)
        z = rng.uniform(z0 + 0.6, z1 - 0.7)
        hw, hd = hull_at(hull, z)
        pw = rng.uniform(0.9, 2.4)
        ph = rng.uniform(0.6, 1.6)
        rot = batter_rot(hull, side)
        if side in ('-Y', '+Y'):
            sgn = -1 if side == '-Y' else 1
            u = rng.uniform(-hw + 1.1, hw - 1.1)
            p.box((u, sgn * (hd + 0.05), z), (pw, 0.12, ph), mat, rot=rot)
        else:
            sgn = -1 if side == '-X' else 1
            u = rng.uniform(-hd + 1.1, hd - 1.1)
            p.box((sgn * (hw + 0.05), u, z), (0.12, pw, ph), mat, rot=rot)


def ribs(p, hull, us=(), sides=('-Y',), size=0.32, mat=SLATE, inset=0.9):
    """External ribs at irregular spacing, leaning with the wall they are on.

    Irregular is the whole instruction: ribs on a regular pitch are pilasters,
    and pilasters are architecture. Each rib is built between the face position
    at its foot and at its head, so it lies on the batter instead of crossing it.
    """
    z0, z1 = hull[0] + 0.1, hull[1] - inset
    for side in sides:
        sgn = -1 if side[0] == '-' else 1
        for u in us:
            a = face(hull, z0, side) + sgn * 0.03
            b = face(hull, z1, side) + sgn * 0.03
            if side[1] == 'Y':
                p0, p1 = Vector((u, a, z0)), Vector((u, b, z1))
            else:
                p0, p1 = Vector((a, u, z0)), Vector((b, u, z1))
            dv = p1 - p0
            rot = Vector((0, 0, 1)).rotation_difference(
                dv.normalized()).to_matrix().to_4x4()
            sz = (size, 0.30, dv.length) if side[1] == 'Y' \
                else (0.30, size, dv.length)
            p.box((p0 + p1) / 2.0, sz, mat, rot=rot)


def slot_view(p, hull, cu, cz, length, height, side='-Y', bays=3, lit=2,
              cant=15.0):
    """The one big aperture: a canted armoured slit under a projecting brow.

    This replaced the window grid, and two things make it read as armour rather
    than as a letterbox. The brow projects 0.9 m over an opening set 0.3 m back,
    which is what tells the eye the wall has thickness. And the pane is **dark**
    — only `lit` of the bays behind it are amber. A fully emissive slit of this
    size stops being a window and becomes a lightbox, which was the first
    version's mistake: the glow has to be the exception inside the dark band.
    """
    sgn = -1 if side[0] == '-' else 1
    hw, hd = hull_at(hull, cz)
    v0 = hd if side[1] == 'Y' else hw
    rot = Matrix.Rotation(math.radians(-sgn * cant), 4,
                          'X' if side[1] == 'Y' else 'Y')

    def at(off, du=0.0):
        return (cu + du, sgn * (v0 + off), cz) if side[1] == 'Y' \
            else (sgn * (v0 + off), cu + du, cz)

    def sz(a, b, c):
        return (a, b, c) if side[1] == 'Y' else (b, a, c)

    p.box(at(0.02), sz(length + 0.6, 0.34, height + 0.50), SLATE, rot=rot)
    p.box(at(0.22), sz(length, 0.16, height), BLACK, rot=rot)
    for i in range(bays):
        du = -length / 2 + length * (i + 0.5) / bays
        p.box(at(0.28, du), sz(length / bays - 0.14, 0.06, height * 0.82),
              AMBER if i < lit else GLASS, rot=rot)
    for i in range(1, bays):                       # heavy mullions, not a grid
        du = -length / 2 + length * i / bays
        p.box(at(0.18, du), sz(0.18, 0.34, height + 0.34), STEEL, rot=rot)
    # the brow, angled down over the opening, on visible stays
    p.box((cu, sgn * (v0 + 0.52), cz + height / 2 + 0.40) if side[1] == 'Y'
          else (sgn * (v0 + 0.52), cu, cz + height / 2 + 0.40),
          sz(length + 1.0, 0.94, 0.18), SLATE,
          rot=Matrix.Rotation(math.radians(sgn * 24), 4,
                              'X' if side[1] == 'Y' else 'Y'))
    for s in (-1, 1):
        du = s * (length / 2 + 0.30)
        p.box(at(0.38, du), sz(0.11, 0.70, 0.50), STEEL,
              rot=Matrix.Rotation(math.radians(-sgn * 40), 4,
                                  'X' if side[1] == 'Y' else 'Y'))
    p.box(at(0.32, 0.0)[:2] + (cz - height / 2 - 0.30,),
          sz(length + 0.8, 0.48, 0.13), STEEL)
    for k in range(4):                             # corrosion wash below
        du = -length / 2 + 0.4 + k * (length / 3.4)
        p.box(at(0.06, du)[:2] + (cz - height / 2 - 1.25,),
              sz(0.24, 0.10, 1.70), RUST)


def port(p, hull, cu, cz, side='-Y', r=0.32, lit=False):
    """A small armoured port: deep, bolted, and rare.

    Placed in ones and twos and never in a line. The bolt ring is what stops it
    reading as a porthole on a ferry.
    """
    sgn = -1 if side[0] == '-' else 1
    hw, hd = hull_at(hull, cz)
    v0 = hd if side[1] == 'Y' else hw
    axis = 'Y' if side[1] == 'Y' else 'X'

    def at(off):
        return (cu, sgn * (v0 + off), cz) if side[1] == 'Y' \
            else (sgn * (v0 + off), cu, cz)

    p.cyl(at(0.04), r + 0.22, 0.28, axis=axis, seg=10, mat=SLATE)
    p.cyl(at(0.18), r, 0.24, axis=axis, seg=10, mat=BLACK)
    p.cyl(at(0.26), r * 0.80, 0.06, axis=axis, seg=10,
          mat=AMBER if lit else GLASS)
    for k in range(6):
        a = 2 * math.pi * k / 6
        c = at(0.20)
        off = ((r + 0.14) * math.cos(a), (r + 0.14) * math.sin(a))
        pos = (c[0] + off[0], c[1], c[2] + off[1]) if side[1] == 'Y' \
            else (c[0], c[1] + off[0], c[2] + off[1])
        p.cyl(pos, 0.038, 0.11, axis=axis, seg=5, mat=DARK)
    c = at(0.10)
    p.box((c[0], c[1], c[2] - r - 0.45), (0.18, 0.12, 0.80), RUST)


def airlock(p, hull, cu, side='-Y', z0=0.0):
    """A pressure hatch with a hood — not a door with a porch.

    Rectangular with the top corners cut, a heavy collar, dogging lugs round the
    rim and a grab rail. Everything that says *seal* rather than *entrance*.
    """
    sgn = -1 if side[0] == '-' else 1
    hw, hd = hull_at(hull, z0 + 1.1)
    v0 = (hd if side[1] == 'Y' else hw) * 1.0
    prof = [(-0.88, 0.0), (0.88, 0.0), (0.88, 1.60), (0.54, 2.06),
            (-0.54, 2.06), (-0.88, 1.60)]
    axis = 'Y' if side[1] == 'Y' else 'X'
    base = (cu, sgn * (v0 + 0.08), 0) if side[1] == 'Y' \
        else (sgn * (v0 + 0.08), cu, 0)
    f = p.prism([(u, v + z0 + 0.10) for u, v in prof], 0.36, axis=axis,
                mat=SLATE, offset=base)
    p.shade(f, False)
    base2 = (cu, sgn * (v0 + 0.26), 0) if side[1] == 'Y' \
        else (sgn * (v0 + 0.26), cu, 0)
    f = p.prism([(u * 0.80, v * 0.86 + z0 + 0.24) for u, v in prof], 0.18,
                axis=axis, mat=DARK, offset=base2)
    p.shade(f, False)
    for k in range(8):
        a = math.pi * k / 7 - math.pi / 2
        du, dz = 0.98 * math.sin(a), 1.05 + 0.95 * math.cos(a)
        pos = (cu + du, sgn * (v0 + 0.24), z0 + dz) if side[1] == 'Y' \
            else (sgn * (v0 + 0.24), cu + du, z0 + dz)
        p.cyl(pos, 0.075, 0.17, axis=axis, seg=5, mat=STEEL)
    hood = (cu, sgn * (v0 + 0.54), z0 + 2.34) if side[1] == 'Y' \
        else (sgn * (v0 + 0.54), cu, z0 + 2.34)
    p.box(hood, (2.20, 0.78, 0.17) if side[1] == 'Y' else (0.78, 2.20, 0.17),
          SLATE, rot=Matrix.Rotation(math.radians(sgn * 18), 4,
                                     'X' if side[1] == 'Y' else 'Y'))
    sill = (cu, sgn * (v0 + 0.18), z0 + 0.06) if side[1] == 'Y' \
        else (sgn * (v0 + 0.18), cu, z0 + 0.06)
    p.box(sill, (1.95, 0.62, 0.15) if side[1] == 'Y' else (0.62, 1.95, 0.15),
          STEEL)
    mark = (cu, sgn * (v0 + 0.14), z0 + 2.66) if side[1] == 'Y' \
        else (sgn * (v0 + 0.14), cu, z0 + 2.66)
    p.box(mark, (1.35, 0.06, 0.24) if side[1] == 'Y' else (0.06, 1.35, 0.24),
          RED)


def radiator(p, hull, cu, cz, side='+Y', fins=9, span=2.6, height=2.2):
    """A fin bank — thermal kit, and pure sci-fi shorthand.

    Nothing on a terrestrial building needs to dump heat to a thin atmosphere,
    so a rack of thin fins standing off the hull does more to place the thing
    off-world than any amount of greeble.
    """
    sgn = -1 if side[0] == '-' else 1
    hw, hd = hull_at(hull, cz)
    v0 = (hd if side[1] == 'Y' else hw) + 0.46

    def at(off, du=0.0, dz=0.0):
        return (cu + du, sgn * (v0 + off), cz + dz) if side[1] == 'Y' \
            else (sgn * (v0 + off), cu + du, cz + dz)

    def sz(a, b, c):
        return (a, b, c) if side[1] == 'Y' else (b, a, c)

    p.box(at(0.0), sz(span + 0.34, 0.22, height + 0.34), SLATE)
    for k in range(fins):
        du = -span / 2 + span * (k + 0.5) / fins
        p.box(at(0.36, du), sz(0.075, 0.68, height), STEEL)
    for dz in (height / 2, -height / 2):
        p.box(at(0.66, 0.0, dz), sz(span + 0.22, 0.15, 0.15), DARK)
    for s in (-1, 1):
        p.cyl(at(0.32, s * (span / 2 + 0.12), -height / 2 - 0.55), 0.10, 1.15,
              seg=7, mat=COPPER)


def tank_cluster(p, hull, cu, cz, side='-X', n=3, r=0.62, h=2.9):
    """Pressure bottles clamped to a flank, on a welded cradle."""
    sgn = -1 if side[0] == '-' else 1
    hw, hd = hull_at(hull, cz)
    x0 = sgn * (hw + r + 0.22)
    for k in range(n):
        y = cu + (k - (n - 1) / 2.0) * (r * 2.25)
        p.cyl((x0, y, cz), r, h, seg=10, mat=HULL if k % 2 else RUST)
        p.cyl((x0, y, cz + h / 2), r * 0.98, 0.22, seg=10, mat=SLATE)
        p.cyl((x0, y, cz - h / 2), r * 0.98, 0.22, seg=10, mat=SLATE)
        p.cyl((x0, y, cz + h / 2 + 0.26), 0.11, 0.32, seg=6, mat=DARK)
    for zz in (cz - h * 0.28, cz + h * 0.28):
        p.box((x0, cu, zz), (r * 2.4, r * 2.25 * n + 0.3, 0.15), STEEL)
        p.box((sgn * (hw + 0.10), cu, zz), (0.36, r * 2.25 * n * 0.7, 0.17),
              SLATE)


def conduit(p, hull, z, side='-Y', count=3, span=0.9, mat=COPPER):
    """Pipe runs wrapping the hull, with clamps where they cross a rib."""
    sgn = -1 if side[0] == '-' else 1
    hw, hd = hull_at(hull, z)
    for k in range(count):
        p.cyl((0, sgn * (hd + 0.28 + k * 0.21), z + k * 0.17), 0.11,
              hw * 2 * span, axis='X', seg=7, mat=mat if k % 2 else STEEL)
    for u in (-hw * 0.55, hw * 0.30, hw * 0.72):
        p.box((u, sgn * (hd + 0.32), z + 0.10), (0.22, 0.66, 0.46), DARK)


def stack_hood(p, at, z, r=0.42, h=1.5):
    """A short capped flue — the exhaust of something unpleasant."""
    x, y = at
    p.cyl((x, y, z + h / 2), r, h, seg=9, mat=DARK)
    p.cyl((x, y, z + h + 0.10), r * 1.35, 0.20, seg=9, mat=STEEL)
    p.cyl((x, y, z + h * 0.4), r * 1.15, 0.14, seg=9, mat=RUST)


def roof_farm(p, hull, seed=0, tanks=True):
    """The roof is an equipment farm, not a terrace with a parapet.

    Kept **dark** and broken into levels. The first version laid one bright
    steel grating over the whole footprint, and a 14 x 10 m pale plane is the
    largest single surface on the component — it read as an empty car park and
    undid the rust everywhere else. Now the deck is slate, two raised plinths
    break the plane, and the kit is bunched at one end rather than spread.
    """
    rng = random.Random(seed)
    z = hull[1]
    hw, hd = hull_at(hull, z)
    w, d = hw * 2, hd * 2
    p.box((0, 0, z + 0.09), (w - 0.7, d - 0.7, 0.18), SLATE)
    p.box((-w * 0.16, 0.0, z + 0.28), (w * 0.46, d * 0.60, 0.24), RUST)
    p.box((w * 0.28, -d * 0.18, z + 0.24), (w * 0.30, d * 0.42, 0.18), HULL)
    # Two runs of duckboard across the open deck. Whatever the plinths do not
    # cover is bare plate, and bare plate at 13 x 9 m is the largest single
    # surface on the hull — left empty it reads as a car park and undoes the
    # rust on every face below it.
    for k in range(2):
        p.box((0, -d * 0.30 + k * d * 0.44, z + 0.22), (w - 1.4, 0.85, 0.10),
              STEEL)
    for k in range(6):
        p.box((-w * 0.36 + k * w * 0.145, d * 0.36, z + 0.24),
              (0.5, 0.5, 0.14), rng.choice((RUST, HULL, DARK)))
    for s in (-1, 1):                                    # low kick rails only
        p.box((s * (hw - 0.42), 0, z + 0.34), (0.11, d - 0.9, 0.32), SLATE)
        p.box((0, s * (hd - 0.42), z + 0.34), (w - 0.9, 0.11, 0.32), SLATE)
    if tanks:
        p.cyl((w * 0.20, d * 0.18, z + 1.30), 1.05, 2.20, seg=12, mat=HULL)
        p.cyl((w * 0.20, d * 0.18, z + 2.44), 1.08, 0.20, seg=12, mat=SLATE)
        p.cyl((w * 0.20, d * 0.18, z + 0.28), 1.16, 0.24, seg=12, mat=RUST)
        for k in range(2):
            p.cyl((w * 0.20, d * 0.18, z + 0.75 + k * 1.05), 1.09, 0.13,
                  seg=12, mat=STEEL)
    p.box((-w * 0.26, d * 0.20, z + 0.76), (1.70, 1.25, 0.90), DARK)
    p.box((-w * 0.26, d * 0.20, z + 1.28), (0.85, 0.62, 0.18), STEEL)
    for k in range(4):                                   # roof fin bank
        p.box((-w * 0.34 + k * 0.27, -d * 0.24, z + 0.90), (0.08, 1.55, 0.98),
              STEEL)
    p.box((-w * 0.32, -d * 0.24, z + 0.44), (1.20, 1.75, 0.18), SLATE)
    stack_hood(p, (w * 0.36, -d * 0.26), z + 0.20, r=0.36, h=1.40)
    stack_hood(p, (w * 0.06, d * 0.30), z + 0.20, r=0.26, h=0.95)
    for _ in range(7):
        p.box((rng.uniform(-hw * 0.8, hw * 0.8), rng.uniform(-hd * 0.8, hd * 0.8),
               z + 0.34), (rng.uniform(0.3, 0.8), rng.uniform(0.3, 0.8),
                           rng.uniform(0.24, 0.5)),
              rng.choice((DARK, STEEL, SLATE, RUST)))


def ladder(p, hull, u, side='-Y', z0=0.5, z1=None):
    z1 = hull[1] + 0.5 if z1 is None else z1
    sgn = -1 if side[0] == '-' else 1
    v = face(hull, (z0 + z1) / 2, side) + sgn * 0.38
    for s in (-1, 1):
        p.box((u + s * 0.28, v, (z0 + z1) / 2), (0.07, 0.07, z1 - z0), STEEL)
    n = max(2, int((z1 - z0) / 0.34))
    for k in range(n):
        p.box((u, v, z0 + (k + 0.5) * (z1 - z0) / n), (0.60, 0.05, 0.05), STEEL)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def block_station(coll, mats):
    """The main hull: 15.0 x 11.0 m, 7.40 m. One slit, three ports, no grid.

    Deliberately asymmetric. The slit sits left of centre, the airlock right of
    it, the tanks on one flank and the radiator on the other, the roof kit
    bunched at one end. Everything the first version mirrored is now placed once.
    """
    H = (0.32, 7.40, 15.0, 11.0, 13.3, 9.4)          # ~6.4 degrees of batter
    p = Part(mats)
    mass(p, H, 1.40, 1.05)
    mass(p, (0.0, 0.36, 15.3, 11.3, 15.3, 11.3), 1.5, 1.5, SLATE)
    p.box((0, 0, 7.26), (13.5, 9.6, 0.34), SLATE)                # hull cap ring
    patch_skin(p, H, seed=11, count=20)
    ribs(p, H, us=(-6.0, -3.3, 1.3, 4.8), sides=('-Y', '+Y'))
    ribs(p, H, us=(-2.6, 2.2), sides=('+X',))

    slot_view(p, H, -3.30, 4.60, 7.40, 1.55, '-Y', bays=4, lit=2)
    port(p, H, 4.55, 5.05, '-Y', lit=True)
    port(p, H, 6.05, 2.35, '-Y')
    port(p, H, -6.05, 2.55, '-Y', lit=True)
    airlock(p, H, 5.30, '-Y', z0=0.36)
    slot_view(p, H, 2.10, 5.10, 4.20, 1.20, '+Y', bays=3, lit=1)

    radiator(p, H, -5.10, 3.70, '+Y', fins=11, span=4.2, height=2.9)
    tank_cluster(p, H, 3.30, 3.40, '-X', n=3)
    conduit(p, H, 1.55, '-Y', count=3, span=0.72)
    p.box((7.05, -2.8, 5.30), (0.36, 3.2, 1.9), DARK)            # flank plant
    for k in range(5):
        p.box((7.30, -4.2 + k * 0.7, 5.30), (0.32, 0.17, 1.7), STEEL)
    ladder(p, H, -6.1, '-Y', z0=0.6)
    roof_farm(p, H, seed=3)
    p.bevel(width=0.022, segments=1)
    return p.finish("Mesh_OutpostBlock_Station", coll)


def block_plant(coll, mats):
    """The machine module clamped to a mast. 6.0 x 5.4 x 4.20 m, near blind.

    One port, because there is nobody in here. It is the counterweight to the
    hulls that do have crew, and the tank on its roof is what a long lattice run
    needs to interrupt it.
    """
    H = (0.24, 4.20, 6.0, 5.4, 5.3, 4.7)
    p = Part(mats)
    mass(p, H, 0.90, 0.72)
    mass(p, (0.0, 0.28, 6.24, 5.64, 6.24, 5.64), 0.95, 0.95, SLATE)
    patch_skin(p, H, seed=5, count=10)
    ribs(p, H, us=(-1.6, 1.5), sides=('-Y',), inset=0.5)
    port(p, H, -1.70, 2.95, '-Y', r=0.26, lit=True)
    p.box((0, 2.86, 2.10), (5.2, 0.36, 3.0), STEEL)              # mast bracket
    for s in (-1, 1):
        p.box((s * 2.05, 3.04, 2.10), (0.26, 0.30, 3.4), DARK)
    radiator(p, H, 1.20, 2.55, '-Y', fins=7, span=2.0, height=1.9)
    p.box((0, 0, 4.28), (5.0, 4.4, 0.20), SLATE)
    # the tank, off-centre and strapped rather than sitting square
    p.cyl((0.84, -0.25, 6.25), 1.35, 3.70, seg=12, mat=HULL)
    p.cyl((0.84, -0.25, 8.14), 1.42, 0.28, seg=12, mat=SLATE)
    p.cyl((0.84, -0.25, 4.50), 1.52, 0.32, seg=12, mat=RUST)
    for k in range(3):
        p.cyl((0.84, -0.25, 5.10 + k * 1.15), 1.40, 0.14, seg=12, mat=STEEL)
    p.box((0.84, -0.25, 8.36), (0.9, 0.9, 0.22), DARK)
    stack_hood(p, (-1.80, 0.95), 4.38, r=0.46, h=1.9)
    for k in range(4):
        p.cyl((-0.61, -1.15 + k * 0.6, 4.82), 0.09, 1.10, seg=6, mat=COPPER)
    p.box((-2.05, -1.50, 4.72), (0.90, 0.80, 0.56), DARK)
    p.bevel(width=0.022, segments=1)
    return p.finish("Mesh_OutpostBlock_Plant", coll)


def block_hab(coll, mats):
    """A tall hull: 8.5 x 7.0 m over 9.20 m, with a vertical slit and a setback.

    Turned on end, and the aperture turns with it — one tall narrow slot up the
    face instead of the horizontal band, which is the cheapest way to make a
    second hull that is not the first one stretched.
    """
    LO = (0.32, 5.70, 8.5, 7.0, 7.7, 6.2)
    HI = (5.70, 9.20, 7.4, 5.9, 6.5, 5.0)
    p = Part(mats)
    mass(p, LO, 1.00, 0.86)
    mass(p, HI, 0.82, 0.70)
    mass(p, (0.0, 0.36, 8.8, 7.3, 8.8, 7.3), 1.05, 1.05, SLATE)
    p.box((0, 0, 5.70), (7.9, 6.4, 0.30), SLATE)                 # setback ring
    patch_skin(p, LO, seed=7, count=13)
    patch_skin(p, HI, seed=8, count=6)
    ribs(p, LO, us=(-2.9, -0.3, 2.6), sides=('-Y', '+X'), inset=0.4)
    # the vertical slit, off-axis, leaning with the wall
    for k in range(3):
        slot_view(p, LO, -1.55, 1.95 + k * 1.55, 1.15, 1.25, '-Y', bays=1,
                  lit=1 if k != 1 else 0, cant=9.0)
    port(p, HI, 2.05, 7.35, '-Y', r=0.28, lit=True)
    port(p, LO, 2.95, 2.20, '-Y', r=0.26)
    airlock(p, LO, 2.30, '-Y', z0=0.36)
    tank_cluster(p, LO, 0.0, 3.10, '+X', n=2, r=0.55, h=2.4)
    radiator(p, LO, 1.30, 4.20, '+Y', fins=8, span=2.6, height=2.4)
    conduit(p, LO, 1.60, '-Y', count=2, span=0.7)
    ladder(p, LO, -3.2, '+Y', z0=0.6, z1=5.9)
    roof_farm(p, HI, seed=9)
    p.bevel(width=0.022, segments=1)
    return p.finish("Mesh_OutpostBlock_Hab", coll)


def block_annex(coll, mats):
    """A low blind store: 12.0 x 8.0 m, 4.00 m, one armoured shutter.

    Every outpost needs the hull nobody looks at, and a deck of hulls with lit
    slits reads as a hotel without one.
    """
    H = (0.30, 4.00, 12.0, 8.0, 10.9, 6.9)
    p = Part(mats)
    mass(p, H, 1.20, 0.98, RUST)
    mass(p, (0.0, 0.34, 12.26, 8.26, 12.26, 8.26), 1.25, 1.25, SLATE)
    patch_skin(p, H, seed=21, count=13)
    ribs(p, H, us=(-4.3, -1.0, 3.5), sides=('+Y',), inset=0.4)
    # the shutter: a recessed armoured roller behind a lintel beam
    p.box((-1.70, -3.76, 1.95), (5.60, 0.36, 3.10), SLATE)
    p.box((-1.70, -3.94, 1.90), (5.10, 0.14, 2.80), DARK)
    for k in range(9):
        p.box((-1.70, -4.03, 0.66 + k * 0.30), (5.02, 0.09, 0.20), STEEL)
    p.box((-1.70, -4.12, 3.58), (6.10, 0.70, 0.32), SLATE,
          rot=Matrix.Rotation(math.radians(13), 4, 'X'))
    for s in (-1, 1):
        p.box((-1.70 + s * 2.90, -3.98, 1.95), (0.26, 0.28, 3.20), STEEL)
    p.box((-1.70, -3.90, 0.34), (5.80, 0.52, 0.20), RUST)
    port(p, H, 4.05, 2.90, '-Y', r=0.26)
    airlock(p, H, 4.10, '+Y', z0=0.34)
    tank_cluster(p, H, 0.0, 2.10, '-X', n=2, r=0.50, h=2.0)
    stack_hood(p, (3.60, 1.60), 4.10, r=0.40, h=1.7)
    roof_farm(p, H, seed=13, tanks=False)
    p.bevel(width=0.022, segments=1)
    return p.finish("Mesh_OutpostBlock_Annex", coll)


def block_bleached(coll, mats):
    """`Station`'s envelope after the paint died: bleached, over-patched, dark.

    The slit is unlit and the ports are blind. Nothing structural has failed —
    this is the hull that is still sealed but no longer crewed, which is a
    different thing from `Breached` and reads at a different distance.
    """
    H = (0.32, 7.40, 15.0, 11.0, 13.3, 9.4)
    p = Part(mats)
    mass(p, H, 1.40, 1.05, BLEACH)
    mass(p, (0.0, 0.36, 15.3, 11.3, 15.3, 11.3), 1.5, 1.5, SLATE)
    p.box((0, 0, 7.26), (13.5, 9.6, 0.34), SLATE)
    patch_skin(p, H, seed=31, count=30)                    # heavily over-plated
    ribs(p, H, us=(-6.0, -3.3, 1.3, 4.8), sides=('-Y', '+Y'))
    slot_view(p, H, -3.30, 4.60, 7.40, 1.55, '-Y', bays=4, lit=0)
    port(p, H, 4.55, 5.05, '-Y')
    port(p, H, -6.05, 2.55, '-Y')
    airlock(p, H, 5.30, '-Y', z0=0.36)
    # the radiator has lost half its fins
    p.box((-5.10, 5.16, 3.70), (4.5, 0.22, 3.2), SLATE)
    for k in (0, 1, 3, 4, 7, 8):
        p.box((-5.10 - 2.1 + 4.2 * (k + 0.5) / 11, 5.50, 3.70),
              (0.075, 0.68, 2.9), STEEL)
    for k in range(9):
        p.box((-7.0 + k * 1.7, -5.35, 2.0 + (k % 4) * 1.3), (0.22, 0.10, 2.20),
              RUST)
    tank_cluster(p, H, 3.30, 3.40, '-X', n=3)
    ladder(p, H, -6.1, '-Y', z0=0.6)
    roof_farm(p, H, seed=17, tanks=False)
    p.bevel(width=0.022, segments=1)
    return p.finish("Mesh_OutpostBlock_Bleached", coll)


def block_breached(coll, mats):
    """A hull with one corner torn out, ribs bent back, interior dark.

    The silhouette event. A derelict made of intact hulls reads as an abandoned
    one; something has to be actually open, and a corner is the break that stays
    legible from every angle — unlike a hole in one face, which is invisible
    from the other three.
    """
    cut = 5.2
    MAIN = (0.32, 7.40, 15.0 - cut, 11.0, 15.0 - cut - 1.4, 9.4)
    STUB = (0.32, 7.40, cut, 11.0 - cut, cut - 0.5, 11.0 - cut - 0.5)
    p = Part(mats)
    mass(p, MAIN, 1.20, 1.00, BLEACH, at=(-cut / 2.0, 0.0))
    mass(p, STUB, 0.90, 0.78, BLEACH, at=(15.0 / 2 - cut / 2, -cut / 2))
    mass(p, (0.0, 0.36, 15.3, 11.3, 15.3, 11.3), 1.5, 1.5, SLATE)
    p.box((-cut / 2, 0, 7.26), (15.0 - cut - 0.3, 9.6, 0.34), SLATE)
    patch_skin(p, MAIN, seed=41, count=14, sides=('-Y', '+Y'))
    # the exposed frame standing in the breach
    for k in range(3):
        p.box((15.0 / 2 - cut + 0.3 + k * 2.2, 11.0 / 2 - cut / 2, 3.85),
              (0.24, 0.24, 7.0), STEEL)
    for k in range(3):
        p.box((15.0 / 2 - cut / 2, 11.0 / 2 - 0.5, 1.6 + k * 2.4),
              (cut, 0.22, 0.22), STEEL)
    for k in range(4):                                     # skin peeled back
        p.box((15.0 / 2 - cut + 0.1, 11.0 / 2 - cut + 0.4 + k * 1.1,
               2.0 + k * 1.4), (0.13, 0.95, 0.85), RUST,
              rot=Matrix.Rotation(math.radians(14 * (k - 2)), 4, 'X'))
    p.box((15.0 / 2 - cut / 2, 11.0 / 2 - cut + 0.1, 3.7), (cut, 0.16, 6.6),
          BLACK)
    p.box((15.0 / 2 - cut + 0.1, 11.0 / 2 - cut / 2, 3.7), (0.16, 11.0 - cut,
          6.6), BLACK)
    slot_view(p, MAIN, -4.20, 4.60, 5.20, 1.55, '-Y', bays=3, lit=0)
    port(p, MAIN, -6.30, 2.40, '-Y')
    ribs(p, MAIN, us=(-4.1, -1.0), sides=('-Y',))
    for k in range(5):
        p.box((-6.5 + k * 1.9, -5.35, 3.4 + (k % 3) * 1.5), (0.24, 0.10, 1.80),
              RUST)
    ladder(p, MAIN, -6.3, '-Y', z0=0.6)
    p.box((-cut / 2, 0, 7.52), (15.0 - cut - 0.7, 9.2, 0.18), SLATE)
    p.box((-3.3, 2.0, 7.92), (1.35, 1.05, 0.64), DARK)
    stack_hood(p, (-6.0, -2.4), 7.58, r=0.36, h=1.2)
    p.bevel(width=0.022, segments=1)
    return p.finish("Mesh_OutpostBlock_Breached", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Station", block_station), ("Plant", block_plant),
                     ("Hab", block_hab), ("Annex", block_annex),
                     ("Bleached", block_bleached), ("Breached", block_breached)):
        fn(collection("Coll_OutpostBlock_%s" % name), mats)
    report()
    save(out)


main()
