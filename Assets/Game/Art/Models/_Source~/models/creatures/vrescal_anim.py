"""Bind the Vrescal's sculpted body to its rig and author its movement clips.

Run after `vrescal_legs.py`, which builds the armature and binds the limbs.
This second pass does the two things the first deliberately left alone:

  1. **Binds the sculpted body** -- skull, jaw, eyes, neck spikes, the armour
     plate stack, the thorax and the tail keel -- to the spine, neck, head, jaw
     and tail bones. Rigid bone parenting, not weighted deformation: the armour
     is a stack of separate hard plates and skinning them would smear the
     overlaps. Nothing about the geometry changes, only who moves it.

  2. **Authors six actions**, which `vrescal_export.py` bakes into the FBX as
     separate takes for Unity to slice into clips:

       Vrescal_Idle    96f  loop   breathing, tail sway, occasional weight shift
       Vrescal_Walk    40f  loop   lateral-sequence crawl
       Vrescal_Run     26f  loop   diagonal couplets, longer stride, more bob
       Vrescal_Attack  34f  once   lunge and jaw snap
       Vrescal_Hurt    18f  once   flinch and drop
       Vrescal_Death   56f  once   legs splay, body settles onto the sand

## Why the gait is built the way it is

A sprawling reptile does not walk like a mammal. Two things carry the read and
everything else is detail:

  * **The limb swings in a mostly horizontal arc**, pivoting around a near-
    vertical axis at the shoulder or hip, rather than swinging fore-aft under
    the body. So protraction/retraction here is a rotation about world Z, and
    only the lift during swing phase is a rotation in the limb's own plane.

  * **The trunk undulates side to side**, and the wave travels back along the
    spine into the tail. Without it the animal reads as a table walking. The
    body bends *toward* the side whose forelimb is reaching, which is what
    lengthens the stride.

Footfall order is lateral sequence for the walk (hind, then fore, same side,
then the other side) and diagonal couplets for the run, which is the real gait
change crocodilians make when they stop crawling and start moving.

## The one compromise

`Mesh_Vrescal_TailKeel` is a single 6.6-unit sculpted piece spanning most of the
tail, so it can only be parented to one tail bone and cannot bend with the
chain. Tail amplitude is capped low enough that it does not visibly separate
from the plates. Splitting the keel per segment -- or skinning the tail -- is
the proper fix and is the author's call, since it means cutting hand-modelled
geometry.

    blender --background --python vrescal_anim.py
"""

import math
import os

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "vrescal.blend")

FPS = 30
ARM = "Arm_Vrescal"

# Which bone owns which stretch of the body, by the x span the bone covers.
# Read in order; the first whose range contains the mesh centroid wins.
BODY_SPANS = [
    ("Bone_Head", -0.60, 99.0),
    ("Bone_Neck", -2.00, -0.60),
    ("Bone_Spine_03", -4.20, -2.00),
    ("Bone_Spine_02", -6.50, -4.20),
    ("Bone_Spine_01", -8.80, -6.50),
    ("Bone_Tail_01", -10.60, -8.80),
    ("Bone_Tail_02", -12.40, -10.60),
    ("Bone_Tail_03", -14.20, -12.40),
    ("Bone_Tail_04", -15.90, -14.20),
    ("Bone_Tail_05", -99.0, -15.90),
]

# Meshes whose centroid lies in the wrong place for what they actually are.
BODY_OVERRIDES = {
    "Mesh_Vrescal_Jaw": "Bone_Jaw",
    # The keel spans four tail bones; anchor it near the base so the tip of the
    # tail swings past it rather than through it.
    "Mesh_Vrescal_TailKeel": "Bone_Tail_02",
}

LIMBS = ["FrontP", "FrontS", "RearP", "RearS"]
SIGN = {"FrontP": 1.0, "FrontS": -1.0, "RearP": 1.0, "RearS": -1.0}   # port = +y
FRONT = {"FrontP": True, "FrontS": True, "RearP": False, "RearS": False}

