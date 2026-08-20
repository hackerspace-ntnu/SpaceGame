"""Author the Golem's six actions into `golem.blend`.

Run after `golem_rig.py`. It refuses to run if actions already exist, because
it authors them from scratch and would otherwise leave duplicates.

    blender --background --python golem_anim.py

    Golem_Idle     120f  loop   settling, grinding, a slow weight shift
    Golem_Walk      36f  loop   four-point lateral-sequence lumber
    Golem_Run       26f  loop   two-beat bound: fists slam, then feet
    Golem_Attack    48f  once   rears back, then drives both fists into the ground
    Golem_Hurt      22f  once   a shock that travels up through the stack of rocks
    Golem_Death     72f  once   the legs buckle, it falls onto its fists, it crumbles

Every clip is **in place**. `NavMeshAgentMotor` owns movement, and a clip that
also translated the root would fight it -- which is also why the prefab keeps
`applyRootMotion = false`.

## Why this uses IK, and why the IK is baked

The golem walks on its fists as well as its feet, so four contact points have
to stay welded to the ground while the body rides over them. Written as FK
joint angles that is a guessing game; written as "this contact is at this point
in armature space on this frame" it is exact, and the settle on each footfall
comes out for free -- the body drops, the contact does not move, and the knee
absorbs it.

FBX cannot carry a Blender IK constraint, so the chains are solved
**analytically at authoring time** (`ik2` below) and written straight out as
bone rotations. That is the same thing a constraint-plus-bake would produce,
minus the bake step and minus any dependency on operator context, which
`blender --background` makes awkward.

## Why the strides are short

The golem is short-legged: the hip sits 5.48 units above the sole plane on a
body 10.62 units tall, and at rest the hip-to-ankle run is within 0.001 units
of a completely straight leg. The stride is bounded by how far the ankle can swing
before the leg runs out of length, which is why every locomotion clip crouches
first -- a straight leg has no horizontal budget at all.

The Unity blend-tree thresholds in `GolemBuilder.cs` are derived from that
bound, not chosen. For a gait the contact must track the ground during stance,
so

    speed = 2 * half_stride / (duty * cycle_seconds)

and the constants at the bottom of this file print exactly that when it runs.
Change a stride or a duty factor here and the printed speeds must be copied
into `GolemBuilder.WalkSpeed` / `RunSpeed`, or the creature moon-walks.

## The one compromise

`Mesh_Golem_Torso_Core` is a single boulder spanning y -9.41 .. -1.66 -- pelvis
to head. It is bone-parented to `Bone_Spine` and cannot bend, so spine and hip
flex are capped near 6 degrees; past that a seam opens between it and the rocks
around it. A heavy stone construct should barely flex anyway, so this reads as
intent rather than as a limit, but splitting that boulder is the real fix and it
means cutting the artist's geometry.
"""

import math
import os

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "golem.blend")

FPS = 30
ARM = "Arm_Golem"

FWD = Vector((0.0, 1.0, 0.0))      # the golem faces +Y in the .blend
UP = Vector((0.0, 0.0, 1.0))

SIDES = ("R", "L")

# Pose order matters: a bone cannot be solved before its parent.
ORDER = (["Bone_Root", "Bone_Hips", "Bone_Spine", "Bone_Chest", "Bone_Head"] +
         ["Bone_%s_%s" % (n, s) for s in SIDES
          for n in ("Clav", "UpArm", "LoArm", "Hand")] +
         ["Bone_%s_%s" % (n, s) for s in SIDES
          for n in ("Thigh", "Shin", "Foot")])


# ---------------------------------------------------------------------------
# Posing
# ---------------------------------------------------------------------------

