# Authors the Idle, Walk and Attack actions on ConjurerRig. Additive: touches only pose
# bones of the rig this workflow created.
import bpy, math, re
from mathutils import Matrix, Vector

arm = bpy.data.objects["ConjurerRig"]
# Rebuild cleanly on re-run: drop any action we authored, plus the .001 duplicates Blender
# leaves behind. Dormant and Wake are still on the list although nothing writes them any
# more -- see the SLEEP / WAKE note below -- so that a .blend carrying the old pair loses
# them the first time this runs rather than exporting two dead takes forever.
for _a in [a for a in bpy.data.actions if re.match(r"^(Idle|Walk|Attack|Dormant|Wake)(\.\d+)?$", a.name)]:
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
# 52 frames @30fps = 1.733s full cycle, authored at 8.99 m/s -- the same ground
# speed LightningConjurerBuilder.StrideSpeed has always claimed, now actually true
# of the clip rather than measured off a sixteen-frame window that happened to be
# fast.
#
# ---- why this is solved backwards from the old cycle --------------------------
#
# The old walk drove the JOINTS and hoped the feet followed: thigh = SW*sin(t),
# knee = KN*max(0, sin(t-1.2)), and the body was dropped onto whichever ankle came
# out lower. Every complaint about this creature gliding traces back to that. A
# sinusoidal thigh has zero angular velocity at each extreme, so the planted foot
# STOPS while the body keeps moving; worse, the ankle-height tie-break handed the
# ground to whichever leg was nearest vertical rather than to the leg actually in
# stance, so through the first third of every stance the planted foot slid
# FORWARD -- measured at 4.6 units, about 2.4 m -- before reversing. That is the
# "stops moving its legs, then skates" the brief describes, and no amount of
# retuning SW and KN removes it, because the skate is in the parameterisation.
#
# So the foot is the input now. Each leg follows a TRAJECTORY in the ground's own
# frame -- a straight, constant-velocity line backward through stance and a lifted
# arc forward through swing -- and the hip and knee angles are whatever inverse
# kinematics says puts the ankle there. A foot that is on the ground travels at
# exactly one speed, that speed is the body's speed, and the whole clip has one
# number in it.
#
# ---- the reach problem, which is why the creature now crouches ----------------
#
# The rig stands at FULL LEG EXTENSION: the hip is 20.32 units above the ankle
# against a 20.33-unit leg. At that height the foot cannot reach forward at all,
# which is why the old cycle could only fake a stride. Striding needs slack, so the
# hip rides lower -- and rather than pick a crouch, hip_ride() computes the highest
# the hip may sit for the feet it currently has out, capped at H_CAP. That is the
# vault of a real walk: highest over the stance leg at mid-step, lowest during
# double support when one foot is reaching forward and the other back.
#
# ---- the numbers, and which of them are free ---------------------------------
#
# HALF and WALK_FRAMES together ARE the ground speed:
#     v = 2*HALF / (DUTY * WALK_FRAMES/30)   blender units per second
# and the builder's StrideSpeed is that times the metre scale. Change either and
# re-run stride.py, which measures the sole directly and asserts the answer.
WALK_FRAMES = 52          # 1.733 s at 30 fps
W = new_action("Walk", WALK_FRAMES)

DUTY   = 0.58    # fraction of the cycle each foot spends planted. Above 0.5, so the
                 # two stances overlap and the creature is never airborne.
HALF   = 8.665   # half the stride: how far in front of the hip the foot lands.
LIFT   = 3.5     # peak swing clearance, blender units
FT     = 10.0    # toe pitch through swing. Zero at toe-off AND at touchdown, because
                 # a foot pitching while planted is a foot sliding.
PHASE0 = 0.25    # cycle phase frame 1 sits at. A quarter turn, so neither touchdown
                 # lands on the loop seam and both land on whole frames (14 and 40).
KNEE_MIN = 8.0   # the knee never straightens past this; a locked leg reads as a stilt
H_CAP  = 19.7    # ceiling on the hip's ride height, in units above the ankle plane.
                 # Without it the hip vaults the full 20.28 the leg allows and the
                 # creature heaves a metre a step.

# The ankle JOINT's z in the rest pose. Every stance target is this exact height, which
# is what puts the sole on the floor: the rest pose already stands the creature with its
# soles at z 2.757 (the builder's BlenderFloor), 2.343 below the joint, and stance holds
# the foot flat so that offset never changes.
ANKLE_REST_Z = 5.10
HIP_REST_Z = 25.42


def _segment(bone):
    """(length, rest angle) of a leg bone in the sagittal plane.

    The rest angle matters and is easy to miss: the thigh leans 1.6 deg forward at
    rest, so a keyed 0 is not a vertical thigh. Reading it off the armature keeps the
    IK honest if rig.py's bone table ever moves.
    """
    b = arm.data.bones[bone]
    v = b.tail_local - b.head_local
    return math.hypot(v.x, v.z), math.degrees(math.atan2(-v.x, -v.z))


LT, REST_T = _segment("Hip_L")     # thigh
LS, REST_S = _segment("Knee_L")    # shin


def _reach(knee_deg):
    """Hip-to-ankle distance at a given knee flexion."""
    return math.sqrt(LT * LT + LS * LS
                     + 2 * LT * LS * math.cos(math.radians(knee_deg)))


D_MAX = _reach(KNEE_MIN)


def hermite(u):
    """Swing progress 0..1, with the foot ENTERING and LEAVING at stance speed.

    A cubic through (0,0) and (1,1) whose end slopes are both -(1-DUTY)/DUTY, which
    is the stance velocity expressed in swing-normalised time. That single condition
    is what makes the cycle C1: the foot is already travelling backward at exactly
    the body's speed when it touches down, so there is no jerk at the contact and no
    frame where it has to catch up. It overshoots slightly at each end -- the foot
    drifts a little further back after toe-off and reaches a little further forward
    before it lands, then draws back -- which is what real feet do and is the whole
    reason this is not a smoothstep.
    """
    m = -(1.0 - DUTY) / DUTY
    return m * (2 * u ** 3 - 3 * u * u + u) + (3 * u * u - 2 * u ** 3)


