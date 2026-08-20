"""Six-legged gaits for the Vrescal, plus a neck that holds the head on centre.

    blender --background vrescal_hexapod_rigged.blend --python vrescal_hexapod_anim.py

Writes the six actions `VrescalBuilder` expects, in place, at the frame counts it
hardcodes: Idle 91, Walk 37, Run 25, Attack 40, Hurt 20, Death 64.

## Nothing on disk is edited to get from four legs to six

`vrescal_anim.py` is shared with the older lofted quadruped, so editing its
`LEGS` list would break that pipeline. It does not need editing: the solver
reads `LEGS`, `POLE` and `Gait.phases` as module globals at call time, and takes
every bone length and rest matrix off whatever armature it is handed. Patching
those three names on the imported module is enough, and the quadruped keeps
working. This is the same trick `vrescal_sculpt_anim.py` already uses to install
a stand-in `vrescal_rebuild`.

## The gaits

Six legs in three ranks change what a sensible gait is:

**Walk is a metachronal wave.** Each side lifts rear, then middle, then front,
a third of a cycle apart, with the two sides half a cycle out of phase. At duty
0.72 that leaves 4.3 feet on the ground at any instant -- the right answer for a
heavy animal, and the reason a hexapod reads as *deliberate* rather than busy.

**Run is a tripod.** {FrontP, MidS, RearP} alternates with {FrontS, MidP,
RearS}, so the animal is always on a stable triangle. At duty 0.48 that is
almost exactly three feet down at all times. This replaces the quadruped's
amble, which existed only because a four-legged trot looked like a horse
costume; with six legs the tripod is both faster-reading and genuinely stable.

Body sway and roll are cut to roughly half the quadruped values. Six legs in
three ranks do not need to throw the mass side to side to stay over a support
polygon, and the sway was the single largest contributor to the head swinging.

## Holding the head on centre

The sculpt's head sits 0.42 m to port of the centreline and the snout 0.59 m.
`vrescal_hexapod_rig.py` puts the neck bones *inside* that curve so rotation
behaves, but the rest pose still has the head off-centre -- deliberately, since
that is the shape that was sculpted.

Centring is therefore an animation concern, in two parts:

1. **A rest correction**, solved once: the constant neck yaw that brings the
   head tip to y = 0. Added as a baseline to every clip, so the head reads as
   forward-facing everywhere while the idle head-scan still layers on top.
2. **Per-frame residual cancellation** on the locomotion clips, which removes
   whatever lateral throw the body's sway still passes up the neck.

Both work the same way, and neither assumes which local axis yaws a bone. The
neck bones have roll 0 and point along the animal, so the mapping from
(rx, ry, rz) to world yaw is not obvious and guessing it wrong is silent. The
axis is instead **measured**: perturb each axis, solve, see which moves the head
tip in y, and use that one with a Newton step on the measured derivative.
"""

import math
import os
import sys
import types

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
CREATURES = os.path.dirname(HERE)
for p in (CREATURES, HERE):
    if p not in sys.path:
        sys.path.insert(0, p)

UNITS_PER_M = 3.62450
GROUND = -13.0

LEGS6 = ["FrontP", "FrontS", "MidP", "MidS", "RearP", "RearS"]

# ---- gait phasing ---------------------------------------------------------
#
# PAIR_OFFSET is 0 on purpose, and it is the single most important number here.
# Measured off this mesh: a side's front and middle ankles are 1.75 working
# units apart, their two soles need 1.60 units between centres before the
# geometry stops overlapping, and each foot sweeps a 5.01-unit stride. So the
# margin for *any* phase difference between that pair is 0.15 units -- there is
# none. A search over (pair offset, rear offset) maximising the tightest gap
# returns pair = 0.00 every time; at 0.06 the gap is already 1.39 and the feet
# clip, and at the metrically pretty 1/3 of a metachronal wave it is **-0.57**,
# i.e. the middle foot swings clean through the front one. That is what "the
# second pair doesn't move naturally" looks like.
#
# So each side's front and middle legs step in unison and the animal is phased
# as a quadruped whose forelimb is a pair. REAR_OFFSET stays near 0 because the
# middle-to-rear gap shrinks as it approaches 0.5; 0.90 puts each rear foot down
# just before its own side's front pair, which is a lateral-sequence walk, and
# leaves a 3.30-unit middle-rear gap against the 1.61 needed.
PAIR_OFFSET = 0.00
REAR_OFFSET = 0.90