class Poser:
    """Writes pose bones by way of their armature-space matrices.

    Blender's own relation is

        pose.matrix = parent.pose.matrix
                      @ parent.bone.matrix_local.inverted()
                      @ bone.matrix_local
                      @ pose.matrix_basis

    so given a target armature-space matrix the basis falls straight out. Doing
    it this way -- rather than composing local Euler angles -- means the gait
    can be written in the frame the animal actually has ("this fist is here",
    "pitch the body about world X") instead of in whatever frame each bone's
    roll happened to land in.

    It also means no `view_layer.update()` per bone: the chain is tracked here,
    in `self.M`, so a whole frame is pure arithmetic.
    """

    def __init__(self, arm_obj):
        self.arm = arm_obj
        self.M = {}

    def reset(self):
        self.M.clear()

    def carried(self, name):
        """Where this bone would be if its own basis were identity."""
        bone = self.arm.data.bones[name]
        if bone.parent is None:
            return bone.matrix_local.copy()
        return (self.M[bone.parent.name]
                @ bone.parent.matrix_local.inverted()
                @ bone.matrix_local)

    def _apply(self, name, base, target):
        basis = base.inverted() @ target
        pb = self.arm.pose.bones[name]
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = basis.to_quaternion()
        pb.location = basis.to_translation()
        self.M[name] = target

    def identity(self, name):
        base = self.carried(name)
        self._apply(name, base, base)

    def set_rot(self, name, rot=None, offset=None):
        """Rotate about the bone's head by an armature-space 3x3, and/or slide it."""
        base = self.carried(name)
        mat3 = base.to_3x3()
        if rot is not None:
            mat3 = rot @ mat3
        loc = base.to_translation()
        if offset is not None:
            loc = loc + Vector(offset)
        self._apply(name, base, Matrix.Translation(loc) @ mat3.to_4x4())

    def set_dir(self, name, direction, twist=0.0):
        """Aim the bone's head->tail axis along an armature-space direction.

        The rotation used is the *minimal* one from where the parent left the
        bone pointing to where it should point, so the bone's roll follows the
        limb instead of snapping to some arbitrary reference.
        """
        base = self.carried(name)
        mat3 = base.to_3x3()
        cur = (mat3 @ Vector((0.0, 1.0, 0.0))).normalized()
        tgt = Vector(direction).normalized()
        mat3 = cur.rotation_difference(tgt).to_matrix() @ mat3
        if abs(twist) > 1e-9:
            mat3 = Matrix.Rotation(twist, 3, tgt) @ mat3
        self._apply(name, base,
                    Matrix.Translation(base.to_translation()) @ mat3.to_4x4())

    def head(self, name):
        return self.M[name].to_translation()

    def tail(self, name):
        bone = self.arm.data.bones[name]
        return self.M[name] @ Vector((0.0, bone.length, 0.0))


def axis_rot(deg, axis):
    return Matrix.Rotation(math.radians(deg), 3, axis)


def rest_length(arm_obj, name):
    return arm_obj.data.bones[name].length


# Worst distance by which a requested contact was further away than the limb
# could reach, per chain, filled in by `ik2` and reported at the end of the run.
# A limb that is clamped is a limb that has gone straight and stopped tracking
# the ground, so any non-trivial figure here means a stride is too long for the
# leg and the creature will skate on that frame.
OVERREACH = {}


def ik2(poser, upper, lower, target, pole_local):
    """Two-bone IK. Places `lower`'s tail on `target`, bending toward `pole`.

    `pole_local` is expressed in the upper bone's own rest frame and is carried
    into armature space by whatever the body is doing, so the elbow keeps
    pointing out and the knee keeps pointing the way the model was built --
    even while the torso leans.
    """
    base = poser.carried(upper)
    root = base.to_translation()
    l1 = rest_length(poser.arm, upper)
    l2 = rest_length(poser.arm, lower)

    v = Vector(target) - root
    limit = (l1 + l2) * 0.999
    if v.length > limit:
        OVERREACH[upper] = max(OVERREACH.get(upper, 0.0), v.length - limit)
    d = min(max(v.length, abs(l1 - l2) + 1e-3), limit)
    if v.length < 1e-6:
        v = Vector((0.0, 0.0, -1.0))
    vh = v.normalized()

    pole = base.to_3x3() @ Vector(pole_local)
    perp = pole - vh * pole.dot(vh)
    if perp.length < 1e-4:                      # degenerate: pick any normal
        perp = vh.cross(FWD if abs(vh.dot(FWD)) < 0.9 else UP)
    perp.normalize()

    cos_a = (l1 * l1 + d * d - l2 * l2) / (2.0 * l1 * d)
    a = math.acos(min(1.0, max(-1.0, cos_a)))
    upper_dir = vh * math.cos(a) + perp * math.sin(a)
    joint = root + upper_dir * l1
    end = root + vh * d

    poser.set_dir(upper, upper_dir)
    poser.set_dir(lower, (end - joint).normalized())
    return end