def foot_track(q):
    """(forward offset from the hip, height above the rest ankle plane, toe pitch).

    q is the LEG's own phase: 0 is touchdown, DUTY is toe-off, 1 is touchdown again.
    """
    q %= 1.0
    if q < DUTY:                                  # stance: a straight line, constant speed
        return HALF - 2.0 * HALF * (q / DUTY), 0.0, 0.0
    u = (q - DUTY) / (1.0 - DUTY)                 # swing
    x = -HALF + 2.0 * HALF * hermite(u)
    # Warped so the peak lands at 40% of the swing rather than halfway -- the foot
    # snaps clear at toe-off and settles slowly -- and raised to 1.2 rather than
    # squared so the arc stays FAT near the end. That tail is not decoration: the
    # hermite above reaches past the landing point and draws back, and a foot doing
    # that at ankle height would scuff. At 1.2 it is still a quarter of a unit up
    # when it starts drawing back, and only the last frame puts it down.
    z = LIFT * math.sin(math.pi * u ** 0.75) ** 1.2
    return x, z, FT * math.sin(2 * math.pi * u)


def leg_phase(p, side):
    q = p + PHASE0
    return q + 0.5 if side == "R" else q


def hip_ride(p):
    """How high the hip may sit above the ankle plane at cycle phase p.

    The min over BOTH legs rather than over the planted ones only. A swinging foot is
    lifted, so its own limit sits LIFT higher and it never binds -- which means the
    constraint set never changes size and the ride height comes out continuous
    instead of stepping every time a foot leaves the ground.
    """
    best = H_CAP
    for side in ("L", "R"):
        x, z, _ = foot_track(leg_phase(p, side))
        best = min(best, z + math.sqrt(max(D_MAX * D_MAX - x * x, 1.0)))
    return best


def leg_ik(hip_x, hip_z, target_x, target_z):
    """Keyed (thigh, knee) degrees putting the ankle joint on the target.

    Two-link IK in the sagittal plane. The knee comes out of the cosine rule and is
    therefore always >= 0, which is the one thing the old cycle needed a comment to
    promise: a knee cannot bend the other way, and here it cannot be asked to.
    """
    u, w = hip_x - target_x, hip_z - target_z
    d = min(math.hypot(u, w), D_MAX)
    cos_k = (d * d - LT * LT - LS * LS) / (2.0 * LT * LS)
    knee = math.degrees(math.acos(max(-1.0, min(1.0, cos_k))))
    delta = math.degrees(math.atan2(LS * math.sin(math.radians(knee)),
                                    LT + LS * math.cos(math.radians(knee))))
    thigh_world = math.degrees(math.atan2(u, w)) - delta
    shin_world = thigh_world + knee
    keyed_thigh = thigh_world - REST_T
    return keyed_thigh, (shin_world - REST_S) - keyed_thigh


# ---- the pass ---------------------------------------------------------------
#
# Every frame, not every third: the leg angles are read back off a posed rig, so a
# frame that is not keyed is a frame whose hip position was never measured. The FBX
# exporter bakes per-frame regardless, so this costs nothing downstream.
_miss = 0.0
for i in range(0, WALK_FRAMES + 1):
    f = i + 1
    p = i / float(WALK_FRAMES)
    sway = math.sin(2 * math.pi * (p + PHASE0))

    # The body first, and on its own: the hips have to be where they are going to be
    # before the legs can be solved against them.
    body = {
        "Root":  ('LOC', Vector((0.0, hip_ride(p) - (HIP_REST_Z - ANKLE_REST_Z), 0.0))),
        # Rolls toward the leg that is carrying, which lifts the swing side's hip out
        # of the way. Small: the legs cannot abduct, so a big roll would drag the
        # planted foot sideways.
        "Hips":  ('X',  3.0 * sway),
        "Spine": ('Y',  3.5),
        "Head":  ('Y', -2.5),
        # floating arms trail the body and counter-swing
        "ArmRoot.L": ('LOC', Vector((0.0,  0.7 * math.sin(2 * math.pi * (p + PHASE0) + 0.6), 0.0))),
        "ArmRoot.R": ('LOC', Vector((0.0, -0.7 * math.sin(2 * math.pi * (p + PHASE0) + 0.6), 0.0))),
        "UpperArm.L": ('Y', -10.0 * sway),
        "UpperArm.R": ('Y',  10.0 * sway),
        "Forearm.L":  ('Y',   7.0 * sway - 5),
        "Forearm.R":  ('Y',  -7.0 * sway - 5),
        "Hand.L":     ('Y',   5.0 * sway),
        "Hand.R":     ('Y',  -5.0 * sway),
    }
    # hip_ride() works in the sagittal plane, where the pelvis roll does not exist. It
    # does though: rolling 3 deg lifts one hip 0.11 units, and 0.11 units past a leg
    # that is already at its reach limit is 0.11 units of skate. So the ride height is
    # a TARGET, and the Root is nudged until the higher of the two hips actually sits
    # at it. One correction converges -- Root translation moves both hips 1:1 in z.
    for _try in range(4):
        key(f, body)
        sc.frame_set(f)
        bpy.context.view_layer.update()
        err = (max(arm.pose.bones["Hip_L"].head.z, arm.pose.bones["Hip_R"].head.z)
               - (ANKLE_REST_Z + hip_ride(p)))
        if abs(err) < 1e-4:
            break
        body["Root"] = ('LOC', Vector((0.0, body["Root"][1].y - err, 0.0)))

    for side in ("L", "R"):
        hip = arm.pose.bones[f"Hip_{side}"].head
        x, z, pitch = foot_track(leg_phase(p, side))
        tx, tz = hip.x + x, ANKLE_REST_Z + z
        # Solved against the posed rig rather than against a model of it, by feeding
        # the residual back into the goal. The planar IK above is a couple of hundredths
        # of a unit out on its own -- the pelvis roll tilts the whole leg plane and the
        # thigh does not sit exactly in it -- and a couple of hundredths is exactly the
        # size of the slide that reads as gliding. Two passes take it to zero.
        gx, gz = tx, tz
        for _try in range(4):
            thigh, knee = leg_ik(hip.x, hip.z, gx, gz)
            key(f, {f"Hip_{side}":   ('Y', thigh),
                    f"Knee_{side}":  ('Y', knee),
                    # The sole is held at `pitch` in WORLD terms, so subtracting the two
                    # joints above it is not a flourish -- key() composes rotations down
                    # the chain, and without this the foot inherits the whole leg's angle
                    # and ploughs into the ground.
                    f"Ankle_{side}": ('Y', pitch - (thigh + knee))})
            bpy.context.view_layer.update()
            ank = arm.pose.bones[f"Ankle_{side}"].head
            ex, ez = ank.x - tx, ank.z - tz
            if abs(ex) + abs(ez) < 1e-4:
                break
            gx, gz = gx - ex, gz - ez
        _miss = max(_miss, abs(ex) + abs(ez))

