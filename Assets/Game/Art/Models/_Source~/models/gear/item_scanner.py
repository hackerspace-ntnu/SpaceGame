"""Item Scanner — the forearm-worn salvage finder.

Assembly only. Every surface on this model comes from a component; what is
authored here is the bracket that marries the terminal to the family's webbing
cuff, and the placement that puts the screen face-up on the top of the forearm
where the wearer can glance at it.

The first build of this model carried the terminal on `arm_cuff`'s `Grip`
variation as a hand-held instrument. It is now a gauntlet — see
`docs/superpowers/specs/2026-09-02-body-equipment-design.md` §7 — on the same
cuff, at the same seating, as `grapple_bracer` and `leash_gauntlet`.

Objects shipped, and why each is separate:

| Object | Separate because |
|---|---|
| `Mesh_ArmCuff_Webbing`          | reused component, seated by `_gauntlet` |
| `Mesh_Terminal_Scanner_Case`    | the static body |
| `Mesh_Terminal_Scanner_Screen`  | Unity paints the radar shader on this alone |
| `Mesh_Terminal_Scanner_Dial`    | the game spins it while scanning; origin on its axis |
| `Mesh_Terminal_Scanner_Antenna` | the game whips it; origin at its root |
| `Mesh_ItemScanner_Bracket`      | the only geometry unique to this model |

Component names are kept rather than renamed to `Mesh_ItemScanner_*`, so the
provenance of each piece is readable straight off the outliner, and so the
prefab's serialized references to the screen, dial and antenna survive the
re-export — FBX sub-object ids are derived from object names.


## Orientation — three constraints the code imposes

`ItemScannerArtifact` and `ItemScannerScreen` are not touched by this rework,
so the meshes have to satisfy what they already assume. The export maps
Blender `(x, y, z)` onto Unity `(−x, z, −y)`.

1. **The screen faces +Z (up), with its UV `v` toward the wrist.** The radar
   is a 180-degree forward arc with `v` up meaning "ahead", and ahead is where
   the arm points. `R_z(180) @ R_x(−90)` sends the plate's normal (local −Y)
   to +Z, its `v` (local +Z) to −Y, and its `u` (local +X) to −X — which is
   Unity +X, the viewer's right when looking down the arm. That is the same
   handedness the hand-held build shipped with, so `_FlipX` stays 0.
2. **The dial's axis is Blender Y.** The code spins it with `Euler(0, 0, a)`,
   Unity local Z, which is Blender −Y. The knob was authored protruding from
   the deck along −Y; the deck now faces up, so the knob cannot stay on it.
   It moves to the case's elbow-end face (the terminal's base, local z = 0),
   turned by `R_z(180)` so it protrudes toward +Y, the elbow. A knob is
   rotationally symmetric, so that roll changes nothing else.
3. **The antenna stands up +Z.** The code sways it with `Euler(x, 0, z)`,
   Unity X and Z, both transverse to a mast along Unity Y = Blender Z. So the
   mast is placed with no rotation but a roll, `R_z(−90)`, which sends its
   authored lean (+X, −Y) to (−X, −Y): outboard toward the little-finger side
   and forward, away from the wearer's face. It stands on its own boss on the
   bracket plate, outboard of the case, because the only flat upward surface
   left on the case is the screen.

The terminal is worn at `TERMINAL_K` = 0.8 of its hand-held size. Authored at
0.174 x 0.185 m it is wider than the forearm it lies on; at 0.8 the screen is
still 0.090 x 0.074 m, which at the rig's 2.1x wear is a tablet on the arm.

**No armature.** The dial and antenna are rigid and are already separate
objects with their origins on their own axis of motion — the cleaner form of
the same capability, and it skips a bone hierarchy Unity would have to unpick.

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
from _gauntlet import (  # noqa: E402
    BEVEL_W, CHROME, DARK, MATS, PROPS, STEEL, append_cuff, append_objects,
    clamp_bands, place, seat, spine)
from _tracked import TrackedPart  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

TERMINAL = os.path.join(PROPS, "handheld_terminal.blend")

TERMINAL_K = 0.80

# The spine floor is sunk 0.5 mm into the cuff's mounting boss (outer face at
# z = 0.056). Its rails stop at 0.068 — lower than the bracer's 0.076 — because
# the bracket plate sits on them and the case on the plate, and every
# millimetre here is 2.1 on the arm.
SPINE_FLOOR, SPINE_RAIL = 0.0555, 0.0680
SPINE_Y0, SPINE_Y1 = 0.0200, 0.1700

# The plate: on the rail tops, embedded 0.5 mm. It starts at y = 0.062 rather
# than at the case's wrist end (0.022) because the terminal's bail handle and
# its chrome mounts hang down to z = 0.064 at y = 0.027..0.060, and a plate
# under them would be a plate through them.
PLATE_LO = (-0.0780, 0.0620, 0.0675)
PLATE_HI = (0.0800, 0.1740, 0.0715)

# Where the case's back housing lands: local y = 0.052 is its underside, so
# CASE_AT.z - 0.052 * K = 0.070, 1.5 mm inside the plate. CASE_AT.y centres the
# 0.185 m case on the cuff; the controls end (local z = 0) is at the elbow.
CASE_AT = (0.0, 0.1700, 0.1116)

# The front block's underside (local y = 0.006) is at z = 0.1068 and only the
# back housing reaches the plate, so the −X half of the case would float.
# This block fills that void; its top is 0.5 mm inside the case, its +X face
# 1 mm inside the housing's flank at x = −0.024, its bottom 0.5 mm in the plate.
CRADLE_LO = (-0.0560, 0.0640, 0.0710)
CRADLE_HI = (-0.0230, 0.1660, 0.1073)

# Knob on the elbow-end face, 1 mm inside it so the base never shares its
# plane. The face spans x −0.058..0.053, z 0.107..0.152; the hazard stencil is
# at x −0.040..−0.005 and the lamps at x 0.022..0.042, z 0.154, so this spot is
# clear of both.
DIAL_AT = (0.0300, 0.1690, 0.1300)

# Mast boss on the plate, outboard of the case's −X flank (x = −0.058) and
# aft of the fire button at y = 0.104. The antenna's rubber base extends
# 1.6 mm below its origin at this scale, so 0.0710 buries it 2 mm in the plate
# — deeper than the boss's own 0.3 mm, so the two undersides never share a plane.
ANTENNA_AT = (-0.0670, 0.1500, 0.0710)
BOSS_Z0, BOSS_Z1 = 0.0712, 0.0792


def case_matrix():
    """Terminal local (face −Y, top +Z) onto the forearm, face up, top to the
    wrist. Local (x, y, z) → (−x·K, −z·K, −y·K) + CASE_AT."""
    return (Matrix.Translation(Vector(CASE_AT))
            @ Matrix.Rotation(math.radians(180), 4, 'Z')
            @ Matrix.Rotation(math.radians(-90), 4, 'X')
            @ Matrix.Diagonal(Vector((TERMINAL_K,) * 3)).to_4x4())


def bracket(coll, mats):
    """Spine, clamps, plate, cradle, hold-down clips and the mast boss."""
    p = TrackedPart(mats)
    hard = []

    hard += spine(p, SPINE_Y0, SPINE_Y1, z0=SPINE_FLOOR, z1=SPINE_RAIL)
    hard += clamp_bands(p, pad_top=SPINE_FLOOR + 0.0005)

    hard += p.slab(PLATE_LO, PLATE_HI, STEEL)
    hard += p.slab(CRADLE_LO, CRADLE_HI, DARK)

    # Hold-down clips: steel blocks 2 mm into the case's flanks, 1.5 mm into
    # the plate. The +X pair straddle the coil and connectors on that flank
    # (y 0.079..0.125); the −X pair sit clear of the fire button (y 0.104) and
    # of the mast boss (y 0.138..0.162).
    for y in (0.0680, 0.1600):
        hard += p.box((0.0708, y, 0.0780), (0.0080, 0.0160, 0.0160), STEEL)
    for y in (0.0680, 0.1300):
        hard += p.box((-0.0596, y, 0.0925), (0.0080, 0.0160, 0.0450), STEEL)

    # Mast boss, bottom 0.3 mm in the plate. The antenna's own rubber base
    # passes up through it.
    hard += p.slab((ANTENNA_AT[0] - 0.0120, ANTENNA_AT[1] - 0.0120, BOSS_Z0),
                   (ANTENNA_AT[0] + 0.0120, ANTENNA_AT[1] + 0.0120, BOSS_Z1),
                   STEEL)

    p.rivets((-0.0700, 0.0720, PLATE_HI[2]), (-0.0700, 0.1240, PLATE_HI[2]),
             3, radius=0.0022, height=0.0022, axis='Z', mat=CHROME)
    p.rivets((0.0775, 0.0850, PLATE_HI[2]), (0.0775, 0.1450, PLATE_HI[2]),
             3, radius=0.0022, height=0.0022, axis='Z', mat=CHROME)

    print("  Mesh_ItemScanner_Bracket: %d face(s) re-stamped" % p.restamp())
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_ItemScanner_Bracket", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_ItemScanner")

    append_cuff(coll)
    case, screen, dial, antenna = append_objects(TERMINAL, [
        "Mesh_Terminal_Scanner_Case", "Mesh_Terminal_Scanner_Screen",
        "Mesh_Terminal_Scanner_Dial", "Mesh_Terminal_Scanner_Antenna"], coll)
    place(case, case_matrix())
    place(screen, case_matrix())
    seat(dial, DIAL_AT, Matrix.Rotation(math.radians(180), 4, 'Z'), TERMINAL_K)
    seat(antenna, ANTENNA_AT, Matrix.Rotation(math.radians(-90), 4, 'Z'),
         TERMINAL_K)

    bracket(coll, mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
