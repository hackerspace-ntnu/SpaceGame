"""Helms — the wheel a vessel is steered by, and the post it stands on.

Built for the dune foiler, which shipped with no rudder at all: heading came only
from trimming the main against the jib, which is a lovely model of how a sand
yacht works and close to unusable as a control. It needs something to steer
with, and a helm is exactly the kind of part that should never be modelled twice
— the salvage RV already has a bridge wheel baked into its hull mesh, and that is
the mistake this file exists to stop repeating.

Each variation is TWO objects, and that split is the whole point of the
component:

    Mesh_ShipsWheel_<V>_Column   the pedestal. Bolted down, never moves.
    Mesh_ShipsWheel_<V>_Wheel    the wheel. Spun by the game.

The wheel's origin sits on the hub centre and its spin axis is +Y, which is the
craft's fore-and-aft line — so a helmsman stands aft of it looking forward down
-Y. Blender's -Y forward becomes Unity's +Z, so that axis arrives in Unity as
local Z with no correction needed, and DuneFoilHelm spins it about local Z.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix  # noqa: E402

WOOD, BRASS, STEEL, DARK, RUST, RUBBER = 0, 1, 2, 3, 4, 5
MATS = ["Mat_Wood_Ply_Worn", "Mat_Metal_Brass_Tarnished",
        "Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Metal_Rust_Heavy", "Mat_Plastic_Rubber_Black"]

# Everything is sized off one number so the variations stay a family. 0.62 m
# across the rim is a wheel a standing adult works with both hands without
# reaching — big enough to read as a helm from across a deck, small enough not
# to block the view forward, which on this craft is the view of the sails.
RIM_RADIUS = 0.31


def _radial(angle):
    """Rotation that points a part built along +X outward at `angle` in the XZ
    plane. Negated because Blender's Y rotation carries +X toward -Z, while the
    positions below are laid out as (cos a, 0, sin a) — get this backwards and
    every spoke sits mirrored across the wheel from its own socket."""
    return Matrix.Rotation(-angle, 4, 'Y')


def _hub(p, radius=0.075, depth=0.16, mat=BRASS):
    """Boss and axle. Common to every wheel, and the reason they all mount the
    same way: the origin ends up here."""
    p.cyl((0, 0, 0), radius, depth, 'Y', 16, mat)
    p.cyl((0, 0, 0), radius * 0.45, depth + 0.10, 'Y', 12, STEEL)
    # Retaining collar and pin, so the wheel reads as something that comes off.
    for s in (-1, 1):
        p.tube((0, s * depth * 0.5, 0), radius * 0.72, 0.014, 0.022, 'Y', 14,
               DARK)


def _spoke_ring(p, count, inner, outer, thickness, mat, phase=0.0, skip=()):
    """Spokes on a circle in the XZ plane. `skip` drops indices, which is how
    the salvaged wheel loses two of them."""
    made = []
    for i in range(count):
        if i in skip:
            continue
        a = phase + 2 * math.pi * i / count
        mid = (inner + outer) * 0.5
        # A spoke tapers: fat at the hub, slim at the rim.
        made.append(p.cyl(
            (mid * math.cos(a), 0, mid * math.sin(a)),
            thickness, outer - inner, 'X', 8, mat,
            radius_top=thickness * 0.62,
            rot=_radial(a)))
    return made


def spoked(coll, mats):
    """The classic: eight spokes, turned handles standing proud of the rim, on a
    braced pedestal. This is the one the dune foiler uses."""
    p = Part(mats)
    r = RIM_RADIUS

    p.torus((0, 0, 0), r, 0.032, 'Y', 28, 8, WOOD)
    # Brass banding at the quarters — where the rim is spliced on a real wheel.
    for i in range(4):
        a = math.pi * 0.25 + i * math.pi * 0.5
        p.cyl((r * math.cos(a), 0, r * math.sin(a)), 0.040, 0.055, 'X', 10,
              BRASS, rot=_radial(a))

    _spoke_ring(p, 8, 0.07, r + 0.005, 0.026, WOOD)

    # Handles: the spokes carried past the rim. This is the silhouette that says
    # "ship's wheel" rather than "valve", so they are deliberately long.
    for i in range(8):
        a = 2 * math.pi * i / 8
        base, tip = r + 0.005, r + 0.135
        mid = (base + tip) * 0.5
        p.cyl((mid * math.cos(a), 0, mid * math.sin(a)), 0.024, tip - base,
              'X', 8, WOOD, radius_top=0.019, rot=_radial(a))
        p.cyl((tip * math.cos(a), 0, tip * math.sin(a)), 0.026, 0.022, 'X', 8,
              BRASS, rot=_radial(a))
    # One handle wrapped in cord, the way the king spoke is marked so the
    # helmsman can find rudder-amidships without looking down.
    p.tube((0, 0, r + 0.06), 0.028, 0.008, 0.05, 'Z', 10, RUBBER)

    _hub(p)
    p.bevel(width=0.005, segments=2)
    wheel = p.finish("Mesh_ShipsWheel_Spoked_Wheel", coll)

    c = Part(mats)
    _pedestal(c, height=1.02)
    c.bevel(width=0.006, segments=2)
    column = c.finish("Mesh_ShipsWheel_Spoked_Column", coll, origin=(0, 0, 0))
    return wheel, column


def heavy(coll, mats):
    """An industrial hand-wheel: a solid steel web with lightening holes, a fat
    rolled rim and no protruding handles. Different silhouette, not a recolour —
    for machinery, hatches and anything that should read as gear rather than as
    seamanship."""
    p = Part(mats)
    r = RIM_RADIUS * 0.92

    p.torus((0, 0, 0), r, 0.045, 'Y', 26, 8, STEEL)
    # The web, as a shallow dish rather than a flat plate.
    p.cyl((0, 0, 0), r, 0.030, 'Y', 26, DARK, radius_top=r * 0.97)
    # Lightening holes punched through it: three big, three small, offset.
    for i in range(3):
        a = 2 * math.pi * i / 3
        for radius_at, hole in ((r * 0.60, 0.085), (r * 0.60, 0.038)):
            b = a + (0.0 if hole > 0.06 else math.pi / 3)
            p.tube((radius_at * math.cos(b), 0, radius_at * math.sin(b)),
                   hole, 0.012, 0.034, 'Y', 12, STEEL)
    # Rust, as three collars around the rim rather than a second torus sunk
    # inside the first. A concentric duplicate is invisible geometry that ships
    # in the mesh forever and fails the no-interior-faces check.
    for i in range(3):
        a = 0.4 + 2 * math.pi * i / 3
        p.tube((r * math.cos(a), 0, r * math.sin(a)), 0.050, 0.006, 0.055,
               'X', 12, RUST)
    p.cyl((r * 0.70, 0.05, r * 0.70), 0.030, 0.09, 'Y', 10, RUBBER)

    _hub(p, radius=0.085, depth=0.13, mat=STEEL)
    p.bevel(width=0.004, segments=2)
    wheel = p.finish("Mesh_ShipsWheel_Heavy_Wheel", coll)

    c = Part(mats)
    _pedestal(c, height=0.74, taper=0.80, plated=True)
    c.bevel(width=0.006, segments=2)
    column = c.finish("Mesh_ShipsWheel_Heavy_Column", coll, origin=(0, 0, 0))
    return wheel, column


def salvage(coll, mats):
    """The same wheel after a hard life: six spokes instead of eight, two of them
    snapped off short, the rim spliced with a steel plate and lashed. Built so a
    scene can show two helms that are visibly the same design at different ends
    of their service life."""
    p = Part(mats)
    r = RIM_RADIUS

    p.torus((0, 0, 0), r, 0.030, 'Y', 24, 7, WOOD)
    # The splice: a steel plate bolted across a break in the rim.
    p.box((r * 0.10, 0, -r * 0.99), (0.22, 0.075, 0.055), STEEL,
          rot=Matrix.Rotation(0.18, 4, 'Y'))
    p.rivets((r * 0.02, 0.035, -r * 0.99), (r * 0.18, 0.035, -r * 0.99), 3,
             radius=0.012, height=0.010, mat=DARK)
    # Rust around the rim as collars, not a concentric second torus — see heavy().
    for i in range(4):
        a = 0.9 + 2 * math.pi * i / 4
        p.tube((r * math.cos(a), 0, r * math.sin(a)), 0.035, 0.005, 0.048,
               'X', 12, RUST)

    _spoke_ring(p, 6, 0.07, r + 0.005, 0.026, WOOD, skip=(2, 5))
    # The two that broke, left as stubs.
    _spoke_ring(p, 6, 0.07, 0.17, 0.026, WOOD, skip=(0, 1, 3, 4))

    # Four handles left of six, one of them a bare steel bar someone fitted.
    for i in (0, 1, 3):
        a = 2 * math.pi * i / 6
        base, tip = r + 0.005, r + 0.115
        mid = (base + tip) * 0.5
        p.cyl((mid * math.cos(a), 0, mid * math.sin(a)), 0.023, tip - base,
              'X', 8, WOOD, radius_top=0.018, rot=_radial(a))
    a = 2 * math.pi * 4 / 6
    p.cyl(((r + 0.06) * math.cos(a), 0, (r + 0.06) * math.sin(a)), 0.017, 0.12,
          'X', 6, STEEL, rot=_radial(a))
    # Cord lashing where the splice is.
    for z in (-0.028, 0.0, 0.028):
        p.tube((r * 0.10, z, -r * 0.99), 0.048, 0.007, 0.016, 'Y', 10, RUBBER)

    _hub(p, radius=0.070, depth=0.15, mat=RUST)
    p.bevel(width=0.005, segments=2)
    wheel = p.finish("Mesh_ShipsWheel_Salvage_Wheel", coll)

    c = Part(mats)
    _pedestal(c, height=0.94, taper=1.0, leaning=True)
    c.bevel(width=0.006, segments=2)
    column = c.finish("Mesh_ShipsWheel_Salvage_Column", coll, origin=(0, 0, 0))
    return wheel, column


def tiller(coll, mats):
    """Not a wheel at all — a tiller bar on a vertical pintle, swept fore and aft.

    Included because the smallest craft in a fleet should not have the same helm
    as the largest, and a tiller is the honest answer for a hull under about ten
    metres. Same origin and same spin axis convention, so the same game code
    drives it; only the silhouette changes.
    """
    p = Part(mats)

    # The bar, running aft from the pintle, with a rope-wrapped grip.
    p.cyl((0, 0.52, 0.02), 0.030, 1.04, 'Y', 10, WOOD, radius_top=0.038)
    for y in (0.86, 0.92, 0.98):
        p.tube((0, y, 0.02), 0.036, 0.008, 0.045, 'Y', 10, RUBBER)
    p.cyl((0, 1.04, 0.02), 0.042, 0.05, 'Y', 10, BRASS)
    # Yoke where it clamps onto the rudder head.
    p.slab((-0.075, -0.02, -0.06), (0.075, 0.16, 0.10), STEEL)
    p.rivets((-0.045, 0.10, 0.10), (0.045, 0.10, 0.10), 2, radius=0.014,
             height=0.010, mat=DARK)

    _hub(p, radius=0.062, depth=0.12, mat=BRASS)
    p.bevel(width=0.005, segments=2)
    wheel = p.finish("Mesh_ShipsWheel_Tiller_Wheel", coll)

    c = Part(mats)
    # A tiller has no pedestal — it has a rudder head coming up through the deck.
    c.cyl((0, 0, -0.30), 0.075, 0.62, 'Z', 12, STEEL)
    c.tube((0, 0, -0.03), 0.098, 0.016, 0.07, 'Z', 14, BRASS)
    c.cyl((0, 0, -0.60), 0.20, 0.05, 'Z', 16, DARK)
    c.rivets((-0.15, 0, -0.60), (0.15, 0, -0.60), 4, radius=0.016,
             height=0.012, mat=DARK)
    c.bevel(width=0.006, segments=2)
    column = c.finish("Mesh_ShipsWheel_Tiller_Column", coll, origin=(0, 0, 0))
    return wheel, column


def _pedestal(c, height, taper=0.85, plated=False, leaning=False):
    """The post the wheel stands on. Built DOWNWARD from the hub at z = 0, so a
    column and its wheel share one origin and drop onto a deck as a pair."""
    rot = Matrix.Rotation(math.radians(-6.0), 4, 'X') if leaning else None

    c.cyl((0, 0, -height * 0.5), 0.085, height, 'Z', 12, STEEL,
          radius_top=0.085 * taper, rot=rot)
    # Bearing housing right under the hub, so the axle has somewhere to live.
    c.tube((0, 0, -0.10), 0.105, 0.020, 0.16, 'Z', 14, DARK)
    c.cyl((0, 0, -0.20), 0.12, 0.05, 'Z', 12, BRASS)

    # Foot: a flange bolted through the deck.
    c.cyl((0, 0, -height + 0.02), 0.19, 0.045, 'Z', 16, DARK)
    for i in range(4):
        a = math.pi * 0.25 + i * math.pi * 0.5
        c.cyl((0.145 * math.cos(a), 0.145 * math.sin(a), -height + 0.05),
              0.018, 0.030, 'Z', 6, DARK)

    if plated:
        # A bolted-on access plate — the machinery read.
        c.slab((-0.075, 0.075, -height * 0.72), (0.075, 0.10, -height * 0.34),
               STEEL)
    else:
        # Two knee braces, fore and aft.
        for s in (-1, 1):
            c.box((0, s * 0.10, -height * 0.78), (0.030, 0.16, 0.24), STEEL,
                  rot=Matrix.Rotation(s * 0.5, 4, 'X'))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Spoked", spoked), ("Heavy", heavy),
                     ("Salvage", salvage), ("Tiller", tiller)):
        fn(collection("Coll_ShipsWheel_" + name), mats)

    report()
    save(out)


main()
