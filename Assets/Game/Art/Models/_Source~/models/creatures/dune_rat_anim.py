"""Author the Dune Rat's six actions on the rig `dune_rat_rig.py` repaired.

    blender --background --python dune_rat_anim.py

Run after `dune_rat_rig.py`. Re-runnable: it deletes any actions already in the
file and authors the set from scratch, so tuning a number and running again is
the intended workflow.

## It is a biped

The bone names say quadruped -- `femur`/`fibula`/`metarsal` behind,
`scapula`/`humerus`/`radius`/`metacarpal` in front -- and that is misleading.
Measured off the rig, the hind chain reaches 0.99 m and the fore chain 0.29 m,
and in the author's rest pose the front feet hang 0.41 m clear of the sand
while the hind feet stand on it. This animal runs on two legs with the trunk
held horizontal and the tail out behind as the counterweight, the way a jerboa
or a small theropod does. Every clip here is built on that reading, which came
from rendering the rest pose rather than from the names.

So: the hind feet carry the gait, and the forelimbs never touch the ground.
They gesture, they tuck, and in the attack they swipe.

## The feet do not slide, by construction

Rather than swinging the femur and hoping, each clip places the **toe tip** --
the point that actually touches sand -- and works backwards to where the IK
target must be:

    ik_target = contact - R_x(toe_angle) . (hoof_tail - hoof_head)

That inversion is the whole trick. It means the toe can roll through push-off
while its tip stays welded to the same speck of sand, which is the one thing
that separates a walk cycle from a moonwalk. During stance the contact travels
backwards at a constant rate; during swing it arcs forward on a smoothstep so
the foot neither jerks at lift-off nor lands with a snap.

Because the contact rate is constant and known, the clip's ground speed is not
a guess either:

    speed = sweep / (duty x clip_duration)

`main` prints both figures. They are the numbers that must appear as the blend
tree thresholds in `DuneRatBuilder.cs`; if the two disagree the animal slides.

`clip_duration` is Unity's, `(lastFrame - firstFrame) / fps`, not Blender's
frame count -- see the loop note below. Matching Blender's instead would put
the run 7% fast.

## Loops close on themselves

Each looping action authors `N` frames where frame `N` is an exact copy of
frame 1, and `DuneRatBuilder` then slices frames 1..N-1. Playing both the first
and the last would hold one pose for two frames every lap, which on a 16-frame
run cycle is a visible hitch at the top of every stride.

## What moves what

`root` is the body. It is parentless and carries the whole trunk (translation,
pitch, roll, sway); the spine, neck, head, ears and tail hang off it and are
keyed as plain FK rotations on top.

The four IK targets are **also** parentless -- `dune_rat_rig.py` detached them
from `root` precisely so a planted foot could stay planted while the body moves
over it. The consequence to remember is that the forelimb targets then have to
be moved by hand: they are not touching anything, so they must inherit the
body's motion or the arms tear off the chest as it bobs. `root_delta` is
applied to them explicitly for that reason.
"""

import math
import os

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "dune_rat.blend")

FPS = 30
ARM = "Arm_DuneRat"
MESH = "Mesh_DuneRat"

SPINE = ["spine1", "spine2", "spine3", "spine4"]     # hips -> shoulders
NECK = ["neck1", "neck2"]
TAIL = ["tail1", "tail2", "tail3", "tail4"]
EARS = ["ear.L", "ear.R"]
HIND = [("IK_back.L", "hoof_B.L", 0.0), ("IK_back.R", "hoof_B.R", 0.5)]
FORE = [("IK_front.L", "hoof_F.L", 0.5), ("IK_front.R", "hoof_F.R", 0.0)]
CONTROLS = ["IK_back.L", "IK_back.R", "IK_front.L", "IK_front.R"]

# Pitch and roll of the whole animal happen about the hips, not about the
# world origin: a biped nodding forward rotates about the joint that carries
# it, and pivoting on the origin instead swings the head through a metre-wide
# arc for a five-degree nod.
HIP_PIVOT = Vector((0.0, 0.2615, 0.759))


