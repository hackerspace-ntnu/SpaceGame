# Authors Idle and Walk actions on ConjurerRig. Additive: touches only pose bones
# of the rig this workflow created.
import bpy, math, re
from mathutils import Matrix, Vector

arm = bpy.data.objects["ConjurerRig"]
# rebuild cleanly on re-run: drop any Idle/Walk (and .001 duplicates) we authored
for _a in [a for a in bpy.data.actions if re.match(r"^(Idle|Walk)(\.\d+)?$", a.name)]:
    bpy.data.actions.remove(_a)
bpy.context.view_layer.objects.active = arm
sc = bpy.context.scene
sc.render.fps = 30

for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'

def world_rot(pb, axis, deg):
    """Rotation of `deg` about a world axis, expressed in the bone's local space."""
    M = pb.bone.matrix_local.to_3x3()
    Rw = Matrix.Rotation(math.radians(deg), 3, axis)
    return (M.inverted() @ Rw @ M).to_quaternion()

def key(frame, pose):
    """pose: {bone: (axis, degrees)} or {bone: ('LOC', Vector)}"""
    for name, val in pose.items():
        pb = arm.pose.bones[name]
        if val[0] == 'LOC':
            pb.location = val[1]
            pb.keyframe_insert('location', frame=frame)
        else:
            pb.rotation_quaternion = world_rot(pb, val[0], val[1])
            pb.keyframe_insert('rotation_quaternion', frame=frame)

def new_action(name, length):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_clear()
    arm.animation_data_create().action = act
    sc.frame_start, sc.frame_end = 1, length
    return act

# Bone local axes: every bone was built with roll 0. Root/Hips/Spine/Head point +Z,
# so their local Y is world +Z. Legs and arms point -Z. Forward is world +X, so a
# forward/back swing is a rotation about world 'Y', and a side lean is about 'X'.

# ------------------------------------------------------------------ IDLE
# 120 frames @30fps = 4.0s loop. Slow hover: body breathes, arms drift out of phase,
# halo turns steadily.
A = new_action("Idle", 120)
for f, k in ((1,0.0), (31,1.0), (61,0.0), (91,-1.0), (120,0.0)):
    key(f, {
        "Root":       ('LOC', Vector((0.0,  0.18*k, 0.0))),   # local Y == world Z
        "Spine":      ('Y',  1.4*k),
        "Head":       ('Y', -2.2*k),
        "ArmRoot.L":  ('LOC', Vector((0.0,  0.45*k, 0.0))),
        "ArmRoot.R":  ('LOC', Vector((0.0, -0.45*k, 0.0))),
        "UpperArm.L": ('Y',  3.5*k),
        "UpperArm.R": ('Y', -3.5*k),
        "Forearm.L":  ('Y', -5.0*k),
        "Forearm.R":  ('Y',  5.0*k),
        "Hand.L":     ('Y',  4.0*k),
        "Hand.R":     ('Y', -4.0*k),
        "Hip_L":    ('Y',  1.0*k),
        "Hip_R":    ('Y', -1.0*k),
    })
for f, ang in ((1,0), (120,90)):        # halo keeps turning; 90 deg tiles seamlessly on a 4-fold-symmetric cube
    key(f, {"Halo": ('Z', ang)})

# ------------------------------------------------------------------ WALK
# 72 frames @30fps = 2.4s full cycle. Deliberately ponderous: at 18 m this
# thing is six times the player's height, and a giant that steps at a human
# cadence reads as a toy. Step frequency in nature falls off roughly as
# 1/sqrt(length), so doubling the size alone argues for ~0.7x; the rest is
# taste, and the brief asked for slower.
#
# SIGN CONVENTION, the thing that makes or breaks this: world_rot(bone, 'Y', d)
# turns the bone about world +Y, and forward is world +X. Rotating a bone that
# points DOWN by angle d moves its tail to
#     tail = head + (-L*sin(d), 0, -L*cos(d))
# so a NEGATIVE angle swings the tail FORWARD and a positive one swings it back.
#
# The knee therefore has to be POSITIVE to fold the way a knee folds. A negative
# knee angle rotates the shin forward past straight, which puts the joint behind
# the hip-ankle line - a leg hyperextending through itself.
WALK_FRAMES = 72
W = new_action("Walk", WALK_FRAMES)

