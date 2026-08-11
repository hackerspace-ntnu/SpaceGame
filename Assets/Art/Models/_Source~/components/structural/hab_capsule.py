"""components/structural/hab_capsule — pressure-vessel modules bolted to a wall.

`cabin_module` is the other module family in this library and it is a converted
shipping container: flat sides, corner castings, corrugation. This one is the
opposite lineage — a rolled pressure hull with domed ends, the thing that got
flown or dragged in whole and then bolted onto a structure that was built around
it. On a refinery the two sit side by side, and the contrast between the boxy
one and the round one is most of what makes the pile read as accreted over time
rather than designed at once.

Authored running along **-Y** from a mount plane at y = 0, origin on the axis at
that plane, because -Y is the library's forward and these are almost always
cantilevered off the front of something. Mounting one is a translation.

The hull is lofted in three pieces with a genuine recessed band between them,
rather than a dark stripe painted on a single loft. The step where the diameter
drops is what makes glazing look let into a hull instead of stuck onto it, and
it costs one extra ring of faces.

    blender --background --python hab_capsule.py -- --out hab_capsule.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

from mathutils import Matrix  # noqa: E402

MATS = [
    "Mat_Paint_White_Arctic",    # 0 the hull
    "Mat_Paint_Safety_Orange",   # 1 nose caps, bands, hazard ends
    "Mat_Metal_Steel_Dark",      # 2 saddles, collars, fittings
    "Mat_Metal_Steel_Worn",      # 3 bare steel: rails, brackets, manifolds
    "Mat_Glass_Canopy_Tinted",   # 4 glazing
    "Mat_Neutral_Black_Matte",   # 5 the recessed window band behind the glass
    "Mat_Neutral_Slate_Dark",    # 6 contrast panels and roof plant
    "Mat_Emissive_Amber",        # 7 marker lamps
    "Mat_Paint_Warn_Red",        # 8 stencils
    "Mat_Metal_Rust_Heavy",      # 9 streaking below the fittings
]
WHITE, ORANGE, DARK, STEEL, GLASS, BLACK, SLATE, AMBER, RED, RUST = range(10)


# ---------------------------------------------------------------------------
# Hull profile
# ---------------------------------------------------------------------------

def rrect(hw, hh, r, cseg=3):
    """A rounded-rectangle cross-section, counter-clockwise, 4*(cseg+1) points.

    Not a circle: the flats are what let flat glazing, flat plating and flat
    saddles sit against the hull without a gap, and a rolled tank of this size
    genuinely is a box with big radii rather than a cylinder.
    """
    r = min(r, hw * 0.98, hh * 0.98)
    pts = []
    for cx, cy, a0 in ((hw - r, -(hh - r), -90.0), (hw - r, hh - r, 0.0),
                       (-(hw - r), hh - r, 90.0), (-(hw - r), -(hh - r), 180.0)):
        for i in range(cseg + 1):
            a = math.radians(a0 + 90.0 * i / cseg)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def hull(p, stations, mat=WHITE, scale=1.0, cap=True):
    """Loft a run of (y, half_width, half_height, corner_radius) stations."""
    return p.loft([(y, rrect(hw * scale, hh * scale, r * scale))
                   for y, hw, hh, r in stations], axis='Y', mat=mat, cap=cap)


def saddle(p, y, hw, hh, drop=1.5, mat=DARK):
    """The cradle a capsule sits in. Without it the module floats."""
    p.box((0, y, -hh - drop / 2 + 0.25), (hw * 2.05, 1.1, drop), mat)
    for sx in (-1, 1):
        p.box((sx * (hw + 0.18), y, -hh * 0.35), (0.5, 0.9, hh * 1.3), mat,
              rot=Matrix.Rotation(math.radians(-sx * 14), 4, 'Y'))


def ribs(p, y0, y1, hw, hh, count, mat=DARK, proud=0.14):
    """Reinforcing hoops. The scale cue on an otherwise featureless hull."""
    for i in range(count):
        y = y0 + (y1 - y0) * (i + 0.5) / count
        hull(p, [(y - 0.13, hw, hh, min(hw, hh) * 0.55),
                 (y + 0.13, hw, hh, min(hw, hh) * 0.55)],
             mat=mat, scale=1.0 + proud / max(hw, hh), cap=False)


def mount_ring(p, hw, hh, mat=DARK):
    """The bolted flange at the mount plane, where it meets the wall."""
    hull(p, [(0.42, hw, hh, min(hw, hh) * 0.5), (0.0, hw, hh, min(hw, hh) * 0.5)],
         mat=mat, scale=1.16, cap=True)
    for i in range(12):
        a = 2 * math.pi * i / 12
        p.cyl((math.cos(a) * hw * 1.08, 0.30, math.sin(a) * hh * 1.08),
              0.11, 0.34, 'Y', seg=6, mat=STEEL)


def panes(p, y0, y1, hw, hh, count, mat=GLASS, frame=DARK):
    """Flat glazing let into the recessed band on both flanks."""
    for sx in (-1, 1):
        for i in range(count):
            t0 = i / count
            t1 = (i + 1) / count
            yc = y0 + (y1 - y0) * (t0 + t1) / 2
            ln = abs(y1 - y0) / count
            p.box((sx * hw, yc, 0.0), (0.14, ln * 0.82, hh * 1.15), mat)
            p.box((sx * (hw - 0.03), y0 + (y1 - y0) * t1, 0.0),
                  (0.22, ln * 0.16, hh * 1.3), frame)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def long_capsule(coll, mats):
    """16 m crew module: recessed glazing band, orange nose, roof walkway.

    The hero of the family and the one the tower actually cantilevers. Long
    enough that it needs the walkway on top to be believable, because nobody
    builds a 16 m module with only one way into it.
    """
    hw, hh = 2.60, 2.40
    r = 1.05
    p = Part(mats)
    # Rear hull, recessed band, front hull — three lofts, two real steps.
    hull(p, [(0.0, 2.40, 2.20, r), (-0.5, hw, hh, r), (-3.2, hw, hh, r)])
    hull(p, [(-3.2, hw, hh, r), (-11.0, hw, hh, r)], mat=BLACK, scale=0.90)
    hull(p, [(-11.0, hw, hh, r), (-13.6, hw, hh, r), (-15.0, 2.30, 2.15, r),
             (-15.9, 1.55, 1.45, 0.75), (-16.3, 0.72, 0.68, 0.34)])
    # Orange nose cap over the front two stations, slightly proud.
    hull(p, [(-14.4, 2.42, 2.26, r), (-15.0, 2.30, 2.15, r),
             (-15.9, 1.55, 1.45, 0.75), (-16.35, 0.72, 0.68, 0.34)],
         mat=ORANGE, scale=1.03)
    panes(p, -10.7, -3.5, hw * 0.90, hh * 0.66, 6)
    # Forward screen — the module looks where it is pointing.
    p.box((0, -13.75, 0.35), (3.5, 0.35, 2.0), BLACK)
    p.box((0, -13.92, 0.35), (3.1, 0.16, 1.7), GLASS)
    ribs(p, -13.4, -11.2, hw, hh, 2)
    ribs(p, -3.0, -0.6, hw, hh, 2)
    mount_ring(p, 2.40, 2.20)
    saddle(p, -1.9, hw, hh)
    saddle(p, -12.4, hw, hh)
    # Roof walkway with a light rail, and the plant that justifies it.
    p.box((0, -8.0, hh + 0.12), (2.0, 15.0, 0.16), STEEL)
    for sx in (-1, 1):
        for i in range(7):
            p.box((sx * 0.95, -1.6 - i * 2.2, hh + 0.62), (0.08, 0.08, 0.92),
                  STEEL)
        p.box((sx * 0.95, -8.0, hh + 1.06), (0.06, 14.6, 0.06), STEEL)
    p.box((0.0, -5.4, hh + 0.75), (1.5, 2.2, 1.1), SLATE)
    for i in range(3):
        p.cyl((-0.6 + i * 0.6, -10.4, hh + 0.85), 0.22, 1.3, 'Z', seg=8,
              mat=STEEL)
    # Underslung service run and marker lamps.
    for sx in (-1, 1):
        p.cyl((sx * 1.5, -8.0, -hh - 0.20), 0.20, 12.0, 'Y', seg=8, mat=STEEL)
        p.cyl((sx * (hw + 0.12), -14.2, 0.9), 0.20, 0.30, 'X', seg=8, mat=AMBER)
    p.box((0, -13.0, -hh - 0.25), (2.2, 1.6, 0.5), RUST)
    p.box((hw + 0.05, -6.0, -1.3), (0.06, 2.4, 1.0), RED)
    p.bevel(width=0.04, segments=1)
    return p.finish("Mesh_HabCapsule_Long", coll)


def short_capsule(coll, mats):
    """9 m module with a docking collar instead of a nose — reads as connectable.

    Distinct in silhouette from `Long`, not just shorter: the blunt collared end
    is a different shape event, so two of these next to a Long do not look like
    one asset scaled.
    """
    hw, hh, r = 2.45, 2.30, 0.95
    p = Part(mats)
    hull(p, [(0.0, 2.30, 2.15, r), (-0.5, hw, hh, r), (-2.6, hw, hh, r)])
    hull(p, [(-2.6, hw, hh, r), (-6.6, hw, hh, r)], mat=BLACK, scale=0.90)
    hull(p, [(-6.6, hw, hh, r), (-8.4, hw, hh, r), (-8.9, 2.10, 2.00, 0.85)])
    # Docking collar: a ring, a face plate, and a hatch you could believe opens.
    p.cyl((0, -9.25, 0), 1.85, 0.7, 'Y', seg=14, mat=ORANGE)
    p.cyl((0, -9.65, 0), 1.55, 0.3, 'Y', seg=14, mat=DARK)
    p.cyl((0, -9.72, 0), 1.15, 0.22, 'Y', seg=14, mat=STEEL)
    for i in range(8):
        a = 2 * math.pi * i / 8
        p.cyl((math.cos(a) * 1.62, -9.55, math.sin(a) * 1.62), 0.13, 0.42,
              'Y', seg=6, mat=DARK)
    panes(p, -6.3, -2.9, hw * 0.90, hh * 0.60, 3)
    ribs(p, -8.3, -6.8, hw, hh, 2)
    ribs(p, -2.4, -0.6, hw, hh, 2)
    mount_ring(p, 2.30, 2.15)
    saddle(p, -1.7, hw, hh)
    saddle(p, -7.4, hw, hh)
    p.box((0, -4.6, hh + 0.55), (1.6, 2.4, 1.0), SLATE)
    p.cyl((0, -4.6, hh + 1.35), 0.34, 0.9, 'Z', seg=8, mat=STEEL)
    p.box((hw + 0.05, -5.0, 1.2), (0.06, 1.8, 0.9), RED)
    p.bevel(width=0.04, segments=1)
    return p.finish("Mesh_HabCapsule_Short", coll)


def tank(coll, mats):
    """A 12 m storage vessel: no glazing, heavy hoops, a valve manifold on top.

    The industrial sibling. Refineries are mostly tanks, and having one that
    shares the capsule silhouette lets a scene stack hab and process modules in
    the same visual family.
    """
    hw, hh, r = 2.55, 2.55, 2.40      # nearly circular — it holds pressure
    p = Part(mats)
    hull(p, [(0.4, 1.9, 1.9, 1.8), (0.0, 2.35, 2.35, 2.2),
             (-0.9, hw, hh, r), (-11.1, hw, hh, r),
             (-12.0, 2.35, 2.35, 2.2), (-12.4, 1.9, 1.9, 1.8)])
    ribs(p, -11.0, -1.0, hw, hh, 7, proud=0.20)
    hull(p, [(-6.6, hw, hh, r), (-5.4, hw, hh, r)], mat=ORANGE, scale=1.02,
         cap=False)
    mount_ring(p, 1.9, 1.9)
    saddle(p, -2.4, hw, hh, drop=1.3)
    saddle(p, -9.6, hw, hh, drop=1.3)
    # Manifold: header, branch valves, relief stack. The reason it reads as
    # plant rather than a dumb cylinder.
    p.cyl((0, -6.2, hh + 0.55), 0.34, 9.6, 'Y', seg=8, mat=STEEL)
    for i in range(4):
        y = -2.6 - i * 2.4
        p.cyl((0, y, hh + 0.95), 0.20, 0.9, 'Z', seg=6, mat=STEEL)
        p.cyl((0, y, hh + 1.42), 0.34, 0.22, 'Z', seg=8, mat=RED)
        p.box((0, y, hh + 0.30), (0.7, 0.7, 0.5), DARK)
    p.cyl((0.9, -10.6, hh + 1.6), 0.26, 2.4, 'Z', seg=8, mat=STEEL)
    p.box((-1.2, -1.9, hh + 0.45), (1.3, 1.6, 0.8), SLATE)
    for i in range(8):                                 # inspection ladder
        p.box((hw * 0.72, -1.6 - i * 1.2, -hh * 0.1 + i * 0.0), (0.9, 0.09, 0.09),
              STEEL)
    p.box((hw + 0.05, -4.0, -1.4), (0.06, 3.0, 1.1), RED)
    p.box((0, -11.6, -hh - 0.3), (2.0, 1.4, 0.5), RUST)
    p.bevel(width=0.04, segments=1)
    return p.finish("Mesh_HabCapsule_Tank", coll)


def cab(coll, mats):
    """A 7 m operator cab with a projecting glazed bay — somebody is watching.

    The one variation with a face. A structure this size needs exactly one
    place the eye decides is 'the control room', and a raked wraparound screen
    on a short pod does that far better than another window band.
    """
    hw, hh, r = 2.55, 2.30, 0.90
    p = Part(mats)
    hull(p, [(0.0, 2.35, 2.15, r), (-0.5, hw, hh, r), (-4.4, hw, hh, r),
             (-5.2, 2.45, 2.20, r)])
    # The bay: a raked, faceted screen box hung off the front.
    p.loft([(-5.2, [(-2.45, -2.20), (2.45, -2.20), (2.45, 2.20),
                    (-2.45, 2.20)]),
            (-6.9, [(-2.75, -2.55), (2.75, -2.55), (2.30, 1.75),
                    (-2.30, 1.75)])], axis='Y', mat=DARK)
    p.loft([(-5.35, [(-2.30, -2.02), (2.30, -2.02), (2.30, 2.02),
                     (-2.30, 2.02)]),
            (-6.98, [(-2.60, -2.36), (2.60, -2.36), (2.15, 1.58),
                     (-2.15, 1.58)])], axis='Y', mat=GLASS, cap=False)
    # Mullions, or it is a black hole with a frame round it.
    for x in (-1.3, 0.0, 1.3):
        p.box((x, -6.1, -0.25), (0.14, 1.9, 4.5), DARK,
              rot=Matrix.Rotation(math.radians(-11), 4, 'X'))
    p.box((0, -7.05, 1.75), (5.6, 1.5, 0.34), ORANGE)       # brow / sunshade
    p.box((0, -7.15, -2.55), (5.6, 1.4, 0.40), DARK)        # sill
    for sx in (-1, 1):
        p.cyl((sx * 2.3, -7.0, 2.05), 0.22, 0.34, 'Z', seg=8, mat=AMBER)
    ribs(p, -4.2, -0.7, hw, hh, 3)
    mount_ring(p, 2.35, 2.15)
    saddle(p, -1.6, hw, hh)
    saddle(p, -4.6, hw, hh)
    p.box((0, -2.6, hh + 0.5), (1.4, 2.0, 0.9), SLATE)
    p.cyl((0.7, -1.4, hh + 1.2), 0.14, 2.2, 'Z', seg=6, mat=STEEL)
    p.box((-0.7, -1.4, hh + 0.95), (0.9, 0.9, 0.8), STEEL)  # comms dome base
    p.bevel(width=0.04, segments=1)
    return p.finish("Mesh_HabCapsule_Cab", coll)


def pod(coll, mats):
    """A 5 m single-occupant pod, hung rather than saddled.

    Small enough to hang off a catwalk or clip to a leg, which is what the
    top-mounted lifting lugs are for. The cheap one to scatter — 1.5 k
    triangles — so a scene can carry six without thinking about it.
    """
    hw, hh, r = 1.75, 1.65, 0.80
    p = Part(mats)
    hull(p, [(0.3, 1.35, 1.30, 0.6), (0.0, hw, hh, r), (-3.8, hw, hh, r),
             (-4.6, 1.60, 1.52, 0.7), (-5.0, 1.05, 1.00, 0.45)])
    hull(p, [(-4.55, 1.62, 1.54, 0.7), (-5.05, 1.05, 1.00, 0.45)],
         mat=ORANGE, scale=1.04)
    p.box((0, -4.05, 0.15), (2.1, 0.30, 1.5), BLACK)
    p.box((0, -4.18, 0.15), (1.8, 0.14, 1.25), GLASS)
    ribs(p, -3.6, -0.5, hw, hh, 3)
    for sx in (-1, 1):                                  # lifting lugs
        p.box((sx * 0.75, -1.2, hh + 0.18), (0.4, 0.5, 0.5), DARK)
        p.torus((sx * 0.75, -1.2, hh + 0.48), 0.24, 0.07, 'X', maj_seg=10,
                min_seg=6, mat=STEEL)
        p.box((sx * 0.75, -3.0, hh + 0.18), (0.4, 0.5, 0.5), DARK)
        p.torus((sx * 0.75, -3.0, hh + 0.48), 0.24, 0.07, 'X', maj_seg=10,
                min_seg=6, mat=STEEL)
    p.box((0, -2.2, -hh - 0.22), (1.5, 2.4, 0.35), STEEL)
    p.cyl((0, -4.35, -1.0), 0.18, 0.26, 'Y', seg=8, mat=AMBER)
    p.box((hw + 0.04, -2.4, 0.9), (0.05, 1.4, 0.7), RED)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_HabCapsule_Pod", coll)


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Long", long_capsule), ("Short", short_capsule),
                     ("Tank", tank), ("Cab", cab), ("Pod", pod)):
        fn(collection("Coll_HabCapsule_%s" % name), mats)
    report()
    save(out)


build()