# ---------------------------------------------------------------------------
# Gait tuning
#
# `amp` is half the stance sweep, `yc` its centre. Both are set from the leg
# geometry: the hip sits at y = +0.26 and the foot rests at y = 0, so the
# animal already stands with its feet ahead of its hips, and the sweep is
# nudged rearward (`yc` > 0) to stop the forward extreme running the chain out
# to full stretch. At the values below the worst-case chord is 84% of the
# 0.99 m the leg can reach, which leaves the knee visibly bent all cycle.
# ---------------------------------------------------------------------------

# `tail_sweep` is the fore-aft half-amplitude at the tip segment, in degrees,
# and it is the loudest thing on the animal at speed. The first version of this
# gait had it at 2.0/3.5 and the result was a dead tail: measured off the
# exported FBX, tail4 swung ~8 deg in Walk against ~11 deg in *Idle*, so the
# faster the animal went the quieter its tail got. That is backwards. A jerboa
# or a small theropod is doing the opposite -- the tail is a counterweight, and
# it works hardest exactly when the legs do. The ordering Idle < Walk < Run is
# the property to preserve if these are ever retuned.

WALK = dict(
    frames=26, duty=0.62, amp=0.275, yc=0.06, lift=0.10,
    toe_off=28.0, land=-12.0,
    bob=0.018, bob_sign=1.0, crouch=0.0, sway=0.020, roll=2.5, pitch=1.6,
    lean=1.0, spine_yaw=2.0, spine_pitch=1.2, tail_yaw=5.0, tail_lift=3.0,
    tail_sweep=10.0, tail_lag=0.45,
    arm_swing=0.035, arm_lift=0.018, head_bob=0.6, ear=2.5,
)

RUN = dict(
    frames=16, duty=0.36, amp=0.386, yc=0.10, lift=0.18,
    toe_off=36.0, land=-16.0,
    bob=0.050, bob_sign=-1.0, crouch=0.075, sway=0.013, roll=3.5, pitch=4.0,
    lean=9.0, spine_yaw=3.0, spine_pitch=2.4, tail_yaw=8.0, tail_lift=12.0,
    tail_sweep=18.0, tail_lag=0.55,
    arm_swing=0.055, arm_lift=0.030, head_bob=1.4, ear=6.0,
)


# ---------------------------------------------------------------------------
# Pose helpers
#
# `world_axis_rot` and `local_axis_rot` are the pattern the Vrescal established
# and are kept deliberately identical, so anyone who has read one gait script
# in this library can read the other.
# ---------------------------------------------------------------------------

def world_axis_rot(pbone, axis, degrees):
    """A bone-local quaternion equal to rotating `degrees` about a world axis.

    Lets the gait be written in the terms the animal has -- "yaw the spine",
    "lift the tail" -- rather than in whatever frame each bone's roll landed
    in. `matrix_local` is the rest orientation in armature space, so
    conjugating by it carries a world rotation into the bone.
    """
    angle = math.radians(degrees)
    if abs(angle) < 1e-9:
        return Quaternion((1.0, 0.0, 0.0, 0.0))
    rest = pbone.bone.matrix_local.to_3x3()
    rot = Matrix.Rotation(angle, 3, axis)
    return (rest.inverted() @ rot @ rest).to_quaternion()


def local_axis_rot(axis, degrees):
    """Rotation about one of the bone's own axes -- joint flexion."""
    angle = math.radians(degrees)
    if abs(angle) < 1e-9:
        return Quaternion((1.0, 0.0, 0.0, 0.0))
    return Quaternion({'X': (1, 0, 0), 'Y': (0, 1, 0), 'Z': (0, 0, 1)}[axis],
                      angle)


def pose(pbone, *quats):
    pbone.rotation_mode = 'QUATERNION'
    q = Quaternion((1.0, 0.0, 0.0, 0.0))
    for extra in quats:
        q = q @ extra
    pbone.rotation_quaternion = q


def rest(arm):
    for pbone in arm.pose.bones:
        pbone.rotation_mode = 'QUATERNION'
        pbone.matrix_basis = Matrix.Identity(4)