# ---------------------------------------------------------------------------
# The pose the whole file is written in terms of
# ---------------------------------------------------------------------------

class Anatomy:
    """Rest anchors and IK poles, measured off the armature rather than typed in."""

    def __init__(self, arm_obj):
        b = arm_obj.data.bones
        self.wrist = {s: b["Bone_LoArm_%s" % s].tail_local.copy() for s in SIDES}
        self.ankle = {s: b["Bone_Shin_%s" % s].tail_local.copy() for s in SIDES}
        self.knuckle = {s: b["Bone_Hand_%s" % s].tail_local.copy() for s in SIDES}
        self.pole = {}
        for s in SIDES:
            self.pole["arm" + s] = self._pole(arm_obj, "Bone_UpArm_%s" % s,
                                              "Bone_LoArm_%s" % s, FWD)
            self.pole["leg" + s] = self._pole(arm_obj, "Bone_Thigh_%s" % s,
                                              "Bone_Shin_%s" % s, -FWD)

    @staticmethod
    def _pole(arm_obj, upper, lower, fallback):
        """The direction the joint already bends in, in the upper bone's frame.

        Taken from the model rather than chosen: both limbs are stacks of rocks
        and sit within a few degrees of straight, so picking a pole by anatomy
        ("knees forward") would snap them the other way on the first frame. The
        rest bend is small but it is unambiguous, and using it means the rig's
        rest pose is exactly what the artist assembled.
        """
        b = arm_obj.data.bones
        root = b[upper].head_local
        joint = b[lower].head_local
        end = b[lower].tail_local
        vh = (end - root).normalized()
        off = joint - root
        perp = off - vh * off.dot(vh)
        if perp.length < 1e-3:
            perp = Vector(fallback) - vh * Vector(fallback).dot(vh)
        return b[upper].matrix_local.to_3x3().inverted() @ perp.normalized()


def apply_pose(poser, an, p):
    """One complete frame. Every key in `p` is optional and defaults to rest."""
    poser.reset()

    poser.set_rot("Bone_Root",
                  axis_rot(p.get("pitch", 0.0), 'X')
                  @ axis_rot(p.get("roll", 0.0), 'Y')
                  @ axis_rot(p.get("yaw", 0.0), 'Z'),
                  offset=p.get("root_off", (0.0, 0.0, 0.0)))
    poser.set_rot("Bone_Hips", axis_rot(p.get("hip_flex", 0.0), 'X')
                  @ axis_rot(p.get("hip_yaw", 0.0), 'Z'))
    poser.set_rot("Bone_Spine", axis_rot(p.get("spine_flex", 0.0), 'X')
                  @ axis_rot(p.get("spine_roll", 0.0), 'Y'))
    poser.set_rot("Bone_Chest", axis_rot(p.get("chest_flex", 0.0), 'X')
                  @ axis_rot(p.get("chest_yaw", 0.0), 'Z'))
    poser.set_rot("Bone_Head", axis_rot(p.get("head_pitch", 0.0), 'X')
                  @ axis_rot(p.get("head_yaw", 0.0), 'Z'))

    for s in SIDES:
        poser.set_rot("Bone_Clav_%s" % s,
                      axis_rot(p.get("clav", {}).get(s, 0.0), 'X'))
        ik2(poser, "Bone_UpArm_%s" % s, "Bone_LoArm_%s" % s,
            p.get("wrist", {}).get(s, an.wrist[s]), an.pole["arm" + s])
        poser.set_rot("Bone_Hand_%s" % s,
                      axis_rot(p.get("hand", {}).get(s, 0.0), 'X'))

    for s in SIDES:
        ik2(poser, "Bone_Thigh_%s" % s, "Bone_Shin_%s" % s,
            p.get("ankle", {}).get(s, an.ankle[s]), an.pole["leg" + s])
        poser.set_rot("Bone_Foot_%s" % s,
                      axis_rot(p.get("foot", {}).get(s, 0.0), 'X'))


