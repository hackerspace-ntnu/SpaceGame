"""Ram slide — the linear mechanism that drives a striking head forward.

Four parts of one machine, kept separate because two of them move and two do
not. Anything that hits by shooting a mass along a track can be built from these:
a powered fist, a breaching ram, a pile driver, a bolt thrower.

| Variation | Moves? | What it is |
|---|---|---|
| `Rails`        | fixed  | anchor plate, twin guide rails, front stop yoke |
| `Carriage`     | slides | the bushing block a head bolts to |
| `Cylinder`     | fixed  | the shell the steam pushes against |
| `Rod`          | slides | the piston rod and its clevis — travels with the carriage |
| `SpringReturn` | fixed  | the coil stack that drags the carriage home |

## Axes and origins

**−Y forward, +Z up.** The ram fires along −Y.

`Rails`, `Cylinder` and `SpringReturn` have their origin at their **rear
anchor** — the plane that bolts to the machine — and run forward into −Y.

`Carriage` is the exception: its origin sits on the **rail axis at its own
centre of travel**, because an assembly does not place a carriage where it is
bolted (it is bolted to nothing) but slides it along that axis. That makes the
Unity side a single `localPosition.z` animation with no offset to unpick.

## Travel

The rails are 0.280 m long and the carriage is 0.062 m deep, so usable stroke is
about 0.19 m once the stop yoke and the rear plate are accounted for. That number
is the mechanism's contract with whatever animates it — a longer stroke drives
the carriage through the yoke.

## Why the rod is its own object

A cylinder whose rod is part of the body is fine for a 6 cm twitch and obviously
broken at 19 cm: the fork stays welded to the shell while the carriage it is
supposed to be driving walks away from it. So `Rod` ships separately and belongs
to the MOVING group — it slides out of the shell exactly as a real ram does, and
the shell is long enough to swallow it again at rest.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, _HERE)

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from panel_control import tube_path  # noqa: E402

from mathutils import Vector  # noqa: E402

# Index 0 is STEEL: `bmesh.ops.bevel` stamps its new faces with material index 0.
STEEL, DARK, CHROME, BRASS, RUBBER, AMBER = range(6)
MATS = ["Mat_Metal_Steel_Worn",       # plates, yokes, cylinder shell
        "Mat_Metal_Steel_Dark",       # bushings, bolt bosses, shadowed recesses
        "Mat_Metal_Chrome_Scuffed",   # rails and the piston rod — ground surfaces
        "Mat_Metal_Brass_Tarnished",  # gland nuts, unions, the steam fittings
        "Mat_Plastic_Rubber_Black",   # wipers, hose stubs, bump stops
        "Mat_Emissive_Amber"]         # the pressure gauge face

BEVEL_W = 0.0012

RAIL_X = 0.052       # half-spacing of the twin rails
RAIL_Z = 0.018       # rail height above the mounting plane
RAIL_R = 0.0052
RAIL_LEN = 0.280
CARRIAGE_DEPTH = 0.062
ROD_LEN = 0.215        # stroke plus engagement; the shell has to be longer still


def _f(d):
    """Forward distance `d` (metres in front of the rear anchor) as a Y."""
    return -d


def rails(coll, mats):
    """Anchor plate, two ground rails, and the yoke that stops the carriage."""
    p = TrackedPart(mats)

    # Rear anchor plate.
    hard = p.slab((-0.068, _f(0.012), -0.020), (0.068, 0.0, RAIL_Z + 0.020),
                  STEEL)
    hard += p.slab((-0.058, _f(0.020), -0.012), (0.058, _f(0.012), RAIL_Z + 0.012),
                   DARK)

    for sx in (-1, 1):
        # The rail itself. Chrome, because a guide rail is a ground surface and
        # the one place on a filthy machine that stays bright.
        p.cyl((sx * RAIL_X, _f(RAIL_LEN / 2), RAIL_Z), RAIL_R, RAIL_LEN, 'Y',
              10, CHROME)
        # Rubber bump stop at the back of the stroke.
        p.cyl((sx * RAIL_X, _f(0.026), RAIL_Z), RAIL_R * 1.7, 0.010, 'Y', 8,
              RUBBER)

    # Front stop yoke: a plate across both rails with the rail ends passing
    # through it, so it reads as bolted on rather than floating.
    hard += p.slab((-0.068, _f(RAIL_LEN), -0.014), (0.068, _f(RAIL_LEN - 0.013),
                                                    RAIL_Z + 0.018), STEEL)
    for sx in (-1, 1):
        p.cyl((sx * RAIL_X, _f(RAIL_LEN - 0.006), RAIL_Z), RAIL_R * 1.5, 0.014,
              'Y', 8, BRASS)

    # Bolt bosses where the whole slide screws down onto a housing.
    for sx in (-1, 1):
        hard += p.box((sx * 0.060, _f(0.050), -0.016), (0.016, 0.070, 0.010),
                      DARK)
        p.rivets((sx * 0.060, _f(0.024), -0.020), (sx * 0.060, _f(0.078), -0.020),
                 3, radius=0.0035, height=0.005, axis='Z', mat=CHROME)

    p.restamp("rails")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_RamSlide_Rails", coll)


def carriage(coll, mats):
    """The sliding block. Origin on the rail axis at its centre of travel."""
    p = TrackedPart(mats)
    half = CARRIAGE_DEPTH / 2

    hard = []
    for sx in (-1, 1):
        # Bushing barrel riding the rail, with a brass wear collar at each end.
        p.tube((sx * RAIL_X, 0.0, RAIL_Z), RAIL_R * 2.0, RAIL_R * 0.95,
               CARRIAGE_DEPTH, 'Y', 12, DARK)
        for sy in (-1, 1):
            p.cyl((sx * RAIL_X, sy * (half - 0.004), RAIL_Z), RAIL_R * 2.3,
                  0.008, 'Y', 12, BRASS)
        # Cheek plate tying the bushing down to the mounting face.
        hard += p.box((sx * RAIL_X, 0.0, RAIL_Z * 0.5),
                      (0.014, CARRIAGE_DEPTH - 0.010, RAIL_Z + 0.024), STEEL)

    # Cross yoke joining the two bushings, and the face a head bolts onto.
    hard += p.slab((-0.058, -0.008, RAIL_Z - 0.026), (0.058, 0.010,
                                                      RAIL_Z + 0.020), STEEL)
    hard += p.slab((-0.066, _f(half), RAIL_Z - 0.034),
                   (0.066, _f(half - 0.011), RAIL_Z + 0.028), STEEL)

    # Bolt pattern on the mounting face — four, matching knuckle_block's.
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.cyl((sx * 0.052, _f(half + 0.003), RAIL_Z - 0.003 + sz * 0.025),
                  0.0055, 0.008, 'Y', 6, CHROME, radius_top=0.0042)

    # Clevis at the back, where the cylinder's rod pins in.
    for sx in (-1, 1):
        hard += p.box((sx * 0.012, half - 0.006, RAIL_Z - 0.006),
                      (0.008, 0.022, 0.024), DARK)
    p.cyl((0.0, half + 0.003, RAIL_Z - 0.006), 0.0045, 0.034, 'X', 8, CHROME)

    p.restamp("carriage")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_RamSlide_Carriage", coll)


def cylinder(coll, mats):
    """The cylinder shell: barrel, gland, tie rods and the fittings that feed it.

    Long enough to swallow the rod at rest — `ROD_LEN` plus engagement — because a
    shell shorter than its own stroke is a ram that bottoms out in mid-air.
    """
    p = TrackedPart(mats)
    body_len = 0.230
    r = 0.021

    # Shell, with the end cap and gland as separate rings so the barrel does not
    # read as a single extruded tube.
    p.cyl((0.0, _f(body_len / 2), 0.0), r, body_len, 'Y', 14, STEEL)
    p.cyl((0.0, _f(0.006), 0.0), r * 1.18, 0.012, 'Y', 14, DARK)
    p.cyl((0.0, _f(body_len - 0.007), 0.0), r * 1.22, 0.014, 'Y', 14, BRASS)

    # Tie rods down the outside — the detail that makes a tube read as a
    # pressure vessel rather than as a pipe.
    hard = []
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        p.cyl((r * 1.05 * math.cos(a), _f(body_len / 2), r * 1.05 * math.sin(a)),
              0.0028, body_len - 0.004, 'Y', 6, CHROME)

    # Steam union and a hose stub at the rear.
    p.cyl((0.0, _f(0.004), r * 0.55), 0.007, 0.016, 'Z', 8, BRASS)
    tube_path(p, [(0.0, _f(0.004), r * 0.55 + 0.008),
                  (0.0, 0.012, r * 0.55 + 0.026),
                  (0.0, 0.030, r * 0.55 + 0.030)], 0.0042, RUBBER, seg=6)

    p.restamp("cylinder")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_RamSlide_Cylinder", coll)


def rod(coll, mats):
    """The piston rod and its clevis. MOVES with the carriage.

    Origin at the **clevis pin** — the point it is pinned to the carriage — because that
    is the only place it is attached to anything, and an assembly positions it by where it
    joins rather than by where it disappears into the shell.

    Geometry therefore runs BACKWARD (+Y) from the origin into the cylinder, which is the
    opposite of everything else in this file and is called out here so nobody 'fixes' it.
    """
    p = TrackedPart(mats)

    p.cyl((0.0, ROD_LEN / 2, 0.0), 0.0075, ROD_LEN, 'Y', 10, CHROME)
    # Piston head at the far end, sized to the bore so the shell reads as sealed.
    p.cyl((0.0, ROD_LEN - 0.008, 0.0), 0.0185, 0.016, 'Y', 12, DARK)

    hard = []
    for sx in (-1, 1):
        hard += p.box((sx * 0.010, 0.014, 0.0), (0.007, 0.028, 0.022), DARK)
    p.cyl((0.0, 0.0, 0.0), 0.0052, 0.036, 'X', 8, CHROME)

    p.restamp("rod")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_RamSlide_Rod", coll)


def spring_return(coll, mats):
    """A coil over a guide rod between two collars — what pulls the ram back.

    A real helix rather than a stack of rings: from the side a ring stack reads
    as a threaded bar, and the whole point of the part is that it looks sprung.
    """
    p = TrackedPart(mats)
    length = 0.086
    coil_r = 0.013
    turns = 7
    steps_per_turn = 8

    pts = []
    n = turns * steps_per_turn
    for i in range(n + 1):
        t = i / n
        a = 2 * math.pi * turns * t
        pts.append((coil_r * math.cos(a), _f(0.010 + t * (length - 0.020)),
                    coil_r * math.sin(a)))
    tube_path(p, pts, 0.0028, CHROME, seg=5, joint=False)

    # Guide rod down the middle and a collar at each end.
    p.cyl((0.0, _f(length / 2), 0.0), 0.005, length, 'Y', 8, DARK)
    hard = []
    for y, mat in ((_f(0.005), STEEL), (_f(length - 0.005), STEEL)):
        p.cyl((0.0, y, 0.0), coil_r * 1.35, 0.010, 'Y', 12, mat)
    hard += p.box((0.0, _f(0.002), -coil_r * 1.35),
                  (0.042, 0.008, 0.010), STEEL)

    p.restamp("spring")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_RamSlide_SpringReturn", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    rails(collection("Coll_RamSlide_Rails"), mats)
    carriage(collection("Coll_RamSlide_Carriage"), mats)
    cylinder(collection("Coll_RamSlide_Cylinder"), mats)
    rod(collection("Coll_RamSlide_Rod"), mats)
    spring_return(collection("Coll_RamSlide_SpringReturn"), mats)

    save(out)
    report()


if __name__ == "__main__":
    main()