# Footfall phase within the stride, 0..1.
WALK_PHASE = {"RearP": 0.00, "FrontP": 0.25, "RearS": 0.50, "FrontS": 0.75}
RUN_PHASE = {"FrontP": 0.00, "RearS": 0.00, "FrontS": 0.50, "RearP": 0.50}

SPINE_CHAIN = ["Bone_Spine_01", "Bone_Spine_02", "Bone_Spine_03", "Bone_Neck"]
TAIL_CHAIN = ["Bone_Tail_0%d" % i for i in range(1, 6)]


# ---------------------------------------------------------------------------
# Binding
# ---------------------------------------------------------------------------

def centroid_x(obj):
    xs = [(obj.matrix_world @ v.co).x for v in obj.data.vertices]
    return (min(xs) + max(xs)) * 0.5


def bone_for(obj):
    if obj.name in BODY_OVERRIDES:
        return BODY_OVERRIDES[obj.name]
    x = centroid_x(obj)
    for name, lo, hi in BODY_SPANS:
        if lo <= x < hi:
            return name
    return "Bone_Spine_02"


def bind(obj, arm_obj, bone_name):
    """Rigid-parent an object to a bone without moving it.

    Bone parenting measures from the bone's tail, so the transform has to be
    resolved after the parent is set; assigning `matrix_world` back does that.
    """
    world = obj.matrix_world.copy()
    obj.parent = arm_obj
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    obj.matrix_world = world


def bind_body(arm_obj):
    bound = []
    for obj in bpy.data.objects:
        if obj.type != 'MESH' or obj.parent is not None:
            continue
        name = bone_for(obj)
        bind(obj, arm_obj, name)
        bound.append((obj.name, name))
    return bound


# ---------------------------------------------------------------------------
# Posing helpers
# ---------------------------------------------------------------------------

def world_axis_rot(pbone, axis, angle):
    """A local-space quaternion equal to rotating `angle` about a world axis.

    Lets the gait be written in terms the body actually has -- "swing the hip
    about vertical", "yaw the spine" -- instead of in whatever frame each bone's
    roll happened to land in. `matrix_local` is the bone's rest orientation in
    armature space, so conjugating by it moves a world rotation into the bone.
    """
    if abs(angle) < 1e-9:
        return Quaternion((1.0, 0.0, 0.0, 0.0))
    rest = pbone.bone.matrix_local.to_3x3()
    rot = Matrix.Rotation(angle, 3, axis)
    return (rest.inverted() @ rot @ rest).to_quaternion()


def local_axis_rot(axis, angle):
    """Rotation about one of the bone's own axes.

    Used for joint flexion. A knee or an elbow bends in the plane that contains
    the bone and the vertical, and a bone built head-to-tail with default roll
    has its local X perpendicular to exactly that plane -- so local X is the
    hinge, in any pose, without needing to know which way the limb points.
    """
    q = Quaternion((1.0, 0.0, 0.0, 0.0))
    if abs(angle) > 1e-9:
        q = Quaternion({'X': (1, 0, 0), 'Y': (0, 1, 0), 'Z': (0, 0, 1)}[axis],
                       angle)
    return q


def pose(pbone, *quats):
    pbone.rotation_mode = 'QUATERNION'
    q = Quaternion((1.0, 0.0, 0.0, 0.0))
    for extra in quats:
        q = q @ extra
    pbone.rotation_quaternion = q


def key(pbone, frame, loc=False):
    pbone.keyframe_insert("rotation_quaternion", frame=frame)
    if loc:
        pbone.keyframe_insert("location", frame=frame)


def new_action(arm_obj, name, length, loop):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True          # survives the save with no NLA strip
    arm_obj.animation_data.action = action
    action.frame_start, action.frame_end = 1, length
    action["loop"] = loop                # read back by the exporter's manifest
    return action


def rest(arm_obj):
    for pbone in arm_obj.pose.bones:
        pbone.rotation_mode = 'QUATERNION'
        pbone.rotation_quaternion = Quaternion((1.0, 0.0, 0.0, 0.0))
        pbone.location = (0.0, 0.0, 0.0)