def key_all(arm_obj, frame):
    for pb in arm_obj.pose.bones:
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        if pb.name == "Bone_Root":
            pb.keyframe_insert("location", frame=frame)


def new_action(arm_obj, name, length, loop):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True          # survives the save with no NLA strip
    arm_obj.animation_data.action = action
    action.frame_start, action.frame_end = 1, length
    action["loop"] = loop
    return action


# ---------------------------------------------------------------------------
# Gait
# ---------------------------------------------------------------------------

def contact(anchor, u, cfg, lift):
    """Where one contact point is at `u` (0..1) through its own step.

    Stance is linear on purpose. The contact is welded to the ground, so it has
    to travel backwards through the body at exactly the speed the body is
    travelling forwards -- any easing here and the golem skates.
    """
    duty = cfg["duty"]
    half = cfg["stride"]
    if u < duty:
        s = 1.0 - 2.0 * (u / duty)
        return Vector(anchor) + FWD * (half * s)
    w = (u - duty) / (1.0 - duty)
    # Swing eases at both ends: the rock is heavy, it does not snap forward.
    e = 0.5 - 0.5 * math.cos(math.pi * w)
    s = -1.0 + 2.0 * e
    return (Vector(anchor) + FWD * (half * s)
            + UP * (math.sin(math.pi * w) * lift))


def settle(t, beats, width, amp):
    """The body dropping onto each contact as it lands.

    This is the whole reason the creature reads as stone rather than as a
    costume: the mass arrives after the foot does.
    """
    total = 0.0
    for phase in beats:
        x = (t - phase) % 1.0
        if x < width:
            total += amp * math.sin(math.pi * x / width)
    return total


def build_locomotion(arm_obj, an, name, length, cfg):
    poser = Poser(arm_obj)
    new_action(arm_obj, name, length, loop=True)
    beats = sorted(cfg["phase"].values())
    for frame in range(1, length + 1):
        # Frame `length` reproduces frame 1 exactly, so the cycle closes; the
        # Unity importer then slices one frame short of that.
        t = (frame - 1) / float(length - 1)
        dip = settle(t, beats, cfg["settle_w"], cfg["settle"])

        p = {
            "root_off": (0.0,
                         cfg["surge"] * math.sin(2.0 * math.pi * t),
                         -cfg["crouch"] - dip),
            "pitch": cfg["pitch"] * math.sin(2.0 * math.pi * (t + 0.12)),
            "roll": cfg["roll"] * math.sin(2.0 * math.pi * t),
            "hip_flex": cfg["hip_flex"] * math.sin(2.0 * math.pi * t),
            "spine_flex": -cfg["spine_flex"] * math.sin(2.0 * math.pi * (t + 0.2)),
            "chest_flex": cfg["spine_flex"] * 0.6 * math.sin(2.0 * math.pi * (t + 0.3)),
            "head_pitch": cfg["head"] * math.sin(2.0 * math.pi * (t + 0.35)),
            "head_yaw": cfg["head"] * 0.7 * math.sin(2.0 * math.pi * t),
            "wrist": {}, "ankle": {}, "hand": {}, "foot": {}, "clav": {},
        }

        for s in SIDES:
            ua = (t + cfg["phase"]["fist" + s]) % 1.0
            p["wrist"][s] = contact(an.wrist[s], ua, cfg, cfg["fist_lift"])
            # The fist hangs plumb while planted and rolls forward over the
            # knuckles as it swings, the way a knuckle-walker's hand does.
            p["hand"][s] = (0.0 if ua < cfg["duty"] else
                            -cfg["knuckle"] * math.sin(math.pi * (
                                (ua - cfg["duty"]) / (1.0 - cfg["duty"]))))
            p["clav"][s] = cfg["shrug"] * math.sin(2.0 * math.pi * ua)

            ul = (t + cfg["phase"]["foot" + s]) % 1.0
            p["ankle"][s] = contact(an.ankle[s], ul, cfg, cfg["foot_lift"])
            p["foot"][s] = (0.0 if ul < cfg["duty"] else
                            cfg["toe"] * math.sin(2.0 * math.pi * (
                                (ul - cfg["duty"]) / (1.0 - cfg["duty"]))))

        apply_pose(poser, an, p)
        key_all(arm_obj, frame)


