"""Shoulder guards for the left shoulder, cupped over the top of the upper arm.

    blender --background --python components/apparel/shoulder_guard.py -- --out components/apparel/shoulder_guard.blend

Built in place on the Slim mannequin (see `_fit.py`); origin at the shoulder joint.
Binds to `LeftArm`, so it rides the arm. Built for the left shoulder only - the sky
soldier wears one; a right-hand guard is this mirrored across X.

    Coll_Guard_Layered   the sky soldier's: three overlapping plates stepping down the
                         arm, each over the one below, orange-edged
    Coll_Guard_Round     one domed cap

Generation script -- historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from mathutils import Vector  # noqa: E402

import _buildlib as B  # noqa: E402
import _fit as F  # noqa: E402

MATS = ["Mat_Fabric_Tarp_Azure", "Mat_Paint_Safety_Orange"]
PLATE, TRIM = range(2)


def arm_frame(j):
    shoulder, elbow = j["LeftArm"]
    axis = (elbow - shoulder).normalized()
    up = Vector((0, 0, 1))
    up = (up - axis * up.dot(axis)).normalized()
    front = axis.cross(up).normalized()
    return shoulder, axis, up, front


def cup(shoulder, axis, up, front, along, radius, spread, count=12):
    """A row of points arcing over the top of the arm at `along` down it."""
    row = []
    for i in range(count):
        a = math.radians(-spread + 2 * spread * i / (count - 1))
        row.append(shoulder + axis * along + (up * math.cos(a) + front * math.sin(a)) * radius)
    return row


def plate(p, frame, s0, s1, r0, r1, spread, thickness, mat):
    shoulder, axis, up, front = frame
    rows = [cup(shoulder, axis, up, front, F.lerp(s0, s1, t), F.lerp(r0, r1, t), spread) for t in (0.0, 0.5, 1.0)]
    p.sheet(rows, thickness, mat)


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)
    j = F.body_joints()
    frame = arm_frame(j)
    origin = frame[0]

    layered = B.collection("Coll_Guard_Layered")
    # Broad and deep-cupped (a pauldron, not bands): each plate spans 130 degrees round the arm
    # and 16-20 cm down it, the next one up on a radius 3 cm larger so it laps over.
    steps = (("Lower", 0.12, 0.30, 0.15, 0.14), ("Middle", 0.0, 0.2, 0.18, 0.17), ("Upper", -0.12, 0.08, 0.21, 0.2))
    for label, s0, s1, r0, r1 in steps:
        p = B.Part(mats)
        plate(p, frame, s0, s1, r0, r1, 130.0, 0.03, PLATE)
        F.bind(p.finish("Mesh_Guard_Layered_Plate%s" % label, layered, origin=origin), "LeftArm")
        t = B.Part(mats)
        plate(t, frame, s1 - 0.03, s1 + 0.004, r1 + 0.017, r1 + 0.016, 130.0, 0.012, TRIM)
        F.bind(t.finish("Mesh_Guard_Layered_Plate%s_Trim" % label, layered, origin=origin), "LeftArm")

    rounded = B.collection("Coll_Guard_Round")
    p = B.Part(mats)
    shoulder, axis, up, front = frame
    rows = [cup(shoulder, axis, up, front, s, r, 110.0) for s, r in ((-0.1, 0.06), (-0.06, 0.13), (0.02, 0.16), (0.14, 0.15))]
    p.sheet(rows, 0.024, PLATE)
    F.bind(p.finish("Mesh_Guard_Round_Cap", rounded, origin=origin), "LeftArm")

    B.save(out)
    B.report()


main()
