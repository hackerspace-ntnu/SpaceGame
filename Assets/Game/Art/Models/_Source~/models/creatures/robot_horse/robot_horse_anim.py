"""Author the robot horse's clips: Idle, Walk, Run, TurnL, TurnR.

Run (from the repo root), then export:

    blender --background Assets/Game/Art/Models/_Source~/models/creatures/robot_horse/robot_horse.blend \
        --python Assets/Game/Art/Models/_Source~/models/creatures/robot_horse/robot_horse_anim.py -- --save

Same shape as appa_anim.py, and for the same reasons: every clip is IN PLACE (the
NavMeshAgent owns travel and heading), every pose goes through a world-axis helper so a
keyed component can never be the bone's own twist axis, and `verify` refuses a clip whose
curves are flat.

Axis conventions, measured by robot_horse_rig.py's probe (forward is -Y, up +Z, the
animal's left is +X):

    leg bone   +X rotation swings the hoof BACKWARD (+Y); -X swings it forward
    leg bone   +Y rotation swings the hoof to the animal's RIGHT (-X)
    spine/neck +X rotation pitches the head DOWN; +Z yaws the nose to the animal's LEFT
    tail1      +X lifts the tail

So "pitch" below is world X, "yaw" world Z, "roll" world Y, and a positive `swing` is a
leg swung FORWARD (i.e. -X). The hind leg is femur > fibula > metarsal (sic) > hoof_B; the
front leg is scapula > humerus > radius > metacarpal > hoof_F.
"""
import bpy
import math
import sys
from mathutils import Matrix, Vector

ARM = "Arm_RobotHorse"
FPS = 24

IDLE_FRAMES = 144        # 6 s; two incommensurate motions hide the loop
WALK_FRAMES = 40         # 1.67 s per four-beat cycle
RUN_FRAMES = 22          # 0.92 s -- a gallop, driven hard; this is the fast mount
TURN_FRAMES = 32         # 1.33 s

# Four-beat walk: lateral sequence, LH LF RH RF. Stance 65 %.
WALK_PHASE = {"HL": 0.00, "FL": 0.25, "HR": 0.50, "FR": 0.75}
WALK_SWING = 0.35
# Transverse gallop: hinds close together, then fores, then air. Stance 35 %.
RUN_PHASE = {"HR": 0.00, "HL": 0.12, "FR": 0.40, "FL": 0.52}
RUN_SWING = 0.65

HIND = {"L": ("femur.L", "fibula.L", "metarsal.L", "hoof_B.L"),
        "R": ("femur.R", "fibula.R", "metarsal.R", "hoof_B.R")}
FRONT = {"L": ("scapula.L", "humerus.L", "radius.L", "metacarpal.L", "hoof_F.L"),
         "R": ("scapula.R", "humerus.R", "radius.R", "metacarpal.R", "hoof_F.R")}
LEG_BONES = [b for side in HIND.values() for b in side] + [b for side in FRONT.values() for b in side]
SPINE = ["spine1", "spine2", "spine3", "spine4", "neck1", "neck2", "head"]
TAIL = ["tail1", "tail2", "tail3", "tail4"]
EARS = ["ear.L", "ear.R"]
ALL = LEG_BONES + SPINE + TAIL + EARS

# Turning in place: each foot rides a circle about this point on the body's centreline
# (Blender Y, world). The hips sit at y ~ +0.51 (hind) and y ~ -0.75 (front, scapula).
TURN_CENTRE_Y = -0.10
TURN_SWEEP_DEG = 30.0
LEG_LENGTH = 1.20        # hip to hoof, for arc-length -> rotation


def d(deg):
    return math.radians(deg)


def _local_axis(pb, world_axis):
    basis = pb.bone.matrix_local.to_3x3()
    return (basis.inverted() @ Vector(world_axis)).normalized()