# Lateral sequence, the gait every heavy quadruped walks: hind foot, then the
# fore limb on the same side, then across. Three contacts are down at any
# instant, which is what lets it be this slow without wobbling.
WALK = dict(
    phase={"footL": 0.00, "fistL": 0.25, "footR": 0.50, "fistR": 0.75},
    duty=0.72, stride=1.66, crouch=0.96,
    foot_lift=0.85, fist_lift=1.05, toe=14.0, knuckle=18.0,
    settle=0.11, settle_w=0.16, surge=0.10,
    pitch=1.7, roll=1.9, hip_flex=3.2, spine_flex=2.4, head=3.0, shrug=2.5,
)

# A bound: both fists slam, the body vaults over them, both feet land. The 0.35
# duty leaves two short flight phases, which is where the extra ground speed
# comes from -- the contacts only have to track the ground while they are on it.
RUN = dict(
    phase={"fistR": 0.00, "fistL": 0.02, "footR": 0.50, "footL": 0.52},
    duty=0.35, stride=2.30, crouch=1.25,
    foot_lift=1.50, fist_lift=1.70, toe=22.0, knuckle=30.0,
    settle=0.30, settle_w=0.22, surge=0.35,
    pitch=5.0, roll=1.2, hip_flex=5.5, spine_flex=4.5, head=6.0, shrug=5.0,
)


def gait_speed(cfg, length, units_per_metre):
    """The forward speed this cycle is foot-locked to, in metres per second."""
    seconds = (length - 1) / float(FPS)
    return (2.0 * cfg["stride"]) / (cfg["duty"] * seconds) / units_per_metre


# ---------------------------------------------------------------------------
# Idle
# ---------------------------------------------------------------------------

def build_idle(arm_obj, an, length=120):
    """Four points down, nothing going anywhere.

    Three periods that do not divide into each other -- one settle, one weight
    shift, one head turn -- so a four-second loop never reads as a metronome.
    """
    poser = Poser(arm_obj)
    new_action(arm_obj, "Golem_Idle", length, loop=True)
    for frame in range(1, length + 1):
        t = (frame - 1) / float(length - 1)
        breathe = math.sin(2.0 * math.pi * t)
        shift = math.sin(2.0 * math.pi * t * 2.0 - 0.9)
        turn = math.sin(2.0 * math.pi * t * 0.5)

        p = {
            "root_off": (0.0, 0.02 * breathe, -0.16 - 0.06 * breathe),
            "pitch": 0.5 * breathe,
            "roll": 1.1 * shift,
            "hip_flex": 0.9 * breathe,
            "spine_flex": -0.7 * breathe,
            "chest_flex": 0.5 * breathe,
            "head_pitch": 1.6 * breathe,
            "head_yaw": 5.0 * turn,
            "clav": {"R": 1.2 * shift, "L": -1.2 * shift},
            "wrist": {}, "ankle": {},
        }
        # The weight rocks between the fists; the loaded arm compresses.
        for s, sign in (("R", 1.0), ("L", -1.0)):
            p["wrist"][s] = an.wrist[s] + UP * (0.10 * sign * shift)
            p["ankle"][s] = an.ankle[s] + UP * (0.04 * sign * shift)

        apply_pose(poser, an, p)
        key_all(arm_obj, frame)


