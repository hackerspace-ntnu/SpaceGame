"""components/structural/station_tower — octagonal shaft sections for a mast.

`structural/tower_bay` already covers the other kind of tower: a rectangular
9 m building storey, 14 m across, that stacks into a slab. This is the opposite
lineage — small in plan, octagonal, and *tapered*, so it reads as a lighthouse
or a survey mast rather than as a block of floors. The two are not variations of
each other, which is why they are separate files.

The taper is the whole point and it does not survive being modelled straight
and scaled: the facet pilasters, the flange rings and the conduit run all have
to follow the batter. Every section is authored at its true angle.

Sections splice at flange rings, so `Flare` → `Taper` → `Collar` stacks without
arithmetic. Origin at the base centre of each section, on its own splice plane.

    blender --background --python station_tower.py -- --out station_tower.blend

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
    "Mat_Paint_Blue_Station",    # 0  the shaft cladding
    "Mat_Paint_White_Arctic",    # 1  lighter panels and flange rings
    "Mat_Metal_Steel_Worn",      # 2  bare structure, ladders, brackets
    "Mat_Metal_Steel_Dark",      # 3  bolts, frames, conduit
    "Mat_Metal_HullRust_Orange", # 4  the oxidised band at the waist
    "Mat_Metal_Rust_Heavy",      # 5  streaks below every fitting
    "Mat_Glass_Canopy_Tinted",   # 6  the small shaft windows
    "Mat_Neutral_Slate_Dark",    # 7  recesses, vent backing
    "Mat_Neutral_Black_Matte",   # 8  shadow gaps
    "Mat_Paint_Warn_Red",        # 9  stencilled numerals and bands
]
BLUE, WHITE, STEEL, DARK, ORANGE, RUST, GLASS, SLATE, BLACK, RED = range(10)

N = 8                    # octagon
FLAT = math.cos(math.pi / N)   # across-flats / across-corners ratio, 0.9239


def octa(r, phase=math.pi / N):
    """An octagon as a profile, phase-rotated so a flat faces +X and +Y."""
    return [(r * math.cos(2 * math.pi * i / N + phase),
             r * math.sin(2 * math.pi * i / N + phase)) for i in range(N)]


def facet(r, i, phase=math.pi / N):
    """Centre point and outward yaw of facet `i` on an octagon of radius `r`."""
    a = 2 * math.pi * (i + 0.5) / N + phase
    rf = r * FLAT
    return (rf * math.cos(a), rf * math.sin(a)), a


def shell(p, z0, z1, r0, r1, mat, steps=2):
    """The tapered octagonal skin. Flat-shaded — a faceted tower should read
    faceted, so the smoothing `loft` applies to a curved surface is undone."""
    secs = []
    for k in range(steps + 1):
        t = k / float(steps)
        secs.append((z0 + (z1 - z0) * t, octa(r0 + (r1 - r0) * t)))
    faces = p.loft(secs, axis='Z', mat=mat)
    p.shade(faces, False)
    return faces


def flange(p, z, r, mat=WHITE, bolt_mat=DARK, depth=0.16, over=0.14, bolts=True):
    """A splice ring: the horizontal that stops a taper reading as a cone."""
    p.loft([(z - depth / 2, octa(r + over)), (z + depth / 2, octa(r + over))],
           axis='Z', mat=mat)
    p.shade(p.loft([(z - depth / 2 - 0.05, octa(r + over * 0.5)),
                    (z + depth / 2 + 0.05, octa(r + over * 0.5))],
                   axis='Z', mat=mat), False)
    if bolts:
        for i in range(N):
            (fx, fy), a = facet(r + over, i)
            for s in (-0.3, 0.3):
                p.cyl((fx - math.sin(a) * s * r, fy + math.cos(a) * s * r, z),
                      0.035, 0.06, axis='X', seg=6, mat=bolt_mat,
                      rot=Matrix.Rotation(a, 4, 'Z'))


def pilasters(p, z0, z1, r0, r1, mat, faces=(0, 2, 4, 6), width=0.3, out=0.07):
    """Vertical rib strips up alternating facets, following the batter."""
    batter = math.atan2(r0 - r1, z1 - z0)
    for i in faces:
        (x0, y0), a = facet(r0, i)
        (x1, y1), _ = facet(r1, i)
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        h = math.hypot(z1 - z0, math.hypot(x1 - x0, y1 - y0))
        rot = Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(-batter, 4, 'Y')
        p.box((cx + math.cos(a) * out / 2, cy + math.sin(a) * out / 2,
               (z0 + z1) / 2), (out, width, h), mat, rot=rot)


def porthole(p, z, r, i, mat=GLASS, rim=STEEL, rad=0.26):
    (fx, fy), a = facet(r, i)
    rot = Matrix.Rotation(a, 4, 'Z')
    p.cyl((fx * 1.005, fy * 1.005, z), rad * 1.3, 0.1, axis='X', seg=12,
          mat=rim, rot=rot)
    p.cyl((fx * 1.03, fy * 1.03, z), rad, 0.06, axis='X', seg=12, mat=mat,
          rot=rot)
    p.box((fx * 1.0, fy * 1.0, z - rad * 2.2), (0.02, rad * 1.2, rad * 2.6),
          RUST, rot=rot)                                    # the streak beneath


def conduit(p, z0, z1, r0, r1, i, mat=DARK, rad=0.055):
    """A service run climbing one corner — cheap, and it breaks the symmetry."""
    (x0, y0), a = facet(r0, i)
    (x1, y1), _ = facet(r1, i)
    batter = math.atan2(math.hypot(x0 - x1, y0 - y1), z1 - z0)
    rot = Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(-batter, 4, 'Y')
    h = math.hypot(z1 - z0, math.hypot(x1 - x0, y1 - y0))
    for s in (-0.16, 0.16):
        p.cyl(((x0 + x1) / 2 - math.sin(a) * s + math.cos(a) * rad,
               (y0 + y1) / 2 + math.cos(a) * s + math.sin(a) * rad,
               (z0 + z1) / 2), rad, h, seg=8, mat=mat, rot=rot)
    for k in range(4):                                      # pipe clamps
        t = (k + 0.5) / 4
        p.box(((x0 + (x1 - x0) * t) + math.cos(a) * rad,
               (y0 + (y1 - y0) * t) + math.sin(a) * rad, z0 + (z1 - z0) * t),
              (0.05, 0.5, 0.07), STEEL, rot=Matrix.Rotation(a, 4, 'Z'))


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def tower_taper(mats, coll):
    """The main 8.6 m shaft: 4.6 m across the flats at the foot, 3.2 at the top."""
    p = Part(mats)
    h = 8.6
    r0, r1 = 2.49, 1.73
    rm = (r0 + r1) / 2
    shell(p, 0.0, h, r0, r1, BLUE, steps=3)
    flange(p, 0.06, r0, bolts=True)
    flange(p, h - 0.06, r1, bolts=True)
    # The rust band at the waist — the reference's one strong horizontal.
    zb = h * 0.46
    rb = r0 + (r1 - r0) * 0.46
    p.shade(p.loft([(zb - 0.34, octa(rb + 0.03)), (zb + 0.34, octa(rb + 0.03))],
                   axis='Z', mat=ORANGE), False)
    p.shade(p.loft([(zb + 0.34, octa(rb + 0.06)), (zb + 0.46, octa(rb + 0.06))],
                   axis='Z', mat=STEEL), False)
    pilasters(p, 0.2, h - 0.2, r0 - 0.02, r1 - 0.02, BLUE)
    pilasters(p, 0.2, h - 0.2, r0 - 0.02, r1 - 0.02, WHITE, faces=(1, 5),
              width=0.9, out=0.045)
    porthole(p, h * 0.72, r0 + (r1 - r0) * 0.72, 3)
    porthole(p, h * 0.3, r0 + (r1 - r0) * 0.3, 7, rad=0.2)
    conduit(p, 0.3, h - 0.3, r0, r1, 5)
    # A shallow vent bank low on one facet, backed dark.
    (vx, vy), va = facet(r0 + (r1 - r0) * 0.18, 1)
    rot = Matrix.Rotation(va, 4, 'Z')
    p.box((vx * 1.01, vy * 1.01, h * 0.18), (0.06, 1.1, 0.72), SLATE, rot=rot)
    for k in range(5):
        p.box((vx * 1.04, vy * 1.04, h * 0.18 - 0.28 + k * 0.14),
              (0.05, 1.0, 0.06), STEEL, rot=rot)
    # Stencilled bay number, low and off to one side.
    (nx, ny), na = facet(r0 + (r1 - r0) * 0.1, 0)
    p.box((nx * 1.01, ny * 1.01, h * 0.1), (0.02, 0.44, 0.46), RED,
          rot=Matrix.Rotation(na, 4, 'Z'))
    p.bevel(width=0.016)
    return p.finish("Mesh_StationTower_Taper", coll)


def tower_straight(mats, coll):
    """A parallel-sided 6 m section with a door at the foot — the walk-in one."""
    p = Part(mats)
    h, r = 6.0, 1.95
    shell(p, 0.0, h, r, r, BLUE, steps=2)
    flange(p, 0.06, r)
    flange(p, h - 0.06, r)
    pilasters(p, 0.2, h - 0.2, r - 0.02, r - 0.02, BLUE, faces=(1, 3, 5, 7))
    # Door bay: recess, frame, threshold. This is the section you enter at.
    (dx, dy), da = facet(r, 0)
    rot = Matrix.Rotation(da, 4, 'Z')
    p.box((dx * 0.94, dy * 0.94, 1.14), (0.24, 1.16, 2.24), BLACK, rot=rot)
    p.box((dx * 1.02, dy * 1.02, 1.14), (0.1, 1.34, 2.42), STEEL, rot=rot)
    p.box((dx * 1.06, dy * 1.06, 0.06), (0.34, 1.34, 0.12), DARK, rot=rot)
    p.box((dx * 1.05, dy * 1.05, 1.9), (0.06, 0.5, 0.26), GLASS, rot=rot)
    for k in range(2):
        porthole(p, 2.6 + k * 1.9, r, 2 + k * 3, rad=0.22)
    conduit(p, 0.3, h - 0.3, r, r, 6)
    p.shade(p.loft([(h * 0.52, octa(r + 0.04)), (h * 0.62, octa(r + 0.04))],
                   axis='Z', mat=WHITE), False)
    p.bevel(width=0.016)
    return p.finish("Mesh_StationTower_Straight", coll)


def tower_flare(mats, coll):
    """A 1.9 m skirt spreading the shaft out onto whatever carries it."""
    p = Part(mats)
    h = 1.9
    r0, r1 = 3.05, 2.49
    shell(p, 0.0, h, r0, r1, WHITE, steps=2)
    flange(p, h - 0.05, r1)
    # Base ring: wide, bolted, and sitting proud so it reads as a fixing.
    p.shade(p.loft([(0.0, octa(r0 + 0.34)), (0.22, octa(r0 + 0.34))],
                   axis='Z', mat=STEEL), False)
    p.shade(p.loft([(0.22, octa(r0 + 0.16)), (0.34, octa(r0 + 0.16))],
                   axis='Z', mat=DARK), False)
    for i in range(N):
        (fx, fy), a = facet(r0 + 0.34, i)
        for s in (-0.42, 0.0, 0.42):
            p.cyl((fx - math.sin(a) * s, fy + math.cos(a) * s, 0.24), 0.05, 0.1,
                  seg=6, mat=DARK)
    # Gussets up every facet — the load path made visible.
    for i in range(N):
        (fx, fy), a = facet(r0, i)
        p.box((fx * 1.03, fy * 1.03, h * 0.36), (0.5, 0.07, h * 0.66), STEEL,
              rot=Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(math.radians(15),
                                                               4, 'Y'))
    p.shade(p.loft([(h * 0.62, octa((r0 + r1) / 2 + 0.02)),
                    (h * 0.74, octa((r0 + r1) / 2 + 0.02))], axis='Z',
                   mat=ORANGE), False)
    p.bevel(width=0.016)
    return p.finish("Mesh_StationTower_Flare", coll)


def tower_collar(mats, coll):
    """A 1.1 m waist ring with a bracket platform — the splice that shows."""
    p = Part(mats)
    h, r = 1.1, 1.8
    shell(p, 0.0, h, r, r, SLATE, steps=1)
    p.shade(p.loft([(0.16, octa(r + 0.3)), (0.94, octa(r + 0.3))], axis='Z',
                   mat=WHITE), False)
    flange(p, 0.05, r, bolts=False)
    flange(p, h - 0.05, r, bolts=False)
    for i in range(N):
        (fx, fy), a = facet(r + 0.3, i)
        rot = Matrix.Rotation(a, 4, 'Z')
        p.box((fx * 1.02, fy * 1.02, h / 2), (0.08, 0.24, 0.86), STEEL, rot=rot)
        for z in (0.3, 0.8):
            p.cyl((fx * 1.05, fy * 1.05, z), 0.04, 0.08, axis='X', seg=6,
                  mat=DARK, rot=rot)
    # Outrigger bracket on one facet — somewhere for a lamp or an aerial.
    (bx, by), ba = facet(r + 0.3, 2)
    rot = Matrix.Rotation(ba, 4, 'Z')
    p.box((bx * 1.5, by * 1.5, h * 0.72), (1.1, 0.5, 0.08), STEEL, rot=rot)
    p.box((bx * 1.3, by * 1.3, h * 0.5), (0.7, 0.07, 0.5), STEEL,
          rot=rot @ Matrix.Rotation(math.radians(38), 4, 'Y'))
    p.shade(p.loft([(0.44, octa(r + 0.36)), (0.56, octa(r + 0.36))], axis='Z',
                   mat=ORANGE), False)
    p.bevel(width=0.014)
    return p.finish("Mesh_StationTower_Collar", coll)


def tower_braced(mats, coll):
    """5 m of open lattice. No cladding at all — the structural counterpart, and
    the only variation whose silhouette is mostly sky."""
    p = Part(mats)
    h, r0, r1 = 5.0, 2.2, 1.75
    for i in range(N // 2):                                  # four corner posts
        a = 2 * math.pi * i / (N // 2) + math.pi / 4
        x0, y0 = r0 * math.cos(a), r0 * math.sin(a)
        x1, y1 = r1 * math.cos(a), r1 * math.sin(a)
        batter = math.atan2(math.hypot(x0 - x1, y0 - y1), h)
        p.box(((x0 + x1) / 2, (y0 + y1) / 2, h / 2), (0.16, 0.16, h * 1.01),
              STEEL, rot=Matrix.Rotation(a, 4, 'Z')
              @ Matrix.Rotation(-batter, 4, 'Y'))
    lifts = 4
    for k in range(lifts):
        za, zb = h * k / lifts, h * (k + 1) / lifts
        ra = r0 + (r1 - r0) * k / lifts
        rb = r0 + (r1 - r0) * (k + 1) / lifts
        for i in range(N // 2):                              # ring beams
            a0 = 2 * math.pi * i / (N // 2) + math.pi / 4
            a1 = 2 * math.pi * (i + 1) / (N // 2) + math.pi / 4
            for z, rr in ((za, ra), (zb, rb)):
                mx, my = (rr * (math.cos(a0) + math.cos(a1)) / 2,
                          rr * (math.sin(a0) + math.sin(a1)) / 2)
                ln = rr * math.sqrt(2) * 1.02
                p.box((mx, my, z), (0.1, ln, 0.1), STEEL,
                      rot=Matrix.Rotation((a0 + a1) / 2, 4, 'Z'))
            # Diagonal brace across the bay, alternating hand each lift.
            s = 1 if (k + i) % 2 else -1
            ax, ay = ra * math.cos(a0 if s > 0 else a1), ra * math.sin(a0 if s > 0 else a1)
            bx, by = rb * math.cos(a1 if s > 0 else a0), rb * math.sin(a1 if s > 0 else a0)
            ln = math.sqrt((ax - bx) ** 2 + (ay - by) ** 2 + (zb - za) ** 2)
            yaw = math.atan2(by - ay, bx - ax)
            pitch = math.asin((zb - za) / ln)
            p.box(((ax + bx) / 2, (ay + by) / 2, (za + zb) / 2),
                  (ln, 0.08, 0.08), STEEL,
                  rot=Matrix.Rotation(yaw, 4, 'Z')
                  @ Matrix.Rotation(-pitch, 4, 'Y'))
    for z, rr in ((0.06, r0), (h - 0.06, r1)):               # splice plates
        for i in range(N // 2):
            a = 2 * math.pi * i / (N // 2) + math.pi / 4
            p.box((rr * math.cos(a), rr * math.sin(a), z), (0.32, 0.32, 0.12),
                  DARK)
    p.box((r0 * 0.9, 0, h * 0.5), (0.1, 0.7, h * 0.9), RUST,
          rot=Matrix.Rotation(math.radians(3), 4, 'Y'))       # a climbing rail
    p.bevel(width=0.012)
    return p.finish("Mesh_StationTower_Braced", coll)


def tower_stub(mats, coll):
    """Squat and heavily vented — a plant head, not a lookout. 3.2 m."""
    p = Part(mats)
    h, r0, r1 = 3.2, 2.3, 2.12
    shell(p, 0.0, h, r0, r1, BLUE, steps=2)
    flange(p, 0.06, r0)
    flange(p, h - 0.06, r1, over=0.24)
    for i in range(N):
        rr = r0 + (r1 - r0) * 0.5
        (fx, fy), a = facet(rr, i)
        rot = Matrix.Rotation(a, 4, 'Z')
        if i % 2 == 0:                                       # louvred bays
            p.box((fx * 1.0, fy * 1.0, h * 0.52), (0.07, 1.3, 1.5), SLATE,
                  rot=rot)
            for k in range(7):
                p.box((fx * 1.04, fy * 1.04, h * 0.52 - 0.6 + k * 0.2),
                      (0.06, 1.22, 0.09), STEEL, rot=rot)
        else:                                                # blank panel + hoop
            p.box((fx * 1.01, fy * 1.01, h * 0.52), (0.04, 1.2, 1.4), WHITE,
                  rot=rot)
            p.box((fx * 1.06, fy * 1.06, h * 0.3), (0.05, 0.9, 0.06), STEEL,
                  rot=rot)
    p.shade(p.loft([(h * 0.86, octa(r1 + 0.05)), (h * 0.94, octa(r1 + 0.05))],
                   axis='Z', mat=ORANGE), False)
    p.cyl((0, 0, h + 0.14), r1 * 0.42, 0.28, seg=12, mat=STEEL)   # extract stack
    p.cyl((0, 0, h + 0.3), r1 * 0.5, 0.08, seg=12, mat=DARK)
    p.bevel(width=0.014)
    return p.finish("Mesh_StationTower_Stub", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Taper", tower_taper), ("Straight", tower_straight),
                     ("Flare", tower_flare), ("Collar", tower_collar),
                     ("Braced", tower_braced), ("Stub", tower_stub)):
        fn(mats, collection("Coll_StationTower_%s" % name))
    report()
    save(out)


main()