def pose(pb, pitch=0.0, yaw=0.0, roll=0.0):
    """World-space pitch (about +X), yaw (about +Z), roll (about +Y), radians."""
    m = Matrix.Identity(3)
    if yaw:
        m = Matrix.Rotation(yaw, 3, _local_axis(pb, (0.0, 0.0, 1.0))) @ m
    if pitch:
        m = Matrix.Rotation(pitch, 3, _local_axis(pb, (1.0, 0.0, 0.0))) @ m
    if roll:
        m = Matrix.Rotation(roll, 3, _local_axis(pb, (0.0, 1.0, 0.0))) @ m
    pb.rotation_euler = m.to_euler("XYZ")


def _smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def _leg_cycle(cycle, swing_fraction, reach, fold, stance_flex):
    """(swing_fwd, fold, flex) for one leg at `cycle` in [0, 1).

    swing_fwd is the upper leg's forward swing in radians (positive = forward); fold is how
    far the lower leg tucks during swing (the hock / knee flex, which is what clears the
    ground); flex is the mid-stance compression that makes a planted leg read as carrying
    weight. Swing is eased; stance is linear because a planted hoof travels with the body
    at constant speed.
    """
    if cycle < swing_fraction:
        t = _smoothstep(cycle / swing_fraction)
        swing = -reach + 2.0 * reach * t
        lift = math.sin(math.pi * (cycle / swing_fraction))
        return swing, fold * lift, 0.0
    t = (cycle - swing_fraction) / (1.0 - swing_fraction)
    swing = reach - 2.0 * reach * t
    squash = math.sin(math.pi * t)
    return swing, 0.0, stance_flex * squash


def pose_hind(arm, side, swing, fold, flex):
    femur, fibula, metarsal, hoof = HIND[side]
    pose(arm.pose.bones[femur], pitch=-swing)
    # Hock flexes the cannon backward during swing; the stifle gives a little under load.
    pose(arm.pose.bones[fibula], pitch=-flex * 0.6 + fold * 0.35)
    pose(arm.pose.bones[metarsal], pitch=fold * 0.9 + flex * 0.5)
    pose(arm.pose.bones[hoof], pitch=-fold * 0.4 - flex * 0.2)


def pose_front(arm, side, swing, fold, flex):
    scapula, humerus, radius, metacarpal, hoof = FRONT[side]
    pose(arm.pose.bones[scapula], pitch=-swing * 0.35)
    pose(arm.pose.bones[humerus], pitch=-swing * 0.75 - fold * 0.25)
    # The knee (carpus) folds the cannon back and up under the forearm.
    pose(arm.pose.bones[radius], pitch=fold * 0.35 - flex * 0.5)
    pose(arm.pose.bones[metacarpal], pitch=fold * 0.95 + flex * 0.4)
    pose(arm.pose.bones[hoof], pitch=-fold * 0.5)