# ---------------------------------------------------------------------------
# One-shots
#
# These are keyed on stages rather than per frame, so Blender's default bezier
# interpolation does the easing. That is what a heavy thing wants: slow out of
# the anticipation, fast through the strike, dead stop on the impact.
# ---------------------------------------------------------------------------

def build_stages(arm_obj, an, name, length, stages):
    poser = Poser(arm_obj)
    new_action(arm_obj, name, length, loop=False)
    for frame, p in stages:
        apply_pose(poser, an, p)
        key_all(arm_obj, frame)


def build_attack(arm_obj, an):
    """Rock back onto the hind legs, wind both arms up, drive them down.

    The feet never leave the ground. A construct this heavy does not lunge --
    it plants and swings its own mass, and the fists arrive well ahead of the
    body settling behind them.
    """
    def arms(dy, dz):
        return {s: an.wrist[s] + FWD * dy + UP * dz for s in SIDES}

    stages = [
        # frame                                        y      z
        (1,  dict(root_off=(0, 0, -0.10))),
        (10, dict(root_off=(0, -0.35, -0.30), pitch=-3.0, hip_flex=-3.0,
                  head_pitch=-4.0, wrist=arms(-0.45, 0.15),
                  hand={"R": 6.0, "L": 6.0})),          # gather, weight back
        # Rearing *drops* the root rather than lifting it. The rest leg is
        # 0.001 units short of straight, so any backward pitch already lengthens
        # the hip-to-ankle run; lifting as well would tear the feet off the
        # ground, which is exactly what a heavy thing sitting back does not do.
        (20, dict(root_off=(0, -0.55, -0.50), pitch=-9.0, hip_flex=-5.5,
                  spine_flex=-4.5, chest_flex=-3.0, head_pitch=-11.0,
                  wrist=arms(-1.25, 3.60), clav={"R": -10.0, "L": -10.0},
                  hand={"R": 30.0, "L": 30.0})),        # reared, fists overhead
        (26, dict(root_off=(0, 0.30, -0.85), pitch=7.0, hip_flex=4.0,
                  spine_flex=3.5, chest_flex=2.5, head_pitch=9.0,
                  wrist=arms(0.75, -0.05), clav={"R": 5.0, "L": 5.0},
                  hand={"R": -8.0, "L": -8.0})),        # impact
        (30, dict(root_off=(0, 0.18, -0.55), pitch=4.0, hip_flex=2.0,
                  spine_flex=1.5, head_pitch=5.0,
                  wrist=arms(0.55, 0.05), hand={"R": -4.0, "L": -4.0})),
        (38, dict(root_off=(0, 0.04, -0.18), pitch=1.0,
                  wrist=arms(0.15, 0.02))),
        (48, dict(root_off=(0, 0, -0.10))),
    ]
    build_stages(arm_obj, an, "Golem_Attack", 48, stages)


def build_hurt(arm_obj, an):
    """A shock travelling up the stack: the body jolts back, the head last."""
    stages = [
        (1,  dict(root_off=(0, 0, -0.10))),
        (4,  dict(root_off=(0, -0.30, -0.45), pitch=-5.0, hip_flex=-4.0,
                  spine_flex=-3.0, chest_flex=-2.0, head_pitch=-3.0, roll=3.0,
                  wrist={s: an.wrist[s] - FWD * 0.30 + UP * 0.20 for s in SIDES},
                  clav={"R": -6.0, "L": -4.0})),
        (8,  dict(root_off=(0, -0.10, -0.60), pitch=3.5, hip_flex=3.0,
                  head_pitch=8.0, roll=-1.5, head_yaw=6.0,
                  wrist={s: an.wrist[s] - FWD * 0.10 for s in SIDES})),
        (14, dict(root_off=(0, 0.02, -0.24), pitch=-1.0, head_pitch=-2.0,
                  head_yaw=-2.0)),
        (22, dict(root_off=(0, 0, -0.10))),
    ]
    build_stages(arm_obj, an, "Golem_Hurt", 22, stages)


