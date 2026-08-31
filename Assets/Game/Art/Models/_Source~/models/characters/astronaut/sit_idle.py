"""Author "Sit Idle" -- the astronaut's seated idle -- and export it as an FBX clip.

    blender --background astronaut.blend --python sit_idle.py
    blender --background astronaut.blend --python sit_idle.py -- --preview out.png

Like `astronaut_export.py` this NEVER writes back to the .blend. The .blend is the
source of truth; this file is the source of truth for the pose.

Why the pose is built from numbers rather than hand-keyed: there is no GUI in this
workflow, so every angle here was rendered, looked at, and adjusted. Keeping them as
named constants is what makes the next adjustment a one-line change instead of a
re-authoring session.

Three things are load-bearing:

  * **Armature space is Y-up, +Z forward, +X the character's LEFT, in centimetres.**
    The armature object sits at scale 0.01 with a rotation that carries local Y to
    world Z, so every number below is a centimetre or a degree in the rig's own
    frame, not Blender's. Measured off the rest pose, not assumed.

  * **A chair pose stays in the sagittal plane.** Both legs go forward together with
    no abduction, which is exactly what separates this from `MountedRiderPose`'s
    saddle (where the thighs wrap a barrel and the maths needs the limb's own frame).
    Here plain pitch deltas about armature X are enough for the whole lower body.

  * **The hips have to come DOWN 0.465 m.** Rotating the legs alone leaves the
    astronaut sitting in mid-air with its feet 0.465 m off the deck -- bone rotations
    do not move the pelvis. That drop is the clip's only translation, and on import it
    must be baked into the pose rather than left as root motion (see the .meta:
    `lockRootHeightY: 1`). With it left as root motion the player's Animator, which
    does not apply root motion, silently stands the astronaut back up.

Geometry behind SIT_DROP, all measured off the rest pose in this file:

    thigh 55.1 cm, shin 45.4 cm, ankle 19.2 cm above the sole, hip joint 119.1 cm up.
    Seated: thigh 10 degrees below horizontal puts the knee 9.6 cm below the hip; the
    shin hangs vertical; the sole lands 45.4 + 19.2 below the knee. So the hip joint
    sits 9.6 + 45.4 + 19.2 = 74.2... solved exactly in solve_sit_drop() rather than
    by hand, so a change to any angle re-solves instead of drifting out of agreement.
"""

import math
import os
import sys

import bpy
import mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

DST = os.path.join(REPO, "Assets", "Game", "Art", "Animations", "Player", "Sit Idle.fbx")

ACTION = "Sit Idle"
FPS = 30
LOOP_FRAMES = 180                 # 6.0 s; frame LOOP_FRAMES+1 repeats frame 1 exactly.

B = "mixamorig:%s"

# ─────────── Pose (degrees, armature space) ───────────
# Rotation about +X is the sagittal pitch. On a limb pointing DOWN, a NEGATIVE angle
# swings it FORWARD; on the spine, which points UP, a negative angle leans it BACK.
# Verified against the rest pose rather than guessed -- the sign flips with the
# limb's rest direction, which is exactly the trap MountedRiderPose documents.

HIPS_PITCH = -5.0        # pelvis rolled back into the seat back
THIGH_FROM_VERTICAL = 80.0   # 80 leaves the thigh 10 degrees below horizontal
FOOT_PITCH = 4.0         # a relaxed foot, not a plank

SPINE_PITCH = -3.0       # ~8 degrees of recline spread over three joints, so the
SPINE1_PITCH = -3.0      # back curves instead of hinging at one place
SPINE2_PITCH = -2.0
NECK_PITCH = 3.0
HEAD_PITCH = 4.0         # counter-pitch, so the recline does not leave them stargazing

SHOULDER_PITCH = -4.0    # collarbone forward a touch; the arms hang off a leaning chest
ARM_DOWN = 52.0          # roll about +Z, mirrored: brings the A-pose arm down the side
ARM_PITCH = -14.0        # and forward, toward the lap
ELBOW = -58.0            # forearms in across the lap
FOREARM_YAW = 10.0       # mirrored, so the hands meet rather than run parallel
HAND_PITCH = -8.0

