"""Export caraxoid.blend to Creatures/Organic/Caraxoid/, one FBX per Caraxoid with all its takes.

    blender --background --python caraxoid_export.py

The file holds four Caraxoids side by side, each in its own collection with its own rig:

    Caraxoid_Red   -> caraxoid_red.fbx    (Caraxoid_Rig)       quadruped clip set
    Caraxoid_Blue  -> caraxoid_blue.fbx   (CaraxoidBlue_Rig)   quadruped clip set
    Caraxoid_Tan   -> caraxoid_tan.fbx    (CaraxoidTan_Rig)    raptor clip set (also the mother)
    Caraxoid_Baby  -> caraxoid_baby.fbx   (CaraxoidBaby_Rig)   raptor clip set + Beg

Each export keeps only its own collection and only the actions named `<Label>_<Clip>`, so the
takes read `<rig>|<Label>_<Clip>`. The rigs stand side by side in the .blend (offsets on the rig
OBJECT) and the baby's rig is scaled down; both are reset in memory so every FBX stands on its own
origin at model scale. In-game size is the prefab's job, not the export's.

The clips are in place apart from the root bone: Pounce/Jump/Turn move or rotate `Root`, which
Unity plays as visual motion only (root motion is off; the motor moves the creature).
"""
import os
import sys

LIB = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
sys.path.insert(0, LIB)
from _exportlib import export, unity_path  # noqa: E402

import bpy  # noqa: E402

SRC = os.path.join(LIB, "models", "creatures", "caraxoid.blend")

# collection, rig, action label, fbx name
JOBS = (("Caraxoid_Red", "Caraxoid_Rig", "CaraxoidRed", "caraxoid_red.fbx"),
        ("Caraxoid_Blue", "CaraxoidBlue_Rig", "CaraxoidBlue", "caraxoid_blue.fbx"),
        ("Caraxoid_Tan", "CaraxoidTan_Rig", "CaraxoidTan", "caraxoid_tan.fbx"),
        ("Caraxoid_Baby", "CaraxoidBaby_Rig", "CaraxoidBaby", "caraxoid_baby.fbx"))


def preparer(collection, rig_name, label):
    def prepare():
        keep = {o.name for o in bpy.data.collections[collection].all_objects}
        for o in list(bpy.data.objects):
            if o.name not in keep:
                bpy.data.objects.remove(o)
        for action in list(bpy.data.actions):
            if not action.name.startswith(label + "_"):
                bpy.data.actions.remove(action)
        rig = bpy.data.objects[rig_name]
        rig.location = (0, 0, 0)
        rig.rotation_euler = (0, 0, 0)
        rig.scale = (1, 1, 1)
        idle = bpy.data.actions[label + "_Idle"]
        rig.animation_data.action = idle
        rig.animation_data.action_slot = idle.slots[0]
        print("  %s: %d take(s)" % (label, len(bpy.data.actions)))
    return prepare


for collection, rig, label, fbx in JOBS:
    export(SRC, unity_path("Creatures", "Organic", "Caraxoid", fbx), keep_armature=True,
           prepare=preparer(collection, rig, label), animations=True)