def key_all(arm, frame):
    for pbone in arm.pose.bones:
        pbone.keyframe_insert("rotation_quaternion", frame=frame)
        if pbone.name == "root" or pbone.name in CONTROLS:
            pbone.keyframe_insert("location", frame=frame)


def new_action(arm, name, length, loop):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True          # survives the save with no NLA strip
    arm.animation_data.action = action
    action.frame_start, action.frame_end = 1, length
    action["loop"] = loop
    return action


# ---------------------------------------------------------------------------
# Body and feet
# ---------------------------------------------------------------------------

def body_matrix(offset, pitch=0.0, roll=0.0, yaw=0.0):
    """The trunk's armature-space transform, rotating about the hips."""
    rot = (Matrix.Rotation(math.radians(yaw), 4, 'Z')
           @ Matrix.Rotation(math.radians(roll), 4, 'Y')
           @ Matrix.Rotation(math.radians(pitch), 4, 'X'))
    return (Matrix.Translation(HIP_PIVOT + Vector(offset))
            @ rot
            @ Matrix.Translation(-HIP_PIVOT))


def set_body(arm, delta):
    pbone = arm.pose.bones["root"]
    pbone.rotation_mode = 'QUATERNION'
    pbone.matrix = delta @ pbone.bone.matrix_local
    return delta


def plant(arm, geom, ik_name, hoof_name, contact, toe_deg):
    """Put the toe tip on `contact` with the toe rolled `toe_deg`.

    The inversion described in the module docstring. `contact` is where the tip
    of the toe must end up; everything else follows from it.
    """
    v = geom[hoof_name]
    rolled = Matrix.Rotation(math.radians(toe_deg), 3, 'X') @ v
    target = Vector(contact) - rolled

    ik = arm.pose.bones[ik_name]
    ik.rotation_mode = 'QUATERNION'
    ik.matrix = Matrix.Translation(target - geom[ik_name]) @ ik.bone.matrix_local
    pose(arm.pose.bones[hoof_name],
         world_axis_rot(arm.pose.bones[hoof_name], 'X', toe_deg))


def foot_track(t, cfg):
    """Contact height, fore-aft position and toe angle at stride time `t`.

    Stance sweeps the contact backwards at a constant rate -- that constant is
    what makes the clip's ground speed a known number rather than an
    impression. Swing takes it forward again on a smoothstep and lifts it on a
    flattened sine.
    """
    duty = cfg["duty"]
    y_front = cfg["yc"] - cfg["amp"]
    y_back = cfg["yc"] + cfg["amp"]

    if t < duty:                                    # planted
        u = t / duty
        y = y_front + (y_back - y_front) * u
        z = 0.0
        toe = 0.0 if u < 0.66 else cfg["toe_off"] * (u - 0.66) / 0.34
    else:                                           # airborne
        u = (t - duty) / (1.0 - duty)
        s = u * u * (3.0 - 2.0 * u)
        y = y_back + (y_front - y_back) * s
        z = cfg["lift"] * math.sin(math.pi * u) ** 0.85
        if u < 0.35:                                # unroll the push-off
            toe = cfg["toe_off"] * (1.0 - u / 0.35)
        else:                                       # toe up to clear, then flat
            toe = cfg["land"] * math.sin(math.pi * (u - 0.35) / 0.65)
    return y, z, toe


def measure(arm, mesh):
    """Everything the clips need off the rest pose, in armature space.

    Read rather than hardcoded: `dune_rat_rig.py` decides the shipping scale,
    and a second copy of these numbers here would rot the first time it
    changed.
    """
    geom = {}
    for name in CONTROLS:
        geom[name] = arm.data.bones[name].head_local.copy()
    for _ik, hoof, _p in HIND + FORE:
        bone = arm.data.bones[hoof]
        geom[hoof] = bone.tail_local - bone.head_local

    # Where each hind toe's *skin* bottoms out, so the sole -- not the bone
    # tail, which sits a couple of centimetres inside it -- lands on z = 0.
    groups = {g.name: g.index for g in mesh.vertex_groups}
    contacts = {}
    for ik, hoof, _p in HIND:
        idx = groups[hoof]
        zs = [v.co.z for v in mesh.data.vertices
              if any(g.group == idx and g.weight > 0.5 for g in v.groups)]
        bone = arm.data.bones[hoof]
        sole = min(zs) if zs else bone.tail_local.z
        contacts[ik] = Vector((bone.tail_local.x, bone.tail_local.y,
                               bone.tail_local.z - sole))
    return geom, contacts


