"""Author the Vrescal's action set against the rebuilt rig.

Replaces the six hand-posed actions the old sprawling model carried. Those were
built for a crocodile whose limbs swung through a horizontal arc; this animal
stands on columns 2.4 m tall and needs a different set of ideas entirely.

**Everything is solved, not posed.** Each frame is computed: the body's motion
is a small stack of sinusoids, the feet are placed on an explicit gait schedule,
and the legs are then solved backwards from the feet with closed-form two-bone
IK. Nothing uses Blender constraints, so nothing has to be baked and the result
is identical every run.

That choice is what fixes the thing the author complained about. Hand-posed
quadruped legs slide: the foot is keyed at a few positions and interpolates
between them, and because the interpolation does not know the body is also
moving, the planted foot drifts under the animal by a few centimetres every
frame. The eye reads that as skating, and no amount of secondary motion hides
it. Here a planted foot is given a *fixed world position* for the whole of its
stance and the leg bends to whatever the body does above it, so the contact is
exact by construction.

## The gaits

**Walk** is a lateral-sequence walk -- left hind, left fore, right hind, right
fore, evenly quartered -- at duty 0.72, so three feet are down almost all the
time. This is what heavy animals actually use, and it is the gait the reference
animal is standing in.

**Run** is an *amble*: the ipsilateral pair moves almost together (0.12 apart
rather than 0.25), which throws the body into a pronounced side-to-side rock.
Camels, giraffes and elephants all move like this at speed, and it reads far
better on something this tall than a trot -- a trot on a 4.5 m animal looks like
a horse costume.

## Why the body crouches

The rest pose stands with its legs 99.5 % extended, which is correct for a
columnar animal and leaves the IK no headroom at all: a foot placed half a
stride forward is simply out of reach, and the solver would straighten the leg
and let the foot float. Every locomotion clip therefore drops the root by
`CROUCH` first, which buys the bend room the stride needs. Animals do this too.

## Layering

Nothing that moves runs at the same period as anything else, except where it
must. The body bobs twice per cycle, sways once, the spine's counter-rotation
lags the pelvis progressively down its length, and the neck lags further still
and partly cancels the body's motion the way a real animal stabilises its head.
The tail is a damped follower with no period of its own. Getting these to share
a period is the single fastest way to make an animation look mechanical.

    blender --background vrescal.blend --python vrescal_anim.py
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import vrescal_rebuild as R  # noqa: E402  -- the geometry constants live there

FPS = 30
GROUND = R.GROUND
UNITS_PER_M = R.UNITS_PER_M

LEGS = ["FrontP", "FrontS", "RearP", "RearS"]

# Which way each knee bends, as a world direction the joint is pushed toward.
# Fore limbs fold their elbow backwards, hind limbs their knee forwards; on a
# columnar animal both are shallow, but getting the sign wrong inverts the joint
# and is instantly, comically wrong.
POLE = {"FrontP": Vector((-1, 0, 0)), "FrontS": Vector((-1, 0, 0)),
        "RearP": Vector((1, 0, 0)), "RearS": Vector((1, 0, 0))}

SPINE_BONES = ["Bone_Spine_01", "Bone_Spine_02", "Bone_Spine_03"]
NECK_BONES = ["Bone_Neck_01", "Bone_Neck_02", "Bone_Neck_03", "Bone_Neck_04"]
TAIL_BONES = ["Bone_Tail_%02d" % i for i in range(1, 6)]


# --------------------------------------------------------------------------
# Rig access
# --------------------------------------------------------------------------

class Rig:
    """Rest-pose bookkeeping plus the world <-> pose-basis conversion.

    `matrix_basis` is what actually gets keyed, and it lives in the space of
    (parent pose) x (rest offset). Everything below computes the pose it wants
    in *world* space -- which is the only space a gait can sensibly be described
    in -- and this converts at the end.
    """

    def __init__(self, arm):
        self.arm = arm
        self.bones = arm.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in self.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None)
                       for b in self.bones}
        self.order = [b.name for b in self.bones]          # already hierarchical
        self.length = {b.name: b.length for b in self.bones}
        self.rest_rel = {}
        for name in self.order:
            p = self.parent[name]
            self.rest_rel[name] = (self.rest[p].inverted() @ self.rest[name]
                                   if p else self.rest[name])

    def head(self, world, name):
        """World position of a bone's head given the solved parents.

        Independent of the bone's own rotation, which is what lets the leg
        solver ask where the hip is before it has decided anything else.
        """
        p = self.parent[name]
        base = (world[p] @ self.rest_rel[name]) if p else self.rest[name]
        return base.translation.copy()

    def basis_from_world(self, world, name, desired):
        p = self.parent[name]
        base = (world[p] @ self.rest_rel[name]) if p else self.rest[name]
        return base.inverted() @ desired


def aim(rest, head, tail):
    """Bone world matrix pointing head -> tail, carrying the rest roll with it.

    Blender bone space is +Y along the bone with the origin at the head, so
    pointing a bone is a statement about its Y axis only -- the roll about that
    axis is free, and choosing it badly is a silent disaster.

    Building a fresh orthonormal frame from some reference vector is the obvious
    implementation and it is wrong: the frame it produces does not agree with
    the bone's *rest* roll, so even a bone left in its rest direction comes out
    twisted about its own axis. On the leg shafts that is invisible, being
    circular. On the foot it rotated a pad that hangs 1.4 units below the ankle,
    and every clip in the set drove a toe a quarter of a metre into the sand.

    So: take the shortest arc from the rest direction to the wanted one and
    apply it to the whole rest matrix. Twist-free by construction, and a bone
    asked for its rest direction gets its rest matrix back exactly.
    """
    y = tail - head
    if y.length < 1e-7:
        m = rest.copy()
        m.translation = head
        return m
    q = rest.col[1].xyz.normalized().rotation_difference(y.normalized())
    m = q.to_matrix().to_4x4() @ rest
    m.translation = head
    return m


def two_bone_ik(root, target, l1, l2, pole):
    """Elbow position for a two-link chain, by the law of cosines.

    Clamps the target inside reach rather than letting the chain straighten and
    the foot float: at this animal's proportions the legs run at 95 % extension
    through the whole stride, so 'just out of reach' is a routine case, not an
    error.
    """
    d = Vector(target) - Vector(root)
    dist = min(max(d.length, 1e-4), (l1 + l2) * 0.999)
    if d.length > 1e-7:
        d = d.normalized()
    else:
        d = Vector((0, 0, -1))
    cos_a = (l1 * l1 + dist * dist - l2 * l2) / (2.0 * l1 * dist)
    a = math.acos(min(1.0, max(-1.0, cos_a)))

    n = d.cross(Vector(pole))
    if n.length < 1e-6:
        n = d.cross(Vector((0, 1, 0)))
    n.normalize()
    perp = n.cross(d).normalized()
    return Vector(root) + (d * math.cos(a) + perp * math.sin(a)) * l1


def smoothstep(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3.0 - 2.0 * t)


def curve(p, keys):
    """Smoothstep through `[(phase, value), ...]`, wrapping at 1.0.

    Foot roll and the like are far easier to read and tune as a handful of
    labelled instants than as a sum of sines, and wrapping means a looping clip
    cannot develop a seam at frame 0.
    """
    p = p % 1.0
    for i in range(len(keys)):
        p0, v0 = keys[i]
        p1, v1 = keys[(i + 1) % len(keys)]
        span = (p1 - p0) % 1.0
        if span <= 0.0:
            span = 1.0
        rel = (p - p0) % 1.0
        if rel <= span:
            return v0 + (v1 - v0) * smoothstep(rel / span)
    return keys[0][1]


# --------------------------------------------------------------------------
# Gait
# --------------------------------------------------------------------------

class Gait:
    """One locomotion cycle's parameters, in working units and radians."""

    def __init__(self, frames, speed_ms, duty, phases, lift, crouch,
                 bob, sway, roll, pitch, yaw, spine_yaw, neck_nod, stab):
        self.frames = frames
        self.duty = duty
        self.phases = phases
        self.lift = lift
        self.crouch = crouch
        self.bob, self.sway = bob, sway
        self.roll, self.pitch, self.yaw = roll, pitch, yaw
        self.spine_yaw = spine_yaw
        self.neck_nod = neck_nod
        self.stab = stab
        # A planted foot must travel backwards at exactly the speed the creature
        # is meant to be moving, or it skates. Stance lasts duty x cycle, so the
        # stride follows from the speed rather than being chosen.
        self.period = frames / float(FPS)
        self.stride = speed_ms * duty * self.period * UNITS_PER_M


