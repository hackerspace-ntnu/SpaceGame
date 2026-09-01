"""Jumping Rod — a scavenged pogo stick.

A spring-loaded hopping stick: plant it, and the coil throws you back up every
time the foot hits the sand.

Authored standing on +Z with the origin AT THE GROUND CONTACT POINT, because
that is the point the game reasons about — the item probes downward from it and
the coil compresses toward it. −Y is forward, which the library's default export
conversion lands on Unity's +Z.

Thirteen objects, nothing joined, each with its origin at the point it pivots or
slides about; no armature. **The decomposition, what each object is for, and why
there is no rig are recorded once, in `jumping_rod_BUILD.md` beside this file.**
Repeating the table here is how the two drift apart.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.

    blender --background --python models/gear/jumping_rod.py -- \
        --out models/gear/jumping_rod.blend
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))

import bpy  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

STEEL, DARK, CHROME, RUBBER, BRASS, YELLOW, AMBER, GLASS, CANVAS = range(9)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Metal_Chrome_Scuffed", "Mat_Plastic_Rubber_Black",
        "Mat_Metal_Brass_Tarnished", "Mat_Paint_Hazard_Yellow",
        "Mat_Emissive_Amber", "Mat_Glass_Canopy_Tinted",
        "Mat_Fabric_Canvas_Faded"]

# ---------------------------------------------------------------------------
# The stack, bottom to top. Every number below is a height above the ground
# contact point, so the whole machine can be re-proportioned by editing this
# block rather than by hunting through the geometry.
# ---------------------------------------------------------------------------
FOOT_TOP = 0.058          # top of the rubber pad
SEAT_Z = 0.105            # spring seat, rides the piston
SPRING_LO = 0.120         # coil, lower end at full extension
SPRING_HI = 0.400         # coil, upper end — anchored in the collar
COLLAR_Z = 0.430          # fixed spring anchor at the shaft's foot
SHAFT_LO = 0.410          # outer tube. Below the pegs, which hang off IT and
SHAFT_HI = 1.598          # not off the piston — the pegs must not travel.
PISTON_HI = 0.720         # buried inside the shaft even at full extension
PEG_Z = 0.520             # the rider stands here, clear of the collar
GAUGE_Z = 1.050
BAND_Z = 1.200
BAR_Z = 1.620

SHAFT_R = 0.048
PISTON_R = 0.030
COIL_R = 0.058
BAR_HALF = 0.235
PEG_OUT = 0.210

# How far the piston can travel before the coil is solid. The Unity spring rig
# reads the same figure — see JumpingRodSpring.travel — and a change here has to
# be carried across, or the coil passes through its own seat at full squash.
TRAVEL = 0.110


def foot(coll, mats):
    """The rubber pad that meets the sand, plus the steel shoe it is bonded to.

    Rides the piston: Unity parents this under it, so the pad and the piston
    travel together and only one transform is driven.
    """
    p = Part(mats)
    hard = []
    # Domed pad — wider than the piston so the stick does not sink point-first.
    p.cyl((0, 0, 0.020), 0.076, 0.040, 'Z', 20, RUBBER, radius_top=0.068)
    p.cyl((0, 0, 0.048), 0.068, 0.020, 'Z', 20, RUBBER, radius_top=0.052)
    # Tread: a ring of blocks, so the pad reads as grip rather than as a bung.
    for i in range(6):
        a = 2 * math.pi * i / 6
        p.box((0.050 * math.cos(a), 0.050 * math.sin(a), 0.006),
              (0.026, 0.026, 0.014), DARK, rot=Matrix.Rotation(a, 4, 'Z'))
    hard += p.cyl((0, 0, FOOT_TOP), 0.056, 0.014, 'Z', 16, STEEL)
    p.bevel(hard, width=0.003, segments=2)
    return p.finish("Mesh_JumpingRod_Foot", coll, origin=(0, 0, 0))


def piston(coll, mats):
    """The chromed inner shaft. Slides up into the outer tube under load.

    Origin at its buried top end — the point that stays inside the shaft — so a
    local translation along Z is exactly the compression the game is modelling.
    """
    p = Part(mats)
    lo = FOOT_TOP + 0.006
    p.cyl((0, 0, (lo + PISTON_HI) / 2), PISTON_R, PISTON_HI - lo, 'Z', 16,
          CHROME)
    # Wiper collar where it enters the tube: stops the chrome reading as a rod
    # that simply passes through a hole.
    hard = p.cyl((0, 0, PISTON_HI - 0.020), PISTON_R + 0.005, 0.026, 'Z', 16,
                 DARK)
    p.bevel(hard, width=0.002, segments=2)
    return p.finish("Mesh_JumpingRod_Piston", coll, origin=(0, 0, PISTON_HI))


def spring_seat(coll, mats):
    """The flange the coil pushes against. Rides the piston."""
    p = Part(mats)
    hard = p.cyl((0, 0, SEAT_Z), COIL_R + 0.016, 0.018, 'Z', 20, DARK)
    hard += p.cyl((0, 0, SEAT_Z + 0.014), COIL_R + 0.006, 0.012, 'Z', 20, BRASS)
    p.bevel(hard, width=0.003, segments=2)
    return p.finish("Mesh_JumpingRod_SpringSeat", coll, origin=(0, 0, SEAT_Z))


def spring(coll, mats):
    """The coil.

    Origin at its TOP, which is the end bolted into the fixed collar. The Unity
    rig squashes it by scaling local Z, and scaling happens about the origin —
    so with the origin here the coil shortens downward from a fixed anchor,
    which is what the machine actually does. With the origin at the base it
    would grow up through the shaft instead.
    """
    p = Part(mats)
    p.helix(SPRING_LO, SPRING_HI, COIL_R, 0.010, 5.0, DARK)
    return p.finish("Mesh_JumpingRod_Spring", coll, origin=(0, 0, SPRING_HI))


def collar(coll, mats):
    """Fixed brass anchor at the shaft's foot — the coil's upper seat."""
    p = Part(mats)
    hard = p.cyl((0, 0, COLLAR_Z), COIL_R + 0.010, 0.070, 'Z', 20, BRASS)
    hard += p.cyl((0, 0, COLLAR_Z - 0.042), SHAFT_R + 0.008, 0.018, 'Z', 20,
                  DARK)
    p.rivets((-0.052, 0.0, COLLAR_Z + 0.030), (0.052, 0.0, COLLAR_Z + 0.030),
             4, radius=0.005, height=0.005, axis='Z', mat=STEEL)
    p.bevel(hard, width=0.003, segments=2)
    return p.finish("Mesh_JumpingRod_Collar", coll, origin=(0, 0, COLLAR_Z))


def shaft(coll, mats):
    """The outer tube: everything fixed hangs off this, and it is what the
    prefab treats as the body."""
    p = Part(mats)
    mid = (SHAFT_LO + SHAFT_HI) / 2
    p.tube((0, 0, mid), SHAFT_R, 0.008, SHAFT_HI - SHAFT_LO, 'Z', 20, STEEL)
    # Peg mounting bosses and the bar clamp — welded to the tube, so part of it.
    hard = []
    for sx in (-1, 1):
        hard += p.box((sx * 0.040, 0, PEG_Z), (0.036, 0.052, 0.062), STEEL)
    hard += p.cyl((0, 0, SHAFT_HI - 0.030), SHAFT_R + 0.010, 0.060, 'Z', 20,
                  DARK)
    hard += p.box((0, 0, SHAFT_HI + 0.006), (0.052, 0.036, 0.030), DARK)
    p.bevel(hard, width=0.003, segments=2)
    return p.finish("Mesh_JumpingRod_Shaft", coll, origin=(0, 0, SHAFT_LO))


def band(coll, mats):
    """The hazard stripe. Its own object so the accent colour can be changed
    without touching the tube it is painted on."""
    p = Part(mats)
    p.tube((0, 0, BAND_Z), SHAFT_R + 0.004, 0.004, 0.100, 'Z', 20, YELLOW)
    return p.finish("Mesh_JumpingRod_Band", coll, origin=(0, 0, BAND_Z))


def gauge(coll, mats):
    """Travel dial on the front face, angled up at the rider's eye.

    Built about the origin and then swung into place, so its own +Z is the dial
    normal — a needle added later turns about the axis the object already has.
    """
    p = Part(mats)
    hard = p.cyl((0, 0, -0.014), 0.032, 0.028, 'Z', 18, BRASS)
    p.cyl((0, 0, 0.002), 0.026, 0.006, 'Z', 18, AMBER)
    p.cyl((0, 0, 0.007), 0.028, 0.004, 'Z', 18, GLASS)
    p.bevel(hard, width=0.002, segments=2)
    obj = p.finish("Mesh_JumpingRod_Gauge", coll, origin=(0, 0, 0))

    # -Y is forward, so the dial faces the rider by pointing its normal at -Y
    # and tipping back 20 degrees.
    m = (Matrix.Translation(Vector((0, -SHAFT_R - 0.006, GAUGE_Z)))
         @ Matrix.Rotation(math.radians(-70), 4, 'X'))
    obj.data.transform(m.to_3x3().to_4x4())
    obj.location = m @ obj.location
    return obj


def peg(coll, mats, side):
    """One footboard. Built per side rather than mirrored, so either can be
    edited — or removed for a battered variant — without disturbing the other.
    """
    sx = 1.0 if side == "R" else -1.0
    p = Part(mats)
    root = sx * (SHAFT_R + 0.004)
    tip = sx * PEG_OUT

    hard = p.cyl(((root + tip) / 2, 0, PEG_Z), 0.019, abs(tip - root), 'X', 12,
                 DARK)
    # The board itself: what the boot actually stands on.
    hard += p.box(((root + tip) / 2, 0, PEG_Z + 0.024),
                  (abs(tip - root) * 0.9, 0.086, 0.014), STEEL)
    hard += p.box(((root + tip) / 2, 0, PEG_Z + 0.033),
                  (abs(tip - root) * 0.82, 0.078, 0.008), RUBBER)
    # Grit ribs across the board, so it reads as a foothold at a distance.
    for i in range(4):
        t = (i + 0.5) / 4
        p.box((root + (tip - root) * t, 0, PEG_Z + 0.038),
              (0.010, 0.070, 0.006), DARK)
    hard += p.cyl((tip, 0, PEG_Z + 0.014), 0.024, 0.014, 'X', 12, BRASS)
    p.bevel(hard, width=0.003, segments=2)
    return p.finish("Mesh_JumpingRod_Peg_" + side, coll, origin=(root, 0, PEG_Z))


def handlebar(coll, mats):
    """One swept bar across both hands, ends turned back toward the rider."""
    p = Part(mats)
    pts = [(-BAR_HALF, 0.052, BAR_Z - 0.014),
           (-0.150, 0.010, BAR_Z),
           (-0.055, 0.0, BAR_Z + 0.004),
           (0.055, 0.0, BAR_Z + 0.004),
           (0.150, 0.010, BAR_Z),
           (BAR_HALF, 0.052, BAR_Z - 0.014)]
    tube_path(p, pts, 0.019, CHROME, seg=10)
    hard = p.box((0, 0, BAR_Z + 0.004), (0.060, 0.046, 0.044), DARK)
    hard += p.box((0, 0.030, BAR_Z + 0.004), (0.030, 0.026, 0.030), CANVAS)
    p.bevel(hard, width=0.003, segments=2)
    return p.finish("Mesh_JumpingRod_Handlebar", coll, origin=(0, 0, BAR_Z))


def grip(coll, mats, side):
    """One ribbed rubber hand grip, and the bar-end plug that caps it.

    Built here rather than reused. `Mesh_WeaponGrip_Fore` was tried first and is
    the closest thing in the library — a 0.125 m moulded sleeve — but it is a
    VERTICAL foregrip with a wide finger flange down one face, and laid onto a
    handlebar it reads as a flat plate bolted across the bar rather than as
    something a hand closes round. A bar-end grip is a body of revolution; that
    component is not one, and no amount of placement fixes the silhouette.
    """
    sx = 1.0 if side == "R" else -1.0
    p = Part(mats)

    # Follow the bar's swept end rather than crossing it: the outer 0.085 m of
    # the bar rises 0.052 in Y and drops 0.014 in Z, so the sleeve is laid on
    # that same slope.
    inner = Vector((sx * 0.150, 0.010, BAR_Z))
    outer = Vector((sx * BAR_HALF, 0.052, BAR_Z - 0.014))
    axis = (outer - inner).normalized()
    rot = axis.to_track_quat('Z', 'Y').to_matrix().to_4x4()

    hard = []
    for t in (0.10, 0.90):
        p.cyl(inner.lerp(outer, t), 0.030, 0.012, 'Z', 14, RUBBER, rot=rot)
    p.cyl(inner.lerp(outer, 0.5), 0.027, (outer - inner).length * 0.86, 'Z', 14,
          RUBBER, rot=rot)
    # Ribs, so the grip reads as moulded rubber and not as a smooth dowel.
    for i in range(5):
        t = 0.22 + 0.14 * i
        p.cyl(inner.lerp(outer, t), 0.0295, 0.010, 'Z', 14, DARK, rot=rot)
    hard += p.cyl(outer + axis * 0.008, 0.024, 0.016, 'Z', 14, BRASS, rot=rot)

    p.bevel(hard, width=0.002, segments=2)
    return p.finish("Mesh_JumpingRod_Grip_" + side, coll, origin=tuple(inner))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_JumpingRod")

    shaft(coll, mats)
    collar(coll, mats)
    band(coll, mats)
    gauge(coll, mats)
    piston(coll, mats)
    spring_seat(coll, mats)
    spring(coll, mats)
    foot(coll, mats)
    peg(coll, mats, "L")
    peg(coll, mats, "R")
    handlebar(coll, mats)
    grip(coll, mats, "L")
    grip(coll, mats, "R")

    save(out)
    report()


if __name__ == "__main__":
    main()