# The IK clamps rather than throws when a target is out of reach, so an over-long
# HALF would quietly come back as a skate instead of as an error. This is the assert
# that makes that impossible.
assert _miss < 0.02, (
    f"the walk's feet miss their targets by up to {_miss:.3f} units -- HALF={HALF} is "
    f"further than the leg can reach at hip ride {H_CAP}. Lower HALF or H_CAP.")

WALK_SPEED_U = 2.0 * HALF / (DUTY * WALK_FRAMES / 30.0)
print(f"[anim] Walk {WALK_FRAMES}f ({WALK_FRAMES / 30.0:.3f}s), stride {2 * HALF:.2f}u, "
      f"authored at {WALK_SPEED_U:.3f} u/s = {WALK_SPEED_U * 0.5215211:.3f} m/s; "
      f"feet track their targets to {_miss * 1000:.1f} milliunits")

for f, ang in ((1, 0), (WALK_FRAMES + 1, 90)):
    key(f, {"Halo": ('Z', ang)})

# ------------------------------------------------------------------ ATTACK
# 135 frames @30fps = 4.5s. Four beats, and the middle one is the specification:
#
#   raise     f1-f35     1.17s  the right arm lifts the staff overhead until the
#                               turbine stands clear above the head. The left arm
#                               sweeps out and open, counterbalancing.
#   charge    f35-f105   2.33s  the staff is held aloft and trembles while the
#                               turbine spins up. ConjurerStaffCharge lights the
#                               emitter and throws arcs off the blades; nothing
#                               about them is keyed here.
#   strike    f105-f120  0.5s   the staff thrusts higher and straighter and the
#                               free left hand snaps down and out to point at the
#                               target. The bolt falls out of the sky at f120.
#   recover   f120-f135  0.5s   recoil, then neutral by the last frame so the
#                               clip can hand back to Idle or Walk cleanly.
#
# ---- the bolt no longer leaves the creature -----------------------------------
#
# The old clip fired from between the two palms and every pose in it existed to
# serve that: both arms came up to a ring in the chest, held either side of it,
# then threw forward into a steeple whose gap was the muzzle. There is no ring and
# no muzzle now. The lightning comes DOWN, from the sky, onto wherever the target
# is standing, so what the animation has to sell is not aiming but SUMMONING --
# and the two look nothing alike. Hence one arm holding a conductor as high as it
# will go, and the other pointing out the victim.
#
# The 2.33s charge is the beat the player has to learn to read. It is shorter than
# the old 3.0s only because the raise is longer: lifting a 39-unit staff overhead
# cannot be done in the 0.5s the old reach took without looking weightless.
#
# ---- the clip is 4.5s and the module's castSeconds is 4.0s --------------------
#
# Unchanged, and deliberately so: FIRE (120) is when the bolt lands and
# ATTACK_FRAMES (135) is when the arms are back at rest, with the recoil in
# between. LightningConjurerBuilder derives castSeconds from FireFrame and the
# importer's clip length from ATTACK_FRAMES. Conflating them would drop the bolt
# half a second after the staff had already come down.
ATTACK_FRAMES = 135
RAISE_END, CHARGE_END, FIRE = 35, 105, 120


def ease(t):
    """Smoothstep. Clamped, so callers can hand it any frame."""
    t = min(1.0, max(0.0, t))
    return t * t * (3.0 - 2.0 * t)


def hand_frame(side):
    """(fingers, thumb) of one hand, as world unit vectors. MEASURED off the rig.

    `fingers` is the Hand bone's own direction. The THUMB axis is the fist's bore,
    taken from the KNUCKLE LINE -- the row the four fingers' base joints sit on,
    running away from the thumb -- with the finger component projected out, since
    the fingers splay a few degrees off perpendicular.

    staff.py holds the same function and poses the hand with it; if the two ever
    drift the fingers close on a different circle from the one the shaft was placed
    on, so keep them in step.
    """
    b = arm.data.bones
    f = (b[f"Hand.{side}"].tail_local - b[f"Hand.{side}"].head_local).normalized()
    k = b[f"Pinky1.{side}"].head_local - b[f"Index1.{side}"].head_local
    return f, -(k - f * k.dot(f)).normalized()


def curl_axis(digit, side):
    """Which axis closes a digit, in the world, with the sign already in it.

    DERIVED rather than written down, and that is the change worth noting. A finger
    closes about the knuckle line -- the THUMB axis -- and the thumb closes about
    the FINGER axis; both are facts about a hand, so they hold whatever pose the
    rest of the rig is in. The world axis LETTERS they happen to line up with are
    not, and the previous version wrote down the letters, together with a warning
    that they had moved once already when staff.py first turned the right hand and
    would have to be kept in step by hand if it ever turned it again. It has now:
    the right arm rests in a carry, with the elbow bent and the hand level, and the
    letters are different again.

    Curling on a stale axis is a failure that reports nothing -- the fingers barely
    move at all -- which is why this is measured instead.

    The two hands differ only in chirality, and that is the sign flip: a right
    hand's fingers close about +thumb and its thumb about -fingers, a left hand's
    the other way round.
    """
    f, t = hand_frame(side)
    if side == "R":
        return -f if digit == "Thumb" else t
    return f if digit == "Thumb" else -t