# Toe-off and touch-down roll, as a fraction of the cycle. Phase 0 is the
# instant the foot is set down; `duty` is the instant it leaves.
def foot_roll_keys(duty):
    return [(0.0, -0.10),                       # touch down slightly toe-high
            (duty * 0.25, 0.0),                 # sole flat, taking load
            (duty * 0.80, 0.10),
            (duty, 0.62),                       # heel up, pushing off
            (duty + (1 - duty) * 0.35, -0.34),  # toe up, clearing the sand
            (duty + (1 - duty) * 0.80, -0.16)]


def foot_track(p, g):
    """Where one foot is, relative to its neutral stance position.

    Returns (fore-aft offset, height above the sole plane, roll, planted).
    Stance is *linear* on purpose: any easing there is foot slide.
    """
    p = p % 1.0
    roll = curve(p, foot_roll_keys(g.duty))
    if p < g.duty:
        u = p / g.duty
        return g.stride * (0.5 - u), 0.0, roll, True
    u = (p - g.duty) / (1.0 - g.duty)
    # Lift fast, set down slow: the weight comes off quickly and goes on gently.
    h = g.lift * math.sin(math.pi * (u ** 0.82))
    return g.stride * (-0.5 + smoothstep(u)), h, roll, False


def body_delta(t, g):
    """World-space transform applied to the root for one cycle phase."""
    tau = math.tau
    bob = g.bob * math.sin(2.0 * tau * t)
    sway = g.sway * math.sin(tau * t)
    roll = g.roll * math.sin(tau * t + 0.35)
    pitch = g.pitch * math.sin(2.0 * tau * t + 0.9)
    yaw = g.yaw * math.sin(tau * t + 1.6)
    return (Matrix.Translation(Vector((0.0, sway, bob - g.crouch)))
            @ Matrix.Rotation(roll, 4, 'X')
            @ Matrix.Rotation(pitch, 4, 'Y')
            @ Matrix.Rotation(yaw, 4, 'Z'))


