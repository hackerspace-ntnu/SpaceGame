"""Author Appa's clips: Idle, Walk, Run, TurnL, TurnR, Graze, Happy, Roar, Ram,
Hurt and Death.

    blender --background --python appa_anim.py -- --save

Re-runnable: it replaces its own actions and touches nothing else. Additive like
`appa_rig.py` -- it writes pose F-curves and nothing more. No geometry, no
bones, no materials. Without `--save` it builds the actions and throws them
away, which is what you want when you only need to look at the print-out.

## Bone axes: never key a raw euler component on this rig

Every bone here rotates about its **local** axes, and those axes are wherever
the bone's roll happened to leave them. Measured on this rig, +15 deg about
local Y moves the tip of *every single bone* by nothing at all -- Y is the
bone's own length, so keying it is a pure twist and invisible.

An earlier version of this file put the whole leg swing and the whole body bob
on Y. The result was a walk cycle in which no leg ever moved: the animal slid
along with its feet locked in the rest pose, and the only thing that survived
was the idle's X-component head drift. It looked like a subtle problem and was
a total one.

So nothing below sets `rotation_euler` directly. `pitch/yaw/roll` take an angle
about a **world** axis and convert it into whatever local euler that particular
bone needs, via its rest matrix. Read them as:

    pitch(+)  nose up, or a leg swinging FORWARD   (about world +Y)
    yaw(+)    turning to the animal's LEFT         (about world +Z)
    roll(+)   banking                              (about world +X)

The .blend's own space: **-X is forward** (the head is at -X), +Z is up, +Y is
the animal's left, soles at z = -1.76. One convention covers both ends of the
body: a bone pointing forward pitches its tip up, a leg bone pointing down
pitches its tip forward, and both are `pitch(+)`.

## The gait

Six legs, so the interesting decision is the order they land in. This uses a
**metachronal wave**: on each side the legs fire back-to-front a third of a
cycle apart, and the two sides are half a cycle out of phase with each other.
That is what real hexapods do at walking speed, and it reads as a deliberate
lumbering animal rather than the alternating-tripod scuttle an insect uses --
the right call for something built like a bison.

## Two things that are deliberate

  * **Nothing keys `root`.** Unity's `RootMotionCurveStripper` deletes
    root-bound curves from every imported clip, so a bob authored on the root
    bone would silently vanish. The body bob lives on `spine1` instead, where it
    survives the import -- and so does the lunge in Ram and the collapse in
    Death, both of which need real translation rather than rotation.

  * **The clips are in place.** No forward travel anywhere. Movement is the
    motor's job in Unity, and a clip that also walked the animal forward would
    fight it. Same rule `dune_rat_anim.py` follows.

## Why FK and not IK

`dune_rat_rig.py` drives its four limbs with IK targets and bakes the solver
output on export. That is the better rig for a creature whose feet must stick
to uneven ground. Appa's gait is fixed and authored, and six IK chains would be
six more things to bake, verify and go wrong. If Appa ever needs real foot
planting on slopes, that is the point to revisit this.
"""

import math
import sys

import bpy
from mathutils import Matrix, Vector

ARM = "Arm_Appa"
FPS = 24

# Looping locomotion. The last frame repeats the first so the cycle closes;
# AppaBuilder slices one frame short so the pose is not held for two frames.
IDLE_FRAMES = 192         # 8 s. Long, because the jaw now opens ONCE per loop --
                          # twice per four seconds read as a nervous animal.
WALK_FRAMES = 48          # 2 s per full cycle -- a heavy, unhurried animal
RUN_FRAMES = 30           # 1.25 s -- same gait, driven harder
TURN_FRAMES = 36          # 1.5 s -- a heavy animal shuffles round, it does not pivot
GRAZE_FRAMES = 96         # 4.0 s -- long, so a herd grazing does not chew in unison

# One-shots. None of these loop; AppaBuilder clamps them.
ROAR_FRAMES = 48          # 2.0 s -- the attack telegraph, so it must be legible
RAM_FRAMES = 36           # 1.5 s
HURT_FRAMES = 18          # 0.75 s
DEATH_FRAMES = 72         # 3.0 s
HAPPY_FRAMES = 60         # 2.5 s -- one pet
JUMP_FRAMES = 26          # 1.1 s, which is NavMeshAgentMotor's 0.55 s hop at the
                          # 2.0 playback rate every Appa clip is played at. The
                          # pose has to finish landing exactly as the motor puts
                          # him back down, or he settles onto legs already straight.

# Turn-on-the-spot geometry. The centre is PIVOT.x from appa_export.py -- midway
# between the front and back feet -- and the length is hip (z -0.45) to sole
# (z -1.76), which is what converts a step in metres into femur radians.
TURN_CENTRE_X = 1.44
LEG_LENGTH = 1.31

# Degrees of body rotation the clip covers per half cycle, so a full cycle turns
# him 2x this. At 36 frames / 24 fps and the prefab's 2.0 playback rate that is
# one cycle every 0.75 s -- 45 deg/s, which is what AppaBuilder sets the
# NavMeshAgent's angularSpeed to. Change one and the feet start sliding.
#
# Each foot's step is scaled by ITS OWN radius from the turn centre, because a
# rigid rotation moves a point at omega*r: a fixed step in metres would make the
# middle legs, which sit almost on the centre, over-step by half as much again.
TURN_SWEEP_DEG = 17.0