# ---------------------------------------------------------------------------
# Gait
# ---------------------------------------------------------------------------

def limb_pose(arm_obj, limb, t, cfg):
    """Place one limb at normalised stride time `t` (0..1).

    Stance runs from 0 to `duty` with the limb sweeping from fully protracted to
    fully retracted at a constant rate -- it is planted, so it must track the
    ground. Swing takes it back the other way over the remainder, lifting on a
    sine so the foot clears.
    """
    duty = cfg["duty"]
    swing_amp = cfg["swing"] * (1.0 if FRONT[limb] else cfg["rear_swing"])
    side = SIGN[limb]

    if t < duty:                                  # planted
        u = t / duty
        protract = swing_amp * (1.0 - 2.0 * u)
        lift = 0.0
        crouch = math.sin(math.pi * u) * cfg["stance_crouch"]
    else:                                         # airborne
        u = (t - duty) / (1.0 - duty)
        protract = swing_amp * (2.0 * u - 1.0)
        lift = math.sin(math.pi * u) * cfg["lift"]
        crouch = 0.0

    upper = arm_obj.pose.bones["Bone_Limb%s_Upper" % limb]
    lower = arm_obj.pose.bones["Bone_Limb%s_Lower" % limb]
    foot = arm_obj.pose.bones["Bone_Limb%s_Foot" % limb]

    # Protraction is a yaw about vertical: a sprawling limb swings through a
    # near-horizontal arc, not fore-aft under the body.
    pose(upper,
         world_axis_rot(upper, 'Z', math.radians(protract) * side),
         local_axis_rot('X', math.radians(-lift - crouch * 0.4)))
    # The elbow and knee flex hardest just after the foot leaves the ground.
    pose(lower, local_axis_rot('X', math.radians(
        cfg["flex"] * (lift / max(cfg["lift"], 1e-6)) + crouch * 0.6)))
    # The foot stays flat while planted and toes down through the swing.
    pose(foot, local_axis_rot('X', math.radians(
        -cfg["ankle"] * (lift / max(cfg["lift"], 1e-6)))))


def body_wave(arm_obj, t, cfg):
    """Lateral undulation travelling from shoulder to tail tip.

    The phase lag down the chain is what makes it a wave rather than the whole
    animal wagging as one rigid piece; the amplitude ramp is what keeps the tail
    the loudest part of it.
    """
    for i, name in enumerate(SPINE_CHAIN):
        pbone = arm_obj.pose.bones[name]
        phase = t + cfg["wave_lag"] * i
        amp = cfg["yaw"] * (1.0 - 0.15 * i)
        pose(pbone,
             world_axis_rot(pbone, 'Z', math.radians(amp) * math.sin(
                 2.0 * math.pi * phase)),
             world_axis_rot(pbone, 'X', math.radians(cfg["roll"]) * math.sin(
                 2.0 * math.pi * phase + math.pi * 0.5)))
    for i, name in enumerate(TAIL_CHAIN):
        pbone = arm_obj.pose.bones[name]
        phase = t + cfg["wave_lag"] * (len(SPINE_CHAIN) + i)
        amp = cfg["tail_yaw"] * (0.5 + 0.16 * i)
        pose(pbone, world_axis_rot(pbone, 'Z', math.radians(amp) * math.sin(
            2.0 * math.pi * phase)))

    # Two bobs per stride: one for each diagonal pair landing.
    root = arm_obj.pose.bones["Bone_Root"]
    pose(root, world_axis_rot(root, 'Y', math.radians(cfg["pitch"]) * math.sin(
        4.0 * math.pi * t)))
    root.location = (0.0, 0.0, cfg["bob"] * math.sin(4.0 * math.pi * t))

    head = arm_obj.pose.bones["Bone_Head"]
    pose(head, world_axis_rot(head, 'Z', math.radians(-cfg["yaw"] * 0.8)
                              * math.sin(2.0 * math.pi * (t + cfg["wave_lag"]
                                                          * len(SPINE_CHAIN)))))