# --------------------------------------------------------------------------
# Solving one frame
# --------------------------------------------------------------------------

class Skeleton:
    """Rest measurements the solver needs, read off the armature once."""

    def __init__(self, rig):
        self.rig = rig
        self.seg = {}      # leg -> (l_upper, l_lower, l_cannon)
        self.ankle = {}    # leg -> rest world ankle position
        self.foot_dir = {}  # leg -> rest world ankle->toe direction
        self.lateral = {}  # leg -> outboard unit vector
        self.pad_h = {}
        self.pad_r = {}
        for leg in LEGS:
            b = ["Bone_%s_%s" % (leg, s)
                 for s in ("Upper", "Lower", "Cannon", "Foot")]
            self.seg[leg] = tuple(rig.length[n] for n in b[:3])
            ank = rig.rest[b[3]].translation.copy()
            self.ankle[leg] = ank
            toe = (rig.rest[b[3]] @ Matrix.Translation(
                (0, rig.length[b[3]], 0))).translation
            self.foot_dir[leg] = (toe - ank).normalized()
            self.lateral[leg] = Vector((0, 1 if leg.endswith("P") else -1, 0))
            variant = R.LIMBS["%sP" % leg[:-1]]["foot"]
            self.pad_h[leg] = R.FOOT_HEIGHT_M[variant] * R.FOOT_SCALE
            self.pad_r[leg] = R.FOOT_SOLE_M[variant] * R.FOOT_SCALE

    def target(self, leg, dx=0.0, dz=0.0, roll=0.0, dy=0.0):
        """World ankle position, with the roll pivoted on the sole's contact edge.

        Rolling the foot about the *ankle* swings the pad -- which hangs 1.4
        units below it and reaches 1.6 forward -- straight through the sand: at
        the 0.62 rad of toe-off this clip uses, that buried the foot 0.2 m deep.
        Real animals pivot on the toe going into push-off and on the heel coming
        out of it, so the ankle has to rise by exactly enough to keep the lowest
        point of the rotated sole on the ground. That rise *is* the heel lifting.
        """
        a = self.ankle[leg]
        lift = (abs(self.pad_r[leg] * math.sin(roll))
                - self.pad_h[leg] * (1.0 - math.cos(roll)))
        return Vector((a.x + dx, a.y + dy, a.z + dz + lift))


