"""Export refinery_tower.blend to the FBX Unity consumes.

An export, not a generator — it never writes to the .blend it opens.

Static, no armature. The largest thing in the library at 87 x 47 x 77 m, and it
sits 1.66 m below the origin plane — the base is modelled dug in, so dropping it
on terrain at y=0 buries the plinth by design rather than by accident.

    blender --background --python refinery_tower_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = HERE
while LIB != os.path.dirname(LIB) and not os.path.exists(os.path.join(LIB, "_exportlib.py")):
    LIB = os.path.dirname(LIB)
sys.path.insert(0, LIB)

from _exportlib import export, unity_path  # noqa: E402

print("refinery_tower")
export(os.path.join(HERE, "refinery_tower.blend"),
       unity_path("Environment", "Structures", "Industrial", "refinery_tower.fbx"))