def _rest(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
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
    act.use_fake_user = True
    return act


def _key(arm, names, frame):
    for n in names:
        arm.pose.bones[n].keyframe_insert("rotation_euler", frame=frame)


def _body(arm, u, beats, bob_deg, sway_deg, lean_deg, head_deg, tail_deg):
    """Spine, neck, head and tail follow-through for a gait at cycle position u."""
    bob = math.sin(u * beats * 2.0 * math.pi)
    sway = math.sin(u * 2.0 * math.pi)
    pose(arm.pose.bones["spine1"], pitch=d(lean_deg) + bob * d(bob_deg), yaw=sway * d(sway_deg))
    pose(arm.pose.bones["spine2"], pitch=bob * d(-bob_deg * 0.6), yaw=sway * d(-sway_deg * 0.5))
    pose(arm.pose.bones["spine3"], pitch=bob * d(bob_deg * 0.5), yaw=sway * d(-sway_deg * 0.4))
    pose(arm.pose.bones["spine4"], pitch=bob * d(-bob_deg * 0.4), yaw=sway * d(sway_deg * 0.3))
    # The neck counters the back so the head stays steady, and the head nods into it.
    pose(arm.pose.bones["neck1"], pitch=d(head_deg) + bob * d(-bob_deg * 0.9), yaw=sway * d(sway_deg * 0.4))
    pose(arm.pose.bones["neck2"], pitch=bob * d(bob_deg * 0.7))
    pose(arm.pose.bones["head"], pitch=d(head_deg * 0.5) + bob * d(bob_deg * 1.1))
    lag = math.sin((u - 0.15) * 2.0 * math.pi)
    pose(arm.pose.bones["tail1"], pitch=d(tail_deg) + lag * d(4.0), yaw=lag * d(5.0))
    pose(arm.pose.bones["tail2"], pitch=lag * d(5.0), yaw=lag * d(7.0))
    pose(arm.pose.bones["tail3"], pitch=lag * d(6.0), yaw=lag * d(8.5))
    pose(arm.pose.bones["tail4"], pitch=lag * d(6.5), yaw=lag * d(9.5))
    flick = math.sin(u * 2.0 * math.pi * 2.0 + 0.7)
    pose(arm.pose.bones["ear.L"], pitch=flick * d(4.0))
    pose(arm.pose.bones["ear.R"], pitch=-flick * d(4.0))


def _build_gait(arm, name, frames, phases, swing_fraction, reach, fold, stance_flex,
                beats, bob_deg, sway_deg, lean_deg, head_deg, tail_deg):
    _action(arm, name)
    _rest(arm)
    for f in range(frames + 1):
        u = (f % frames) / float(frames)
        for leg, phase in phases.items():
            swing, fld, flex = _leg_cycle((u + phase) % 1.0, swing_fraction, reach, fold, stance_flex)
            if leg[0] == "H":
                pose_hind(arm, leg[1], swing, fld, flex)
            else:
                pose_front(arm, leg[1], swing, fld, flex)
        _body(arm, u, beats, bob_deg, sway_deg, lean_deg, head_deg, tail_deg)
        _key(arm, ALL, f)
    return frames


def build_walk(arm):
    return _build_gait(arm, "RobotHorse_Walk", WALK_FRAMES, WALK_PHASE, WALK_SWING,
                       reach=d(22), fold=d(40), stance_flex=d(7),
                       beats=2.0, bob_deg=1.6, sway_deg=2.4, lean_deg=0.0, head_deg=0.0, tail_deg=0.0)


def build_run(arm):
    # Longer reach, more fold, the whole back flexing with the gallop, nose out and low, tail
    # streaming. One bob per cycle: the gallop has a single suspension.
    return _build_gait(arm, "RobotHorse_Run", RUN_FRAMES, RUN_PHASE, RUN_SWING,
                       reach=d(38), fold=d(62), stance_flex=d(12),
                       beats=1.0, bob_deg=4.5, sway_deg=1.5, lean_deg=-3.0, head_deg=-10.0, tail_deg=18.0)


def _turn_tangents(arm):
    """Direction each hoof travels when the body turns left, and its radius from the pivot."""
    out = {}
    for leg, bones in list(("H" + s, HIND[s]) for s in HIND) + list(("F" + s, FRONT[s]) for s in FRONT):
        head = arm.matrix_world @ arm.pose.bones[bones[0]].bone.head_local
        rx, ry = head.x, head.y - TURN_CENTRE_Y
        radius = math.hypot(rx, ry) or 1.0
        # +Z rotation sends a point (rx, ry) along (-ry, rx).
        out[leg] = (-ry / radius, rx / radius, radius)
    return out


def _build_turn(arm, name, direction):
    """Turning on the spot; `direction` +1 = the animal's left (+Z).

    In place, like Appa's: the NavMeshAgent owns the heading, so the clip carries no root
    rotation and only shows the feet doing the work. A planted hoof moves along -tangent
    (the body turns over it), a swinging hoof recovers along +tangent. Pitch (about X)
    moves a hoof along Y, roll (about Y) moves it along -X, so a tangential step (tx, ty)
    is pitch = ty, roll = -tx, scaled by arc length / leg length.
    """
    _action(arm, name)
    _rest(arm)
    lead = direction * d(8.0)
    tangent = _turn_tangents(arm)
    phases = {"HL": 0.0, "FR": 0.25, "HR": 0.5, "FL": 0.75}
    for f in range(TURN_FRAMES + 1):
        u = (f % TURN_FRAMES) / float(TURN_FRAMES)
        for leg, phase in phases.items():
            cycle = (u + phase) % 1.0
            _, fld, flex = _leg_cycle(cycle, WALK_SWING, 0.0, d(26), d(5))
            if cycle < WALK_SWING:
                sweep = -1.0 + 2.0 * _smoothstep(cycle / WALK_SWING)
            else:
                sweep = 1.0 - 2.0 * (cycle - WALK_SWING) / (1.0 - WALK_SWING)
            tx, ty, radius = tangent[leg]
            step = direction * sweep * d(TURN_SWEEP_DEG) * radius / LEG_LENGTH
            side = leg[1]
            if leg[0] == "H":
                pose_hind(arm, side, 0.0, fld, flex)
                femur = arm.pose.bones[HIND[side][0]]
                pose(femur, pitch=step * ty, roll=-step * tx)
            else:
                pose_front(arm, side, 0.0, fld, flex)
                humerus = arm.pose.bones[FRONT[side][1]]
                pose(humerus, pitch=step * ty * 0.75, roll=-step * tx * 0.75)
                scapula = arm.pose.bones[FRONT[side][0]]
                pose(scapula, pitch=step * ty * 0.35, roll=-step * tx * 0.35)
        bob = math.sin(u * 4.0 * math.pi)
        pose(arm.pose.bones["spine1"], pitch=bob * d(1.2), yaw=lead * 0.3)
        pose(arm.pose.bones["spine2"], yaw=lead * 0.45)
        pose(arm.pose.bones["spine3"], yaw=lead * 0.6)
        pose(arm.pose.bones["spine4"], yaw=lead * 0.7)
        pose(arm.pose.bones["neck1"], yaw=lead * 0.9)
        pose(arm.pose.bones["neck2"], yaw=lead * 0.6)
        pose(arm.pose.bones["head"], yaw=lead * 0.8)
        lag = math.sin((u - 0.15) * 2.0 * math.pi)
        for i, t in enumerate(TAIL):
            pose(arm.pose.bones[t], yaw=-lead * (0.3 + 0.15 * i) + lag * d(3.5 + 1.5 * i))
        for e in EARS:
            pose(arm.pose.bones[e], yaw=lead * 0.5)
        _key(arm, ALL, f)
    return TURN_FRAMES


def build_turn_left(arm):
    return _build_turn(arm, "RobotHorse_TurnL", +1)


def build_turn_right(arm):
    return _build_turn(arm, "RobotHorse_TurnR", -1)


def build_idle(arm):
    _action(arm, "RobotHorse_Idle")
    _rest(arm)
    for f in range(IDLE_FRAMES + 1):
        u = (f % IDLE_FRAMES) / float(IDLE_FRAMES)
        breath = math.sin(u * 2.0 * math.pi)
        drift = math.sin(u * 2.0 * math.pi * 3.0 + 1.1)
        pose(arm.pose.bones["spine1"], pitch=breath * d(0.8))
        pose(arm.pose.bones["spine2"], pitch=breath * d(-0.6))
        pose(arm.pose.bones["spine3"], pitch=breath * d(0.5))
        pose(arm.pose.bones["neck1"], pitch=breath * d(1.5), yaw=drift * d(3.0))
        pose(arm.pose.bones["neck2"], pitch=breath * d(-0.8), yaw=drift * d(2.0))
        pose(arm.pose.bones["head"], pitch=breath * d(-1.2), yaw=drift * d(-4.0))
        swish = math.sin(u * 2.0 * math.pi * 2.0)
        for i, t in enumerate(TAIL):
            pose(arm.pose.bones[t], yaw=swish * d(3.0 + 2.0 * i))
        flick = max(0.0, math.sin(u * 2.0 * math.pi * 5.0 + 2.0)) ** 8
        pose(arm.pose.bones["ear.L"], pitch=flick * d(12.0))
        pose(arm.pose.bones["ear.R"], pitch=-flick * d(6.0))
        # A machine at rest still shifts its weight: one hind leg eases and re-plants.
        shift = math.sin(u * 2.0 * math.pi) * 0.5 + 0.5
        pose_hind(arm, "L", 0.0, 0.0, d(3.0) * shift)
        pose_hind(arm, "R", 0.0, 0.0, d(3.0) * (1.0 - shift))
        pose_front(arm, "L", 0.0, 0.0, 0.0)
        pose_front(arm, "R", 0.0, 0.0, 0.0)
        _key(arm, ALL, f)
    return IDLE_FRAMES


BUILDERS = [
    ("RobotHorse_Idle", build_idle),
    ("RobotHorse_Walk", build_walk),
    ("RobotHorse_Run", build_run),
    ("RobotHorse_TurnL", build_turn_left),
    ("RobotHorse_TurnR", build_turn_right),
]


def build():
    arm = bpy.data.objects.get(ARM)
    if arm is None:
        raise SystemExit("No %s -- run robot_horse_rig.py first." % ARM)
    bpy.context.scene.render.fps = FPS
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    built = [(name, fn(arm)) for name, fn in BUILDERS]
    arm.animation_data.action = None
    _rest(arm)
    bpy.ops.object.mode_set(mode="OBJECT")
    return built


def stride(arm, name, frames, swing_fraction):
    """Hoof travel along Y across the cycle, and the ground speed it implies."""
    act = bpy.data.actions[name]
    arm.animation_data.action = act
    ys = []
    for f in range(frames + 1):
        bpy.context.scene.frame_set(f)
        ys.append((arm.matrix_world @ arm.pose.bones["hoof_B.L"].tail).y)
    arm.animation_data.action = None
    travel = max(ys) - min(ys)
    seconds = frames / float(FPS)
    return travel, travel / ((1.0 - swing_fraction) * seconds)


def verify(built):
    for name, _ in built:
        act = bpy.data.actions[name]
        moved = {fc.data_path for fc in act.fcurves
                 if fc.keyframe_points and
                 max(k.co[1] for k in fc.keyframe_points) - min(k.co[1] for k in fc.keyframe_points) > 1e-5}
        if not moved:
            raise SystemExit("%s has no curve that changes." % name)
        legs = sum(1 for p in moved if any(b in p for b in ("femur", "fibula", "metarsal", "humerus", "radius", "metacarpal")))
        if name in ("RobotHorse_Walk", "RobotHorse_Run", "RobotHorse_TurnL", "RobotHorse_TurnR") and legs < 12:
            raise SystemExit("%s moves only %d leg curves; the gait is not animating." % (name, legs))


if __name__ == "__main__":
    built = build()
    verify(built)
    arm = bpy.data.objects[ARM]
    for name, frames in built:
        print("  %-18s %3d frames (%.2f s)" % (name, frames, frames / float(FPS)))
    for name, frames, swing in (("RobotHorse_Walk", WALK_FRAMES, WALK_SWING), ("RobotHorse_Run", RUN_FRAMES, RUN_SWING)):
        travel, speed = stride(arm, name, frames, swing)
        print("  %-18s hind hoof travels %.2f m per cycle -> ground speed %.2f m/s at scale 1"
              % (name, travel, speed))
    if "--save" in sys.argv:
        bpy.ops.wm.save_mainfile()
        print("saved %s" % bpy.data.filepath)
    else:
        print("NOT saved (pass -- --save to write the .blend)")
