"""Export mining_rig_derelict.blend to the FBX Unity consumes.

An export, not a generator — it never writes to the .blend it opens.

**This is the one building that keeps its armature.** `Arm_MiningRig` carries 12
bones and none of them are deformation bones — they are ambient-motion handles:
`Bone_VentFan_A/B` spin, `Bone_FloodSweep_Crown/West` pan, `Bone_RoofHatch`
opens, `Bone_StackFlue` rocks, and `Bone_CableSway_0..5` drive the slack cables.
Strip the rig and the derelict becomes a completely still prop, which is exactly
the read the model is built to avoid. Leaf bones stay off, so a script walking a
bone's children finds the mesh it expects rather than a `<bone>_end` stub.

    blender --background --python mining_rig_derelict_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = HERE
while LIB != os.path.dirname(LIB) and not os.path.exists(os.path.join(LIB, "_exportlib.py")):
    LIB = os.path.dirname(LIB)
sys.path.insert(0, LIB)

from _exportlib import export, unity_path  # noqa: E402

print("mining_rig_derelict")
export(os.path.join(HERE, "mining_rig_derelict.blend"),
       unity_path("Environment", "Structures", "Industrial", "mining_rig_derelict.fbx"),
       keep_armature=True)