# Fraction of the cycle a foot spends in the air. Under half, so at any instant
# most of the legs are carrying weight, which is what makes it look heavy.
SWING = 0.4

# Back-to-front on each side, sides half a cycle apart. See the module docstring.
LEG_PHASE = {
    "F.L": 0.00, "M.L": 0.33, "B.L": 0.66,
    "F.R": 0.50, "M.R": 0.83, "B.R": 0.16,
}

LEGS = list(LEG_PHASE)
LEG_BONES = (["femur_%s" % k for k in LEGS]
             + ["tibia_%s" % k for k in LEGS]
             + ["hoof_%s" % k for k in LEGS])
SPINE = ["spine1", "spine2", "spine3", "neck", "head"]
TAIL = ["tail1", "tail2", "tail3"]
ALL_BONES = LEG_BONES + SPINE + TAIL + ["jaw"]

FORWARD = Vector((-1.0, 0.0, 0.0))   # the head is at -X
UP = Vector((0.0, 0.0, 1.0))


# ---------------------------------------------------------------------------
# World-axis posing. See "Bone axes" in the module docstring -- this is the part
# that stops a clip from silently animating nothing.
# ---------------------------------------------------------------------------

def _local_axis(pb, world_axis):
    """`world_axis` expressed in the bone's own rest space."""
    basis = pb.bone.matrix_local.to_3x3()
    return (basis.inverted() @ Vector(world_axis)).normalized()


def pose(pb, pitch=0.0, yaw=0.0, roll=0.0):
    """Rotate a pose bone by world-space pitch/yaw/roll, in radians.

    Composed in a fixed order so a given triple always means the same pose:
    yaw about world +Z, then pitch about world +Y, then roll about world +X.
    """
    m = Matrix.Identity(3)
    if yaw:
        m = Matrix.Rotation(yaw, 3, _local_axis(pb, (0.0, 0.0, 1.0))) @ m
    if pitch:
        m = Matrix.Rotation(pitch, 3, _local_axis(pb, (0.0, 1.0, 0.0))) @ m
    if roll:
        m = Matrix.Rotation(roll, 3, _local_axis(pb, (1.0, 0.0, 0.0))) @ m
    pb.rotation_euler = m.to_euler('XYZ')


def shift(pb, world_offset):
    """Translate a pose bone by a world-space offset, in metres.

    Only ever used on `spine1` -- the lunge in Ram and the collapse in Death both
    need the body to actually move, and `root` is the one bone whose curves Unity
    throws away on import.
    """
    basis = pb.bone.matrix_local.to_3x3()
    pb.location = basis.inverted() @ Vector(world_offset)


def d(deg):
    return math.radians(deg)


# ---------------------------------------------------------------------------
# The jaw
# ---------------------------------------------------------------------------

# **The sculpt rests with his mouth open.** Measured in Unity by rotating the jaw
# bone on a live instance until the lip line met: 38 deg. (26 was the first
# reading, taken while the head mesh still carried jaw weights -- the face was
# being dragged along and closed the gap early, so it looked shut sooner than it
# was. With those weights gone the true figure is 38; see appa_weights.py.) So an unposed jaw is not a closed one, and
# every clip that leaves the jaw alone ships a creature walking around gaping --
# which is exactly what Walk, Run, TurnL and TurnR used to do, because they never
# keyed the bone at all.
#
# Nothing below poses `jaw` directly any more. `set_jaw` takes how far open the
# mouth should be **measured from shut**, which is the only definition that
# survives someone re-sculpting the head.
JAW_CLOSED = d(38.0)


def set_jaw(arm, open_angle=0.0):
    """Open the mouth by `open_angle` radians, measured from fully shut."""
    pose(arm.pose.bones["jaw"], pitch=JAW_CLOSED - open_angle)


# ---------------------------------------------------------------------------
# Curve shaping
# ---------------------------------------------------------------------------

def _smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def _ramp(u, start, end):
    """0 before `start`, 1 after `end`, eased between. For one-shot staging."""
    if u <= start:
        return 0.0
    if u >= end:
        return 1.0
    return _smoothstep((u - start) / (end - start))


def _pulse(u, start, peak, end):
    """Rises to 1 at `peak`, falls back to 0 by `end`."""
    if u <= start or u >= end:
        return 0.0
    if u <= peak:
        return _smoothstep((u - start) / (peak - start))
    return 1.0 - _smoothstep((u - peak) / (end - peak))


def _leg_pose(cycle, reach, lift_fold, hoof_level, stance_flex):
    """(femur, tibia, hoof) pitches for one leg at `cycle` in [0, 1).

    Swing is eased so the foot accelerates off the ground and settles onto it;
    stance is linear, because a planted foot travels with the body at constant
    speed and easing it would make the animal look like it is skating.

    Positive femur pitch swings the leg forward -- see the docstring.

    ## Why the knee never stops moving

    The first version folded the knee only during SWING, which left it perfectly
    straight and rigid for the 60% of the cycle the foot is planted. Because the
    hoof sits at the end of the chain it inherits femur + tibia + hoof rotation
    and swings through a wide arc regardless, so what the eye got was busy feet
    on stiff legs -- "like he's running on his feet while the legs are more
    still", which is exactly what it was.

    `stance_flex` adds the missing half: the knee compresses as the body's weight
    passes over the planted foot and extends again to push off. Real legged gait
    does this, and it is what makes a leg read as carrying something.
    """
    if cycle < SWING:
        t = _smoothstep(cycle / SWING)
        femur = -reach + 2.0 * reach * t          # back -> front
        lift = math.sin(math.pi * (cycle / SWING))
        tibia = -lift_fold * lift
        hoof = hoof_level * lift
    else:
        t = (cycle - SWING) / (1.0 - SWING)
        femur = reach - 2.0 * reach * t           # front -> back
        # Compress through mid-stance, extend into toe-off.
        squash = math.sin(math.pi * t)
        tibia = -stance_flex * squash
        hoof = hoof_level * 0.35 * squash

    return femur, tibia, hoof