# The run covers 4.2 m/s. At one cycle per clip that forces a 5.85-unit stride
# against a 4.00-unit middle-to-rear spacing, and no phasing avoids a crossing.
# Stride = speed x duty x period, and speed and duty are fixed by the Unity
# blend tree, so the only lever is the period: two cycles inside the same
# 24-frame clip halves the stride to 2.92 units. The animal still covers 4.2 m/s
# and a planted foot still tracks the ground exactly -- it just takes two
# strides per clip instead of one.
RUN_FRAMES = 24
RUN_CYCLES = 2


def phases(pair=PAIR_OFFSET, rear=REAR_OFFSET):
    """Per-leg cycle offsets, wrapped into [0, 1).

    The wrap is not cosmetic. An earlier version wrote `0.5 + 2/3` for FrontS
    and shipped a phase of 1.167; `foot_track` takes the fractional part, so it
    happened to survive, but nothing guarantees that and it made the printed
    gait table unreadable.
    """
    p = {"FrontP": 0.0, "MidP": -pair, "RearP": rear,
         "FrontS": 0.5, "MidS": 0.5 - pair, "RearS": 0.5 + rear}
    return {k: v % 1.0 for k, v in p.items()}


import vrescal_hexapod_rig as RIG      # noqa: E402  -- main() is guarded


def build_shim(foot):
    """Stand in for `vrescal_rebuild`, the six names the solver touches."""
    m = types.ModuleType("vrescal_rebuild")
    m.GROUND = GROUND
    m.UNITS_PER_M = UNITS_PER_M
    m.FOOT_SCALE = UNITS_PER_M
    # Skeleton does R.LIMBS["%sP" % leg[:-1]]["foot"], so one entry per rank.
    m.LIMBS = {"FrontP": {"foot": "Front"},
               "MidP": {"foot": "Mid"},
               "RearP": {"foot": "Rear"}}
    m.FOOT_HEIGHT_M = {r: 0.42 for r in ("Front", "Mid", "Rear")}
    m.FOOT_SOLE_M = {
        "Front": (foot["FrontP"]["sole"] + foot["FrontS"]["sole"]) / 2.0,
        "Mid": (foot["MidP"]["sole"] + foot["MidS"]["sole"]) / 2.0,
        "Rear": (foot["RearP"]["sole"] + foot["RearS"]["sole"]) / 2.0}
    return m


def measure_feet():
    """Re-derive the six limbs from the mesh in the open file."""
    obj = bpy.data.objects["Mesh_Vrescal_Sculpt"]
    co, ed = RIG.mesh_arrays(obj)
    RIG.FOOT.clear()
    RIG.limbs(co, ed)
    return dict(RIG.FOOT)


# --------------------------------------------------------------------------
# Head centring
# --------------------------------------------------------------------------