def solve(rig, sk, root_delta, locals_, targets, rolls):
    """World matrix per bone for one frame.

    `locals_` carries body-bone rotations as (rx, ry, rz) in bone-local space;
    `targets` and `rolls` carry each leg's world ankle position and foot roll.
    The legs are solved after the body because the hip's position is whatever
    the spine above it ended up doing.
    """
    world = {}
    leg_bones = {"Bone_%s_%s" % (leg, s)
                 for leg in LEGS for s in ("Upper", "Lower", "Cannon", "Foot")}

    for name in rig.order:
        if name in leg_bones:
            continue
        p = rig.parent[name]
        base = (world[p] @ rig.rest_rel[name]) if p else rig.rest[name]
        if name == "Bone_Root":
            # The root's delta is expressed in world space; conjugating by the
            # rest matrix moves it into the bone's own space.
            base = rig.rest[name] @ (rig.rest[name].inverted() @ root_delta
                                     @ rig.rest[name])
        rx, ry, rz = locals_.get(name, (0.0, 0.0, 0.0))
        if rx or ry or rz:
            base = base @ (Matrix.Rotation(rx, 4, 'X')
                           @ Matrix.Rotation(ry, 4, 'Y')
                           @ Matrix.Rotation(rz, 4, 'Z'))
        world[name] = base

    for leg in LEGS:
        up, lo, ca, ft = ["Bone_%s_%s" % (leg, s)
                          for s in ("Upper", "Lower", "Cannon", "Foot")]
        l1, l2, l3 = sk.seg[leg]
        hip = rig.head(world, up)
        ankle = Vector(targets[leg])

        # Two links: the femur, and the shank plus cannon treated as one. The
        # hock is then placed back on that line at its rest proportion, with a
        # small backward kick so it does not read as a single straight bone.
        knee = two_bone_ik(hip, ankle, l1, l2 + l3, POLE[leg])
        shank = ankle - knee
        f = l2 / (l2 + l3)
        bend = POLE[leg] * (0.10 * shank.length * (1.0 - shank.length
                                                   / max(l2 + l3, 1e-6)))
        hock = knee + shank * f + bend

        lat = sk.lateral[leg]
        world[up] = aim(rig.rest[up], hip, knee)
        world[lo] = aim(rig.rest[lo], knee, hock)
        world[ca] = aim(rig.rest[ca], hock, ankle)

        d = Matrix.Rotation(rolls[leg], 4, lat).to_3x3() @ sk.foot_dir[leg]
        world[ft] = aim(rig.rest[ft], ankle, ankle + d * rig.length[ft])

    return world


# --------------------------------------------------------------------------
# Writing keys
# --------------------------------------------------------------------------