# ---------------------------------------------------------------------------
# Action plumbing
# ---------------------------------------------------------------------------

def _rest(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.location = (0.0, 0.0, 0.0)


def _action(arm, name):
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = act
    act.use_fake_user = True      # or Blender drops it on save and the export finds nothing
    return act


def _key(arm, names, frame, location=()):
    for n in names:
        arm.pose.bones[n].keyframe_insert("rotation_euler", frame=frame)
    for n in location:
        arm.pose.bones[n].keyframe_insert("location", frame=frame)


# ---------------------------------------------------------------------------
# Locomotion
# ---------------------------------------------------------------------------

def _build_gait(arm, name, frames, reach, fold, hoof_level, stance_flex,
                bob_deg, sway_deg, head_pitch_deg, lean_deg):
    """Walk and Run are the same cycle at different amplitudes.

    Keeping them one function is what stops the run from drifting into a
    different gait than the walk when either is retuned -- they have to blend
    into each other in the animator.
    """
    _action(arm, name)
    _rest(arm)

    for f in range(frames + 1):
        u = (f % frames) / float(frames)          # last frame repeats the first

        for leg, phase in LEG_PHASE.items():
            femur, tibia, hoof = _leg_pose((u + phase) % 1.0, reach, fold,
                                           hoof_level, stance_flex)
            pose(arm.pose.bones["femur_%s" % leg], pitch=femur)
            pose(arm.pose.bones["tibia_%s" % leg], pitch=tibia)
            pose(arm.pose.bones["hoof_%s" % leg], pitch=hoof)

        # Body follow-through at twice the leg frequency: with this gait a foot
        # lands roughly twice per cycle per side.
        bob = math.sin(u * 4.0 * math.pi)
        sway = math.sin(u * 2.0 * math.pi)

        pose(arm.pose.bones["spine1"], pitch=d(lean_deg) + bob * d(bob_deg),
             yaw=sway * d(sway_deg))
        pose(arm.pose.bones["spine2"], pitch=bob * d(-bob_deg * 0.65),
             yaw=sway * d(-sway_deg * 0.6))
        pose(arm.pose.bones["spine3"], pitch=bob * d(bob_deg * 0.4),
             yaw=sway * d(-sway_deg * 0.4))
        pose(arm.pose.bones["neck"], pitch=d(head_pitch_deg) + bob * d(bob_deg * 1.2),
             yaw=sway * d(sway_deg * 0.5))
        pose(arm.pose.bones["head"], pitch=d(head_pitch_deg * 0.5) + bob * d(-bob_deg * 0.9))

        # The tail lags the body, which is what sells it as weight rather than
        # decoration.
        lag = math.sin((u - 0.15) * 2.0 * math.pi)
        pose(arm.pose.bones["tail1"], pitch=lag * d(3.0), yaw=lag * d(5.0))
        pose(arm.pose.bones["tail2"], pitch=lag * d(4.5), yaw=lag * d(7.5))
        pose(arm.pose.bones["tail3"], pitch=lag * d(5.5), yaw=lag * d(9.0))

        # Shut. A gait that does not key the jaw leaves it at the sculpt's resting
        # gape, so he ambled and galloped with his mouth hanging open.
        set_jaw(arm)

        _key(arm, LEG_BONES + SPINE + TAIL + ["jaw"], f)

    return frames


def build_walk(arm):
    return _build_gait(arm, "Appa_Walk", WALK_FRAMES,
                       reach=d(34), fold=d(52), hoof_level=d(24), stance_flex=d(16),
                       bob_deg=2.6, sway_deg=3.2, head_pitch_deg=0.0, lean_deg=0.0)


def build_run(arm):
    # Longer reach, more knee, more bob, nose down and body pitched into it --
    # the same animal leaning on the throttle rather than a different gait.
    return _build_gait(arm, "Appa_Run", RUN_FRAMES,
                       reach=d(52), fold=d(72), hoof_level=d(30), stance_flex=d(24),
                       bob_deg=4.6, sway_deg=4.2, head_pitch_deg=-9.0, lean_deg=-4.0)


def _turn_tangents(arm):
    """Unit direction each foot travels when the body turns to its left.

    Returns (tangent x, tangent y, radius) per leg. Read off the rig rather than
    hard-coded, so moving a leg station in `appa_rig.py` re-aims the step instead
    of quietly authoring a wrong one. Rotating about +Z sends a point at radius
    (rx, ry) along (-ry, rx), at a speed proportional to |r|.
    """
    out = {}
    for leg in LEGS:
        head = arm.matrix_world @ arm.pose.bones["femur_%s" % leg].bone.head_local
        rx, ry = head.x - TURN_CENTRE_X, head.y
        radius = math.hypot(rx, ry) or 1.0
        out[leg] = (-ry / radius, rx / radius, radius)
    return out


def _build_turn(arm, name, direction):
    """Turning on the spot. `direction` is +1 for the animal's left, -1 for right.

    ## In place, deliberately

    The clip carries no root rotation. The NavMeshAgent already owns Appa's
    heading and turns it at its own rate; a clip that also rotated the root would
    turn him twice and at a rate the AI never asked for. That is
    GDC-L1-ANIM-0004 applied per action -- his whole locomotion set is
    code-driven, `m_ApplyRootMotion: 0`, and the turn has to live on the same
    side of that line as the walk it blends with.

    ## Each foot walks its own arc

    The gait's phasing and vertical lift are reused; what changes is the
    direction each foot travels. On a turn in place every foot rides a circle
    about the body's centre, so its direction depends on where it stands: the
    middle legs are almost level with the centre and step nearly straight
    fore-and-aft, while the front and back legs step diagonally. Each leg
    therefore gets its own tangent, taken from where its hip actually sits, and
    the sweep is decomposed onto the femur's pitch (fore-aft) and roll
    (sideways). While a foot is planted it travels backwards along that tangent,
    because the body is rotating over a foot that is not; during swing it
    recovers.

    **Do not reach for yaw here.** Yawing the femur turns the leg about a
    vertical axis through its own hip, and the foot hangs almost *on* that axis,
    so it pivots in place: measured, a ±15 deg yaw moved the front hoof 0.23 m
    sideways while the knee fold alone moved it 0.36 m fore-aft, and the clip
    read as marching rather than turning.

    Six legs on the existing `LEG_PHASE` offsets means most of them are planted
    at any instant, which is what keeps a nine-tonne animal from looking like it
    is pirouetting.

    Head and neck lead the body into the turn and the tail swings out the other
    way -- the secondary motion of GDC-L1-ANIM-0005, and the part that reads as
    intent rather than a model being rotated.
    """
    _action(arm, name)
    _rest(arm)

    lead = direction * d(9.0)      # how far the spine commits ahead of the feet
    tangent = _turn_tangents(arm)

    for f in range(TURN_FRAMES + 1):
        u = (f % TURN_FRAMES) / float(TURN_FRAMES)

        for leg, phase in LEG_PHASE.items():
            cycle = (u + phase) % 1.0

            # reach=0: the fore-aft sweep comes from the tangent below, so all
            # this contributes is the lift and the knee that clear the ground.
            #
            # A much lower lift than the gait uses. Folding the knee swings the
            # shin fore-and-aft whether you want it to or not, and on a turn that
            # is parasitic travel: the middle legs sit nearest the turn centre and
            # take the shortest tangential step, so at the gait's 40 deg fold they
            # implied 61 deg of body rotation per cycle against the outer legs' 36
            # and scuffed the ground. It is also simply what a heavy animal does
            # -- it shuffles round rather than high-stepping.
            _, tibia, hoof = _leg_pose(cycle, 0.0, d(22), d(12), d(9))

            if cycle < SWING:
                t = _smoothstep(cycle / SWING)
                sweep = -1.0 + 2.0 * t          # swing round to the new spot
            else:
                t = (cycle - SWING) / (1.0 - SWING)
                sweep = 1.0 - 2.0 * t           # planted, body turns over it

            tx, ty, radius = tangent[leg]
            # arc length omega*r, expressed as the femur rotation that moves the
            # foot that far.
            step = direction * sweep * d(TURN_SWEEP_DEG) * radius / LEG_LENGTH
            # pitch(+) swings the leg toward -X, which is forward; roll(+) swings
            # it toward +Y. So a tangential step decomposes with a sign flip on x.
            pose(arm.pose.bones["femur_%s" % leg], pitch=-step * tx, roll=step * ty)
            pose(arm.pose.bones["tibia_%s" % leg], pitch=tibia)
            pose(arm.pose.bones["hoof_%s" % leg], pitch=hoof)

        bob = math.sin(u * 4.0 * math.pi)
        sway = math.sin(u * 2.0 * math.pi)

        pose(arm.pose.bones["spine1"], pitch=bob * d(1.8),
             yaw=lead * 0.35 + sway * d(2.2))
        pose(arm.pose.bones["spine2"], pitch=bob * d(-1.1), yaw=lead * 0.55)
        pose(arm.pose.bones["spine3"], pitch=bob * d(0.7), yaw=lead * 0.75)
        pose(arm.pose.bones["neck"], pitch=bob * d(1.4), yaw=lead * 1.0)
        pose(arm.pose.bones["head"], pitch=bob * d(-0.8), yaw=lead * 0.85)

        # Counterswing: the tail hangs behind the turn rather than following it.
        lag = math.sin((u - 0.15) * 2.0 * math.pi)
        pose(arm.pose.bones["tail1"], yaw=-lead * 0.30 + lag * d(4.0))
        pose(arm.pose.bones["tail2"], yaw=-lead * 0.50 + lag * d(6.0))
        pose(arm.pose.bones["tail3"], yaw=-lead * 0.70 + lag * d(7.5))

        set_jaw(arm)

        _key(arm, LEG_BONES + SPINE + TAIL + ["jaw"], f)

    return TURN_FRAMES


def build_turn_left(arm):
    return _build_turn(arm, "Appa_TurnL", +1)


def build_turn_right(arm):
    return _build_turn(arm, "Appa_TurnR", -1)


def build_idle(arm):
    _action(arm, "Appa_Idle")
    _rest(arm)

    for f in range(IDLE_FRAMES + 1):
        u = (f % IDLE_FRAMES) / float(IDLE_FRAMES)

        breath = math.sin(u * 2.0 * math.pi)
        # A second, slower and offset motion so the loop does not read as one
        # sine wave. Two incommensurate rates is the cheapest way to hide a loop.
        drift = math.sin(u * 2.0 * math.pi * 3.0 + 1.1)

        pose(arm.pose.bones["spine1"], pitch=breath * d(1.1))
        pose(arm.pose.bones["spine2"], pitch=breath * d(-0.9))
        pose(arm.pose.bones["spine3"], pitch=breath * d(0.7))
        pose(arm.pose.bones["neck"], pitch=breath * d(1.8), yaw=drift * d(2.6))
        pose(arm.pose.bones["head"], pitch=breath * d(-1.2), yaw=drift * d(-3.4))

        # Mostly shut, opening twice in the four-second loop. A jaw that chewed
        # continuously read as a nervous animal; a resting one holds its mouth
        # closed and works it occasionally. Negative pitch opens.
        # Once per eight seconds, and briefly. He is standing about, not chewing.
        gape = _pulse(u, 0.34, 0.40, 0.50) * d(30.0)
        set_jaw(arm, gape)

        swish = math.sin(u * 2.0 * math.pi * 2.0)
        pose(arm.pose.bones["tail1"], yaw=swish * d(4.0))
        pose(arm.pose.bones["tail2"], yaw=swish * d(6.5))
        pose(arm.pose.bones["tail3"], yaw=swish * d(8.0))

        _key(arm, SPINE + TAIL + ["jaw"], f)

    return IDLE_FRAMES



def build_graze(arm):
    """Head down in the scrub, chewing. Loops for as long as the task runs.

    Nose to the ground is a big neck rotation, so the spine takes some of it --
    dropping the whole neck from a rigid back would fold him at one joint and
    read as a broken puppet. The jaw works steadily here, unlike the idle: he is
    actually eating.
    """
    _action(arm, "Appa_Graze")
    _rest(arm)

    for f in range(GRAZE_FRAMES + 1):
        u = (f % GRAZE_FRAMES) / float(GRAZE_FRAMES)

        # Slow search left and right along the ground, plus a gentle bob.
        sweep = math.sin(u * 2.0 * math.pi)
        bob = math.sin(u * 2.0 * math.pi * 2.0)
        chew = 0.5 + 0.5 * math.sin(u * 2.0 * math.pi * 6.0)

        pose(arm.pose.bones["spine1"], pitch=d(-2.0) + bob * d(0.8))
        pose(arm.pose.bones["spine2"], pitch=d(-5.0) + bob * d(0.6))
        pose(arm.pose.bones["spine3"], pitch=d(-9.0), yaw=sweep * d(3.0))
        pose(arm.pose.bones["neck"], pitch=d(-34.0) + bob * d(1.5), yaw=sweep * d(7.0))
        pose(arm.pose.bones["head"], pitch=d(-24.0) + bob * d(1.2), yaw=sweep * d(5.0))
        set_jaw(arm, chew * d(40.0))

        lag = math.sin((u - 0.2) * 2.0 * math.pi)
        pose(arm.pose.bones["tail1"], yaw=lag * d(3.0))
        pose(arm.pose.bones["tail2"], yaw=lag * d(5.0))
        pose(arm.pose.bones["tail3"], yaw=lag * d(6.5))

        _key(arm, SPINE + TAIL + ["jaw"], f)

    return GRAZE_FRAMES


def build_happy(arm):
    """Being petted. A one-shot reaction, not a pose to hold.

    Reads as pleasure rather than aggression, which matters because the roar
    uses the same two bones: the head lifts and tips *sideways* into the hand
    instead of thrusting forward, the jaw hangs open loosely rather than gaping,
    and the tail goes twice as fast as anything else he does. Nothing in the
    legs -- he stays planted, so the player is never pushed away mid-pet.
    """
    _action(arm, "Appa_Happy")
    _rest(arm)

    for f in range(HAPPY_FRAMES + 1):
        u = f / float(HAPPY_FRAMES)

        lean = _pulse(u, 0.00, 0.30, 1.00)            # rise, hold, settle
        nuzzle = math.sin(u * 2.0 * math.pi * 2.0) * lean
        wag = math.sin(u * 2.0 * math.pi * 5.0)
        gape = _pulse(u, 0.08, 0.35, 0.95)

        pose(arm.pose.bones["spine1"], pitch=lean * d(2.0))
        pose(arm.pose.bones["spine2"], pitch=lean * d(3.0), yaw=nuzzle * d(2.0))
        pose(arm.pose.bones["spine3"], pitch=lean * d(4.0), yaw=nuzzle * d(3.0))
        pose(arm.pose.bones["neck"], pitch=lean * d(15.0), yaw=nuzzle * d(7.0),
             roll=lean * d(6.0))
        pose(arm.pose.bones["head"], pitch=lean * d(10.0), yaw=nuzzle * d(9.0),
             roll=lean * d(11.0))
        set_jaw(arm, gape * d(34.0))

        pose(arm.pose.bones["tail1"], yaw=wag * d(9.0) * lean)
        pose(arm.pose.bones["tail2"], yaw=wag * d(14.0) * lean)
        pose(arm.pose.bones["tail3"], yaw=wag * d(18.0) * lean)

        _key(arm, SPINE + TAIL + ["jaw"], f)

    return HAPPY_FRAMES


# ---------------------------------------------------------------------------
# One-shots
# ---------------------------------------------------------------------------

def build_roar(arm):
    """The attack telegraph.

    This clip is the only fair warning the player gets that a friendly animal has
    stopped being friendly, so it is authored to be *read* rather than to look
    impressive (GDC-L1-ANIM-0003). Three beats, deliberately slow:

        anticipation  head drops and coils back
        strike        head and neck snap up, jaw wide, chest lifts
        hold          held long enough to register, with a tremor on it
        settle        back to neutral, ready for the charge

    The dip before the lift is the anticipation (GDC-L1-ANIM-0001): without it
    the head just rises and the whole thing reads as a stretch.
    """
    _action(arm, "Appa_Roar")
    _rest(arm)

    for f in range(ROAR_FRAMES + 1):
        u = f / float(ROAR_FRAMES)

        coil = _pulse(u, 0.00, 0.16, 0.40)        # the dip
        rise = _ramp(u, 0.16, 0.42) * (1.0 - _ramp(u, 0.70, 1.00))
        tremor = math.sin(u * 2.0 * math.pi * 9.0) * _pulse(u, 0.42, 0.55, 0.72)

        pose(arm.pose.bones["spine1"], pitch=rise * d(7.0) - coil * d(3.0))
        pose(arm.pose.bones["spine2"], pitch=rise * d(9.0) - coil * d(4.0))
        pose(arm.pose.bones["spine3"], pitch=rise * d(11.0) - coil * d(5.0))
        pose(arm.pose.bones["neck"], pitch=rise * d(34.0) - coil * d(16.0)
             + tremor * d(1.6))
        pose(arm.pose.bones["head"], pitch=rise * d(26.0) - coil * d(12.0)
             + tremor * d(2.2))
        set_jaw(arm, rise * d(55.0) + tremor * d(3.0))

        # Front legs brace and splay as the chest comes up.
        for leg in LEGS:
            side = 1.0 if leg.endswith(".L") else -1.0
            if leg.startswith("F"):
                pose(arm.pose.bones["femur_%s" % leg], pitch=-rise * d(9.0),
                     roll=side * rise * d(7.0))
                pose(arm.pose.bones["tibia_%s" % leg], pitch=rise * d(6.0))
            else:
                pose(arm.pose.bones["femur_%s" % leg], pitch=rise * d(4.0))

        pose(arm.pose.bones["tail1"], pitch=rise * d(10.0))
        pose(arm.pose.bones["tail2"], pitch=rise * d(8.0))
        pose(arm.pose.bones["tail3"], pitch=rise * d(6.0))

        _key(arm, ALL_BONES, f)

    return ROAR_FRAMES


def build_ram(arm):
    """Head-down charge: coil, drive, impact, recover.

    The drive is a real 0.55 m lunge on `spine1` rather than rotation alone. A
    ram built only out of neck rotation reads as a nod, because the mass never
    goes anywhere -- and the mass is the entire point of a six-legged bison
    hitting something.
    """
    _action(arm, "Appa_Ram")
    _rest(arm)

    for f in range(RAM_FRAMES + 1):
        u = f / float(RAM_FRAMES)

        coil = _pulse(u, 0.00, 0.22, 0.44)                       # rear back
        drive = _ramp(u, 0.22, 0.44) * (1.0 - _ramp(u, 0.62, 1.00))
        impact = _pulse(u, 0.44, 0.50, 0.66)                     # the jolt

        lunge = FORWARD * (drive * 0.55) + UP * (coil * 0.10 - impact * 0.06)
        shift(arm.pose.bones["spine1"], lunge)

        pose(arm.pose.bones["spine1"], pitch=coil * d(9.0) - drive * d(13.0))
        pose(arm.pose.bones["spine2"], pitch=coil * d(6.0) - drive * d(9.0))
        pose(arm.pose.bones["spine3"], pitch=coil * d(5.0) - drive * d(7.0))
        pose(arm.pose.bones["neck"], pitch=coil * d(22.0) - drive * d(30.0)
             - impact * d(5.0))
        pose(arm.pose.bones["head"], pitch=coil * d(14.0) - drive * d(24.0)
             - impact * d(6.0))
        set_jaw(arm, drive * d(34.0))

        # Back legs push, front legs reach out to catch the landing.
        for leg in LEGS:
            if leg.startswith("B"):
                pose(arm.pose.bones["femur_%s" % leg], pitch=-drive * d(26.0))
                pose(arm.pose.bones["tibia_%s" % leg], pitch=drive * d(20.0))
            elif leg.startswith("F"):
                pose(arm.pose.bones["femur_%s" % leg], pitch=drive * d(22.0))
                pose(arm.pose.bones["tibia_%s" % leg], pitch=-drive * d(14.0))
            else:
                pose(arm.pose.bones["femur_%s" % leg], pitch=-drive * d(8.0))

        pose(arm.pose.bones["tail1"], pitch=-drive * d(14.0))
        pose(arm.pose.bones["tail2"], pitch=-drive * d(11.0))
        pose(arm.pose.bones["tail3"], pitch=-drive * d(9.0))

        _key(arm, ALL_BONES, f, location=["spine1"])

    return RAM_FRAMES


def build_hurt(arm):
    """A flinch. Short, because it has to confirm a hit without eating the frame.

    Hit reactions are confirmation, not performance (GDC-L1-ANIM-0003): the
    player needs to know the shot landed, and a long recoil on a creature that
    is mid-charge would read as a stagger it never actually took.
    """
    _action(arm, "Appa_Hurt")
    _rest(arm)

    for f in range(HURT_FRAMES + 1):
        u = f / float(HURT_FRAMES)
        hit = _pulse(u, 0.0, 0.22, 1.0)
        shudder = math.sin(u * 2.0 * math.pi * 6.0) * hit

        pose(arm.pose.bones["spine1"], pitch=hit * d(6.0), yaw=shudder * d(3.0))
        pose(arm.pose.bones["spine2"], pitch=hit * d(-4.0), yaw=shudder * d(-2.0))
        pose(arm.pose.bones["spine3"], pitch=hit * d(-3.0))
        pose(arm.pose.bones["neck"], pitch=hit * d(14.0), yaw=shudder * d(5.0))
        pose(arm.pose.bones["head"], pitch=hit * d(10.0), yaw=shudder * d(-6.0))
        set_jaw(arm, hit * d(38.0))

        for leg in LEGS:
            side = 1.0 if leg.endswith(".L") else -1.0
            pose(arm.pose.bones["femur_%s" % leg], roll=side * hit * d(4.0))
            pose(arm.pose.bones["tibia_%s" % leg], pitch=hit * d(5.0))

        pose(arm.pose.bones["tail1"], pitch=hit * d(12.0))
        pose(arm.pose.bones["tail2"], pitch=hit * d(9.0))
        pose(arm.pose.bones["tail3"], pitch=hit * d(7.0))

        _key(arm, ALL_BONES, f)

    return HURT_FRAMES


def build_jump(arm):
    """A mounted hop: gather, push off, tuck, reach, absorb. Strictly in place.

    The height is not here. `NavMeshAgentMotor` lifts him by animating the
    agent's `baseOffset`, so this clip supplies only the *pose* that makes the
    lift read as his own effort -- exactly the in-place/root-motion split in
    GDC-L1-ANIM-0004. Keying a rise in here as well would double it.

    Six legs make the timing legible on their own: the front pairs reach for
    the ground before the back ones do, so he lands nose-first the way a heavy
    quadruped does rather than dropping flat like a lift.
    """
    _action(arm, "Appa_Jump")
    _rest(arm)

    # Front to back, so the gather rolls down him and the landing rolls back up.
    ORDER = {"F.L": 0.0, "F.R": 0.0, "M.L": 0.5, "M.R": 0.5, "B.L": 1.0, "B.R": 1.0}

    for f in range(JUMP_FRAMES + 1):
        u = f / float(JUMP_FRAMES)
        gather = _pulse(u, 0.0, 0.14, 0.34)     # crouch before the push
        tuck = _pulse(u, 0.20, 0.52, 0.94)      # legs folded under him
        land = _pulse(u, 0.78, 0.92, 1.0)       # knees absorbing the drop

        # He rounds his back to gather and arches over the apex.
        pose(arm.pose.bones["spine1"], pitch=gather * d(7.0) - tuck * d(5.0))
        pose(arm.pose.bones["spine2"], pitch=gather * d(5.0) - tuck * d(4.0))
        pose(arm.pose.bones["spine3"], pitch=gather * d(4.0) - tuck * d(3.0))
        # The head leads: down into the crouch, up and out over the top.
        pose(arm.pose.bones["neck"], pitch=gather * d(12.0) - tuck * d(16.0) + land * d(8.0))
        pose(arm.pose.bones["head"], pitch=gather * d(6.0) - tuck * d(10.0) + land * d(6.0))
        set_jaw(arm, tuck * d(22.0))            # a grunt of effort, not a roar

        for leg in LEGS:
            lag = ORDER[leg] * 0.10
            reach = _pulse(u, 0.62 + lag, 0.80 + lag, 0.98)
            # Crouch folds the knee, the tuck pulls the whole leg up and under,
            # and the reach straightens it again to meet the ground.
            pose(arm.pose.bones["femur_%s" % leg],
                 pitch=-gather * d(8.0) + tuck * d(34.0) - reach * d(20.0))
            pose(arm.pose.bones["tibia_%s" % leg],
                 pitch=-gather * d(22.0) - tuck * d(46.0) + reach * d(30.0) - land * d(16.0))
            pose(arm.pose.bones["hoof_%s" % leg],
                 pitch=tuck * d(24.0) - reach * d(14.0))

        # The tail streams behind whatever the body just did.
        pose(arm.pose.bones["tail1"], pitch=-gather * d(10.0) + tuck * d(16.0))
        pose(arm.pose.bones["tail2"], pitch=-gather * d(8.0) + tuck * d(20.0))
        pose(arm.pose.bones["tail3"], pitch=-gather * d(6.0) + tuck * d(22.0))

        _key(arm, ALL_BONES, f)

    return JUMP_FRAMES


def build_death(arm):
    """Collapse onto the left side, and stay there.

    The body genuinely sinks 1.15 m on `spine1`, which is roughly the standing
    height of the belly -- rotation alone leaves the animal folded but still
    floating at walking height. The clip ends dead still on the last frame so
    the animator can hold it with no visible settle.
    """
    _action(arm, "Appa_Death")
    _rest(arm)

    for f in range(DEATH_FRAMES + 1):
        u = f / float(DEATH_FRAMES)

        buckle = _ramp(u, 0.05, 0.45)      # legs give way
        fall = _ramp(u, 0.18, 0.62)        # body goes down
        limp = _ramp(u, 0.55, 0.95)        # everything goes slack
        # One last lift of the head before it goes down for good.
        gasp = _pulse(u, 0.60, 0.70, 0.88)

        shift(arm.pose.bones["spine1"], UP * (-fall * 1.15) + FORWARD * (fall * 0.18))

        pose(arm.pose.bones["spine1"], pitch=-fall * d(10.0), roll=fall * d(46.0))
        pose(arm.pose.bones["spine2"], pitch=-fall * d(7.0), roll=fall * d(16.0))
        pose(arm.pose.bones["spine3"], pitch=-fall * d(5.0), roll=fall * d(10.0))
        pose(arm.pose.bones["neck"], pitch=-fall * d(26.0) + gasp * d(20.0),
             yaw=fall * d(18.0))
        pose(arm.pose.bones["head"], pitch=-fall * d(20.0) + gasp * d(14.0)
             - limp * d(10.0), yaw=fall * d(12.0))
        set_jaw(arm, gasp * d(40.0) + limp * d(10.0))

        # Legs fold under the body, the near side further than the far side.
        for leg in LEGS:
            side = 1.0 if leg.endswith(".L") else -1.0
            fold = buckle * (1.0 if side > 0 else 0.7)
            pose(arm.pose.bones["femur_%s" % leg], pitch=fold * d(34.0),
                 roll=side * fold * d(20.0))
            pose(arm.pose.bones["tibia_%s" % leg], pitch=-fold * d(62.0))
            pose(arm.pose.bones["hoof_%s" % leg], pitch=fold * d(24.0))

        pose(arm.pose.bones["tail1"], pitch=-fall * d(16.0), yaw=fall * d(22.0))
        pose(arm.pose.bones["tail2"], pitch=-fall * d(12.0), yaw=fall * d(18.0))
        pose(arm.pose.bones["tail3"], pitch=-fall * d(9.0), yaw=fall * d(14.0))

        _key(arm, ALL_BONES, f, location=["spine1"])

    return DEATH_FRAMES


# ---------------------------------------------------------------------------

BUILDERS = [
    ("Appa_Idle", build_idle),
    ("Appa_Walk", build_walk),
    ("Appa_Run", build_run),
    ("Appa_TurnL", build_turn_left),
    ("Appa_TurnR", build_turn_right),
    ("Appa_Graze", build_graze),
    ("Appa_Happy", build_happy),
    ("Appa_Roar", build_roar),
    ("Appa_Ram", build_ram),
    ("Appa_Hurt", build_hurt),
    ("Appa_Jump", build_jump),
    ("Appa_Death", build_death),
]


def build():
    arm = bpy.data.objects.get(ARM)
    if arm is None:
        raise SystemExit("No %s -- run appa_rig.py first." % ARM)

    bpy.context.scene.render.fps = FPS
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')

    built = [(name, fn(arm)) for name, fn in BUILDERS]

    # Leave no action assigned, so the rig is not stuck in a pose next time the
    # author opens the file.
    arm.animation_data.action = None
    _rest(arm)
    bpy.ops.object.mode_set(mode='OBJECT')

    return built


def verify(built):
    """Fail loudly if a clip animates nothing.

    Guards the exact defect this file was rewritten to fix: a clip full of
    keyframes whose every curve is flat, because the euler component being keyed
    was the bone's own twist axis. Counting keys would have called that healthy.
    """
    for name, _ in built:
        act = bpy.data.actions[name]
        moved = set()
        for fc in act.fcurves:
            values = [k.co[1] for k in fc.keyframe_points]
            if values and max(values) - min(values) > 1e-5:
                moved.add(fc.data_path)

        if not moved:
            raise SystemExit("%s has no curve that actually changes -- it would "
                             "import into Unity as a static pose." % name)

        # The jaw must be keyed in EVERY clip, not just the ones that open it.
        # An unkeyed jaw falls back to the sculpt's rest pose, which is 26 deg
        # OPEN -- so a clip that simply ignores the bone ships him gaping. Walk,
        # Run, TurnL and TurnR all did.
        if not any(p.endswith('["jaw"].rotation_euler') for p in
                   {fc.data_path for fc in act.fcurves}):
            raise SystemExit("%s never keys the jaw; it would inherit the sculpt's "
                             "resting gape and play with his mouth open." % name)

        legs = sum(1 for p in moved if "femur_" in p or "tibia_" in p)
        if name in ("Appa_Walk", "Appa_Run", "Appa_TurnL", "Appa_TurnR") and legs < 12:
            raise SystemExit("%s moves only %d leg curves; the gait is not "
                             "animating." % (name, legs))


if __name__ == "__main__":
    built = build()
    verify(built)

    for name, frames in built:
        print("  %-12s %3d frames (%.2f s)" % (name, frames, frames / float(FPS)))
    print("actions: %s" % [a.name for a in bpy.data.actions])

    if "--save" in sys.argv:
        bpy.ops.wm.save_mainfile()
        print("saved %s" % bpy.data.filepath)
    else:
        print("NOT saved (pass -- --save to write the .blend)")