# Degrees per phalanx, progressive down the chain the way a finger actually
# closes: the knuckle barely moves and the tip does most of it.
CUP_CURL = {"Thumb": (30.0, 34.0, 30.0), "Index": (32.0, 48.0, 44.0),
            "Middle": (34.0, 50.0, 46.0), "Ring": (32.0, 48.0, 44.0),
            "Pinky": (30.0, 46.0, 42.0)}


def digits_pose(side, table, amount):
    """Every phalanx of one hand at `amount` of `table`. 0 is the open hand."""
    pose = {}
    for digit, degrees in table.items():
        axis = curl_axis(digit, side)
        for i, deg in enumerate(degrees, start=1):
            pose[f"{digit}{i}.{side}"] = (axis, deg * amount)
    return pose


# ---- only one hand is animated, and it never changes --------------------------
#
# GRIP_CURL is the right hand closed round the shaft, and it is CONSTANT: keyed at
# the same value on every frame of every clip, Idle and Walk included, because the
# staff is bone-parented to Hand.R and is therefore in that hand permanently. A
# hand that opens between casts is a hand a staff hangs off with the fingers
# splayed around nothing.
#
# 0.85 of a cup. A closed finger's joints lie on a circle whose radius the curl
# controls -- 0.76 here, 0.651 at a full curl -- and the shaft sits on that circle's
# centre. A full curl was tried and is worse: the joints land near the 0.60 surface
# but the finger MESH is thick, so its inner face drives through the shaft and a
# fingertip pokes out the far side. 0.85 keeps the phalanges against it and the
# poke-through hidden. staff.py holds the same numbers and places the shaft from
# them, so the two have to stay in step.
#
# The LEFT hand is not posed at all any more. It used to open through the charge
# and close to a point at the strike; the arm is held down now, and a hand
# gesturing on the end of a limb that is not moving reads as a twitch.
GRIP_CURL = {d: tuple(v * 0.85 for v in degs) for d, degs in CUP_CURL.items()}

STAFF_BONE = "Staff"
ROTOR_BONE = "StaffRotor"
TIP_BONE = "StaffTip"

# ---- the staff arm, and the one piece of arithmetic that makes it work --------
#
# The staff is bone-parented to Hand.R, so the shaft's direction IS the hand's,
# and the hand's is the product of every rotation above it in the chain. Every one
# of those is about world Y -- the convention this whole file runs on, for the
# reason given in the WALK notes -- and rotations about a common axis COMMUTE AND
# ADD. So the shaft's lean off vertical is just the sum of the three angles, and
# the wrist becomes a SOLVED quantity rather than a tuned one:
#
#     lean = upper + fore + wrist        =>      wrist = lean - upper - fore
#
# Getting this wrong is not subtle. At rest the shaft stands vertical, so an arm
# swung to -75 with the wrist left alone lays the entire staff over at 75 degrees
# with the turbine pointing at the horizon -- in an attack whose whole premise is
# that it points at the sky. The assertion under HOLD/PEAK checks the result
# against the posed rig rather than trusting the algebra.
def staff_arm(upper, fore, lean):
    return {
        "UpperArm.R": [('Y', upper)],
        "Forearm.R":  [('Y', fore)],
        "Hand.R":     [('Y', lean - upper - fore)],
    }


def measure(rot, bone):
    """World position of `bone` under `rot`, with every other bone at rest."""
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    for b, pairs in rot.items():
        pb = arm.pose.bones[b]
        pb.rotation_quaternion = world_rot_multi(pb, pairs)
    bpy.context.view_layer.update()
    return arm.pose.bones[bone].matrix.translation.copy()