def write_action(rig, sk, name, frames, frame_fn, loop=False):
    """Build one action by solving and keying every frame.

    Every frame is keyed rather than a sparse set with interpolation between,
    because the whole point of solving the legs is that the contact is exact --
    letting Blender interpolate between solved frames would put the slide back.

    A looping clip gets one extra frame, at cycle phase exactly 1.0 and so
    identical to its first. Unity then has a range whose last frame equals its
    first and the loop closes with no jump; without it the clip is a frame short
    and the animal hitches once per stride.
    """
    if loop:
        frames = frames + 1
    arm = rig.arm
    if arm.animation_data is None:
        arm.animation_data_create()
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    # Blender 4.4+ stores curves under a slot; keyframe_insert makes one, but
    # the action has to be the active one first for it to be bound.
    if hasattr(arm.animation_data, "action_slot") and act.slots:
        arm.animation_data.action_slot = act.slots[0]

    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'

    cycle = float(frames - 1) if loop else float(frames)
    for f in range(1, frames + 1):
        t = (f - 1) / cycle
        root_delta, locals_, targets, rolls = frame_fn(t, f)
        world = solve(rig, sk, root_delta, locals_, targets, rolls)

        for bone_name, wm in world.items():
            pb = arm.pose.bones[bone_name]
            basis = rig.basis_from_world(world, bone_name, wm)
            loc, rot, _scale = basis.decompose()
            pb.location = loc
            pb.rotation_quaternion = rot
            pb.keyframe_insert("rotation_quaternion", frame=f)
            if bone_name == "Bone_Root":
                pb.keyframe_insert("location", frame=f)

    for fc in _fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'
    return act


def _fcurves(act):
    """F-curves of an action, across Blender's slotted and flat layouts."""
    if hasattr(act, "fcurves"):
        return list(act.fcurves)
    out = []
    for lay in act.layers:
        for st in lay.strips:
            for cb in st.channelbags:
                out += list(cb.fcurves)
    return out


# --------------------------------------------------------------------------
# Clips
# --------------------------------------------------------------------------

WALK = Gait(frames=36, speed_ms=1.6, duty=0.72,
            phases={"RearP": 0.00, "FrontP": 0.25,
                    "RearS": 0.50, "FrontS": 0.75},
            lift=1.15, crouch=0.75,
            bob=0.22, sway=0.30, roll=math.radians(3.2),
            pitch=math.radians(1.4), yaw=math.radians(1.5),
            spine_yaw=math.radians(2.6), neck_nod=math.radians(4.2), stab=0.55)

# An amble, not a trot: the ipsilateral pair is 0.12 apart rather than a
# quarter cycle, which is what produces the heavy side-to-side rock.
RUN = Gait(frames=24, speed_ms=4.2, duty=0.48,
           phases={"RearP": 0.00, "FrontP": 0.12,
                   "RearS": 0.50, "FrontS": 0.62},
           lift=2.05, crouch=1.10,
           bob=0.55, sway=0.62, roll=math.radians(6.5),
           pitch=math.radians(3.0), yaw=math.radians(2.6),
           spine_yaw=math.radians(4.4), neck_nod=math.radians(8.0), stab=0.45)


def locomotion_frame(rig, sk, g):
    """Frame function for a gait clip."""
    def fn(t, _f):
        root = body_delta(t, g)
        targets, rolls = {}, {}
        for leg in LEGS:
            dx, dz, roll, _planted = foot_track(t + g.phases[leg], g)
            targets[leg] = sk.target(leg, dx=dx, dz=dz, roll=roll)
            rolls[leg] = roll

        locals_ = {}
        # Spine counter-rotation, lagging further the further forward it is.
        for i, name in enumerate(SPINE_BONES):
            lag = 0.08 * (i + 1)
            locals_[name] = (0.0, 0.0,
                             g.spine_yaw * math.sin(math.tau * (t - lag)))
        # The neck partly cancels the body's roll and bob -- animals hold their
        # heads still -- and adds a nod of its own, one per cycle.
        for i, name in enumerate(NECK_BONES):
            lag = 0.10 + 0.05 * i
            nod = g.neck_nod * math.sin(math.tau * (t - lag)) / len(NECK_BONES)
            cancel = -g.stab * g.roll * math.sin(math.tau * (t - lag)) \
                / len(NECK_BONES)
            locals_[name] = (cancel * 3.0, 0.0, nod * 0.55)
        locals_["Bone_Head"] = (
            -g.stab * g.roll * math.sin(math.tau * (t - 0.30)) * 1.5,
            0.0,
            -g.neck_nod * 0.30 * math.sin(math.tau * (t - 0.30)))
        # Tail: a damped follower. No period of its own, just the pelvis's,
        # delayed a little more at every joint.
        for i, name in enumerate(TAIL_BONES):
            lag = 0.09 * (i + 1)
            amp = (0.55 + 0.22 * i)
            locals_[name] = (
                g.pitch * 0.9 * amp * math.sin(2.0 * math.tau * (t - lag)),
                0.0,
                -g.spine_yaw * amp * math.sin(math.tau * (t - lag)))
        locals_["Bone_Jaw"] = (0.0, 0.0, 0.0)
        return root, locals_, targets, rolls
    return fn