# ─────────── Idle motion ───────────
# GDC-L1-ANIM-0005: the life is in the secondary motion. Deliberately tiny -- a
# seated idle that visibly drifts reads worse than one that is still, and the body
# must not wander off the cushion. Both cycle counts are INTEGERS, which is what
# makes frame LOOP_FRAMES+1 land exactly back on frame 1.
BREATH_CYCLES = 2        # two breaths per 6 s loop
BREATH_CHEST = 0.9       # degrees of chest rise
BREATH_SHOULDER = 0.7
SHIFT_CYCLES = 1         # one slow weight shift per loop
SHIFT_ROLL = 1.3         # degrees of side-to-side lean
SHIFT_HEAD_YAW = 2.0

# Bones this clip drives. Everything else -- fingers above all -- is left at rest so
# a hold pose on the Upper Body mask layer still owns the hands.
POSED = [
    "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head",
    "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
    "RightShoulder", "RightArm", "RightForeArm", "RightHand",
    "LeftUpLeg", "LeftLeg", "LeftFoot",
    "RightUpLeg", "RightLeg", "RightFoot",
]


def armature():
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    if len(arms) != 1:
        raise SystemExit("Expected exactly one armature, found %d" % len(arms))
    return arms[0]


def sync():
    """Flush the pose so the next bone reads a parent that has already moved.

    Parents before children, always -- the same rule MountedRiderPose spells out.
    Each delta is composed onto the armature-space matrix the bone has ALREADY been
    given, so reclining the chest carries the arms with it; without this flush every
    child would compose onto a stale parent and the pose would come apart.
    """
    bpy.context.view_layer.update()


def rotate(arm, name, deg, axis):
    """Rotate one bone by `deg` about an armature-space axis, pinning its head."""
    pb = arm.pose.bones[B % name]
    sync()
    m = pb.matrix.copy()
    head = m.to_translation()
    r = mathutils.Matrix.Rotation(math.radians(deg), 4, axis)
    out = (r.to_3x3() @ m.to_3x3()).to_4x4()
    out.translation = head
    pb.matrix = out
    sync()


def translate(arm, name, offset):
    pb = arm.pose.bones[B % name]
    sync()
    m = pb.matrix.copy()
    m.translation = m.to_translation() + mathutils.Vector(offset)
    pb.matrix = m
    sync()


def rest_pose(arm):
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    sync()


def sole_height(arm):
    """Lowest sole in armature space, from the ankle bones and a measured offset.

    Read off the toe bones rather than the mesh: the boot mesh is skinned to them, so
    the bones are what actually moves, and a bounding box would drag the whole
    silhouette (scarf included) into a measurement about feet.
    """
    lo = 1e9
    for side in ("Left", "Right"):
        for bone in ("Foot", "ToeBase", "Toe_End"):
            key = B % (side + bone)
            if key not in arm.pose.bones:
                continue
            pb = arm.pose.bones[key]
            lo = min(lo, pb.matrix.to_translation().y, (pb.matrix @ mathutils.Vector(
                (0.0, pb.bone.length, 0.0))).y)
    return lo


def apply_base_pose(arm):
    """The seated pose itself, parents first."""
    rest_pose(arm)

    rotate(arm, "Hips", HIPS_PITCH, 'X')

    # Thigh: swung forward to an ABSOLUTE angle, so the pelvis tilt above does not
    # quietly change how deep the sit is.
    thigh = -THIGH_FROM_VERTICAL - HIPS_PITCH
    for side in ("Left", "Right"):
        rotate(arm, side + "UpLeg", thigh, 'X')
        # Shin back to vertical. The thigh carried it forward by exactly
        # THIGH_FROM_VERTICAL, so undoing that leaves it hanging plumb -- which is
        # what a knee at ~80 degrees over the front edge of a seat actually looks like.
        rotate(arm, side + "Leg", THIGH_FROM_VERTICAL, 'X')
        rotate(arm, side + "Foot", FOOT_PITCH, 'X')

    rotate(arm, "Spine", SPINE_PITCH, 'X')
    rotate(arm, "Spine1", SPINE1_PITCH, 'X')
    rotate(arm, "Spine2", SPINE2_PITCH, 'X')
    rotate(arm, "Neck", NECK_PITCH, 'X')
    rotate(arm, "Head", HEAD_PITCH, 'X')

    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        rotate(arm, side + "Shoulder", SHOULDER_PITCH, 'X')
        rotate(arm, side + "Arm", -sign * ARM_DOWN, 'Z')
        rotate(arm, side + "Arm", ARM_PITCH, 'X')
        rotate(arm, side + "ForeArm", ELBOW, 'X')
        rotate(arm, side + "ForeArm", sign * FOREARM_YAW, 'Y')
        rotate(arm, side + "Hand", HAND_PITCH, 'X')