class NeckCentre:
    """Drives the head tip toward y = 0 by yawing the neck chain.

    The correction is spread over the four neck bones and the head, weighted
    toward the base, so the neck bends as a curve rather than kinking at one
    joint.
    """

    WEIGHTS = {"Bone_Neck_01": 0.30, "Bone_Neck_02": 0.25,
               "Bone_Neck_03": 0.20, "Bone_Neck_04": 0.15,
               "Bone_Head": 0.10}

    def __init__(self, V, rig, sk):
        self.V, self.rig, self.sk = V, rig, sk
        self.axis = self._find_axis()

    def _tip(self, locals_, targets, rolls, root=None):
        root = root if root is not None else Matrix.Identity(4)
        world = self.V.solve(self.rig, self.sk, root, locals_, targets, rolls)
        m = world["Bone_Head"]
        return (m @ Matrix.Translation(
            (0.0, self.rig.length["Bone_Head"], 0.0))).translation

    def _find_axis(self):
        """Which of (rx, ry, rz) on a neck bone actually yaws the head."""
        targets, rolls = self.V.planted(self.sk)
        base = self._tip({}, targets, rolls)
        best, best_d = 2, 0.0
        for i in range(3):
            loc = {"Bone_Neck_02": tuple(0.05 if j == i else 0.0
                                         for j in range(3))}
            d = abs(self._tip(loc, targets, rolls).y - base.y)
            print("      axis %d moves head tip y by %.4f units" % (i, d))
            if d > best_d:
                best, best_d = i, d
        print("      using axis %d for neck yaw" % best)
        return best

    def solve_offset(self, locals_, targets, rolls, root=None, iters=6):
        """Yaw offsets per neck bone that put the head tip on y = 0."""
        out = {n: 0.0 for n in self.WEIGHTS}
        for _ in range(iters):
            merged = self._merge(locals_, out)
            err = self._tip(merged, targets, rolls, root).y
            if abs(err) < 0.01:
                break
            # Measured derivative of tip.y w.r.t. a unit yaw spread over the
            # chain, so no assumption about neck length or bone roll.
            step = 0.05
            probe = {n: out[n] + step * w for n, w in self.WEIGHTS.items()}
            d = (self._tip(self._merge(locals_, probe), targets, rolls, root).y
                 - err) / step
            if abs(d) < 1e-9:
                break
            delta = -err / d
            delta = max(-0.6, min(0.6, delta))          # keep it sane
            for n, w in self.WEIGHTS.items():
                out[n] += delta * w
        return out

    def _merge(self, locals_, yaw):
        merged = dict(locals_)
        for n, v in yaw.items():
            base = list(merged.get(n, (0.0, 0.0, 0.0)))
            base[self.axis] += v
            merged[n] = tuple(base)
        return merged


