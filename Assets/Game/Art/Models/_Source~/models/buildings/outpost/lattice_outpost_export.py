"""Export lattice_outpost.blend to the FBX Unity consumes.

An export, not a generator — it never writes to the .blend it opens.

Static, no armature. Tall: 52 m from the pad to the mast tip, against a 22 x 26 m
footprint, so this one wants a culling/LOD pass more than the low outposts do.

    blender --background --python lattice_outpost_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = HERE
while LIB != os.path.dirname(LIB) and not os.path.exists(os.path.join(LIB, "_exportlib.py")):
    LIB = os.path.dirname(LIB)
sys.path.insert(0, LIB)

from _exportlib import export, unity_path  # noqa: E402

print("lattice_outpost")
export(os.path.join(HERE, "lattice_outpost.blend"),
       unity_path("Environment", "Structures", "Outpost", "lattice_outpost.fbx"))