# ---- the shoulder does not move, and the arm is posed by ANGLE ---------------
#
# Two things this went through before arriving here, and both are worth keeping
# because both produced poses that shipped.
#
# The first version placed the hand by TRANSLATING `ArmRoot.R`. The arms float, so
# ArmRoot is a free 3-DOF translation of the whole limb and the correction is exact
# rather than iterated -- and it slides the entire arm bodily through space. Nothing
# else on the creature moves with it, so at full extension the shoulder ended up a
# couple of units out and up from where the torso expects it and the limb visibly
# tore off the body. `ArmRoot.R` is pinned at zero for the whole clip now.
#
# The second solved two-link IK for a target GRIP POSITION with the shoulder fixed.
# That is the right shape of answer for an arm that is reaching for something, and
# this one is not: it is holding a staff, and what has to stay true is a
# RELATIONSHIP between the joints rather than a point in space. The wrist is the
# whole of it. The staff is bone-parented to Hand.R, so the shaft's direction IS the
# hand's; ask for a grip position and the hand's angle is already spoken for, so
# whatever is left over lands in the wrist -- and what landed there was 30 to 45
# degrees on top of a rest pose that was itself a 90-degree sideways crank. The
# arm came out thrust straight forward with the fist turned out on the end of it,
# which is the thing this pass exists to fix.
#
# So the pose is three ANGLES, and the position is what gets checked:
#
#     upper   the shoulder's swing about world Y. Negative is forward and up.
#     fore    the elbow's, in the same sense, on top of the shoulder's.
#     lean    how far off vertical the shaft ends up. The WRIST takes the
#             remainder, and that is the one piece of arithmetic here.
#
# ---- the wrist is arithmetic, not taste --------------------------------------
#
# Every rotation in this clip is about world Y -- the convention the whole file
# runs on -- and rotations about a common axis commute and add. The staff stands
# vertical in the rest pose, so the shaft's lean off vertical is just the sum:
#
#     lean = upper + fore + wrist      =>      wrist = lean - upper - fore
#
# Getting it wrong is not subtle. An arm swung to -150 with the wrist left alone
# lays the whole staff over at 150 degrees, turbine pointing at the floor, in an
# attack whose entire premise is that it points at the sky.
#
# The useful consequence is that the wrist's BEND -- the angle between the hand and
# the forearm, which is what reads as a limb or as a break -- depends on `wrist`
# ALONE. Everything above the wrist turns the hand and the forearm together and
# cannot change the angle between them. The carry pose rests at 40 degrees of
# extension and a positive `wrist` straightens it from there, so the clip is free
# to move the arm as far as it likes provided the three numbers sum to a small
# lean. build_pose asserts that, against the posed rig rather than the algebra.
#
# ---- AN ELBOW ONLY BENDS ONE WAY, and that is a hard limit ---------------------
#
# The version before this one swung the shoulder through 150 degrees to stand the
# elbow above the head, and it hyperextended the elbow to do it: the joint came out
# at +64 degrees at the hold and +91 at the strike, which is 244 and 271 measured
# the way a person would measure an arm. Snapped backwards.
#
# It shipped because the guard could not see it. The assertion measured
# `ud.angle(fd)` -- the UNSIGNED angle between the two bones, which is 0..180 by
# construction and identical for a joint folded 70 degrees the right way and one
# folded 70 degrees the wrong way. An unsigned angle cannot express the thing that
# was wrong, so it read 65 and 92 and passed. The measure has to carry the SIGN,
# and it has to be signed about the joint's own hinge; see sagittal() below.
#
# The constraint is one line, and it prices the whole clip:
#
#     elbow = FORE_REST + fore        (FORE_REST is about -48, the carry)
#
# because `fore` IS the elbow's rotation, on top of the shoulder's, about the same
# world Y every angle here turns about. The shoulder cancels out entirely. So
# keeping the elbow flexed means keeping `fore` well under +48, and that is what
# caps the raise -- not taste, and not the shoulder.
#
# ---- what is reachable once the elbow is honest -------------------------------
#
# The hand has to stay LEVEL for the shaft to stand up, so the forearm has to stay
# near horizontal, so the fist sits about eight units forward of the elbow whatever
# else happens. The elbow itself cannot get far above the shoulder without `fore`
# going positive past the limit. Between them:
#
#   The fist CANNOT get above the head. A raise that puts it there needs either a
#   hyperextended elbow (the last version) or a forearm pointing up, which folds
#   the wrist to 90 degrees or more (the version before that). Both were tried and
#   both are visible in the git history as broken poses.
#
#   So the cast PRESENTS the staff forward and up rather than hoisting it overhead.
#   The shoulder swings to -60, the elbow stays flexed at 38 degrees, the wrist
#   straightens from the carry's 40 of extension to 11, and the emitter goes from
#   4.5 units clear of the crown at rest to 11.2 at the hold and 12.5 at the strike.
#   Nearly triple the clearance, on an arm that reads as an arm.
#
# The strike then EXTENDS rather than folding further: elbow 38 to 26, wrist 11 to
# 1, and the shaft comes from 20 degrees back to 10. That is a drive, and it is the
# right way round -- the previous clip folded the elbow tighter on the commit,
# which is what a limb does recoiling, not striking.
HOLD_ARM = (-60.0, 10.0, -20.0)       # (upper, fore, lean) -- the charge
PEAK_ARM = (-72.0, 22.0, -10.0)       # the strike

# The staff is braced BACK while it charges and comes toward upright as the bolt
# falls: lean runs -20 to -10, so the turbine rises and sweeps forward through the
# strike beat.
#
# Back rather than forward, and that is a change from the previous clip's 11 to 2.
# Leaning a twenty-unit shaft forward off a fist that is already in front of the
# head throws the turbine four units further out and a metre and a half DOWN, so
# the beat that is supposed to read as the commit dropped the only thing the eye is
# tracking. Bracing it back puts the turbine at its highest through the charge and
# spends the strike driving it upright, which is the same gesture and the right way
# round.

# ---- the left arm does not take part -----------------------------------------
#
# It used to: it swept out and open through the charge, then snapped down and
# forward to point at the victim on the strike. That is gone, and the clip is a
# ONE-ARMED gesture now -- the staff hand goes up and the other stays at its side.
#
# The left arm is still keyed, at neutral, rather than simply left out of the
# action. A bone with no curves in a clip holds its bind pose, which looks the
# same standing still but is not the same thing while the animator is blending
# out of Idle or Walk: with nothing to blend toward, the arm drifts from wherever
# the previous clip's swing left it. Two flat keys give it somewhere definite to
# be.
LEFT_BONES = ("ArmRoot.L", "UpperArm.L", "Forearm.L", "Hand.L")
LEFT_DOWN = {
    "ArmRoot.L":  ('LOC', Vector((0.0, 0.0, 0.0))),
    "UpperArm.L": ('ROT', [('Y', 0.0)]),
    "Forearm.L":  ('ROT', [('Y', 0.0)]),
    "Hand.L":     ('ROT', [('Z', 0.0)]),
}

def body_top():
    """The top of the BODY, off its real meshes, with the rig standing at neutral.

    The halo is retired and the staff is not part of what the turbine has to clear,
    so this is the head and the torso only. Measured rather than typed so it cannot
    go stale the way the last set of hand-written body figures did.
    """
    return max((o.matrix_world @ v.co).z
               for o in bpy.data.objects
               if o.type == 'MESH' and o.parent_bone in ("Hips", "Spine", "Head")
               for v in o.data.vertices)


def sagittal(v):
    """A bone direction as a SIGNED angle, in the sense every joint here turns in.

    Straight down is 0 and forward is negative, matching the walk notes' convention.
    It is the direction's phase in the XZ plane, which is exactly the right measure
    because every rotation in this clip is about world Y: turning a bone by `t`
    about Y adds `t` to this and nothing else, whatever y-component the rest pose
    gave it. The right arm's rest pose has one -- staff.py splays the forearm 20
    degrees outboard -- so a plain 3D angle would not compose this cleanly.

    THE SIGN IS THE POINT. Two bones meeting at 70 degrees are a folded elbow or a
    snapped one depending on which side the second is on, and an unsigned angle
    says 70 to both. That is not a hypothetical: it is how a clip with the elbow
    bent 244 degrees backwards passed its own assertion and shipped.
    """
    return math.degrees(math.atan2(-v.x, -v.z))