def planted(sk, drop=0.0, spread=0.0, roll=0.0):
    """Feet at their rest positions -- the base every non-gait clip starts from."""
    targets, rolls = {}, {}
    for leg in LEGS:
        dy = spread * (1 if leg.endswith("P") else -1)
        targets[leg] = sk.target(leg, dz=drop, dy=dy, roll=roll)
        rolls[leg] = roll
    return targets, rolls


def idle_frame(rig, sk):
    """Breathing, a slow weight shift, a head scan and a tail sway.

    Four periods that do not divide into each other -- 1, 1/3, 1/2 and 2/5 of
    the clip -- so nothing lines up twice and the loop does not announce itself
    over a three-second cycle.
    """
    def fn(t, _f):
        tau = math.tau
        breathe = 0.085 * math.sin(tau * t * 3.0)
        shift = 0.30 * math.sin(tau * t)
        root = (Matrix.Translation(Vector((0.0, shift, breathe - 0.08)))
                @ Matrix.Rotation(math.radians(1.5) * math.sin(tau * t), 4, 'X')
                @ Matrix.Rotation(math.radians(0.8)
                                  * math.sin(tau * t * 3.0), 4, 'Y'))
        targets, rolls = planted(sk)

        locals_ = {}
        for i, name in enumerate(SPINE_BONES):
            locals_[name] = (0.0, 0.0,
                             math.radians(0.9) * math.sin(tau * (t - 0.1 * i)))
        for i, name in enumerate(NECK_BONES):
            lag = 0.12 * i
            locals_[name] = (
                math.radians(1.3) * math.sin(tau * (t * 2.0 - lag)),
                0.0,
                math.radians(2.6) * math.sin(tau * (t * 0.5 - lag)))
        locals_["Bone_Head"] = (math.radians(2.0) * math.sin(tau * t * 2.0),
                                0.0,
                                math.radians(4.5) * math.sin(tau * t * 0.5))
        for i, name in enumerate(TAIL_BONES):
            amp = math.radians(2.2) * (0.6 + 0.3 * i)
            locals_[name] = (0.0, 0.0, amp * math.sin(tau * (t * 2.5 - 0.1 * i)))
        # The jaw only opens on the exhale, and not very far.
        locals_["Bone_Jaw"] = (0.0, 0.0, 0.0)
        locals_["Bone_Jaw"] = (math.radians(3.0)
                               * max(0.0, math.sin(tau * t * 3.0)), 0.0, 0.0)
        return root, locals_, targets, rolls
    return fn


