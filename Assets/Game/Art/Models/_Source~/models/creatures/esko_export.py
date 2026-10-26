"""Export esko.blend to Creatures/Organic/Esko/esko.fbx with its twelve gameplay takes.

    blender --background --python esko_export.py

The .blend carries all 81 actions of the bear pack it was sculpted from. Only the twelve that fill
Appa's controller states ship, renamed to Esko_<state> so the takes read `Arm_Esko|Esko_<state>`.

The pack's locomotion is root-motion animation: Walk carries the armature OBJECT 1.6 m forward
per cycle, Run 4 m, and the turning walks swing it round. In Unity the NavMeshAgent moves the
creature, so a clip that also moves it would drift ahead and snap back every loop. The forward,
sideways and yaw channels of the armature object are flattened to their first key; the vertical
bob stays.

The pack's Run flicks its two tail bones up to 32 deg (RigTail1) and 74 deg (RigTail2): a bear's stub
wag. On the esko's 0.9 m tail fin, which hangs entirely past those bones, that throws the fin
straight up four times a stride. Run's tail rotations are slerped toward the rest pose by
TAIL_SWING, which brings them to Walk's range (Tail2 at most ~26 deg) and keeps their rhythm.
"""
import os
import sys

LIB = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
sys.path.insert(0, LIB)
from _exportlib import export, unity_path  # noqa: E402

import bpy  # noqa: E402
from mathutils import Quaternion  # noqa: E402

SRC = os.path.join(LIB, "models", "creatures", "esko.blend")
DST = unity_path("Creatures", "Organic", "Esko", "esko.fbx")

# Appa.controller state -> bear-pack action that plays it
TAKES = {
    "Idle": "Stand_Idle_01",
    "Walk": "Walk",
    "Run": "Run",
    "TurnL": "WalkL",
    "TurnR": "WalkR",
    "Graze": "Stand_Eating_01",
    "Happy": "Stand_Idle_02",
    "Roar": "Attack_StandAngry_01_High",
    "Ram": "Attack_StandAngry_01_Low",
    "Hurt": "Hit_Stand_F01",
    "Jump": "CanonBall",
    "Death": "Death_Stand_R01",
}
IN_PLACE = {"Walk", "Run", "TurnL", "TurnR"}
# armature-object channels that carry travel: location X / Z (ground plane under the 90deg root) and yaw
ROOT_TRAVEL = {("location", 0), ("location", 2), ("rotation_euler", 1)}
# state -> share of the pack's tail rotation that ships (1 = as authored)
TAIL_SWING = {"Run": 0.35}
TAIL_BONES = ("RigTail1", "RigTail2")


def _fcurves(action):
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                yield from bag.fcurves


def _flatten(fc):
    v = fc.keyframe_points[0].co[1]
    for k in fc.keyframe_points:
        k.co[1] = v
        k.handle_left[1] = v
        k.handle_right[1] = v


def _damp_tail(action, share):
    """Slerp every tail-bone rotation key toward the rest pose, keeping `share` of it."""
    for bone in TAIL_BONES:
        path = 'pose.bones["%s"].rotation_quaternion' % bone
        wxyz = sorted((fc for fc in _fcurves(action) if fc.data_path == path), key=lambda fc: fc.array_index)
        if len(wxyz) != 4 or len({len(fc.keyframe_points) for fc in wxyz}) != 1:
            raise SystemExit("%s: %s is not four equally keyed quaternion channels" % (action.name, bone))
        for i in range(len(wxyz[0].keyframe_points)):
            keys = [fc.keyframe_points[i] for fc in wxyz]
            q = Quaternion().slerp(Quaternion([k.co[1] for k in keys]), share)
            for k, c in zip(keys, q):
                k.co[1] = c
                k.handle_left[1] = c
                k.handle_right[1] = c
        for fc in wxyz:
            fc.update()


def prepare():
    wanted = {"RigRoot|%s|Animation Base Layer" % src: state for state, src in TAKES.items()}
    missing = set(wanted) - {a.name for a in bpy.data.actions}
    if missing:
        raise SystemExit("Missing actions: %s" % ", ".join(sorted(missing)))
    for action in list(bpy.data.actions):
        if action.name not in wanted:
            bpy.data.actions.remove(action)
    for action in list(bpy.data.actions):
        state = wanted[action.name]
        action.name = "Esko_" + state
        if state in IN_PLACE:
            flat = [fc for fc in _fcurves(action) if (fc.data_path, fc.array_index) in ROOT_TRAVEL]
            for fc in flat:
                _flatten(fc)
            print("  %s: flattened %d root-travel channel(s)" % (action.name, len(flat)))
        if state in TAIL_SWING:
            _damp_tail(action, TAIL_SWING[state])
            print("  %s: tail swing scaled to %.2f" % (action.name, TAIL_SWING[state]))
    arm = bpy.data.objects["Arm_Esko"]
    idle = bpy.data.actions["Esko_Idle"]
    arm.animation_data.action = idle
    if idle.slots:
        arm.animation_data.action_slot = idle.slots[0]


export(SRC, DST, keep_armature=True, keep_empties=True, prepare=prepare, animations=True)