# ---------------------------------------------------------------------------
# Locomotion
# ---------------------------------------------------------------------------

def build_locomotion(arm, geom, contacts, name, cfg):
    n = cfg["frames"]
    rest(arm)
    new_action(arm, name, n, loop=True)

    # Midstance of the left foot. The trunk's vertical and lateral cycles are
    # phased off it, because that is the instant the body is actually being
    # carried by that leg.
    mid = cfg["duty"] * 0.5

    for frame in range(1, n + 1):
        t = (frame - 1) / float(n - 1)
        rest(arm)

        step = math.cos(2.0 * math.pi * 2.0 * (t - mid))     # 2 per cycle
        lap = math.cos(2.0 * math.pi * (t - mid))            # 1 per cycle

        offset = (cfg["sway"] * lap,
                  0.0,
                  -cfg["crouch"] + cfg["bob_sign"] * cfg["bob"] * step)
        delta = set_body(arm, body_matrix(
            offset,
            pitch=cfg["lean"] + cfg["pitch"] * step,
            roll=cfg["roll"] * lap,
            yaw=-cfg["spine_yaw"] * 0.5 * lap))

        # Trunk. The spine counter-yaws against the hips so the shoulders stay
        # pointed where the animal is going while the pelvis swings under it.
        for i, bone in enumerate(SPINE):
            pb = arm.pose.bones[bone]
            k = i / float(len(SPINE) - 1)
            pose(pb,
                 world_axis_rot(pb, 'Z', cfg["spine_yaw"] * k * lap),
                 world_axis_rot(pb, 'X', -cfg["spine_pitch"] * k * step))

        # Neck and head cancel most of the trunk's bob. A head that rides the
        # body up and down reads as a toy; real animals stabilise the eyes.
        for i, bone in enumerate(NECK):
            pb = arm.pose.bones[bone]
            pose(pb,
                 world_axis_rot(pb, 'X', -(cfg["lean"] * 0.45 +
                                           cfg["pitch"] * 0.7 * step)),
                 world_axis_rot(pb, 'Z', -cfg["spine_yaw"] * 0.4 * lap))
        headb = arm.pose.bones["head"]
        pose(headb,
             world_axis_rot(headb, 'X', -cfg["lean"] * 0.25 +
                            cfg["head_bob"] * step),
             world_axis_rot(headb, 'Z', -cfg["spine_yaw"] * 0.3 * lap))

        for i, bone in enumerate(EARS):
            pb = arm.pose.bones[bone]
            pose(pb, world_axis_rot(pb, 'X', -cfg["ear"] * step))

        # Tail: held up as the counterweight, and swept fore-aft in the
        # sagittal plane once per footfall -- twice a cycle, phase-locked to
        # the same `step` that drives the trunk's pitch and bob, so it opposes
        # the body rather than floating free.
        #
        # The `k ** 1.5` ramp is steeper than a linear one on purpose. What an
        # animator sees is the accumulated angle at the tip, but what lands in
        # the FBX and gets measured is each bone's own *local* curve, and a
        # linear ramp spreads the motion so evenly that no single bone reads as
        # doing much. Loading the ramp towards the tip gives the same silhouette
        # with a tip segment that is unmistakably moving.
        #
        # `tail_lag` delays each segment behind the one before it, which is
        # what makes it a whip rather than a rigid see-saw. Kept small: at much
        # over half a radian the segments start cancelling and the accumulated
        # sweep collapses even as the individual angles grow.
        for i, bone in enumerate(TAIL):
            pb = arm.pose.bones[bone]
            k = ((i + 1) / float(len(TAIL))) ** 1.5
            sweep = math.cos(2.0 * math.pi * 2.0 * (t - mid)
                             - cfg["tail_lag"] * i)
            lateral = math.cos(2.0 * math.pi * (t - mid) - cfg["tail_lag"] * i)
            # The constant lift keeps the whole sweep above the horizontal, so
            # the tip cannot reach the sand at full amplitude.
            pose(pb,
                 world_axis_rot(pb, 'X', cfg["tail_lift"] * (0.4 + 0.6 * k)
                                + cfg["tail_sweep"] * k * sweep),
                 world_axis_rot(pb, 'Z', -cfg["tail_yaw"] * k * lateral))

        # Hind feet: the only things touching the ground.
        for ik, hoof, phase in HIND:
            y, z, toe = foot_track((t + phase) % 1.0, cfg)
            base = contacts[ik]
            plant(arm, geom, ik, hoof, (base.x, y, base.z + z), toe)

        # Forelimbs: carried by the body (they are parentless, so `delta` has
        # to be applied by hand) and swung in antiphase with the hind leg on
        # the same side.
        for ik, hoof, phase in FORE:
            swing = math.cos(2.0 * math.pi * ((t + phase) % 1.0))
            pb = arm.pose.bones[ik]
            pb.rotation_mode = 'QUATERNION'
            pb.matrix = (delta
                         @ Matrix.Translation((0.0,
                                               cfg["arm_swing"] * swing,
                                               cfg["arm_lift"] * abs(swing)))
                         @ pb.bone.matrix_local)
            pose(arm.pose.bones[hoof],
                 local_axis_rot('X', -14.0 * swing))

        key_all(arm, frame)

    duration = (n - 2) / float(FPS)          # Unity's, see the module docstring
    return 2.0 * cfg["amp"] / (cfg["duty"] * duration)