def attack_frame(rig, sk, frames):
    """Rear back, drop the whole mass forward, snap the jaw at full reach.

    A tall animal cannot lunge the way the old crocodile did -- it has no
    forward reach at ground level. It attacks by *falling* at the target:
    weight back on the hind legs, then the chest drops and the head comes down
    and forward on a long neck, which is the only thing on this body plan with
    any speed at the end of it.
    """
    def fn(t, _f):
        rear = curve(t, [(0.0, 0.0), (0.26, 1.0), (0.42, 0.85),
                         (0.58, -1.0), (0.80, -0.35), (0.999, 0.0)])
        drop = curve(t, [(0.0, 0.0), (0.26, -0.55), (0.55, 0.95),
                         (0.78, 0.25), (0.999, 0.0)])
        jaw = curve(t, [(0.0, 0.0), (0.30, 0.85), (0.52, 0.95),
                        (0.58, 0.0), (0.999, 0.0)])

        root = (Matrix.Translation(Vector((rear * 1.5, 0.0, drop * -0.9 - 0.1)))
                @ Matrix.Rotation(math.radians(-7.0) * rear, 4, 'Y'))
        targets, rolls = planted(sk)
        # The forefeet leave the ground as the animal rocks back.
        lift = max(0.0, rear) * 1.6
        for leg in ("FrontP", "FrontS"):
            rolls[leg] = -0.5 * max(0.0, rear)
            targets[leg] = sk.target(leg, dz=lift, roll=rolls[leg])

        locals_ = {}
        for i, name in enumerate(SPINE_BONES):
            locals_[name] = (math.radians(4.0) * rear, 0.0, 0.0)
        for i, name in enumerate(NECK_BONES):
            locals_[name] = (math.radians(-13.0) * rear
                             + math.radians(9.0) * drop, 0.0, 0.0)
        locals_["Bone_Head"] = (math.radians(-8.0) * rear
                                + math.radians(14.0) * drop, 0.0, 0.0)
        locals_["Bone_Jaw"] = (math.radians(34.0) * jaw, 0.0, 0.0)
        for i, name in enumerate(TAIL_BONES):
            locals_[name] = (math.radians(-5.0) * rear * (0.5 + 0.2 * i),
                             0.0, 0.0)
        return root, locals_, targets, rolls
    return fn


def hurt_frame(rig, sk):
    """A flinch: the mass drops on to the forelegs and the head recoils up."""
    def fn(t, _f):
        hit = curve(t, [(0.0, 0.0), (0.22, 1.0), (0.55, 0.35), (0.999, 0.0)])
        root = (Matrix.Translation(Vector((-0.9 * hit, 0.35 * hit,
                                           -1.15 * hit - 0.08)))
                @ Matrix.Rotation(math.radians(5.0) * hit, 4, 'X')
                @ Matrix.Rotation(math.radians(6.0) * hit, 4, 'Y'))
        targets, rolls = planted(sk)
        locals_ = {}
        for name in SPINE_BONES:
            locals_[name] = (math.radians(-5.0) * hit, 0.0,
                             math.radians(4.0) * hit)
        for name in NECK_BONES:
            locals_[name] = (math.radians(-9.0) * hit, 0.0,
                             math.radians(3.0) * hit)
        locals_["Bone_Head"] = (math.radians(-16.0) * hit, 0.0, 0.0)
        locals_["Bone_Jaw"] = (math.radians(22.0) * hit, 0.0, 0.0)
        for i, name in enumerate(TAIL_BONES):
            locals_[name] = (0.0, 0.0, math.radians(7.0) * hit * (0.5 + 0.2 * i))
        return root, locals_, targets, rolls
    return fn


