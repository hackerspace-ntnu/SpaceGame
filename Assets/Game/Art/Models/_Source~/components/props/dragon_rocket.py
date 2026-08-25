"""Festival rockets — lacquered firework ordnance.

Built as a firework first and a warhead second: a paper-and-lacquer body wound
with gold rings, a gilt nose cone, canted fins that spin it, and an open
nozzle. That reading is load-bearing rather than decorative — this is the round
the dragon bazooka fires, and the whole point of that weapon is that its shot
wanders like a bottle rocket instead of flying like a missile. A sleek military
warhead would promise a straight line the flight code does not deliver.

Three variations, differing in silhouette and condition rather than colour:

  Coll_DragonRocket_Firework  the hero — 0.30 m, four canted fins, gilt cone
  Coll_DragonRocket_Whelp     0.15 m, three fins, blunt cap: the brood the
                              hero bursts into
  Coll_DragonRocket_Spent     a burnt-out casing with a split nozzle and one
                              fin folded over — loot and set dressing

Front is -Y, up is +Z. The origin sits at the NOZZLE face on the body axis,
because that is the end that stays put: a rocket is aimed from its tail and
grows forward, and a game spawning one wants to place the exhaust.

Both flying variations are under 42 mm across so they clear the dragon head's
44 mm gullet. That is a real constraint, not a coincidence — check it before
fattening the body.

Generation script — historical record. The .blend is the source of truth;
never re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix, Vector  # noqa: E402

# Index 0 is the lacquer: nearly every bevelled edge on a rocket is on a fin,
# and a fin is body-coloured.
(VERM, GOLD, DARK, BLACK, PAPER, SCORCH) = range(6)
MATS = [
    "Mat_Paint_Lacquer_Vermilion",  # 0  body and fins — and every bevel
    "Mat_Metal_Gold_Leaf",          # 1  nose cone, binding rings, fin edges
    "Mat_Metal_Steel_Dark",         # 2  nozzle throat and fin roots
    "Mat_Neutral_Black_Matte",      # 3  the open nozzle mouth
    "Mat_Fabric_Flag_Bleached",     # 4  the paper wrap band
    "Mat_Metal_Rust_Heavy",         # 5  scorching on the spent casing
]

BEVEL_W = 0.0014


def fins(p, count, y_root, span, chord, r_body, cant, mat, edge=GOLD,
         folded=-1):
    """Canted fins around the tail.

    The cant is what spins a firework, and it is why these are boxes turned
    about the body axis rather than flat plates: a fin that sits square to the
    airflow reads as a dart, and this round is not a dart.

    `folded` names one fin index to lay over flat — a crushed fin is the
    cheapest read of "this one already went off".
    """
    faces = []
    for i in range(count):
        a = 2 * math.pi * i / count
        radial = Vector((math.cos(a), 0.0, math.sin(a)))
        lean = math.radians(cant if i != folded else 78)

        turn = (Matrix.Rotation(-a, 4, 'Y')
                @ Matrix.Rotation(lean, 4, 'Y'))
        centre = radial * (r_body + span / 2.0) + Vector((0, y_root, 0))
        faces += p.box(centre, (0.004, chord, span), mat, rot=turn)

        # A gilt edge strip along the fin's trailing side. The offset is taken
        # THROUGH `turn`, into the fin's own frame — offsetting in world space
        # leaves the strip beside a canted fin rather than on it, which came
        # out as four gold bars floating off the tail.
        p.box(centre + (turn.to_3x3() @ Vector((0, chord * 0.44, 0))),
              (0.0055, chord * 0.10, span * 0.98), edge, rot=turn)

        # Root fillet, so the fin does not look glued on. Kept small and tight
        # to the casing: at fin chord it read as a black stripe wrapped round
        # the tail instead of a weld.
        faces += p.box(radial * (r_body + 0.002) + Vector((0, y_root, 0)),
                       (0.009, chord * 0.5, 0.008), DARK,
                       rot=Matrix.Rotation(-a, 4, 'Y'))
    return faces


def body(p, length, r, nose_frac=0.26, rings=4, wrap=True, mat=VERM):
    """The casing: nozzle, tube, binding rings, and a gilt ogive nose."""
    nose = length * nose_frac
    tube_len = length - nose

    p.cyl((0, -tube_len / 2.0, 0), r, tube_len, 'Y', 18, mat)

    # Nose: three lofted stations rather than a cone, so it has an ogive
    # shoulder instead of a point — a cone reads as a pencil.
    p.loft([
        (-tube_len, _ring(r, 18)),
        (-tube_len - nose * 0.45, _ring(r * 0.88, 18)),
        (-tube_len - nose * 0.80, _ring(r * 0.55, 18)),
        (-tube_len - nose, _ring(r * 0.12, 18)),
    ], axis='Y', mat=GOLD)

    hard = []
    for i in range(rings):
        y = -tube_len * (i + 0.6) / (rings + 0.2)
        hard += p.tube((0, y, 0), r + 0.0016, 0.0022, 0.010, 'Y', 18, GOLD)

    if wrap:
        p.cyl((0, -tube_len * 0.52, 0), r + 0.0008, tube_len * 0.30, 'Y', 18,
              PAPER)

    # Nozzle: a dark throat with a black mouth, recessed so the tail reads as
    # open rather than capped.
    p.tube((0, -0.012, 0), r * 0.92, 0.006, 0.024, 'Y', 18, DARK)
    p.cyl((0, -0.004, 0), r * 0.74, 0.008, 'Y', 18, BLACK)
    return hard


def _ring(r, n):
    return [(r * math.cos(2 * math.pi * i / n),
             r * math.sin(2 * math.pi * i / n)) for i in range(n)]


# --------------------------------------------------------------------------
# Variations
# --------------------------------------------------------------------------

def firework(coll, mats, name, length=0.300, r=0.020):
    p = Part(mats)
    hard = body(p, length, r, rings=4)
    hard += fins(p, 4, -0.052, 0.026, 0.070, r, 22.0, VERM)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def whelp(coll, mats, name, length=0.150, r=0.011):
    p = Part(mats)
    hard = body(p, length, r, nose_frac=0.30, rings=2, wrap=False)
    hard += fins(p, 3, -0.030, 0.016, 0.040, r, 26.0, VERM)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def spent(coll, mats, name, length=0.290, r=0.020):
    p = Part(mats)
    # Scorched body and no gilt wrap left: the same casing after it has flown.
    hard = body(p, length, r, nose_frac=0.20, rings=2, wrap=False, mat=SCORCH)
    hard += fins(p, 4, -0.050, 0.024, 0.066, r, 18.0, SCORCH, edge=DARK,
                 folded=2)
    # Split nozzle skirt, peeled open.
    for i in range(5):
        a = 2 * math.pi * i / 5
        radial = Vector((math.cos(a), 0.0, math.sin(a)))
        hard += p.box(radial * (r * 0.9) + Vector((0, 0.010, 0)),
                      (0.008, 0.026, 0.006), DARK,
                      rot=Matrix.Rotation(-a, 4, 'Y')
                          @ Matrix.Rotation(math.radians(24), 4, 'X'))
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def marker(coll, name, at, mats, size=0.003):
    """A tiny cube carrying a coordinate across the FBX. See portal_gun.py."""
    p = Part(mats)
    p.box((0, 0, 0), (size, size, size), GOLD)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    hero = collection("Coll_DragonRocket_Firework")
    firework(hero, mats, "Mesh_DragonRocket_Firework")
    # Where the exhaust plume and the trail are anchored, and the nose the
    # impact test traces from. Read by DragonBazookaBuilder.
    marker(hero, "Marker_Exhaust", (0.0, 0.006, 0.0), mats)
    marker(hero, "Marker_Nose", (0.0, -0.300, 0.0), mats)

    whelp(collection("Coll_DragonRocket_Whelp"), mats, "Mesh_DragonRocket_Whelp")
    spent(collection("Coll_DragonRocket_Spent"), mats, "Mesh_DragonRocket_Spent")

    report()
    save(out)


main()
