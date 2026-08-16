"""Export hulk_settlement.blend to the FBX Unity consumes.

An export, not a generator — it never writes to the .blend it opens.

Static, no armature. Extends 6 m below the origin plane, so the wreck reads as
half-buried when placed at terrain height.

    blender --background --python hulk_settlement_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = HERE
while LIB != os.path.dirname(LIB) and not os.path.exists(os.path.join(LIB, "_exportlib.py")):
    LIB = os.path.dirname(LIB)
sys.path.insert(0, LIB)

from _exportlib import export, unity_path  # noqa: E402

print("hulk_settlement")
export(os.path.join(HERE, "hulk_settlement.blend"),
       unity_path("Environment", "Structures", "Industrial", "hulk_settlement.fbx"))