def build_locomotion(arm_obj, name, length, cfg):
    rest(arm_obj)
    new_action(arm_obj, name, length, loop=True)
    # Frame `length` repeats frame 1 exactly, so the clip closes on itself.
    for frame in range(1, length + 1):
        t = (frame - 1) / float(length - 1)
        body_wave(arm_obj, t, cfg)
        for limb in LIMBS:
            limb_pose(arm_obj, limb, (t + cfg["phase"][limb]) % 1.0, cfg)
        for pbone in arm_obj.pose.bones:
            key(pbone, frame, loc=(pbone.name == "Bone_Root"))


WALK = dict(phase=WALK_PHASE, duty=0.65, swing=17.0, rear_swing=1.15,
            lift=13.0, flex=26.0, ankle=20.0, stance_crouch=4.0,
            yaw=5.5, tail_yaw=5.0, roll=3.0, pitch=1.6, bob=0.16,
            wave_lag=0.085)

RUN = dict(phase=RUN_PHASE, duty=0.45, swing=26.0, rear_swing=1.25,
           lift=22.0, flex=42.0, ankle=28.0, stance_crouch=7.0,
           yaw=8.5, tail_yaw=7.0, roll=5.5, pitch=3.2, bob=0.42,
           wave_lag=0.075)


# ---------------------------------------------------------------------------
# One-shot actions
# ---------------------------------------------------------------------------

def build_idle(arm_obj, length=96):
    """Breathing, a slow tail sway and one weight shift, all on different
    periods so the loop never looks like it is ticking."""
    rest(arm_obj)
    new_action(arm_obj, "Vrescal_Idle", length, loop=True)
    for frame in range(1, length + 1):
        t = (frame - 1) / float(length - 1)
        breathe = math.sin(2.0 * math.pi * t)
        shift = math.sin(2.0 * math.pi * t - math.pi * 0.35)

        root = arm_obj.pose.bones["Bone_Root"]
        pose(root, world_axis_rot(root, 'X', math.radians(1.1) * shift))
        root.location = (0.0, 0.0, 0.055 * breathe)

        for i, name in enumerate(SPINE_CHAIN):
            pbone = arm_obj.pose.bones[name]
            pose(pbone, world_axis_rot(pbone, 'Z', math.radians(1.4) * math.sin(
                2.0 * math.pi * t - 0.5 * i)))
        for i, name in enumerate(TAIL_CHAIN):
            pbone = arm_obj.pose.bones[name]
            pose(pbone, world_axis_rot(pbone, 'Z', math.radians(
                2.2 + 1.1 * i) * math.sin(2.0 * math.pi * t - 0.45 * (i + 3))))

        head = arm_obj.pose.bones["Bone_Head"]
        pose(head,
             world_axis_rot(head, 'Z', math.radians(3.5) * math.sin(
                 2.0 * math.pi * t * 0.5)),
             world_axis_rot(head, 'Y', math.radians(1.8) * breathe))
        pose(arm_obj.pose.bones["Bone_Jaw"],
             local_axis_rot('X', math.radians(1.2 * (1.0 + breathe))))

        for limb in LIMBS:
            upper = arm_obj.pose.bones["Bone_Limb%s_Upper" % limb]
            pose(upper, local_axis_rot('X', math.radians(1.3) * breathe))
        for pbone in arm_obj.pose.bones:
            key(pbone, frame, loc=(pbone.name == "Bone_Root"))


def _hold(arm_obj, frame):
    for pbone in arm_obj.pose.bones:
        key(pbone, frame, loc=(pbone.name == "Bone_Root"))