def joint(parent, child):
    """The signed angle of `child` relative to `parent`, folded into (-180, 180]."""
    return (sagittal(child) - sagittal(parent) + 180.0) % 360.0 - 180.0


def arm_read(rot):
    """Pose the rig on `rot` and read back what the pose actually looks like.

    Everything here is measured off the posed armature rather than derived, which
    is the point: the angles are the input now, so the geometry they produce is
    what has to be checked.
    """
    measure(rot, STAFF_BONE)                 # leaves the rig standing in this pose
    pbs = arm.pose.bones
    ud = (pbs["UpperArm.R"].tail - pbs["UpperArm.R"].head).normalized()
    fd = (pbs["Forearm.R"].tail - pbs["Forearm.R"].head).normalized()
    hd = (pbs["Hand.R"].tail - pbs["Hand.R"].head).normalized()
    shaft = (pbs[STAFF_BONE].matrix.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
    return {
        "grip": pbs[STAFF_BONE].head.copy(),
        "tip": pbs[TIP_BONE].head.copy(),
        "lean": math.degrees(shaft.angle(Vector((0.0, 0.0, 1.0)))),
        "elbow": joint(ud, fd),              # negative = flexed, the way it bends
        "wrist": joint(fd, hd),              # negative = extension, as the carry
    }


def build_pose(upper, fore, lean):
    """The staff arm as three rotations, checked against the rig it produces.

    Nothing translates. The shoulder is pinned, so these three angles are the whole
    pose, and every number the assertions look at is read back off the posed
    armature -- a sign slip in the wrist arithmetic would otherwise leave the arm
    somewhere plausible-looking and silently wrong.
    """
    rot = staff_arm(upper, fore, lean)
    got = arm_read(rot)

    assert abs(got["lean"] - abs(lean)) < 1.0, (
        f"the shaft leans {got['lean']:.1f} deg, not the {abs(lean):.1f} asked "
        "for -- the wrist correction in staff_arm() no longer matches the rig")

    # THE assertion in this file: an elbow only bends one way. Negative is flexion,
    # the direction the carry pose already sits in, and the joint must stay there.
    # Crossing zero is not a near miss -- it is the arm snapped backwards, and it is
    # what the unsigned check this replaced could not see.
    #
    # `fore` is the whole of it: elbow = FORE_REST + fore, with the shoulder
    # cancelling out, so the fix for a failure here is always to lower `fore`.
    assert ELBOW_LIMIT[0] <= got["elbow"] <= ELBOW_LIMIT[1], (
        f"the elbow sits at {got['elbow']:+.0f} deg, outside "
        f"{ELBOW_LIMIT[0]:+.0f}..{ELBOW_LIMIT[1]:+.0f}. Positive is BACKWARDS -- a "
        f"human reading of {180.0 + got['elbow']:.0f} degrees, an arm snapped the "
        "wrong way; too near zero and it is locked straight. elbow = "
        f"{REST_ELBOW:+.0f} + fore, so `fore` is what to change")

    # The wrist, the same way. It may straighten from the carry and must never fold
    # past it, nor flex through to the other side: a hand bent further than the pose
    # the creature stands in has been twisted to make the staff fit.
    assert REST_WRIST - 1.0 <= got["wrist"] <= 5.0, (
        f"the wrist sits at {got['wrist']:+.0f} deg against the carry pose's "
        f"{REST_WRIST:+.0f}; the arm is being twisted to hold the staff up. The "
        "wrist is `lean` minus `upper` minus `fore`, so bring the three closer to "
        "summing to zero")
    assert got["tip"].z > BODY_TOP + 8.0, (
        f"the emitter tops out at z {got['tip'].z:.1f} against a body reaching "
        f"{BODY_TOP:.1f}; the staff is supposed to be held CLEAR of the crown")

    print(f"[anim] upper {upper:7.1f}  fore {fore:7.1f}  "
          f"wrist {lean - upper - fore:6.1f} | lean {got['lean']:4.1f}  "
          f"elbow {got['elbow']:+5.0f}  wrist {got['wrist']:+5.0f} | "
          f"grip ({got['grip'].x:5.1f}, {got['grip'].z:5.1f})  "
          f"emitter ({got['tip'].x:5.1f}, {got['tip'].z:5.1f})")
    return {b: ('ROT', v) for b, v in rot.items()}


# Solved with NO action attached. An assigned action re-evaluates the pose from
# its own keys on the next depsgraph update, which would overwrite the trial pose
# these are trying to measure.
arm.animation_data_clear()

# The carry pose's own wrist bend, measured the same way every other pose here is
# measured. staff.py sets it -- CARRY_ELBOW against a level hand -- and this file
# has no business writing the number down a second time.
_rest = arm_read(staff_arm(0.0, 0.0, 0.0))
REST_ELBOW, REST_WRIST = _rest["elbow"], _rest["wrist"]

# How far the elbow may travel from there. The upper bound is what stops the joint
# reaching zero and hyperextending past it; the lower is what stops the arm doubling
# back on itself. staff.py's CARRY_ELBOW sets where it starts, and `fore` moves it.
ELBOW_LIMIT = (-140.0, -12.0)

# ...and the body's height, taken now, while that call has the rig standing at
# neutral. Taken any earlier it would measure the creature wherever the last frame
# of Walk left it.
BODY_TOP = body_top()
print(f"[anim] the carry pose rests with the elbow at {REST_ELBOW:+.0f} and the "
      f"wrist at {REST_WRIST:+.0f} deg; the body tops out at z {BODY_TOP:.1f}")

HOLD = build_pose(*HOLD_ARM)
PEAK = build_pose(*PEAK_ARM)
NEUTRAL = {b: ('ROT', [(ax, 0.0) for ax, _ in v[1]]) for b, v in HOLD.items()}
for pb in arm.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)

