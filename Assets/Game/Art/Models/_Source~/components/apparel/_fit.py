"""Shared fitting for apparel: where the body is, and what a piece hangs from.

Apparel in this folder is modelled IN PLACE on `components/organic/human_mannequin.blend`'s
Slim build standing at the origin (hips over x = 0, soles on z = 0, facing -Y), so an
assembled character takes a piece exactly where it was built. Every piece records the
bone it rides in a `bind_bone` custom property; the model that assembles it binds it to
its own armature with that name (see `models/characters/sky_soldier/sky_soldier.py`).

Not a generator: imported by the apparel scripts.
"""
import math
import os

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
MANNEQUIN = os.path.join(LIB, "components", "organic", "human_mannequin.blend")
BUILD_ARMATURE = "Arm_Mannequin_Slim"
BONE = "mixamorig:"
BIND = "bind_bone"


def body_joints():
    """{bone: (head, tail)} for the Slim mannequin, moved to stand at the origin.

    Linked read-only for the measurement and removed again, so nothing of the
    mannequin is left in the file being built.
    """
    with bpy.data.libraries.load(MANNEQUIN, link=True) as (src, dst):
        if BUILD_ARMATURE not in src.objects:
            raise SystemExit("No %s in %s" % (BUILD_ARMATURE, MANNEQUIN))
        dst.objects = [BUILD_ARMATURE]
    arm = bpy.data.objects[BUILD_ARMATURE]
    library = arm.library
    # matrix_basis, not matrix_world: a linked object that is in no scene is never
    # evaluated, so its matrix_world is identity and every bone comes back in the
    # armature's raw centimetre, Y-up space. The armature has no parent, so its
    # basis IS its world transform.
    world = arm.matrix_basis
    shift = -(world @ arm.data.bones[BONE + "Hips"].head_local).x
    offset = Vector((shift, 0.0, 0.0))
    joints = {b.name[len(BONE):]: (world @ b.head_local + offset, world @ b.tail_local + offset)
              for b in arm.data.bones}
    data = arm.data
    bpy.data.objects.remove(arm)
    bpy.data.armatures.remove(data)
    bpy.data.libraries.remove(library)
    return joints


def bind(obj, bone):
    """Record the bone this piece rides, by its short name (`Spine2`, `LeftLeg`)."""
    obj[BIND] = BONE + bone
    return obj


def ellipse(centre, rx, ry, angle_deg):
    """A point on a horizontal ellipse. 0 degrees is +X, 90 is +Y (the back), 270 is -Y (the front)."""
    a = math.radians(angle_deg)
    return Vector((centre.x + rx * math.cos(a), centre.y + ry * math.sin(a), centre.z))


def arc(centre, rx, ry, a0, a1, count):
    return [ellipse(centre, rx, ry, a0 + (a1 - a0) * i / (count - 1)) for i in range(count)]


def lerp(a, b, t):
    return a + (b - a) * t