# ---------------------------------------------------------------------------
# Idle
# ---------------------------------------------------------------------------

def build_idle(arm, geom, contacts, n=91):
    """Breathing, a slow weight shift, a head scan and two ear flicks.

    Every component runs a whole number of cycles over the clip so the loop
    closes, except the ear flicks, which are gaussian bumps that start and end
    at zero anyway. The periods are deliberately coprime-ish -- 3 breaths, 1
    weight shift, 2 tail sways -- so the loop never looks like it is ticking.
    """
    rest(arm)
    new_action(arm, "DuneRat_Idle", n, loop=True)

    for frame in range(1, n + 1):
        t = (frame - 1) / float(n - 1)
        rest(arm)

        breathe = math.sin(2.0 * math.pi * 3.0 * t)
        shift = math.sin(2.0 * math.pi * t)
        sway = math.sin(2.0 * math.pi * 2.0 * t)
        # Two flicks, at a fifth and two thirds through, neither near the seam.
        flick = sum(math.exp(-((t - c) / 0.02) ** 2) for c in (0.21, 0.67))

        delta = set_body(arm, body_matrix(
            (0.012 * shift, 0.0, 0.006 * breathe),
            pitch=0.5 * breathe, roll=1.4 * shift, yaw=0.6 * shift))

        for i, bone in enumerate(SPINE):
            pb = arm.pose.bones[bone]
            k = i / float(len(SPINE) - 1)
            pose(pb,
                 world_axis_rot(pb, 'X', -0.9 * k * breathe),
                 world_axis_rot(pb, 'Z', 1.1 * k * shift))
        for pb in (arm.pose.bones[b] for b in NECK):
            pose(pb,
                 world_axis_rot(pb, 'X', 1.2 * breathe),
                 world_axis_rot(pb, 'Z', -2.0 * shift))
        headb = arm.pose.bones["head"]
        pose(headb,
             world_axis_rot(headb, 'Z', -5.5 * shift),
             world_axis_rot(headb, 'X', 2.0 * sway - 3.0 * flick))
        for i, bone in enumerate(EARS):
            pb = arm.pose.bones[bone]
            side = 1.0 if bone.endswith(".L") else -1.0
            pose(pb,
                 world_axis_rot(pb, 'X', -16.0 * flick + 1.5 * breathe),
                 world_axis_rot(pb, 'Y', 6.0 * side * flick))
        for i, bone in enumerate(TAIL):
            pb = arm.pose.bones[bone]
            k = (i + 1) / float(len(TAIL))
            pose(pb,
                 world_axis_rot(pb, 'X', 2.5 * k),
                 world_axis_rot(pb, 'Z', -(2.0 + 3.5 * k)
                                * math.sin(2.0 * math.pi * 2.0 * (t - 0.1 * i))))

        for ik, hoof, _p in HIND:
            base = contacts[ik]
            plant(arm, geom, ik, hoof, (base.x, base.y, base.z), 0.0)
        for ik, hoof, _p in FORE:
            pb = arm.pose.bones[ik]
            pb.rotation_mode = 'QUATERNION'
            pb.matrix = (delta
                         @ Matrix.Translation((0.0, 0.006 * breathe,
                                               0.008 * breathe))
                         @ pb.bone.matrix_local)
            pose(arm.pose.bones[hoof], local_axis_rot('X', 3.0 * breathe))

        key_all(arm, frame)


