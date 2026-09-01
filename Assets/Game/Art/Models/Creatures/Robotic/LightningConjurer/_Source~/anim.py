# Authors Idle and Walk actions on ConjurerRig. Additive: touches only pose bones
# of the rig this workflow created.
import bpy, math, re
from mathutils import Matrix, Vector

arm = bpy.data.objects["ConjurerRig"]
# rebuild cleanly on re-run: drop any Idle/Walk (and .001 duplicates) we authored
for _a in [a for a in bpy.data.actions if re.match(r"^(Idle|Walk|Attack)(\.\d+)?$", a.name)]:
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

def world_rot_multi(pb, pairs):
    """Several world-axis rotations composed, expressed in the bone's local space.

    Applied left to right, so ('Y', -60), ('X', 90) reads as "swing it forward, then
    roll it" -- which is the order a hand is actually posed in. One call rather than
    two because rotation_quaternion is a single channel: keying an axis at a time
    would overwrite, not accumulate.
    """
    M = pb.bone.matrix_local.to_3x3()
    Rw = Matrix.Identity(3)
    for axis, deg in pairs:
        Rw = Matrix.Rotation(math.radians(deg), 3, axis) @ Rw
    return (M.inverted() @ Rw @ M).to_quaternion()

def key(frame, pose):
    """pose: {bone: (axis, degrees)}, {bone: ('LOC', Vector)}, or
    {bone: ('ROT', [(axis, degrees), ...])} for a composed rotation."""
    for name, val in pose.items():
        pb = arm.pose.bones[name]
        if val[0] == 'LOC':
            pb.location = val[1]
            pb.keyframe_insert('location', frame=frame)
        elif val[0] == 'ROT':
            pb.rotation_quaternion = world_rot_multi(pb, val[1])
            pb.keyframe_insert('rotation_quaternion', frame=frame)
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

# ------------------------------------------------------------------ ATTACK
# 90 frames @30fps = 3.0s, and the 3 seconds are the spec rather than a choice:
# ConjurerCastModule times the strike off CastSeconds and the two have to agree.
# NOT a cycle -- it plays once, from neutral to neutral, so the walk or idle it
# returns to has nothing to blend away.
#
# The read, in three beats:
#   wind-up  f1-f24    right arm comes up and rolls palm-to-sky; left arm extends
#                      at the target; body settles back onto its heels.
#   charge   f24-f72   fingers close into the cup and hold it. Everything else
#                      breathes a slow tremor -- a machine holding something that
#                      does not want to be held -- and the halo spins up.
#   release  f72-f90   fingers snap open, body drives forward, arms fall back.
#
# WHICH HAND: the right cups, because hands.py could only give the right one
# working fingers (see its header). The left points and stays rigid.
#
# WHERE IT POINTS: nowhere in particular. The clip aims the left arm straight
# down the model's +X, and ConjurerCastModule claims IFacingModule for the whole
# cast so the BODY is what tracks the target. A baked clip cannot do it any other
# way -- the direction is authored, not computed.
ATTACK_FRAMES = 90
K = new_action("Attack", ATTACK_FRAMES)

# Left arm: shoulder at z 32.95 (~8.6 m up once scaled), target on the ground
# maybe 15 m out, so the point is ~30 deg BELOW horizontal, not level. Rotating a
# down-pointing bone by t about world Y sends its tail to (-sin t, 0, -cos t):
# -90 is dead level, so -62 gives that 28 deg of droop.
POINT = -62.0

# Right arm folded up in front of the chest, then rolled palm-up. The roll is the
# second element of the pair and it is 100 deg rather than 90 because the hand
# stalk is not quite square to the forearm at rest.
CUP_ARM, CUP_FORE, CUP_HAND, CUP_ROLL = -38.0, -74.0, -52.0, 100.0