K = new_action("Attack", ATTACK_FRAMES)


def lerp_pose(a, b, t):
    """One pose between two, `t` from a to b. Per-bone, per-axis."""
    out = {}
    for bone, va in a.items():
        vb = b[bone]
        if va[0] == 'LOC':
            out[bone] = ('LOC', va[1].lerp(vb[1], t))
        else:
            out[bone] = ('ROT', [(ax, da + (db - da) * t)
                                 for (ax, da), (_, db) in zip(va[1], vb[1])])
    return out


# ---- overlap -----------------------------------------------------------------
# Each joint starts later and finishes later than the one above it. A shoulder,
# elbow and wrist moving in lockstep read as one rigid lever swinging; the lag is
# most of the difference between a servo and a limb. Keyed by bone PREFIX so one
# table covers both arms.
#
# The raise windows are more than twice the old reach's, because they are lifting
# a staff taller than the creature rather than moving an empty hand a few units.
# ArmRoot is absent from all three: it does not move any more, so there is
# nothing to stage. It is keyed flat at the end of this section.
#
# The SPREAD of a window is not free any more, and that is new. lean = upper +
# fore + wrist is an identity, so with the wrist on its own schedule the three
# cannot all be held: staging them apart is exactly what makes the shaft tip while
# the arm is moving. That is wanted on the raise -- a twenty-unit staff swinging
# back as it is hoisted is what weight looks like, and it peaks around 40 degrees
# there -- and it is not wanted on the DROP, where a wide spread threw the turbine
# forward past vertical in the five frames the recovery has to settle in. The drop
# windows are tight for that reason, the raise's deliberately are not.
RAISE_LAG = {"UpperArm": (1, 28), "Forearm": (4, 33), "Hand": (8, 35)}
PEAK_LAG = {"UpperArm": (105, 115), "Forearm": (107, 118), "Hand": (109, 121)}
DROP_LAG = {"UpperArm": (121, 133), "Forearm": (122, 134), "Hand": (123, 135)}


def staged(a, b, f, lag, wobble=0.0):
    """Between two poses at frame `f`, every joint on its own schedule.

    Past its window a joint SETTLES rather than stopping dead: a small damped
    oscillation that dies out over about ten frames. Mechanical arms overshoot
    and recover, and arriving exactly on target is the other tell of a servo.
    """
    out = {}
    for bone in a:
        t0, t1 = lag[bone.split('.')[0]]
        if f >= t1 and wobble:
            t = 1.0 + wobble * math.exp(-(f - t1) / 7.0) * math.sin((f - t1) / 2.6)
        else:
            t = ease((f - t0) / float(t1 - t0))
        one = lerp_pose({bone: a[bone]}, {bone: b[bone]}, t)
        out.update(one)
    return out


def body(lean, drop=0.0):
    return {
        "Root":  ('LOC', Vector((0.0, drop, 0.0))),
        "Hips":  ('Y', -2.0 * lean),
        "Spine": ('Y', -6.0 * lean),
        "Head":  ('Y',  4.0 * lean),
    }


# ---- f1-f35, the raise -------------------------------------------------------
# Sampled every frame rather than posed at two keys. The shape of the motion
# lives in ease() and the lag tables, and sampling is what carries that shape
# onto linear keys -- two keys would interpolate straight through it and every
# joint would arrive together again.
#
# The body leans BACK (negative), and further than the old clip's -0.5: a giant
# hoisting something overhead settles onto its heels, and without it the arm
# looked like it was moving independently of the creature holding it.
for f in range(1, RAISE_END + 1):
    p = staged(NEUTRAL, HOLD, f, RAISE_LAG, wobble=0.05)
    p.update(body(-ease((f - 1) / 26.0) * 0.8))
    p.update(digits_pose("R", GRIP_CURL, 1.0))
    key(f, p)

# ---- f35-f105, the charge ----------------------------------------------------
# Hold the pose and tremble: a machine holding up something that is filling with
# charge.
#
# The tremor is a ROTATION now, and it has to be. It used to ride on the ArmRoot
# translation, which was available then because the arm was being placed by
# sliding it; with the shoulder pinned there is no translation left to shake, and
# putting it back would be reintroducing the exact thing that tore the limb off
# the body.
#
# It is split between the shoulder and the elbow, in OPPOSITE directions, and that
# is what keeps it small at the top of the staff. Shaking one joint swings a lever
# more than twenty units long and the turbine thrashes through a huge arc for a
# fraction of a degree; countering at the elbow leaves the hand jittering roughly
# in place while the arm works, which is what holding something heavy looks like.
for f in range(RAISE_END, CHARGE_END + 1, 3):
    t = (f - RAISE_END) / float(CHARGE_END - RAISE_END)
    # Escalating: the shake grows through the charge, which is the part the
    # player reads as "this is nearly ready".
    amp = 0.25 + 0.85 * t * t
    shake = amp * math.sin(t * 2 * math.pi * 9.0)
    p = {}
    for bone, val in HOLD.items():
        pairs = val[1]
        if bone.startswith("UpperArm"):
            p[bone] = ('ROT', [(ax, deg + shake) for ax, deg in pairs])
        elif bone.startswith("Forearm"):
            p[bone] = ('ROT', [(ax, deg - shake * 1.35) for ax, deg in pairs])
        else:
            p[bone] = val
    p.update(body(-0.8, drop=0.12 * math.sin(t * 2 * math.pi * 2.0)))
    grip = 1.0 - 0.05 * math.sin(t * 2 * math.pi * 7.0)
    p.update(digits_pose("R", GRIP_CURL, grip))
    key(f, p)