# ---------------------------------------------------------------------------
# One-shots
#
# Authored as sparse staged poses rather than per-frame, so the default Bezier
# interpolation does the easing. `k` is how far into the action's own shape
# each stage is; every other number is scaled off it, which is what keeps a
# stage editable without unpicking the rest.
# ---------------------------------------------------------------------------

def staged(arm, geom, contacts, name, length, stages, shape):
    rest(arm)
    new_action(arm, name, length, loop=False)
    for frame, k in stages:
        rest(arm)
        shape(arm, geom, contacts, k)
        key_all(arm, frame)


def build_attack(arm, geom, contacts):
    """Coil, lunge, and swipe.

    There is no jaw bone in this rig, so the bite cannot be sold with a gape.
    It is sold with the whole animal instead: the trunk rears, drives forward
    past the hips, and the forelimbs -- useless for walking, which is exactly
    why they are free to do this -- rake across the target as the head arrives.

    `k` runs negative for the coil and positive for the strike, so one shape
    function covers both halves and they cannot drift apart.
    """
    def shape(arm, geom, contacts, k):
        drive = max(k, 0.0)
        coil = max(-k, 0.0)
        # The reach is nearly all translation, and the pitch is deliberately
        # small. Driving the nose down hard looks more violent in isolation and
        # is wrong: the head already rests at 0.97 m, which is a standing
        # player's chest, and every degree of nose-down walks the bite further
        # towards their knees. The strike travels forward, not downward.
        delta = set_body(arm, body_matrix(
            (0.0, 0.13 * coil - 0.28 * drive, -0.05 * coil - 0.01 * drive),
            pitch=-7.0 * coil + 8.0 * drive))

        for i, bone in enumerate(SPINE):
            pb = arm.pose.bones[bone]
            kk = i / float(len(SPINE) - 1)
            pose(pb, world_axis_rot(pb, 'X', kk * (-6.0 * coil + 5.0 * drive)))
        for pb in (arm.pose.bones[b] for b in NECK):
            pose(pb, world_axis_rot(pb, 'X', -14.0 * coil + 4.0 * drive))
        headb = arm.pose.bones["head"]
        pose(headb, world_axis_rot(headb, 'X', -10.0 * coil + 6.0 * drive))
        for bone in EARS:                       # ears flatten back on the strike
            pb = arm.pose.bones[bone]
            pose(pb, world_axis_rot(pb, 'X', 34.0 * drive - 6.0 * coil))
        for i, bone in enumerate(TAIL):         # tail whips up as counterweight
            pb = arm.pose.bones[bone]
            kk = (i + 1) / float(len(TAIL))
            pose(pb, world_axis_rot(pb, 'X', kk * (10.0 * coil + 26.0 * drive)))

        # The feet stay planted through the lunge -- the reach comes from the
        # trunk travelling over them, which is both how the animal would do it
        # and why nothing slides.
        for ik, hoof, _p in HIND:
            base = contacts[ik]
            plant(arm, geom, ik, hoof,
                  (base.x, base.y + 0.04 * coil - 0.05 * drive, base.z),
                  -8.0 * coil + 22.0 * drive)

        for ik, hoof, _p in FORE:
            side = 1.0 if ik.endswith(".L") else -1.0
            pb = arm.pose.bones[ik]
            pb.rotation_mode = 'QUATERNION'
            pb.matrix = (delta @ Matrix.Translation((
                side * (0.05 * coil + 0.09 * drive),
                0.06 * coil - 0.13 * drive,
                0.04 * coil - 0.06 * drive)) @ pb.bone.matrix_local)
            pose(arm.pose.bones[hoof], local_axis_rot('X', -30.0 * drive))

    staged(arm, geom, contacts, "DuneRat_Attack", 30,
           [(1, 0.0), (7, -1.0), (13, 0.55), (16, 1.0), (21, 0.45),
            (25, 0.1), (30, 0.0)], shape)