# Curl direction per finger, about world X in each bone's REST frame -- which is
# what makes this work at all: world_rot reads bone.matrix_local, the rest matrix,
# so the curl axis rides with the posed hand instead of being fixed in the world.
#
# Fingers 1 and 2 hang down either side of the palm and have to converge in Y;
# 3 and 4 spread outward and inward and have to swing DOWN onto the other two.
# Signs are read off the rest directions, not guessed:
#   1  points -Z at y -6.25 (inboard)   -> -1, tip toward -Y
#   2  points -Z at y -6.69 (outboard)  -> +1, tip toward +Y
#   3  points -Y                        -> +1, tip toward -Z
#   4  points +Y                        -> -1, tip toward -Z
CURL_SIGN = {1: -1.0, 2: 1.0, 3: 1.0, 4: -1.0}
# Progressive down the chain, the way a finger actually closes: the knuckle barely
# moves and the tip does most of it.
CURL = (("Meta", 10.0), ("A", 34.0), ("B", 46.0), ("C", 40.0))

def cup_pose(amount):
    """Finger curl at `amount` in 0..1. 0 is the open claw, 1 is the closed cup."""
    pose = {}
    for i in (1, 2, 3, 4):
        sign = CURL_SIGN[i]
        for seg, deg in CURL:
            bone = f"Meta{i}.R" if seg == "Meta" else f"Finger{i}{seg}.R"
            pose[bone] = ('X', sign * deg * amount)
    return pose

def arms(reach, tremor=0.0):
    """Both arms at `reach` in 0..1, from hanging to fully presented."""
    return {
        "UpperArm.R": ('Y', CUP_ARM * reach),
        "Forearm.R":  ('Y', CUP_FORE * reach),
        "Hand.R":     ('ROT', [('Y', CUP_HAND * reach),
                               ('X', CUP_ROLL * reach + tremor)]),
        "UpperArm.L": ('Y', POINT * reach),
        "Forearm.L":  ('Y', -8.0 * reach),
        "Hand.L":     ('Y', -4.0 * reach),
        # The floating arms drift in toward the body as they come up, so the cup
        # ends up in front of the chest rather than out at arm's length.
        "ArmRoot.L": ('LOC', Vector((0.0, -1.30 * reach, 0.0))),
        "ArmRoot.R": ('LOC', Vector((0.0,  1.30 * reach, 0.0))),
    }

def body(lean, drop=0.0):
    return {
        "Root":  ('LOC', Vector((0.0, drop, 0.0))),
        "Hips":  ('Y', -2.0 * lean),
        "Spine": ('Y', -6.0 * lean),
        "Head":  ('Y',  4.0 * lean),
    }

# wind-up: arms rise, fingers still open, body eases back
for f, reach, lean in ((1, 0.0, 0.0), (10, 0.45, -0.4), (18, 0.85, -0.9), (24, 1.0, -1.0)):
    p = {}
    p.update(arms(reach))
    p.update(cup_pose(0.0))
    p.update(body(lean))
    key(f, p)

# close the cup
for f, amt in ((24, 0.0), (30, 0.55), (36, 1.0)):
    key(f, cup_pose(amt))

# charge: hold the cup and tremble. Every third frame, same cadence the walk is
# keyed on, so the two actions cost the same to sample.
for f in range(36, 73, 3):
    t = (f - 36) / 36.0
    tremor = 2.2 * math.sin(t * 2 * math.pi * 4.0)
    p = {}
    p.update(arms(1.0, tremor))
    p.update(body(-1.0, drop=0.10 * math.sin(t * 2 * math.pi * 2.0)))
    key(f, p)
    key(f, cup_pose(1.0 - 0.06 * math.sin(t * 2 * math.pi * 4.0)))

# release: the cup opens, the body drives forward onto it, then everything falls
# back to neutral so the clip can hand over to Idle or Walk without a jump.
key(78, dict(list(arms(0.95).items()) + list(body(0.6).items())))
key(78, cup_pose(0.25))
key(84, dict(list(arms(0.55).items()) + list(body(0.9).items())))
key(84, cup_pose(0.0))
key(90, dict(list(arms(0.0).items()) + list(body(0.0).items())))
key(90, cup_pose(0.0))

# Halo spins up through the charge and settles: 180 deg over the clip against the
# idle's 90 over 120 frames, so it is turning about two and a half times faster.
# Still a multiple of 90, which is what keeps a 4-fold-symmetric cube seamless.
for f, ang in ((1, 0), (36, 40), (72, 150), (90, 180)):
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
for act in (A, W, K):
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