def build_attack(arm_obj):
    """Coil, lunge, snap, recover. The jaw leads the lunge and closes late, so
    the bite lands rather than arriving with the head."""
    rest(arm_obj)
    new_action(arm_obj, "Vrescal_Attack", 34, loop=False)
    stages = [
        # frame, coil(-)/lunge(+), jaw open deg, head pitch deg, root x
        (1, 0.0, 2.0, 0.0, 0.0),
        (8, -1.0, 26.0, -9.0, -0.35),     # coil back, gape
        (14, 0.7, 34.0, 6.0, 0.55),       # drive forward
        (17, 1.0, 3.0, 10.0, 0.80),       # snap shut at full reach
        (24, 0.35, 6.0, 2.0, 0.30),
        (34, 0.0, 2.0, 0.0, 0.0),
    ]
    for frame, drive, gape, pitch, reach in stages:
        rest(arm_obj)
        root = arm_obj.pose.bones["Bone_Root"]
        root.location = (reach, 0.0, -0.10 * abs(drive))
        pose(root, world_axis_rot(root, 'Y', math.radians(-3.0 * drive)))
        for i, name in enumerate(SPINE_CHAIN):
            pbone = arm_obj.pose.bones[name]
            pose(pbone, world_axis_rot(pbone, 'Y', math.radians(
                -4.5 * drive * (1.0 + 0.3 * i))))
        for i, name in enumerate(TAIL_CHAIN):
            pbone = arm_obj.pose.bones[name]
            pose(pbone, world_axis_rot(pbone, 'Y', math.radians(3.0 * drive)))
        head = arm_obj.pose.bones["Bone_Head"]
        pose(head, world_axis_rot(head, 'Y', math.radians(pitch)))
        pose(arm_obj.pose.bones["Bone_Jaw"],
             local_axis_rot('X', math.radians(gape)))
        for limb in LIMBS:
            upper = arm_obj.pose.bones["Bone_Limb%s_Upper" % limb]
            lower = arm_obj.pose.bones["Bone_Limb%s_Lower" % limb]
            pose(upper, local_axis_rot('X', math.radians(-7.0 * drive)))
            pose(lower, local_axis_rot('X', math.radians(12.0 * abs(drive))))
        _hold(arm_obj, frame)


def build_hurt(arm_obj):
    """A short flinch: the head snaps down and away, the body drops, recover."""
    rest(arm_obj)
    new_action(arm_obj, "Vrescal_Hurt", 18, loop=False)
    stages = [(1, 0.0), (4, 1.0), (9, 0.55), (18, 0.0)]
    for frame, k in stages:
        rest(arm_obj)
        root = arm_obj.pose.bones["Bone_Root"]
        root.location = (-0.25 * k, 0.0, -0.30 * k)
        pose(root, world_axis_rot(root, 'X', math.radians(7.0 * k)))
        for i, name in enumerate(SPINE_CHAIN):
            pbone = arm_obj.pose.bones[name]
            pose(pbone,
                 world_axis_rot(pbone, 'Z', math.radians(6.0 * k)),
                 world_axis_rot(pbone, 'Y', math.radians(4.0 * k)))
        for i, name in enumerate(TAIL_CHAIN):
            pbone = arm_obj.pose.bones[name]
            pose(pbone, world_axis_rot(pbone, 'Z', math.radians(-9.0 * k)))
        head = arm_obj.pose.bones["Bone_Head"]
        pose(head,
             world_axis_rot(head, 'Y', math.radians(14.0 * k)),
             world_axis_rot(head, 'Z', math.radians(-10.0 * k)))
        pose(arm_obj.pose.bones["Bone_Jaw"],
             local_axis_rot('X', math.radians(16.0 * k)))
        for limb in LIMBS:
            lower = arm_obj.pose.bones["Bone_Limb%s_Lower" % limb]
            pose(lower, local_axis_rot('X', math.radians(15.0 * k)))
        _hold(arm_obj, frame)