def build_hurt(arm, geom, contacts):
    """A short flinch: the head snaps up and away, the trunk drops and staggers
    half a step back, and the tail whips across."""
    def shape(arm, geom, contacts, k):
        delta = set_body(arm, body_matrix(
            (0.03 * k, 0.09 * k, -0.06 * k),
            pitch=-11.0 * k, roll=7.0 * k, yaw=-6.0 * k))

        for i, bone in enumerate(SPINE):
            pb = arm.pose.bones[bone]
            kk = i / float(len(SPINE) - 1)
            pose(pb,
                 world_axis_rot(pb, 'X', -7.0 * kk * k),
                 world_axis_rot(pb, 'Z', 9.0 * kk * k))
        for pb in (arm.pose.bones[b] for b in NECK):
            pose(pb, world_axis_rot(pb, 'X', -15.0 * k),
                 world_axis_rot(pb, 'Z', 7.0 * k))
        headb = arm.pose.bones["head"]
        pose(headb, world_axis_rot(headb, 'X', -18.0 * k),
             world_axis_rot(headb, 'Z', 12.0 * k))
        for bone in EARS:
            pb = arm.pose.bones[bone]
            pose(pb, world_axis_rot(pb, 'X', 40.0 * k))
        for i, bone in enumerate(TAIL):
            pb = arm.pose.bones[bone]
            kk = (i + 1) / float(len(TAIL))
            pose(pb,
                 world_axis_rot(pb, 'X', 8.0 * kk * k),
                 world_axis_rot(pb, 'Z', 14.0 * kk * k))

        for ik, hoof, _p in HIND:
            base = contacts[ik]
            plant(arm, geom, ik, hoof,
                  (base.x, base.y + 0.07 * k, base.z), -14.0 * k)
        for ik, hoof, _p in FORE:
            pb = arm.pose.bones[ik]
            pb.rotation_mode = 'QUATERNION'
            pb.matrix = (delta @ Matrix.Translation(
                (0.0, 0.05 * k, 0.03 * k)) @ pb.bone.matrix_local)
            pose(arm.pose.bones[hoof], local_axis_rot('X', 22.0 * k))

    staged(arm, geom, contacts, "DuneRat_Hurt", 16,
           [(1, 0.0), (3, 1.0), (7, 0.5), (11, 0.18), (16, 0.0)], shape)