SW   = 24.0   # thigh swing, degrees either side of vertical
KN   = 34.0   # peak knee flexion during swing
BIAS = 4.0    # never fully lock the knee; a locked leg reads as a stilt
FT   = 12.0   # foot pitch: toe-down at toe-off, toe-up at heel strike

# Segment lengths straight off the bone table.
HIP_Z, KNEE_Z, ANKLE_Z = 25.42, 13.93, 5.10
LT, LS = HIP_Z - KNEE_Z, KNEE_Z - ANKLE_Z

def leg_angles(phase):
    """(thigh, knee) in degrees. thigh is a world angle, knee is relative to it."""
    t = phase * 2 * math.pi
    thigh = SW * math.sin(t)
    # Flexes through the swing phase only; max(0, ...) keeps the knee straight
    # while the foot is carrying weight.
    knee = KN * max(0.0, math.sin(t - 1.2)) + BIAS
    return thigh, knee

def ankle_height(phase):
    """Forward kinematics: where this leg's ankle sits, hips held at rest."""
    thigh, knee = leg_angles(phase)
    return (HIP_Z
            - LT * math.cos(math.radians(thigh))
            - LS * math.cos(math.radians(thigh + knee)))

def leg_pose(side, phase):
    thigh, knee = leg_angles(phase)
    # Counter-rotate the foot by the shin's total world angle so the sole stays
    # parallel to the ground, then add the natural toe-down / toe-up pitch.
    foot = -(thigh + knee) + FT * math.sin(phase * 2 * math.pi)
    return {f"Hip_{side}": ('Y', thigh),
            f"Knee_{side}":  ('Y', knee),
            f"Ankle_{side}":  ('Y', foot)}

for i in range(0, WALK_FRAMES + 1, 3):
    f = i + 1
    p = i / float(WALK_FRAMES)
    pose = {}
    pose.update(leg_pose("L", p))
    pose.update(leg_pose("R", (p + 0.5) % 1.0))

    # Ride the hips on whichever foot is lower, so the planted foot stays on the
    # ground instead of the body floating at a fixed height while the legs
    # scissor underneath it. This is the cheap stand-in for foot IK.
    drop = ANKLE_Z - min(ankle_height(p), ankle_height((p + 0.5) % 1.0))
    sway = math.sin(p * 2 * math.pi)
    pose.update({
        "Root":  ('LOC', Vector((0.0, drop, 0.0))),   # local Y == world Z
        "Hips":  ('X',  3.0 * sway),                  # pelvis roll
        "Spine": ('Y',  3.5),                         # slight forward lean
        "Head":  ('Y', -2.5),
        # floating arms trail the body and counter-swing
        "ArmRoot.L": ('LOC', Vector((0.0,  0.7 * math.sin(p * 2 * math.pi + 0.6), 0.0))),
        "ArmRoot.R": ('LOC', Vector((0.0, -0.7 * math.sin(p * 2 * math.pi + 0.6), 0.0))),
        "UpperArm.L": ('Y', -10.0 * sway),
        "UpperArm.R": ('Y',  10.0 * sway),
        "Forearm.L":  ('Y',   7.0 * sway - 5),
        "Forearm.R":  ('Y',  -7.0 * sway - 5),
        "Hand.L":     ('Y',   5.0 * sway),
        "Hand.R":     ('Y',  -5.0 * sway),
    })
    key(f, pose)
for f, ang in ((1, 0), (WALK_FRAMES + 1, 90)):
    key(f, {"Halo": ('Z', ang)})

def iter_fcurves(act):
    """Blender 4.4+ keeps fcurves in slotted layers/strips/channelbags."""
    if hasattr(act, 'fcurves'):
        yield from act.fcurves
        return
    for layer in act.layers:
        for strip in layer.strips:
            for cb in getattr(strip, 'channelbags', []):
                yield from cb.fcurves

# linear interpolation on the cycle so the loop does not stall at the seam
for act in (A, W):
    n = 0
    for fc in iter_fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'
        n += 1
    print(f"{act.name}: {n} fcurves")

arm.animation_data.action = A
bpy.ops.wm.save_mainfile()
print("ACTIONS:", [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
print("SAVED")