def build_death(arm_obj, an):
    """The legs go first, it drops onto its fists, then the arms let go.

    Ends held dead still for the last twenty frames so the clip can simply stop
    on its final pose -- there is no separate corpse state in the controller.
    """
    def spread(dx, dy, dz):
        out = {}
        for s, sign in (("R", 1.0), ("L", -1.0)):
            out[s] = an.ankle[s] + Vector((dx * sign, dy, dz))
        return out

    def fists(dy, dz, dx=0.0):
        out = {}
        for s, sign in (("R", 1.0), ("L", -1.0)):
            out[s] = an.wrist[s] + Vector((dx * sign, dy, dz))
        return out

    stages = [
        (1,  dict(root_off=(0, 0, -0.10))),
        (8,  dict(root_off=(0, -0.10, 0.15), pitch=-2.0, head_pitch=-5.0,
                  clav={"R": -4.0, "L": -4.0})),        # a last brace
        (22, dict(root_off=(0, 0.25, -1.35), pitch=6.0, hip_flex=5.0,
                  spine_flex=4.0, chest_flex=2.0, head_pitch=7.0, roll=2.5,
                  ankle=spread(0.55, -0.35, -0.45),
                  wrist=fists(0.55, -0.02),
                  hand={"R": -10.0, "L": -6.0})),       # knees give out
        (38, dict(root_off=(0, 0.55, -2.35), pitch=12.0, hip_flex=6.0,
                  spine_flex=5.0, chest_flex=3.0, head_pitch=13.0, roll=4.5,
                  yaw=-3.0, ankle=spread(1.05, -0.85, -0.75),
                  wrist=fists(1.05, -0.40, 0.30),
                  hand={"R": -18.0, "L": -12.0})),      # onto the fists
        # The fists slide forward and out but stay near wrist height. Driving
        # them down as well puts them past the arm's reach, and a clamped chain
        # stops tracking its target: the arm would go rigid and straight halfway
        # through the fall instead of folding under the weight.
        (50, dict(root_off=(0, 0.85, -3.05), pitch=17.0, hip_flex=6.0,
                  spine_flex=5.5, chest_flex=3.5, head_pitch=17.0, roll=6.0,
                  yaw=-5.0, ankle=spread(1.35, -1.15, -0.85),
                  wrist=fists(1.40, -0.65, 0.55),
                  hand={"R": -26.0, "L": -18.0})),      # arms fold, chest lands
        (58, dict(root_off=(0, 0.90, -3.20), pitch=18.0, hip_flex=6.0,
                  spine_flex=5.5, chest_flex=3.5, head_pitch=18.5, roll=6.4,
                  yaw=-5.4, ankle=spread(1.40, -1.20, -0.88),
                  wrist=fists(1.48, -0.72, 0.60),
                  hand={"R": -28.0, "L": -20.0})),
        (72, dict(root_off=(0, 0.90, -3.20), pitch=18.0, hip_flex=6.0,
                  spine_flex=5.5, chest_flex=3.5, head_pitch=18.5, roll=6.4,
                  yaw=-5.4, ankle=spread(1.40, -1.20, -0.88),
                  wrist=fists(1.48, -0.72, 0.60),
                  hand={"R": -28.0, "L": -20.0})),      # dead hold
    ]
    build_stages(arm_obj, an, "Golem_Death", 72, stages)


# ---------------------------------------------------------------------------