def build_death(arm, geom, contacts):
    """The hind legs buckle, the animal drops onto its left flank and settles.

    Ends on a dead-still hold so the clip can be left playing on its last frame
    rather than needing a separate corpse state -- `DuneRatBuilder` gives Death
    no exit transition for exactly that reason.

    The feet travel with the collapsing body here rather than staying planted.
    That is the one place in the whole set where contact is abandoned on
    purpose: a corpse is not standing on anything, and holding the toes to the
    sand while the trunk rolls 78 degrees would stretch both legs straight and
    leave the animal apparently propped on stilts.
    """
    def shape(arm, geom, contacts, k):
        delta = set_body(arm, body_matrix(
            (-0.10 * k, 0.05 * k, -0.62 * k),
            pitch=9.0 * k, roll=78.0 * k, yaw=13.0 * k))

        for i, bone in enumerate(SPINE):
            pb = arm.pose.bones[bone]
            kk = i / float(len(SPINE) - 1)
            pose(pb,
                 world_axis_rot(pb, 'Z', 11.0 * kk * k),
                 world_axis_rot(pb, 'X', -5.0 * kk * k))
        for pb in (arm.pose.bones[b] for b in NECK):
            pose(pb, world_axis_rot(pb, 'X', 9.0 * k),
                 world_axis_rot(pb, 'Z', 14.0 * k))
        headb = arm.pose.bones["head"]
        pose(headb, world_axis_rot(headb, 'X', 22.0 * k),
             world_axis_rot(headb, 'Z', 10.0 * k))
        for bone in EARS:
            pb = arm.pose.bones[bone]
            pose(pb, world_axis_rot(pb, 'X', 30.0 * k))
        for i, bone in enumerate(TAIL):
            pb = arm.pose.bones[bone]
            kk = (i + 1) / float(len(TAIL))
            pose(pb,
                 world_axis_rot(pb, 'X', -6.0 * kk * k),
                 world_axis_rot(pb, 'Z', -(9.0 + 11.0 * kk) * k))

        for ik, hoof, _p in HIND:
            side = 1.0 if ik.endswith(".L") else -1.0
            pb = arm.pose.bones[ik]
            pb.rotation_mode = 'QUATERNION'
            pb.matrix = (delta @ Matrix.Translation((
                side * 0.10 * k, -0.16 * k, 0.10 * k)) @ pb.bone.matrix_local)
            pose(arm.pose.bones[hoof], local_axis_rot('X', 34.0 * k))
        for ik, hoof, _p in FORE:
            side = 1.0 if ik.endswith(".L") else -1.0
            pb = arm.pose.bones[ik]
            pb.rotation_mode = 'QUATERNION'
            pb.matrix = (delta @ Matrix.Translation((
                side * 0.06 * k, 0.04 * k, -0.05 * k)) @ pb.bone.matrix_local)
            pose(arm.pose.bones[hoof], local_axis_rot('X', 26.0 * k))

    staged(arm, geom, contacts, "DuneRat_Death", 50,
           [(1, 0.0), (5, 0.14), (14, 0.5), (26, 0.88), (36, 1.0),
            (50, 1.0)], shape)


# ---------------------------------------------------------------------------

def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s -- run dune_rat_rig.py first." % SRC)
    bpy.ops.wm.open_mainfile(filepath=SRC)

    arm = bpy.data.objects.get(ARM)
    mesh = bpy.data.objects.get(MESH)
    if arm is None or mesh is None:
        raise SystemExit("%s / %s missing -- run dune_rat_rig.py first."
                         % (ARM, MESH))

    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)

    bpy.context.scene.render.fps = FPS
    bpy.context.view_layer.objects.active = arm
    if arm.animation_data is None:
        arm.animation_data_create()

    geom, contacts = measure(arm, mesh)
    for name, value in sorted(contacts.items()):
        print("Toe contact %-11s %s" % (name, tuple(round(v, 4) for v in value)))

    build_idle(arm, geom, contacts)
    walk = build_locomotion(arm, geom, contacts, "DuneRat_Walk", WALK)
    run = build_locomotion(arm, geom, contacts, "DuneRat_Run", RUN)
    build_attack(arm, geom, contacts)
    build_hurt(arm, geom, contacts)
    build_death(arm, geom, contacts)

    print("Ground speed the clips actually carry, at %d fps:" % FPS)
    print("   DuneRat_Walk  %2d frames  duty %.2f  sweep %.3f m  -> %.3f m/s"
          % (WALK["frames"], WALK["duty"], 2 * WALK["amp"], walk))
    print("   DuneRat_Run   %2d frames  duty %.2f  sweep %.3f m  -> %.3f m/s"
          % (RUN["frames"], RUN["duty"], 2 * RUN["amp"], run))
    print("   ^ these two are the blend tree thresholds in DuneRatBuilder.cs")

    arm.animation_data.action = bpy.data.actions["DuneRat_Idle"]
    bpy.context.scene.frame_set(1)
    print("Authored %d actions: %s"
          % (len(bpy.data.actions),
             ", ".join("%s(%d)" % (a.name, int(a.frame_end))
                       for a in sorted(bpy.data.actions, key=lambda x: x.name))))

    bpy.ops.wm.save_as_mainfile(filepath=SRC)
    print("Saved %s" % SRC)


main()
