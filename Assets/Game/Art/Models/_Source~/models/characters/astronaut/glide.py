"""Author "Glide" -- the astronaut flying under a wingsuit -- and export it as an FBX clip.

    blender --background astronaut.blend --python glide.py
    blender --background astronaut.blend --python glide.py -- --preview out.png

Like `sit_idle.py`, whose posing helpers this imports, it NEVER writes back to the
.blend. The .blend is the source of truth; this file is the source of truth for
the pose.

The pose is prone and spread: the body pitched face-down onto the flight path,
arms out and swept back so the membranes running arm-to-hip are held open, legs
together and trailing. It plays full-body on its own animator layer while the
wings are out, so it owns the legs as well as the arms -- which is the whole
reason it is a clip rather than something laid on the Upper Body mask.

**The clip carries the constant part of the attitude only.** How far the body is
tipped relative to the horizon, and how far it banks into a turn, are not in here:
`WingsuitPose` measures those off the body's own motion at runtime and lays them
on the hips, on every machine. A clip cannot hold them because they change every
frame, and baking a fixed dive angle in would fight the one that is measured.

Three things are load-bearing:

  * **Armature space is Y-up, +Z forward, +X the character's LEFT, in centimetres**
    -- the same frame `sit_idle.py` documents, and the reason its helpers are
    imported rather than reimplemented.

  * **After the hips pitch, the body's own axes have moved.** Pitching the hips
    +78 degrees about X carries the whole skeleton with it: the body's up axis
    ends up pointing along +Z (head forward), its face along -Y (looking down).
    So "arms out to the sides" is still +/-X, but "swept back toward the feet" is
    -Z, and every limb below is aimed in ARMATURE space with that already true.
    Composing deltas in the body's frame instead is how a pose like this quietly
    comes apart.

  * **Rotation is keyed; location is not.** `sit_idle` bakes a hip drop as a
    translation and needs `lockRootHeightY` in its .meta to stop the player's
    Animator standing the character back up. This clip needs no translation at
    all -- the body pivots about the hips, which is where it hangs from anyway --
    so not keying location leaves the height to whatever the layer underneath is
    doing and sidesteps the whole trap.
"""

import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from sit_idle import (B, POSED, aim, armature, export as export_clip,  # noqa: E402
                      limb_dir, preview as preview_clip, rest_pose, rotate, sync)
import sit_idle  # noqa: E402

REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

ACTION = "Glide"
FPS = 30
LOOP_FRAMES = 90                  # 3.0 s; frame LOOP_FRAMES+1 repeats frame 1 exactly.

# ─────────── Pose (degrees, armature space) ───────────

HIPS_PITCH = 78.0        # face-down onto the flight path. Not 90: a wingsuit pilot
                         # flies slightly head-up of their own body line, and a
                         # perfectly flat body reads as a corpse falling.

# The spine arches BACK out of the hips' pitch, which is what a flyer's braced
# torso actually does -- and it is what lifts the head far enough to see forward.
SPINE_ARCH = -7.0
SPINE1_ARCH = -8.0
SPINE2_ARCH = -6.0
NECK_ARCH = -18.0
HEAD_ARCH = -20.0        # gaze along the flight path rather than at the ground

# Arms. Aimed directly rather than through limb_dir, because limb_dir's `forward`
# collapses to nothing at `out` = 90 -- exactly where an arm held out sideways
# lives -- and a wing needs BOTH the sideways spread and the sweep back.
ARM_SWEEP = 24.0         # back toward the feet, from straight out sideways
ARM_DIHEDRAL = 7.0       # and a touch above the body's plane, as a wing sits
FOREARM_SWEEP = 30.0     # the forearm carries on back, opening the membrane
FOREARM_DIHEDRAL = 2.0
HAND_PITCH = -12.0       # wrists flat into the airflow

SHOULDER_SPREAD = 9.0    # collarbones opened, so the chest is wide under the wing

# Legs together and trailing, with the knees barely off straight. A wingsuit's leg
# wing needs them closed; splayed legs read as falling.
LEG_TRAIL = 6.0          # how far above the body's own line the feet ride
LEG_SPLAY = 3.5          # just enough that the boots do not intersect
KNEE_BEND = 8.0
FOOT_POINT = 22.0        # toes pointed, which is most of what sells a glide

# ─────────── Flight motion ───────────
# GDC-L1-ANIM-0005: the life is in the secondary motion. Here it is buffet rather
# than breath -- a body in a 23 m/s airflow is being shaken, not resting. Kept
# small: a pose that visibly flaps stops reading as a rigid wing. All cycle counts
# are INTEGERS, which is what makes frame LOOP_FRAMES+1 land exactly on frame 1.
BUFFET_CYCLES = 4        # fast tremor through the arms
BUFFET_ARM = 1.1         # degrees
BUFFET_HAND = 2.0
SWELL_CYCLES = 1         # one slow rise and fall of the whole body per loop
SWELL_SPINE = 1.6
SWELL_LEG = 2.2

DST = os.path.join(REPO, "Assets", "Game", "Art", "Animations", "Player", "Glide.fbx")


def wing_dir(sign, sweep, dihedral):
    """An armature-space direction for a limb held out sideways as a wing.

    `sweep` swings the limb back toward the feet from straight out; `dihedral`
    lifts it above the body's plane. Written out rather than reusing
    `limb_dir`, whose `forward` term is multiplied by cos(out) and therefore
    vanishes at the one place a wing pose needs it.

    Remember the frame: the hips have already pitched, so -Z is toward the feet
    and +Y is the direction the flyer's back faces.
    """
    s = math.radians(sweep)
    d = math.radians(dihedral)
    return (sign * math.cos(s) * math.cos(d),
            math.sin(d),
            -math.sin(s) * math.cos(d))


