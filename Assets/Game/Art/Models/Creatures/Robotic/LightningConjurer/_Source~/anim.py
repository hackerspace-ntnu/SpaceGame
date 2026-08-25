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
# 90 frames @30fps = 3.0s loop. Slow hover: body breathes, arms drift out of phase,
# halo turns steadily.
A = new_action("Idle", 90)
for f, k in ((1,0.0), (23,1.0), (45,0.0), (68,-1.0), (90,0.0)):
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
        "Thigh.L":    ('Y',  1.0*k),
        "Thigh.R":    ('Y', -1.0*k),
    })
for f, ang in ((1,0), (90,90)):        # halo keeps turning; 90 deg tiles seamlessly on a 4-fold-symmetric cube
    key(f, {"Halo": ('Z', ang)})

# ------------------------------------------------------------------ WALK
# 40 frames @30fps = 1.333s full cycle. Contact 1 / 21, passes at 11 / 31.
W = new_action("Walk", 40)
SW, KN, FT = 24.0, 34.0, 16.0        # thigh swing, knee bend, foot pitch

def leg(side, phase):
    """phase 0..1 through the cycle for this leg; returns pose fragment."""
    t = phase * 2*math.pi
    thigh = SW * math.sin(t)                       # +: forward
    knee  = -KN * max(0.0, math.sin(t - 1.2)) - 4  # bends on the back-swing
    foot  = FT * math.sin(t + 0.9)
    return {f"Thigh.{side}": ('Y', thigh),
            f"Shin.{side}":  ('Y', knee),
            f"Foot.{side}":  ('Y', foot)}

for i in range(0, 41, 4):
    f = i + 1
    p = i / 40.0
    pose = {}
    pose.update(leg("L", p))
    pose.update(leg("R", (p + 0.5) % 1.0))
    # cos: body sits highest at passing (p=0,.5) and drops onto each contact
    # (p=.25,.75), which is what reads as weight
    bob = math.cos(p * 4*math.pi)
    sway = math.sin(p * 2*math.pi)
    pose.update({
        "Root":  ('LOC', Vector((0.0, 0.55*bob, 0.0))),
        "Hips":  ('X',  3.0*sway),                 # pelvis roll
        "Spine": ('Y',  3.5 + 1.2*bob),            # slight forward lean
        "Head":  ('Y', -2.0 - 1.0*bob),
        # floating arms trail the body and counter-swing
        "ArmRoot.L": ('LOC', Vector((0.0,  0.7*math.sin(p*2*math.pi + 0.6), 0.0))),
        "ArmRoot.R": ('LOC', Vector((0.0, -0.7*math.sin(p*2*math.pi + 0.6), 0.0))),
        "UpperArm.L": ('Y', -10.0*sway),
        "UpperArm.R": ('Y',  10.0*sway),
        "Forearm.L":  ('Y',   7.0*sway - 5),
        "Forearm.R":  ('Y',  -7.0*sway - 5),
        "Hand.L":     ('Y',   5.0*sway),
        "Hand.R":     ('Y',  -5.0*sway),
    })
    key(f, pose)
for f, ang in ((1,0), (41,90)):
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
