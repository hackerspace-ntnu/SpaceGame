"""Export dunehorn.blend to Creatures/Organic/Dunehorn/dunehorn.fbx with its twelve gameplay takes.

    blender --background --python dunehorn_export.py

The .blend carries all 81 actions of the bear pack it was sculpted from. Only the twelve that fill
Appa's controller states ship, renamed to Dunehorn_<state> so the takes read `Arm_Dunehorn|Dunehorn_<state>`.

The pack's locomotion is root-motion animation: Walk carries the armature OBJECT 1.6 m forward
per cycle, Run 4 m, and the turning walks swing it round. In Unity the NavMeshAgent moves the
creature, so a clip that also moves it would drift ahead and snap back every loop. The forward,
sideways and yaw channels of the armature object are flattened to their first key; the vertical
bob stays.
"""
import os
import sys

LIB = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
sys.path.insert(0, LIB)
from _exportlib import export, unity_path  # noqa: E402

import bpy  # noqa: E402

SRC = os.path.join(LIB, "models", "creatures", "dunehorn.blend")
DST = unity_path("Creatures", "Organic", "Dunehorn", "dunehorn.fbx")

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
        action.name = "Dunehorn_" + state
        if state in IN_PLACE:
            flat = [fc for fc in _fcurves(action) if (fc.data_path, fc.array_index) in ROOT_TRAVEL]
            for fc in flat:
                _flatten(fc)
            print("  %s: flattened %d root-travel channel(s)" % (action.name, len(flat)))
    arm = bpy.data.objects["Arm_Dunehorn"]
    idle = bpy.data.actions["Dunehorn_Idle"]
    arm.animation_data.action = idle
    if idle.slots:
        arm.animation_data.action_slot = idle.slots[0]


export(SRC, DST, keep_armature=True, keep_empties=True, prepare=prepare, animations=True)