def leg_dir(sign, trail, splay):
    """Trailing behind the prone body, feet a little high."""
    t = math.radians(trail)
    o = math.radians(splay)
    return (sign * math.sin(o),
            math.sin(t) * math.cos(o),
            -math.cos(t) * math.cos(o))


def apply_base_pose(arm):
    """The glide pose itself, parents before children."""
    rest_pose(arm)

    rotate(arm, "Hips", HIPS_PITCH, 'X')

    rotate(arm, "Spine", SPINE_ARCH, 'X')
    rotate(arm, "Spine1", SPINE1_ARCH, 'X')
    rotate(arm, "Spine2", SPINE2_ARCH, 'X')
    rotate(arm, "Neck", NECK_ARCH, 'X')
    rotate(arm, "Head", HEAD_ARCH, 'X')

    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        rotate(arm, side + "Shoulder", sign * SHOULDER_SPREAD, 'Y')
        aim(arm, side + "Arm", wing_dir(sign, ARM_SWEEP, ARM_DIHEDRAL))
        aim(arm, side + "ForeArm", wing_dir(sign, FOREARM_SWEEP, FOREARM_DIHEDRAL))
        rotate(arm, side + "Hand", HAND_PITCH, 'X')

    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        aim(arm, side + "UpLeg", leg_dir(sign, LEG_TRAIL, LEG_SPLAY))
        aim(arm, side + "Leg", leg_dir(sign, LEG_TRAIL - KNEE_BEND, LEG_SPLAY))
        rotate(arm, side + "Foot", FOOT_POINT, 'X')


def apply_buffet(arm, t):
    """Airflow, layered on the base pose. `t` runs 0..1 over the loop."""
    buffet = math.sin(2.0 * math.pi * BUFFET_CYCLES * t)
    swell = math.sin(2.0 * math.pi * SWELL_CYCLES * t)

    rotate(arm, "Spine1", SWELL_SPINE * swell, 'X')

    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        # Opposed sign across the body, so the tremor reads as air moving over the
        # wing rather than as the whole character shivering in one piece.
        rotate(arm, side + "Arm", sign * BUFFET_ARM * buffet, 'Z')
        rotate(arm, side + "Hand", BUFFET_HAND * buffet, 'X')
        rotate(arm, side + "ForeArm", -sign * BUFFET_ARM * 0.6 * buffet, 'Z')
        rotate(arm, side + "UpLeg", SWELL_LEG * swell, 'X')


def build_action(arm):
    arm.animation_data_create()
    for old in [a for a in bpy.data.actions if a.name == ACTION]:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(ACTION)
    arm.animation_data.action = action

    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.frame_start = 1
    scene.frame_end = LOOP_FRAMES + 1

    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'

    for frame in range(1, LOOP_FRAMES + 2):
        t = (frame - 1) / float(LOOP_FRAMES)
        apply_base_pose(arm)
        apply_buffet(arm, t)

        # Rotation only. See the module docstring: no translation means no root
        # height for the player's Animator to argue with.
        for name in POSED:
            arm.pose.bones[B % name].keyframe_insert("rotation_quaternion", frame=frame)

    return action


def report(arm):
    """Where the posed joints land, in armature-space centimetres.

    A render answers "does this read as flying"; it does not answer "is the wrist
    below the hip", and on a wing that runs arm-to-hip that is the measurement
    that decides whether the membrane has anything to span.
    """
    bpy.context.scene.frame_set(1)
    sync()

    for name in ("Hips", "Spine2", "Head", "LeftArm", "LeftForeArm", "LeftHand",
                 "LeftUpLeg", "LeftFoot"):
        key = B % name
        if key not in arm.pose.bones:
            continue
        p = arm.pose.bones[key].matrix.to_translation()
        print("  %-12s x %7.1f  y %7.1f  z %7.1f" % (name, p.x, p.y, p.z))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    arm = armature()
    build_action(arm)
    report(arm)

    if "--preview" in argv:
        # sit_idle's preview frames a seated character; the glide is prone and
        # wider than it is tall, so it needs its own framing rather than that one.
        preview(argv[argv.index("--preview") + 1])
        return

    sit_idle.DST = DST
    export_clip(arm)


def preview(path):
    """Render the posed astronaut from the side, front and above."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 800
    scene.render.film_transparent = False
    scene.frame_set(1)

    light_data = bpy.data.lights.new("GlideKey", type='SUN')
    light_data.energy = 4.0
    light = bpy.data.objects.new("GlideKey", light_data)
    scene.collection.objects.link(light)
    light.rotation_euler = (math.radians(50), 0, math.radians(35))

    cam_data = bpy.data.cameras.new("GlideCam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 4.6
    cam = bpy.data.objects.new("GlideCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    focus_z = 1.15
    views = {
        "side": ((6.0, 0.0, focus_z), (math.radians(90), 0.0, math.radians(90))),
        "front": ((0.0, -6.0, focus_z), (math.radians(90), 0.0, 0.0)),
        "above": ((0.0, 0.0, 6.0), (0.0, 0.0, 0.0)),
    }

    base, ext = os.path.splitext(path)
    for name, (loc, rot) in views.items():
        cam.location = loc
        cam.rotation_euler = rot
        scene.render.filepath = "%s_%s%s" % (base, name, ext or ".png")
        bpy.ops.render.render(write_still=True)
        print("PREVIEW", scene.render.filepath)


main()
