"""Export relay_outpost.blend to the FBX Unity consumes.

An export, not a generator — it never writes to the .blend it opens, and it is
meant to be re-run whenever the model changes.

Static set dressing: the file has no armature, so nothing is dropped and the
89 meshes arrive as a flat hierarchy under one root. The three authoring
collections (Structure / Unique / Yard) do not survive the FBX; if the yard
clutter needs to be toggled separately in Unity, group it in the prefab.

    blender --background --python relay_outpost_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = HERE
while LIB != os.path.dirname(LIB) and not os.path.exists(os.path.join(LIB, "_exportlib.py")):
    LIB = os.path.dirname(LIB)
sys.path.insert(0, LIB)

from _exportlib import export, unity_path  # noqa: E402

print("relay_outpost")
export(os.path.join(HERE, "relay_outpost.blend"),
       unity_path("Environment", "Structures", "Outpost", "relay_outpost.fbx"))
