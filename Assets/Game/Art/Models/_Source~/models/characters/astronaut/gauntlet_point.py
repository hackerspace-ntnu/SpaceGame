"""Author the "Point" poses -- an arm extended at what the player is looking at -- and
export them as FBX clips for the Upper Body layer.

    blender --background astronaut.blend --python gauntlet_point.py
    blender --background astronaut.blend --python gauntlet_point.py -- --preview out.png

Six static clips come out of this, one per (arm, pitch):

    Point Right {Down,Level,Up}.fbx   right arm only; the left hangs at rest
    Point Both  {Down,Level,Up}.fbx   both arms

The Upper Body layer blends the three pitches on an `AimPitch` float (-45 .. 45,
the player's look pitch) so the forearm follows the crosshair up and down, and
plays the Right clips MIRRORED for the left arm -- there is no Left set, because a
Humanoid clip mirrors for free and a second set would only drift from the first.

Why a clip and not IK: the Upper Body layer sits in an EMPTY state whenever the
hands are empty, and Unity applies IK goals through the layer they are set on --
so an IK raise showed nothing for a player wearing gauntlets and holding nothing,
which is the ordinary case. A clip gives the layer something to play. (An IK raise
was also tried and rejected on look, Aug 2026 -- see the hold-pose notes.)

Same conventions as `sit_idle.py`, whose helpers this imports: armature space is
Y-up, +Z forward, +X the character's LEFT, in centimetres; the .blend is never
written back to.
"""

import math
import os
import sys

import bpy
import mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import sit_idle  # noqa: E402  (posing helpers; its main() is guarded)

CLIP_DIR = os.path.join(sit_idle.REPO, "Assets", "Game", "Art", "Animations", "Player")
FPS = 30
FRAMES = 2                 # a pose, not a motion: two identical frames

B = sit_idle.B

# ─────────── The pose (degrees) ───────────
# `pitch` is the FOREARM's elevation above horizontal. The gauntlet sits on the
# forearm and points along it, so this is the number that has to agree with the
# look ray. The eye is 0.42 m above the shoulder on this character (helmet), so at
# Level the arm points a little up rather than dead level: a target a few metres out
# on the eye line is above the shoulder, and the forearm has to rise to meet it.
PITCHES = {
    "Down": -35.0,
    "Level": 12.0,
    "Up": 55.0,
}

ARM_OUT = 8.0            # the whole arm a little outboard of the shoulder, so the
                         # elbow is not pinned to the ribs
ELBOW_DROP = 7.0         # upper arm this far BELOW the forearm line: a slight bend,
                         # because a dead-straight arm reads as a mannequin
SHOULDER_FWD = 6.0       # collarbone brought forward with the reach

# Bones this clip drives. Fingers stay at rest so the hand shape is the rig's.
ARM_BONES = ["Shoulder", "Arm", "ForeArm", "Hand"]


def point_arm(arm, side, sign, pitch):
    """Extend one arm along the forearm line at `pitch` above horizontal."""
    sit_idle.rotate(arm, side + "Shoulder", -SHOULDER_FWD, 'X')
    # limb_dir: `forward` 0 is straight down, 90 straight ahead, past 90 above horizontal.
    sit_idle.aim(arm, side + "Arm", sit_idle.limb_dir(sign, 90.0 + pitch - ELBOW_DROP, ARM_OUT + 3.0))
    sit_idle.aim(arm, side + "ForeArm", sit_idle.limb_dir(sign, 90.0 + pitch, ARM_OUT))
    # The hand continues the forearm, so a device seated on the hand bone points where
    # the forearm does instead of kinking at the wrist.
    sit_idle.aim(arm, side + "Hand", sit_idle.limb_dir(sign, 90.0 + pitch, ARM_OUT))


def apply_pose(arm, arms, pitch):
    sit_idle.rest_pose(arm)
    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        if side in arms:
            point_arm(arm, side, sign, pitch)


def posed_bones(arms):
    names = ["Spine", "Spine1", "Spine2"]
    for side in arms:
        names += [side + b for b in ARM_BONES]
    return names


def build_action(arm, name, arms, pitch):
    arm.animation_data_create()
    for old in [a for a in bpy.data.actions if a.name == name]:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    arm.animation_data.action = action

    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.frame_start = 1
    scene.frame_end = FRAMES

    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'

    for frame in range(1, FRAMES + 1):
        apply_pose(arm, arms, pitch)
        # Both arms are keyed even for a one-arm clip: the Upper Body mask covers both,
        # and a bone with no curve would be left at whatever the previous state put it.
        for bone in posed_bones(("Left", "Right")):
            arm.pose.bones[B % bone].keyframe_insert("rotation_quaternion", frame=frame)

    return action


def report(arm, label):
    scene = bpy.context.scene
    scene.frame_set(1)
    sit_idle.sync()
    head = arm.pose.bones[B % "Head"].matrix.to_translation()
    for name in ("RightArm", "RightForeArm", "RightHand"):
        p = arm.pose.bones[B % name].matrix.to_translation()
        print("  %-8s %-13s x %7.1f  y %7.1f  z %7.1f  (below head %6.1f)" %
              (label, name, p.x, p.y, p.z, head.y - p.y))
    shoulder = arm.pose.bones[B % "RightForeArm"].matrix.to_translation()
    hand = arm.pose.bones[B % "RightHand"].matrix.to_translation()
    d = hand - shoulder
    print("  %-8s forearm elevation %.1f deg" % (
        label, math.degrees(math.atan2(d.y, math.hypot(d.x, d.z)))))


def export(arm, path):
    for obj in bpy.data.objects:
        obj.select_set(obj is arm)
    bpy.context.view_layer.objects.active = arm

    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={'ARMATURE'},
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        armature_nodetype='NULL',
        bake_space_transform=False,
        path_mode='COPY',
    )
    print("Wrote %s (%.0f KB)" % (path, os.path.getsize(path) / 1e3))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    arm = sit_idle.armature()

    if "--preview" in argv:
        base = argv[argv.index("--preview") + 1]
        root, ext = os.path.splitext(base)
        for pitch_name, pitch in PITCHES.items():
            build_action(arm, "Point Right " + pitch_name, ("Right",), pitch)
            report(arm, pitch_name)
            sit_idle.preview("%s_%s%s" % (root, pitch_name.lower(), ext or ".png"), 0.0)
        return

    for arms_name, arms in (("Right", ("Right",)), ("Both", ("Left", "Right"))):
        for pitch_name, pitch in PITCHES.items():
            name = "Point %s %s" % (arms_name, pitch_name)
            build_action(arm, name, arms, pitch)
            if arms_name == "Right":
                report(arm, pitch_name)
            export(arm, os.path.join(CLIP_DIR, name + ".fbx"))


if __name__ == "__main__":
    main()