def solve_sit_drop(arm):
    """How far the pelvis must fall for the soles to reach the floor plane.

    Solved from the posed skeleton rather than written down, so changing a knee or
    ankle angle re-solves the drop instead of leaving the astronaut hovering.
    """
    apply_base_pose(arm)
    return sole_height(arm)


def apply_idle(arm, t):
    """Breathing and weight shift, layered on the base pose. `t` runs 0..1 over the loop."""
    breath = math.sin(2.0 * math.pi * BREATH_CYCLES * t)
    shift = math.sin(2.0 * math.pi * SHIFT_CYCLES * t)

    rotate(arm, "Spine1", BREATH_CHEST * breath, 'X')
    rotate(arm, "Spine2", BREATH_CHEST * 0.6 * breath, 'X')
    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        rotate(arm, side + "Shoulder", -sign * BREATH_SHOULDER * breath, 'Z')

    rotate(arm, "Spine", SHIFT_ROLL * shift, 'Z')
    rotate(arm, "Head", -SHIFT_ROLL * 0.5 * shift, 'Z')
    rotate(arm, "Head", SHIFT_HEAD_YAW * shift, 'Y')


def build_action(arm, drop):
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

    # frame LOOP_FRAMES+1 evaluates t=1, which is the same phase as t=0, so the loop
    # closes exactly rather than nearly.
    for frame in range(1, LOOP_FRAMES + 2):
        t = (frame - 1) / float(LOOP_FRAMES)
        apply_base_pose(arm)
        translate(arm, "Hips", (0.0, -drop, 0.0))
        apply_idle(arm, t)

        for name in POSED:
            pb = arm.pose.bones[B % name]
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("location", frame=frame)

    return action


def preview(path, drop):
    """Render the posed astronaut from the side and the front, side by side.

    The point of this script having a preview at all: posing a humanoid by numbers
    with no viewport is guesswork until you look at it.
    """
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1000
    scene.render.film_transparent = False
    scene.frame_set(1)

    light_data = bpy.data.lights.new("SitKey", type='SUN')
    light_data.energy = 4.0
    light = bpy.data.objects.new("SitKey", light_data)
    scene.collection.objects.link(light)
    light.rotation_euler = (math.radians(55), 0, math.radians(35))

    cam_data = bpy.data.cameras.new("SitCam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 4.2
    cam = bpy.data.objects.new("SitCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    # Chest height in WORLD metres: armature Y (cm) maps to world Z at scale 0.01.
    focus_z = (128.677 - drop) * 0.01

    views = {
        "side": ((6.0, 0.0, focus_z), (math.radians(90), 0.0, math.radians(90))),
        "front": ((0.0, -6.0, focus_z), (math.radians(90), 0.0, 0.0)),
        "three_quarter": ((4.5, -4.5, focus_z + 0.6),
                          (math.radians(80), 0.0, math.radians(45))),
    }
    base, ext = os.path.splitext(path)
    for name, (loc, rot) in views.items():
        cam.location = loc
        cam.rotation_euler = rot
        scene.render.filepath = "%s_%s%s" % (base, name, ext or ".png")
        bpy.ops.render.render(write_still=True)
        print("PREVIEW", scene.render.filepath)


def export(arm):
    for obj in bpy.data.objects:
        obj.select_set(obj is arm)
    bpy.context.view_layer.objects.active = arm

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=True,
        object_types={'ARMATURE'},
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        # Leaf bones OFF for the same reason astronaut_export.py keeps them off: the
        # `_end` tips are already real bones in the .blend, and adding another
        # generation shifts the skeleton the Humanoid mapping is built against.
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
    print("Wrote %s (%.0f KB)" % (DST, os.path.getsize(DST) / 1e3))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    arm = armature()
    drop = solve_sit_drop(arm)
    print("Sit drop: %.2f cm (%.3f m)" % (drop, drop * 0.01))

    build_action(arm, drop)

    if "--preview" in argv:
        preview(argv[argv.index("--preview") + 1], drop)
        return

    export(arm)
    # Deliberately no save_mainfile: the .blend is the source of truth.


main()
