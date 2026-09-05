"""Author the player's "pet a creature" gesture and export it as its own FBX.

    blender --background --python astronaut_pet.py

Reads `AstronautArmature.fbx` and never writes to it. The output is an
animation-only FBX -- armature and one take, no meshes -- which is the same
shape as the mocap clips already in `Assets/Game/Art/Animations/Player/`.

## Why a script and not mocap

Everything else the player does came from a mocap pack, and none of it is a
reach-and-stroke. This is 65 Mixamo bones posed by hand over 2.5 seconds; it
only has to read as "reaching up to touch something big" through an upper-body
mask while the legs keep doing whatever they were doing.

## Bone axes, again

Same trap as `appa_anim.py`: a pose bone rotates about its own local axes, which
on a Mixamo rig are wherever the retarget left them, so keying a raw euler
component animates something unrelated. Every rotation below is authored about a
**world** axis and converted through the bone's rest matrix.

Measured on this rig, +25 deg about each world axis moves the tip:

    RightArm      X (0.00 +0.15 +0.04)   Y (+0.03 -0.15 0.00)   Z (+0.19 0.00 -0.12)
    RightForeArm  X (0.00 +0.10 -0.07)   Y (+0.12 -0.12 0.00)   Z (+0.11 0.00 -0.12)
    Spine1        X (0.00 -0.10 -0.02)   Y (0.00 0.00 0.00)     Z (-0.10 0.00 -0.02)

so for this rig **-X reaches the arm forward, -Z lifts it**, and +X leans the
spine forward. Note Spine1 barely responds to Y at all -- that is its own length
axis, and keying it would have been the invisible-twist bug all over again.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))

REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(
        os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

SRC = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Characters",
                   "Astronaut", "AstronautArmature.fbx")
DST = os.path.join(REPO, "Assets", "Game", "Art", "Animations", "Player",
                   "PetCreature.fbx")

ACTION = "PetCreature"
FRAMES = 60          # 2.5 s at 24 fps
FPS = 24

B = "mixamorig:"


def local_axis(pb, world_axis):
    basis = pb.bone.matrix_local.to_3x3()
    return (basis.inverted() @ Vector(world_axis)).normalized()


def pose(pb, x=0.0, y=0.0, z=0.0):
    """Rotate by world-space angles, in radians. See the module docstring."""
    m = Matrix.Identity(3)
    if z:
        m = Matrix.Rotation(z, 3, local_axis(pb, (0.0, 0.0, 1.0))) @ m
    if y:
        m = Matrix.Rotation(y, 3, local_axis(pb, (0.0, 1.0, 0.0))) @ m
    if x:
        m = Matrix.Rotation(x, 3, local_axis(pb, (1.0, 0.0, 0.0))) @ m
    pb.rotation_euler = m.to_euler('XYZ')


def d(deg):
    return math.radians(deg)


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def ramp(u, a, b):
    if u <= a:
        return 0.0
    if u >= b:
        return 1.0
    return smoothstep((u - a) / (b - a))


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No astronaut at %s" % SRC)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)

    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')

    # Animation-only, like every mocap clip beside it. The meshes would be a
    # second copy of the astronaut in the project for no reason.
    for obj in [o for o in bpy.data.objects if o.type != 'ARMATURE']:
        bpy.data.objects.remove(obj, do_unlink=True)

    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (0.0, 0.0, 0.0)

    act = bpy.data.actions.new(ACTION)
    act.use_fake_user = True
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = act

    bpy.context.scene.render.fps = FPS
    # The exporter bakes the SCENE range, not the action's. Left at Blender's
    # default 1..250 the FBX carried a 251-frame take with the gesture buried in
    # its first 60 frames and 190 frames of held pose after it.
    bpy.context.scene.frame_start = 0
    bpy.context.scene.frame_end = FRAMES

    keyed = [B + n for n in ("RightShoulder", "RightArm", "RightForeArm", "RightHand",
                             "Spine", "Spine1", "Spine2", "Neck", "Head")]

    for f in range(FRAMES + 1):
        u = f / float(FRAMES)

        # reach out, two strokes, withdraw
        reach = ramp(u, 0.00, 0.30) * (1.0 - ramp(u, 0.80, 1.00))
        stroke = math.sin(u * 2.0 * math.pi * 2.0) * ramp(u, 0.28, 0.38) \
            * (1.0 - ramp(u, 0.74, 0.86))

        pose(arm.pose.bones[B + "Spine"], x=reach * d(4.0))
        pose(arm.pose.bones[B + "Spine1"], x=reach * d(5.0))
        pose(arm.pose.bones[B + "Spine2"], x=reach * d(4.0))
        # Looks up at the animal's head rather than at its own hand.
        pose(arm.pose.bones[B + "Neck"], x=-reach * d(7.0))
        pose(arm.pose.bones[B + "Head"], x=-reach * d(9.0))

        pose(arm.pose.bones[B + "RightShoulder"], z=-reach * d(10.0))
        # -X forward, -Z up. The stroke rides on the lift, so the hand travels
        # up and down the animal's cheek rather than waving side to side.
        pose(arm.pose.bones[B + "RightArm"],
             x=-reach * d(58.0), z=-reach * d(28.0) - stroke * d(9.0))
        pose(arm.pose.bones[B + "RightForeArm"],
             x=-reach * d(34.0) - stroke * d(6.0))
        pose(arm.pose.bones[B + "RightHand"], x=-reach * d(12.0) + stroke * d(5.0))

        for name in keyed:
            arm.pose.bones[name].keyframe_insert("rotation_euler", frame=f)

    # Same guard appa_anim.py carries: a clip full of keys whose every curve is
    # flat imports as a static pose and nothing anywhere says so.
    moving = 0
    for fc in act.fcurves:
        values = [k.co[1] for k in fc.keyframe_points]
        if values and max(values) - min(values) > 1e-5:
            moving += 1
    if moving < 8:
        raise SystemExit("%s has only %d curves that change -- the gesture is not "
                         "animating." % (ACTION, moving))

    bpy.ops.object.mode_set(mode='OBJECT')
    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={'ARMATURE'},
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        # One take per action, named "<object>|<action>" -- so the take is
        # "Armature|PetCreature" and PlayerPetGestureBuilder can find it by name.
        # With this off the exporter emits a single take called "Scene", which is
        # what it did the first time and why the builder could not match it.
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
    )
    print("wrote %s (%d bones, %d curves moving)"
          % (DST, len(arm.data.bones), moving))


main()
