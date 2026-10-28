"""Gyre rod - the 1.3 m metal shaft that runs from a task area to one of the
core's rings and turns it. It runs flush in rod channels, socket to socket.

Run:  blender --background --python gyre_rod.py -- --out gyre_rod.blend

Variations: Coll_GyreRod_6m, _12m, _24m. Each is an armature with one bone,
Bone_RodSpin, along the rod's axis (+Y); the rod mesh is parented to the bone,
so the task logic turns the rod by rolling that bone. Polished bands every
3 m are where it has turned against its sockets for ages; the far end carries
the grip that keys into a ring.

Origin: the rod's root, on its axis, where it leaves its socket.
"""

import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "structural", "gyre"))
from _gyrelib import *  # noqa: E402,F403

OUT = out_path(__file__, "gyre_rod.blend")

R = 0.65
SEG = 16


def profile(length):
    """(radius, y) points and one material per band between them: dark metal,
    polished where the rod has turned in its sockets and on the grip."""
    prof = [(0.0, -0.05), (R, -0.05)]
    bands = []
    y = 3.0
    while y < length - 1.5:
        prof += [(R, y - 0.15), (R - 0.02, y - 0.12), (R - 0.02, y + 0.12),
                 (R, y + 0.15)]
        bands.append((y - 0.15, y + 0.15))
        y += 3.0
    L = length
    prof += [(R, L - 0.9), (R * 0.82, L - 0.75), (R * 0.82, L - 0.35),
             (R * 1.1, L - 0.30), (R * 1.1, L), (0.32, L), (0.32, L + 0.22),
             (0.0, L + 0.22)]
    mats = []
    for (r0, y0), (r1, y1) in zip(prof, prof[1:]):
        ym = (y0 + y1) / 2
        polished = ym >= L - 0.31 or any(a < ym < b for a, b in bands)
        mats.append(1 if polished else 0)
    return prof, mats


def rod(length, x0):
    tag = "%dm" % length
    coll = collection("Coll_GyreRod_" + tag)

    arm_data = bpy.data.armatures.new("Arm_GyreRod_" + tag)
    arm = bpy.data.objects.new("Arm_GyreRod_" + tag, arm_data)
    coll.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    b = arm_data.edit_bones.new("Bone_RodSpin")
    b.head = (0.0, 0.0, 0.0)
    b.tail = (0.0, length, 0.0)
    b.roll = 0.0
    bpy.ops.object.mode_set(mode='OBJECT')

    p = Part(link_materials([METAL, POLISH]))
    prof, mats = profile(length)
    revolve(p, prof, SEG, mats=mats, cap_bottom=False, cap_top=False, axis='Y')
    # three key fins at the grip
    for i in range(3):
        a = 2 * math.pi * i / 3 + 0.3
        rot = Matrix.Rotation(a, 4, 'Y')
        c = rot @ Vector((R * 0.95, 0.0, 0.0))
        f = p.box((c.x, length - 0.55, c.z), (0.42, 0.34, 0.16), mat=1, rot=rot)
    mesh_obj = p.finish("Mesh_GyreRod_" + tag, coll)

    mesh_obj.parent = arm
    mesh_obj.parent_type = 'BONE'
    mesh_obj.parent_bone = "Bone_RodSpin"
    # bone parenting hangs the child off the bone's tail; cancel that so the
    # mesh stays where it was built
    mesh_obj.matrix_parent_inverse = Matrix.Translation((0.0, length, 0.0)).inverted()

    arm.location.x = x0
    coll.instance_offset = (x0, 0.0, 0.0)


def main():
    start(OUT)
    rod(6, 0.0)
    rod(12, 4.0)
    rod(24, 8.0)
    report()
    gyre_save(OUT)


main()
