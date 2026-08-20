"""Export OrkhenRhot.blend to the FBX Unity consumes.

An export, not a generator — it never writes to the .blend it opens.

**Also a blockout.** One mesh, 308 triangles under a Subsurf modifier, a single
material literally named `Material`, and no armature — so unlike every other
creature in the library this one cannot walk, and the FBX carries no rig for a
locomotion controller to bind to. `use_mesh_modifiers=True` bakes the Subsurf,
so the exported mesh is the smooth form rather than the control cage.

Exported anyway because a placeable 5.4 x 3.1 x 5.8 m silhouette is useful for
blocking out scale in a scene, but it is not an animatable creature yet.

    blender --background --python OrkhenRhot_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = HERE
while LIB != os.path.dirname(LIB) and not os.path.exists(os.path.join(LIB, "_exportlib.py")):
    LIB = os.path.dirname(LIB)
sys.path.insert(0, LIB)

from _exportlib import export, unity_path  # noqa: E402

print("OrkhenRhot (blockout)")
export(os.path.join(HERE, "OrkhenRhot.blend"),
       unity_path("Creatures", "Organic", "OrkhenRhot", "orkhen_rhot.fbx"))