# ---- f105-f120, the strike ---------------------------------------------------
# The staff drives up and straightens. Fast: this is the beat that tells the
# player the wind-up is over, and a slow one would read as more charging rather
# than as the commit.
#
# The free hand used to snap out and point at the victim here. With the left arm
# held down the whole burden of the commit falls on the staff, which is why the
# straightening matters more than it used to: the shaft going from 11 degrees off
# vertical to 2 is now the only thing that changes shape on this beat.
#
# The body comes back UP through this, from -0.8 to -0.2, so the creature rises
# into the strike instead of staying sat back on it.
for f in range(CHARGE_END, FIRE + 1):
    p = staged(HOLD, PEAK, f, PEAK_LAG)
    p.update(body(-0.8 + 0.6 * ease((f - CHARGE_END) / 15.0)))
    p.update(digits_pose("R", GRIP_CURL, 1.0))
    key(f, p)

# ---- f120-f135, the recoil and the drop --------------------------------------
# The bolt lands at FIRE. It lands on the TARGET, not here, so the recoil is
# smaller than the old clip's -- the creature did not throw anything, it called
# something down. What it gets is a settle: the staff eases back off its full
# extension and the arms fall to neutral by the last frame.
key(FIRE + 2, {**lerp_pose(PEAK, HOLD, 0.16),
               **body(0.4),
               **digits_pose("R", GRIP_CURL, 1.0)})

for f in range(FIRE + 3, ATTACK_FRAMES + 1, 2):
    p = staged(PEAK, NEUTRAL, f, DROP_LAG)
    p.update(body(0.4 * (1.0 - ease((f - FIRE - 3) / 12.0))))
    p.update(digits_pose("R", GRIP_CURL, 1.0))
    key(f, p)

key(ATTACK_FRAMES, {**NEUTRAL, **body(0.0),
                    **digits_pose("R", GRIP_CURL, 1.0)})

# ---- the shoulders, pinned ---------------------------------------------------
# Both ArmRoots are keyed flat at zero for the whole clip, which is what actually
# holds the shoulders still.
#
# It is not enough to simply stop writing them. Idle and Walk both key
# `ArmRoot.{L,R}` -- the floating arms drift and counter-swing on those -- so a
# bone left unkeyed here has no value to blend TOWARD as the animator crossfades
# out of them, and the shoulder slides for the length of the transition. Two flat
# keys give it somewhere definite to be.
#
# The left arm goes down at the same time and for the same reason.
for _f2 in (1, ATTACK_FRAMES):
    key(_f2, LEFT_DOWN)
    key(_f2, {"ArmRoot.R": ('LOC', Vector((0.0, 0.0, 0.0)))})

# ---- the turbine -------------------------------------------------------------
# It spins up through the charge, and the SPACING of these keys is the speed:
# 120 degrees per key, closer together as the charge escalates.
#
# 120 rather than a bigger step for two reasons. The fan has three blades, so 120
# is seamless -- but mostly because rotation is keyed as a QUATERNION, which
# cannot represent more than half a turn between two keys. A key at 900 degrees
# is indistinguishable from one at 180, and Blender would interpolate the short
# way round regardless. Multi-turn spin has to be spelled out one sub-180 step at
# a time; this is the same constraint that kept the chest rotor on 60 and the
# retired halo on 90.
#
# About world Z, which is the shaft's axis at rest. world_rot expresses that in
# the bone's own rest frame, so the spin stays about the SHAFT however far the arm
# has swung -- it does not become a wobble the moment the staff tilts.
key(1, {ROTOR_BONE: ('Z', 0.0)})
_f, _step, _gap = 1.0, 0, 15.0
while True:
    _f += _gap
    if _f > ATTACK_FRAMES:
        break
    _step += 1
    key(round(_f), {ROTOR_BONE: ('Z', (120.0 * _step) % 360.0)})
    # Accelerate into the strike, then coast down: the spin is the wind-up, and
    # one that kept accelerating after the bolt had fallen would say the attack
    # was still coming.
    _gap = max(2.5, _gap * 0.88) if _f < FIRE else min(15.0, _gap * 1.20)

# The halo bone drives nothing -- restore_parts.RETIRE parks the mesh -- but it
# is still in the rig and still keyed, exactly as before, so putting the halo
# back stays a one-line change there rather than an edit to this file.
for f, ang in ((1, 0), (RAISE_END, 40), (CHARGE_END, 150), (ATTACK_FRAMES, 180)):
    key(f, {"Halo": ('Z', ang)})

# ---- the grip belongs on Idle and Walk too -----------------------------------
# The staff is in that hand permanently, not just during a cast, so the right
# hand has to be closed in every clip. Two keys per action is enough because the
# value never changes and the interpolation between two identical keys is a
# constant.
#
# ------------------------------------------------------------------ SLEEP / WAKE
# Not authored here, and that is deliberate rather than an omission.
#
# There used to be a Dormant action and a 3-second Wake action in this file: a
# creature folded into a deep squat under the sand, unfolding in place. Both are
# gone. What the creature does now is stand exactly as it stands in Idle with its
# eye shut, and open the eye -- so the only thing that actually animates is the
# Eyelid mesh's two shape keys.
#
# Blender cannot put those in the same FBX take as the body. With
# bake_anim_use_all_actions the exporter emits one AnimationStack per (object,
# action) pair, so a shape-key action on the Eyelid comes out as a SEPARATE take
# called "Key|ConjurerRig|<name>" that Unity's clip slicer never looks at. Every
# armature take in the FBX already carries frozen copies of those channels, which
# is why nothing here has ever been able to blink.
#
# So LightningConjurerBuilder authors Sleep and Awakening itself, as generated
# AnimationClips: every bone curve sampled off Idle's first frame and held flat --
# "stands completely still", literally -- plus the two eyelid curves. See
# BuildEyeClips there. Nothing in this file needs to change to retune them.

# Done here rather than up in the Idle and Walk blocks only because CUP_CURL and
# the digit helpers are defined in this section; moving them above IDLE would be
# the tidier arrangement and a much larger diff.
for _act, _last in ((A, 120), (W, WALK_FRAMES + 1)):
    arm.animation_data_clear()
    arm.animation_data_create().action = _act
    for _f2 in (1, _last):
        key(_f2, digits_pose("R", GRIP_CURL, 1.0))

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
