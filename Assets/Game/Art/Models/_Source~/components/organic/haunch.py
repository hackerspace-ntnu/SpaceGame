"""Build components/organic/haunch.blend -- the muscle mass a limb hangs from.

Four variations. This component exists because of a specific failure: a body
that tapers smoothly from head to tail has exactly one mass centre, and a
quadruped needs two. Bolting legs onto a smooth taper gives you a lizard with
sticks glued to it. The haunch is the shoulder or hip swelling that makes the
limb look like it grows out of something.

Built for the **+Y (port) side**, with the limb socket at the origin and the
mass reaching inboard toward -Y, where it fades into the flank. Use
`_buildlib.mirror_y` for the starboard copy.

The inboard end deliberately fades to a small ring rather than a flat face: it
is meant to be pushed *into* a body until it disappears, and a flat inner face
shows as a hard edge the moment the body it is sunk into is thinner than
expected.

**The bulk sits at the socket end, not the inboard end.** Anatomically an ilium
is the other way round -- broad where it meets the spine, narrow at the hip
socket -- and building it that way is the obvious mistake. It puts the entire
mass inside the body where nothing can see it, and drops it below the belly at
the centreline so the animal reads as sagging rather than as hipped. What is
wanted is the swelling *outside* the flank that the femur emerges from, so the
profile runs fat-to-thin outboard-to-inboard.

Authored at final real-world scale for a ~5.5 m animal.

    blender --background --python haunch.py -- \
        --out <lib>/components/organic/haunch.blend
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from _buildlib import (Part, collection, link_materials, parse_out, report,
                       save, start)                                # noqa: E402
from _organic import ring, scutes                                  # noqa: E402

HIDE, PLATE, HORN = 0, 1, 2
MATERIALS = ["Mat_Hide_Sand_Pale", "Mat_Hide_Plate_Tan", "Mat_Hide_Claw_Horn"]


def mass(part, stations, mat=HIDE):
    """Loft the swelling along -Y, inboard from the socket at the origin.

    `stations` are `(y, rx, rz, cx, cz)` -- the semi-axes in the body's fore-aft
    and vertical directions, plus a centre offset, because a haunch leans: a
    shoulder rakes forward over the ribs and a hip rakes back over the tail
    base.

    Both ends dome over two extra rings rather than one. A single step to a
    small ring still leaves a flat cap facet a third the width of the haunch,
    and on the shoulder -- where the limb leaves at a downward angle and covers
    nothing directly outboard -- that facet catches no light and reads as an
    open socket. Two rings curve it away instead.
    """
    def dome(anchor, direction):
        y, rx, rz, cx, cz = anchor
        return [(y + direction * 0.052,
                 ring(rx * 0.66, rz * 0.66, cy=cx, cz=cz, flat_bottom=0.10)),
                (y + direction * 0.088,
                 ring(rx * 0.16, rz * 0.16, cy=cx, cz=cz, flat_bottom=0.10))]

    sections = list(reversed(dome(stations[0], 1.0)))
    for y, rx, rz, cx, cz in stations:
        # loft(axis='Y') maps profile (u, v) onto (x, z), so `ring`'s ry is the
        # fore-aft semi-axis here rather than a lateral one.
        sections.append((y, ring(rx, rz, cy=cx, cz=cz, flat_bottom=0.10)))
    sections += dome(stations[-1], -1.0)
    return part.loft(sections, axis='Y', mat=mat, cap=True)


def hip_heavy(mats):
    """The big rear haunch: tall, raked back, deep enough to give a femur
    somewhere to socket into and low enough to hang below the belly line."""
    part = Part(mats)
    mass(part, [(0.000, 0.400, 0.415, 0.000, 0.020),
                (-0.110, 0.430, 0.440, -0.035, 0.045),
                (-0.260, 0.410, 0.415, -0.075, 0.060),
                (-0.420, 0.335, 0.330, -0.100, 0.065),
                (-0.560, 0.215, 0.205, -0.110, 0.055),
                (-0.650, 0.080, 0.070, -0.115, 0.040)])
    part.bevel(width=0.008, segments=1)
    return part


def shoulder_broad(mats):
    """The front haunch: shallower than the hip and raked forward, sitting over
    the ribs rather than behind them."""
    part = Part(mats)
    mass(part, [(0.000, 0.350, 0.360, 0.000, 0.020),
                (-0.100, 0.380, 0.390, 0.030, 0.045),
                (-0.240, 0.365, 0.365, 0.065, 0.060),
                (-0.390, 0.295, 0.290, 0.085, 0.065),
                (-0.520, 0.190, 0.180, 0.095, 0.055),
                (-0.600, 0.070, 0.062, 0.100, 0.040)])
    part.bevel(width=0.008, segments=1)
    return part


def hip_lean(mats):
    """A lighter hip for a faster or younger animal -- same reach inboard, much
    less depth, so the leg reads as sprung rather than as load-bearing."""
    part = Part(mats)
    mass(part, [(0.000, 0.330, 0.320, 0.000, 0.015),
                (-0.105, 0.350, 0.340, -0.030, 0.035),
                (-0.255, 0.330, 0.315, -0.065, 0.050),
                (-0.410, 0.265, 0.245, -0.085, 0.055),
                (-0.545, 0.170, 0.150, -0.095, 0.045),
                (-0.630, 0.065, 0.055, -0.100, 0.035)])
    part.bevel(width=0.007, segments=1)
    return part


def shoulder_plated(mats):
    """Shoulder mass carrying a run of keratin scutes over the top -- for an
    armoured animal, where the shoulder is the highest point a predator can
    reach."""
    part = Part(mats)
    mass(part, [(0.000, 0.355, 0.365, 0.000, 0.020),
                (-0.100, 0.385, 0.400, 0.030, 0.045),
                (-0.240, 0.370, 0.375, 0.065, 0.060),
                (-0.390, 0.300, 0.295, 0.085, 0.065),
                (-0.520, 0.195, 0.185, 0.095, 0.055),
                (-0.600, 0.072, 0.064, 0.100, 0.040)])
    # Seated on the mass's upper surface -- z here tracks cz + rz at each
    # station, not an eyeballed height, or the row floats clear of the flesh.
    scutes(part, (0.070, -0.110, 0.435), (0.095, -0.450, 0.320), 4, 0.080,
           PLATE, taper=0.45)
    part.bevel(width=0.008, segments=1)
    return part


VARIANTS = [
    ("HipHeavy", hip_heavy),
    ("ShoulderBroad", shoulder_broad),
    ("HipLean", hip_lean),
    ("ShoulderPlated", shoulder_plated),
]


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATERIALS)
    for name, builder in VARIANTS:
        coll = collection("Coll_Haunch_%s" % name)
        builder(mats).finish("Mesh_Haunch_%s" % name, coll)
    report()
    save(out)


main()
