"""components/structural/sensor_cupola — the head that goes on top of a mast.

A tower without a head is a chimney. This is the part that says what the tower
is *for*, so the five variations are deliberately five different answers:
a crewed domed drum, a glazed lookout, a sealed radome, a machine drum that
nobody goes inside, and an open dish yoke that is mostly air.

The gallery and its railing are integral to `Dome` and `Lantern` rather than
assembled from `structural/handrail`. Handrail's straight run is 2.24 m, which
does not divide an octagonal gallery 5.4 m across without either overlapping or
leaving gaps, and a ring rail that follows the octagon is cheaper than eight
mitred segments would be.

Origin at the base of the drum — the splice plane a `station_tower` section
presents. Diameters match `station_tower`'s top flanges.

    blender --background --python sensor_cupola.py -- --out sensor_cupola.blend

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
    "Mat_Paint_Blue_Station",    # 0  the drum skin
    "Mat_Paint_White_Arctic",    # 1  the cap, and brighter panels
    "Mat_Metal_Steel_Worn",      # 2  gallery, rails, brackets
    "Mat_Metal_Steel_Dark",      # 3  fittings, bolts, mullions
    "Mat_Glass_Canopy_Tinted",   # 4  ports and lantern glazing
    "Mat_Metal_Rust_Heavy",      # 5  streaks below fittings
    "Mat_Metal_HullRust_Orange", # 6  the banded accent
    "Mat_Neutral_Slate_Dark",    # 7  recesses, vent backing
    "Mat_Neutral_Black_Matte",   # 8  shadow gaps
    "Mat_Emissive_Cabin_Warm",   # 9  lit interior behind the glass
    "Mat_Emissive_Amber",        # 10 the obstruction lamp
    "Mat_Paint_Warn_Red",        # 11 hazard banding
]
(BLUE, WHITE, STEEL, DARK, GLASS, RUST, ORANGE, SLATE, BLACK, WARM, AMBER,
 RED) = range(12)

SEG = 20          # drum resolution — round, not faceted, unlike the tower


def ring(r, n=SEG, phase=0.0):
    return [(r * math.cos(2 * math.pi * i / n + phase),
             r * math.sin(2 * math.pi * i / n + phase)) for i in range(n)]


def dome_sections(z0, r, height, steps=5, r_top=0.16):
    """An elliptical cap: rings following a quarter ellipse from `r` to a small
    crown radius, so the top closes without a pole of degenerate triangles."""
    secs = []
    for k in range(steps + 1):
        t = k / float(steps)
        rr = max(r * math.cos(t * math.pi / 2), r_top)
        secs.append((z0 + height * math.sin(t * math.pi / 2), ring(rr)))
    return secs


def gallery(p, z, r_in, r_out, posts=10, rail_h=1.06, deck=STEEL, rail=STEEL):
    """A walkable ring with a two-bar pipe rail and toe board.

    Built as a ring rather than from `handrail` segments — see the module note.
    """
    p.shade(p.loft([(z, ring(r_out)), (z + 0.07, ring(r_out))], axis='Z',
                   mat=deck), True)
    p.shade(p.loft([(z + 0.07, ring(r_in)), (z + 0.07, ring(r_out))], axis='Z',
                   mat=deck, cap=False), True)
    p.shade(p.loft([(z + 0.07, ring(r_out)), (z + 0.19, ring(r_out))],
                   axis='Z', mat=DARK), True)                 # toe board
    for k in (0.62, 1.0):                                     # two rails
        p.torus((0, 0, z + rail_h * k), r_out - 0.05, 0.033, maj_seg=SEG,
                min_seg=6, mat=rail)
    for i in range(posts):
        a = 2 * math.pi * i / posts
        p.cyl(((r_out - 0.05) * math.cos(a), (r_out - 0.05) * math.sin(a),
               z + rail_h / 2 + 0.06), 0.028, rail_h, seg=6, mat=rail)
    for i in range(posts // 2):                               # under-brackets
        a = 2 * math.pi * i / (posts // 2) + math.pi / 8
        p.box(((r_in + r_out) / 2 * math.cos(a), (r_in + r_out) / 2 * math.sin(a),
               z - 0.16), (r_out - r_in, 0.08, 0.4), STEEL,
              rot=Matrix.Rotation(a, 4, 'Z')
              @ Matrix.Rotation(math.radians(-26), 4, 'Y'))


def port(p, z, r, ang, rad=0.24, lit=True):
    x, y = r * math.cos(ang), r * math.sin(ang)
    rot = Matrix.Rotation(ang, 4, 'Z')
    p.cyl((x * 1.0, y * 1.0, z), rad * 1.35, 0.12, axis='X', seg=12, mat=STEEL,
          rot=rot)
    p.cyl((x * 1.03, y * 1.03, z), rad, 0.07, axis='X', seg=12,
          mat=WARM if lit else GLASS, rot=rot)
    p.box((x * 0.99, y * 0.99, z - rad * 2.4), (0.02, rad * 1.1, rad * 2.8),
          RUST, rot=rot)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def cupola_dome(mats, coll):
    """The crewed head: a 4.4 m drum, domed cap, gallery all round.

    This is the reference silhouette — the drum is deliberately wider than the
    shaft it lands on, so it overhangs and throws a shadow line that reads from
    a long way off.
    """
    p = Part(mats)
    r, hd = 2.2, 2.85
    p.shade(p.loft([(0.0, ring(r * 0.86)), (0.34, ring(r)), (hd, ring(r)),
                    (hd + 0.16, ring(r * 0.97))], axis='Z', mat=BLUE), True)
    gallery(p, 0.3, r, r + 0.85)
    # Cap: dome, plus a raised crown boss that the aerials stand on.
    p.shade(p.loft(dome_sections(hd + 0.16, r * 0.97, 0.98), axis='Z',
                   mat=WHITE), True)
    p.torus((0, 0, hd + 0.2), r * 0.98, 0.07, maj_seg=SEG, min_seg=6, mat=STEEL)
    for i in range(8):                                        # cap ribs
        a = 2 * math.pi * i / 8
        p.box((r * 0.5 * math.cos(a), r * 0.5 * math.sin(a), hd + 0.62),
              (r * 0.98, 0.06, 0.06), STEEL,
              rot=Matrix.Rotation(a, 4, 'Z')
              @ Matrix.Rotation(math.radians(28), 4, 'Y'))
    p.cyl((0, 0, hd + 1.1), 0.3, 0.26, seg=12, mat=STEEL)
    p.cyl((0, 0, hd + 1.26), 0.34, 0.07, seg=12, mat=DARK)
    p.cyl((0, 0, hd + 1.4), 0.09, 0.24, seg=8, mat=AMBER)     # obstruction lamp
    # Ports round the drum, and a door onto the gallery.
    for i, a in enumerate((0.0, 1.15, 2.4, 3.6, 4.9)):
        port(p, hd * 0.62, r, a, rad=0.22 if i % 2 else 0.27, lit=i != 3)
    da = math.pi * 0.75
    dx, dy = r * math.cos(da), r * math.sin(da)
    rot = Matrix.Rotation(da, 4, 'Z')
    p.box((dx * 0.96, dy * 0.96, 1.36), (0.2, 0.92, 2.02), BLACK, rot=rot)
    p.box((dx * 1.02, dy * 1.02, 1.36), (0.08, 1.06, 2.16), STEEL, rot=rot)
    p.box((dx * 1.05, dy * 1.05, 2.06), (0.05, 0.44, 0.3), GLASS, rot=rot)
    # Banding and a bracket boss for a lamp arm, hung off the gallery side.
    p.shade(p.loft([(hd * 0.24, ring(r + 0.03)), (hd * 0.34, ring(r + 0.03))],
                   axis='Z', mat=ORANGE), True)
    ba = math.pi * 1.35
    p.box(((r + 0.5) * math.cos(ba), (r + 0.5) * math.sin(ba), 0.62),
          (0.44, 0.16, 0.16), STEEL, rot=Matrix.Rotation(ba, 4, 'Z'))
    p.bevel(width=0.014)
    return p.finish("Mesh_SensorCupola_Dome", coll)


def cupola_lantern(mats, coll):
    """Glazed all round on mullions — a lookout. Mostly glass, so it reads light."""
    p = Part(mats)
    r, hb, hg = 2.0, 0.72, 2.16
    n = 10
    p.shade(p.loft([(0.0, ring(r * 0.88)), (0.22, ring(r)), (hb, ring(r))],
                   axis='Z', mat=BLUE), True)                 # solid plinth
    gallery(p, 0.2, r, r + 0.78, posts=n)
    p.shade(p.loft([(hb, ring(r * 0.97)), (hb + hg, ring(r * 0.97))], axis='Z',
                   mat=GLASS, cap=False), True)               # the glazing
    for i in range(n):                                        # mullions
        a = 2 * math.pi * i / n
        p.box((r * 0.99 * math.cos(a), r * 0.99 * math.sin(a), hb + hg / 2),
              (0.12, 0.11, hg), DARK, rot=Matrix.Rotation(a, 4, 'Z'))
    p.torus((0, 0, hb + hg * 0.52), r * 0.99, 0.045, maj_seg=SEG, min_seg=6,
            mat=DARK)                                         # transom
    # Flat cap on a shallow cone, with an eaves overhang and a vent turret.
    p.shade(p.loft([(hb + hg, ring(r * 1.14)), (hb + hg + 0.16, ring(r * 1.14)),
                    (hb + hg + 0.66, ring(r * 0.62))], axis='Z', mat=WHITE),
            True)
    p.torus((0, 0, hb + hg + 0.08), r * 1.14, 0.05, maj_seg=SEG, min_seg=6,
            mat=STEEL)
    p.cyl((0, 0, hb + hg + 0.82), r * 0.4, 0.34, seg=14, mat=WHITE)
    for i in range(6):
        p.box((0, 0, hb + hg + 0.82), (r * 0.84, 0.05, 0.22), SLATE,
              rot=Matrix.Rotation(math.pi * i / 6, 4, 'Z'))
    p.cyl((0, 0, hb + hg + 1.04), r * 0.46, 0.1, seg=14, mat=DARK)
    p.cyl((0, 0, hb + hg + 1.22), 0.08, 0.28, seg=8, mat=AMBER)
    p.cyl((0, 0, hb + hg * 0.5), 0.34, hg * 0.9, seg=12, mat=WARM)  # inner lamp
    p.shade(p.loft([(hb * 0.3, ring(r + 0.03)), (hb * 0.62, ring(r + 0.03))],
                   axis='Z', mat=ORANGE), True)
    p.bevel(width=0.012)
    return p.finish("Mesh_SensorCupola_Lantern", coll)


def cupola_radome(mats, coll):
    """A sealed sphere on a short plinth. No gallery, no ports, no way in."""
    p = Part(mats)
    r, hp = 1.9, 0.86
    p.shade(p.loft([(0.0, ring(r * 0.72)), (0.24, ring(r * 0.8)),
                    (hp, ring(r * 0.8))], axis='Z', mat=SLATE), True)
    for i in range(12):                                       # plinth ribs
        a = 2 * math.pi * i / 12
        p.box((r * 0.8 * math.cos(a), r * 0.8 * math.sin(a), hp * 0.6),
              (0.07, 0.14, hp * 0.8), STEEL, rot=Matrix.Rotation(a, 4, 'Z'))
    p.torus((0, 0, hp), r * 0.82, 0.09, maj_seg=SEG, min_seg=8, mat=STEEL)
    # The dome itself, a touch more than a hemisphere so it bulges at the base.
    p.shade(p.loft([(hp, ring(r * 0.88)), (hp + 0.28, ring(r))]
                   + dome_sections(hp + 0.28, r, r * 1.02)[1:], axis='Z',
                   mat=WHITE), True)
    for i in range(4):                                        # seam tapes
        a = math.pi * i / 4
        for k in range(7):
            t = (k + 0.5) / 7
            rr = r * math.cos(t * math.pi / 2 * 0.98)
            z = hp + 0.28 + r * 1.02 * math.sin(t * math.pi / 2 * 0.98)
            p.box((rr * math.cos(a), rr * math.sin(a), z), (0.05, 0.05, 0.05),
                  WHITE)
            p.box((-rr * math.cos(a), -rr * math.sin(a), z), (0.05, 0.05, 0.05),
                  WHITE)
    p.torus((0, 0, hp + 0.3), r, 0.05, maj_seg=SEG, min_seg=6, mat=DARK)
    p.cyl((0, 0, hp + r * 1.3 + 0.28), 0.12, 0.2, seg=8, mat=DARK)
    p.cyl((0, 0, hp + r * 1.3 + 0.44), 0.07, 0.2, seg=8, mat=AMBER)
    p.box((r * 0.84, 0, hp * 0.5), (0.16, 0.6, 0.5), DARK)    # service box
    p.box((r * 0.9, 0, hp * 0.16), (0.06, 0.44, 0.5), RUST)
    p.bevel(width=0.012)
    return p.finish("Mesh_SensorCupola_Radome", coll)


def cupola_drum(mats, coll):
    """Flat-topped plant: louvre bands and an extract fan. Nobody goes in."""
    p = Part(mats)
    r, h = 2.1, 2.3
    p.shade(p.loft([(0.0, ring(r * 0.9)), (0.2, ring(r)), (h, ring(r)),
                    (h + 0.14, ring(r * 1.06))], axis='Z', mat=SLATE), True)
    for band, z in ((0, 0.62), (1, 1.34), (2, 1.94)):         # louvre bands
        p.shade(p.loft([(z - 0.24, ring(r * 0.96)), (z + 0.24, ring(r * 0.96))],
                       axis='Z', mat=BLACK, cap=False), True)
        for k in range(4):
            p.torus((0, 0, z - 0.18 + k * 0.12), r * 0.99, 0.028, maj_seg=SEG,
                    min_seg=5, mat=STEEL)
    for i in range(8):                                        # corner stiffeners
        a = 2 * math.pi * i / 8 + math.pi / 8
        p.box((r * math.cos(a), r * math.sin(a), h / 2), (0.09, 0.2, h),
              STEEL, rot=Matrix.Rotation(a, 4, 'Z'))
    p.shade(p.loft([(h + 0.14, ring(r * 1.06)), (h + 0.26, ring(r * 1.02))],
                   axis='Z', mat=STEEL), True)                # cap plate
    # Extract fan, cowled, sitting proud of the flat top.
    p.cyl((0, 0, h + 0.48), r * 0.52, 0.44, seg=16, mat=STEEL)
    p.cyl((0, 0, h + 0.74), r * 0.6, 0.1, seg=16, mat=DARK)
    for i in range(6):
        p.box((0, 0, h + 0.72), (r * 0.9, 0.16, 0.04), DARK,
              rot=Matrix.Rotation(math.pi * i / 6, 4, 'Z')
              @ Matrix.Rotation(math.radians(22), 4, 'Y'))
    p.cyl((0, 0, h + 0.8), 0.1, 0.14, seg=8, mat=DARK)
    p.box((r * 0.7, r * 0.7, h * 0.4), (0.5, 0.5, 0.7), SLATE,
          rot=Matrix.Rotation(math.radians(45), 4, 'Z'))      # control box
    p.cyl((r * 0.99, 0, h * 0.72), 0.09, 0.5, axis='X', seg=8, mat=RUST)
    p.shade(p.loft([(0.24, ring(r + 0.03)), (0.4, ring(r + 0.03))], axis='Z',
                   mat=RED), True)
    p.bevel(width=0.012)
    return p.finish("Mesh_SensorCupola_Drum", coll)


def cupola_dish(mats, coll):
    """An open yoke carrying a parabolic dish. Almost no enclosed volume — the
    variation that lets a tower read as a relay rather than a residence."""
    p = Part(mats)
    hp, rd, dep = 0.7, 1.55, 0.42
    z_bear = hp + 1.72                       # elevation bearing height

    # The dish is built first and alone, about its own vertex, then the whole
    # bmesh is tipped onto the yoke. `loft` takes no rotation argument, so a
    # paraboloid cannot be built pre-tilted — and tilting the bmesh only works
    # while the dish is the only thing in it. Everything static is added after.
    secs = []
    for k in range(6):
        t = k / 5.0
        secs.append((-dep * (1 - t) ** 2, ring(rd * t if t else 0.05)))
    p.shade(p.loft(secs, axis='Z', mat=WHITE), True)
    p.torus((0, 0, 0.0), rd, 0.05, maj_seg=SEG, min_seg=6, mat=STEEL)
    for i in range(4):
        a = 2 * math.pi * i / 4 + math.pi / 4
        p.box((rd * 0.5 * math.cos(a), rd * 0.5 * math.sin(a), -dep * 0.4),
              (rd, 0.06, 0.06), STEEL, rot=Matrix.Rotation(a, 4, 'Z'))
        p.box((rd * 0.42 * math.cos(a), rd * 0.42 * math.sin(a), 0.5),
              (0.05, 0.05, 1.1), STEEL,
              rot=Matrix.Rotation(a, 4, 'Z')
              @ Matrix.Rotation(math.radians(36), 4, 'Y'))    # feed struts
    p.cyl((0, 0, 0.94), 0.16, 0.36, seg=10, mat=DARK)         # feed horn
    p.cyl((0, 0, 1.16), 0.1, 0.14, seg=8, mat=STEEL)
    p.cyl((0, 0, -dep - 0.12), 0.34, 0.3, seg=12, mat=DARK)   # hub can
    p.bm.transform(Matrix.Translation((0, 0, z_bear))
                   @ Matrix.Rotation(math.radians(-34), 4, 'Y'))

    # Static half: turret base and the yoke the dish now sits in.
    p.cyl((0, 0, hp / 2), 1.05, hp, seg=14, mat=SLATE)
    p.cyl((0, 0, hp + 0.06), 1.16, 0.14, seg=14, mat=STEEL)
    for i in range(8):
        a = 2 * math.pi * i / 8
        p.box((1.05 * math.cos(a), 1.05 * math.sin(a), hp * 0.5),
              (0.08, 0.16, hp), STEEL, rot=Matrix.Rotation(a, 4, 'Z'))
    for sy in (-1, 1):                                        # yoke arms
        p.box((0, sy * 0.86, hp + (z_bear - hp) / 2), (0.16, 0.16, z_bear - hp),
              STEEL)
        p.cyl((0, sy * 0.8, z_bear), 0.19, 0.2, axis='Y', seg=12, mat=DARK)
        p.box((0, sy * 0.86, hp + 0.34), (0.5, 0.12, 0.12), STEEL,
              rot=Matrix.Rotation(math.radians(-38), 4, 'Y'))  # knee brace
    p.box((0.9, 0, hp * 0.5), (0.4, 0.6, 0.6), SLATE)         # equipment box
    p.cyl((0, 0, hp + 0.3), 0.14, 0.4, seg=8, mat=RUST)       # cable riser
    p.bevel(width=0.012)
    return p.finish("Mesh_SensorCupola_Dish", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Dome", cupola_dome), ("Lantern", cupola_lantern),
                     ("Radome", cupola_radome), ("Drum", cupola_drum),
                     ("Dish", cupola_dish)):
        fn(mats, collection("Coll_SensorCupola_%s" % name))
    report()
    save(out)


main()