def check_contacts(arm_obj, an):
    """Prove the IK actually lands where it was asked to.

    The solver writes bone rotations, not positions, so if the space maths were
    wrong the pose would still look plausible and the feet would still skate.
    This re-derives the wrist and ankle from the bone chain and compares.
    """
    poser = Poser(arm_obj)
    # Deliberately inside every chain's reach: this is a test of the space
    # maths, and a clamped chain would hide a real error behind a legitimate
    # one. Over-reach is tracked separately, by OVERREACH.
    target = {"wrist": {s: an.wrist[s] + FWD * 0.45 + UP * 0.70 for s in SIDES},
              "ankle": {s: an.ankle[s] - FWD * 0.45 + UP * 0.35 for s in SIDES}}
    apply_pose(poser, an, dict(root_off=(0.0, 0.0, -0.55), pitch=3.0,
                               hip_flex=2.0, **target))
    worst = 0.0
    for s in SIDES:
        worst = max(worst,
                    (poser.tail("Bone_LoArm_%s" % s) - target["wrist"][s]).length,
                    (poser.tail("Bone_Shin_%s" % s) - target["ankle"][s]).length)
    print("IK contact error, worst of 4 chains under a loaded pose: %.6f units"
          % worst)
    if worst > 1e-4:
        raise SystemExit("IK is not landing on its targets — the pose maths is "
                         "wrong and every clip built on it would skate.")


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s — run golem_rig.py first." % SRC)
    bpy.ops.wm.open_mainfile(filepath=SRC)

    arm_obj = bpy.data.objects.get(ARM)
    if arm_obj is None:
        raise SystemExit("No %s in the file — run golem_rig.py first." % ARM)
    if bpy.data.actions:
        raise SystemExit(
            "golem.blend already carries %d action(s). This script authors them "
            "from scratch and would leave duplicates; delete them first."
            % len(bpy.data.actions))
    missing = [n for n in ORDER if n not in arm_obj.data.bones]
    if missing:
        raise SystemExit("Armature is missing %s" % missing)

    bpy.context.scene.render.fps = FPS
    bpy.context.view_layer.objects.active = arm_obj
    arm_obj.animation_data_create()

    an = Anatomy(arm_obj)
    check_contacts(arm_obj, an)
    OVERREACH.clear()

    build_idle(arm_obj, an)
    build_locomotion(arm_obj, an, "Golem_Walk", 36, WALK)
    build_locomotion(arm_obj, an, "Golem_Run", 26, RUN)
    loco_overreach = dict(OVERREACH)
    build_attack(arm_obj, an)
    build_hurt(arm_obj, an)
    build_death(arm_obj, an)

    # Only the locomotion clips have to foot-lock. The one-shots are allowed to
    # reach past the limb and let it go straight -- that is what a slam looks
    # like -- so they are reported but not enforced.
    #
    # The threshold is not zero because the model's own rest pose already sits
    # 0.001 units short of a fully straight leg -- the rocks are stacked
    # vertically -- so a degree of body roll clips the limit by a fraction of a
    # millimetre at ship scale. 0.05 units is 1 cm on the finished creature,
    # which is where a clamp starts to be visible.
    loco_overreach = {k: v for k, v in loco_overreach.items() if v > 0.05}
    if loco_overreach:
        raise SystemExit(
            "Locomotion asks a limb past its reach: %s.\n"
            "Shorten `stride` or deepen `crouch` for that gait; a clamped limb "
            "has stopped tracking the ground and the contact will skate."
            % ", ".join("%s by %.3f" % kv for kv in sorted(loco_overreach.items())))
    extra = {k: v for k, v in OVERREACH.items()}
    print("One-shot over-reach (fully-extended frames, expected): %s"
          % (", ".join("%s %.2f" % kv for kv in sorted(extra.items())) or "none"))

    # Source height / ship height, so the printed speeds are in real metres.
    # Both numbers also live in golem_export.py; they are the same measurement,
    # taken from the raw FBX's vertices rather than from `bound_box`.
    upm = 10.623 / 2.60
    print("Foot-locked ground speeds (copy into GolemBuilder.cs):")
    print("  WalkSpeed = %.2f m/s" % gait_speed(WALK, 36, upm))
    print("  RunSpeed  = %.2f m/s" % gait_speed(RUN, 26, upm))

    arm_obj.animation_data.action = bpy.data.actions["Golem_Idle"]
    bpy.context.scene.frame_set(1)

    print("Authored %d actions: %s"
          % (len(bpy.data.actions),
             ", ".join("%s (%d..%d)" % (a.name, a.frame_start, a.frame_end)
                       for a in sorted(bpy.data.actions, key=lambda x: x.name))))

    bpy.ops.wm.save_as_mainfile(filepath=SRC)
    print("Saved %s" % SRC)


main()