def death_frame(rig, sk):
    """The legs give way, the body comes down, the neck follows it and settles.

    Held flat for the last fifth so the clip can stop on its final frame and
    leave a corpse pose rather than snapping back.
    """
    def fn(t, _f):
        buckle = curve(t, [(0.0, 0.0), (0.16, 0.15), (0.44, 1.0),
                           (0.66, 1.0), (0.999, 1.0)])
        settle = curve(t, [(0.0, 0.0), (0.50, 0.0), (0.72, 1.0),
                           (0.86, 0.92), (0.999, 1.0)])
        lean = curve(t, [(0.0, 0.0), (0.30, 0.3), (0.62, 1.0), (0.999, 1.0)])

        # 5.05 units settles the animal without driving its folded knees
        # through the sand -- the limiting part of a collapse is the knee, not
        # the belly, once the legs splay.
        # the sole plane, and the roll below takes up the rest. Dropping the
        # full 8.7 buried the trunk, and the roll then put the tail half a metre
        # under.
        root = (Matrix.Translation(Vector((0.0, 1.1 * lean,
                                           -5.05 * buckle - 0.1)))
                @ Matrix.Rotation(math.radians(13.0) * lean, 4, 'X')
                @ Matrix.Rotation(math.radians(-6.0) * settle, 4, 'Y'))

        # Feet splay outward and forward as the legs fold under the weight.
        targets, rolls = {}, {}
        for leg in LEGS:
            fwd = 1.5 if leg.startswith("Front") else -1.2
            rolls[leg] = 0.45 * buckle
            targets[leg] = sk.target(
                leg, dx=fwd * buckle, roll=rolls[leg],
                dy=1.9 * buckle * (1 if leg.endswith("P") else -1))

        locals_ = {}
        for i, name in enumerate(SPINE_BONES):
            locals_[name] = (math.radians(-6.0) * lean, 0.0,
                             math.radians(5.0) * lean)
        for i, name in enumerate(NECK_BONES):
            locals_[name] = (math.radians(11.0) * settle, 0.0,
                             math.radians(6.0) * lean)
        locals_["Bone_Head"] = (math.radians(15.0) * settle, 0.0,
                                math.radians(9.0) * lean)
        locals_["Bone_Jaw"] = (math.radians(15.0) * settle, 0.0, 0.0)
        # The tail curls up as the body comes down. Without this the rolled
        # trunk swings a tail that already droops 42 degrees straight through
        # the sand -- it is the one part of the animal the root drop moves
        # further from the ground plane rather than toward it.
        for i, name in enumerate(TAIL_BONES):
            locals_[name] = (math.radians(4.0) * buckle * (0.4 + 0.2 * i),
                             0.0,
                             math.radians(-9.0) * lean * (0.4 + 0.25 * i))
            locals_[name] = (locals_[name][0] - math.radians(36.0) * buckle,
                             locals_[name][1], locals_[name][2])
        return root, locals_, targets, rolls
    return fn


def main():
    arm = bpy.data.objects.get("Arm_Vrescal")
    if arm is None:
        raise SystemExit("No Arm_Vrescal -- run vrescal_rebuild.py first.")
    if "Mesh_Vrescal_Body" not in bpy.data.objects:
        raise SystemExit("No Mesh_Vrescal_Body -- this is the old rig. Run "
                         "vrescal_rebuild.py first.")

    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)

    rig = Rig(arm)
    sk = Skeleton(rig)

    built = []
    for name, frames, loop, fn in (
            ("Vrescal_Idle", 90, True, idle_frame(rig, sk)),
            ("Vrescal_Walk", WALK.frames, True,
             locomotion_frame(rig, sk, WALK)),
            ("Vrescal_Run", RUN.frames, True, locomotion_frame(rig, sk, RUN)),
            ("Vrescal_Attack", 40, False, None),
            ("Vrescal_Hurt", 20, False, hurt_frame(rig, sk)),
            ("Vrescal_Death", 64, False, death_frame(rig, sk))):
        if fn is None:
            fn = attack_frame(rig, sk, frames)
        act = write_action(rig, sk, name, frames, fn, loop=loop)
        built.append("%s %d-%d%s" % (name, 1, int(act.frame_range[1]),
                                     " loop" if loop else ""))

    # Leave the rig at rest so the .blend opens looking like the model, not
    # like whatever the last frame of the death clip was.
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.location = (0.0, 0.0, 0.0)
        pb.rotation_quaternion = Quaternion((1.0, 0.0, 0.0, 0.0))

    print("Vrescal actions: %s" % ", ".join(built))
    print("  walk stride %.2f m, run stride %.2f m"
          % (WALK.stride / UNITS_PER_M, RUN.stride / UNITS_PER_M))
    bpy.ops.wm.save_mainfile()
    print("Saved %s" % bpy.data.filepath)


if __name__ == "__main__":
    main()