def build_death(arm_obj):
    """The legs give out sideways and the body settles onto the sand.

    Ends on a dead-still hold so the clip can be left playing on its last frame
    rather than needing a separate corpse pose.
    """
    rest(arm_obj)
    new_action(arm_obj, "Vrescal_Death", 56, loop=False)
    stages = [(1, 0.0), (7, 0.18), (20, 0.62), (34, 0.95), (44, 1.0),
              (56, 1.0)]
    for frame, k in stages:
        rest(arm_obj)
        root = arm_obj.pose.bones["Bone_Root"]
        # The sole plane is 3.35 below the body, so a full collapse is most of
        # the standing clearance -- the belly ends up on the ground.
        root.location = (0.0, 0.0, -1.35 * k)
        pose(root,
             world_axis_rot(root, 'X', math.radians(16.0 * k)),
             world_axis_rot(root, 'Y', math.radians(4.0 * k)))
        for i, name in enumerate(SPINE_CHAIN):
            pbone = arm_obj.pose.bones[name]
            pose(pbone, world_axis_rot(pbone, 'Z', math.radians(
                7.0 * k * (1.0 - 0.2 * i))))
        for i, name in enumerate(TAIL_CHAIN):
            pbone = arm_obj.pose.bones[name]
            pose(pbone, world_axis_rot(pbone, 'Z', math.radians(
                -6.0 * k * (1.0 + 0.35 * i))))
        head = arm_obj.pose.bones["Bone_Head"]
        pose(head,
             world_axis_rot(head, 'Y', math.radians(19.0 * k)),
             world_axis_rot(head, 'Z', math.radians(12.0 * k)))
        pose(arm_obj.pose.bones["Bone_Jaw"],
             local_axis_rot('X', math.radians(11.0 * k)))
        for limb in LIMBS:
            side = SIGN[limb]
            upper = arm_obj.pose.bones["Bone_Limb%s_Upper" % limb]
            lower = arm_obj.pose.bones["Bone_Limb%s_Lower" % limb]
            foot = arm_obj.pose.bones["Bone_Limb%s_Foot" % limb]
            pose(upper,
                 world_axis_rot(upper, 'Z', math.radians(20.0 * k) * side),
                 local_axis_rot('X', math.radians(26.0 * k)))
            pose(lower, local_axis_rot('X', math.radians(-30.0 * k)))
            pose(foot, local_axis_rot('X', math.radians(18.0 * k)))
        _hold(arm_obj, frame)


# ---------------------------------------------------------------------------

def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s -- run vrescal_legs.py first." % SRC)
    bpy.ops.wm.open_mainfile(filepath=SRC)

    arm_obj = bpy.data.objects.get(ARM)
    if arm_obj is None:
        raise SystemExit("No %s in the file -- run vrescal_legs.py first." % ARM)
    if bpy.data.actions:
        raise SystemExit(
            "vrescal.blend already carries %d action(s). This script authors "
            "them from scratch and would leave duplicates; delete them first, "
            "or restore from _backups~ and re-run vrescal_legs.py."
            % len(bpy.data.actions))

    scene = bpy.context.scene
    scene.render.fps = FPS

    bound = bind_body(arm_obj)
    print("Bound %d sculpted meshes to the rig:" % len(bound))
    for obj_name, bone_name in sorted(bound):
        print("    %-30s -> %s" % (obj_name, bone_name))

    bpy.context.view_layer.objects.active = arm_obj
    arm_obj.animation_data_create()

    build_idle(arm_obj)
    build_locomotion(arm_obj, "Vrescal_Walk", 40, WALK)
    build_locomotion(arm_obj, "Vrescal_Run", 26, RUN)
    build_attack(arm_obj)
    build_hurt(arm_obj)
    build_death(arm_obj)

    # Leave the file on the idle pose rather than whatever the last action ended
    # on, so opening it shows the creature standing.
    arm_obj.animation_data.action = bpy.data.actions["Vrescal_Idle"]
    scene.frame_set(1)

    print("Authored %d actions: %s"
          % (len(bpy.data.actions),
             ", ".join(sorted(a.name for a in bpy.data.actions))))

    bpy.ops.wm.save_as_mainfile(filepath=SRC)
    print("Saved %s" % SRC)


main()