def with_centring(V, centre, fn, rest, dynamic):
    """Wrap a frame function so the head is held on centre.

    `rest` is the constant correction every clip gets. `dynamic` additionally
    cancels the residual lateral throw frame by frame, and is only worth its
    cost on the locomotion clips.
    """
    def wrapped(t, f):
        root, locals_, targets, rolls = fn(t, f)
        yaw = dict(rest)
        if dynamic:
            yaw = centre.solve_offset(locals_, targets, rolls, root, iters=4)
        merged = centre._merge(locals_, yaw)
        return root, merged, targets, rolls
    return wrapped


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    arm = bpy.data.objects.get("Arm_Vrescal")
    if arm is None:
        raise SystemExit("No Arm_Vrescal -- run vrescal_hexapod_rig.py first.")
    missing = [b for leg in LEGS6 for b in
               ("Bone_%s_Upper" % leg, "Bone_%s_Foot" % leg)
               if b not in arm.data.bones]
    if missing:
        raise SystemExit("Rig is not six-legged, missing: %s" % ", ".join(missing))

    print("  measuring feet off the mesh:")
    foot = measure_feet()
    sys.modules["vrescal_rebuild"] = build_shim(foot)
    import vrescal_anim as V          # noqa: E402  -- after the shim exists

    # ---- six legs -------------------------------------------------------
    V.LEGS = list(LEGS6)
    V.POLE = {"FrontP": Vector((-1, 0, 0)), "FrontS": Vector((-1, 0, 0)),
              "MidP": Vector((-1, 0, 0)), "MidS": Vector((-1, 0, 0)),
              "RearP": Vector((1, 0, 0)), "RearS": Vector((1, 0, 0))}

    V.WALK = V.Gait(
        frames=36, speed_ms=1.6, duty=0.72, phases=phases(),
        lift=1.15, crouch=0.75,
        bob=0.18, sway=0.15, roll=math.radians(1.9),
        pitch=math.radians(1.0), yaw=math.radians(1.0),
        spine_yaw=math.radians(1.8), neck_nod=math.radians(3.6), stab=0.55)

    # RUN takes TWO cycles inside its 24-frame clip, so the Gait is built at
    # half length. See RUN_CYCLES.
    V.RUN = V.Gait(
        frames=RUN_FRAMES // RUN_CYCLES, speed_ms=4.2, duty=0.48,
        phases=phases(),
        lift=1.30, crouch=1.10,
        bob=0.42, sway=0.28, roll=math.radians(3.8),
        pitch=math.radians(2.2), yaw=math.radians(1.8),
        spine_yaw=math.radians(3.0), neck_nod=math.radians(6.5), stab=0.45)

    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)

    rig = V.Rig(arm)
    sk = V.Skeleton(rig)

    print("  neck centring:")
    centre = NeckCentre(V, rig, sk)
    t0, r0 = V.planted(sk)
    before = centre._tip({}, t0, r0).y
    rest = centre.solve_offset({}, t0, r0)
    after = centre._tip(centre._merge({}, rest), t0, r0).y
    print("      head tip y %+.3f -> %+.3f units (%+.3f -> %+.3f m)"
          % (before, after, before / UNITS_PER_M, after / UNITS_PER_M))
    print("      rest yaw per bone: %s"
          % {n: round(v, 4) for n, v in rest.items()})

    def repeat(fn, cycles):
        """Run `cycles` gait cycles across one clip's 0..1 parameter."""
        if cycles == 1:
            return fn
        return lambda t, f: fn((t * cycles) % 1.0, f)

    built = []
    for name, frames, loop, fn, dyn in (
            ("Vrescal_Idle", 90, True, V.idle_frame(rig, sk), False),
            ("Vrescal_Walk", V.WALK.frames, True,
             V.locomotion_frame(rig, sk, V.WALK), True),
            ("Vrescal_Run", RUN_FRAMES, True,
             repeat(V.locomotion_frame(rig, sk, V.RUN), RUN_CYCLES), True),
            ("Vrescal_Attack", 40, False, None, False),
            ("Vrescal_Hurt", 20, False, V.hurt_frame(rig, sk), False),
            ("Vrescal_Death", 64, False, V.death_frame(rig, sk), False)):
        if fn is None:
            fn = V.attack_frame(rig, sk, frames)
        # Death ends as a corpse; holding its head level would look wrong.
        wrapped = fn if name == "Vrescal_Death" else \
            with_centring(V, centre, fn, rest, dyn)
        act = V.write_action(rig, sk, name, frames, wrapped, loop=loop)
        built.append("%s 1-%d%s" % (name, int(act.frame_range[1]),
                                    " loop" if loop else ""))

    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.location = (0.0, 0.0, 0.0)
        pb.rotation_quaternion = Quaternion((1.0, 0.0, 0.0, 0.0))

    print("\n  actions: %s" % ", ".join(built))
    print("  phases: %s" % {k: round(v, 3) for k, v in phases().items()})
    print("  walk stride %.2f m over 1 cycle; run stride %.2f m x %d cycles"
          % (V.WALK.stride / UNITS_PER_M, V.RUN.stride / UNITS_PER_M,
             RUN_CYCLES))
    print("  walk: %.1f feet down at any instant; run: %.1f"
          % (6 * V.WALK.duty, 6 * V.RUN.duty))
    bpy.ops.wm.save_mainfile()
    print("  saved %s" % bpy.data.filepath)


if __name__ == "__main__":
    main()
