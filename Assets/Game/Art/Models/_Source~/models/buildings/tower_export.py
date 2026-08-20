"""Export tower.blend to the FBX Unity consumes.

An export, not a generator — it never writes to the .blend it opens.

**This one is a blockout, and the FBX is honest about that.** Unlike the rest of
the library, `tower.blend` predates the conventions: its 32 meshes are named
`Cube`, `Cube.002`, `Cube.012`, it carries 64 locally-defined materials named
`Material.001`..`Material.064` instead of linking the palette, every object has
unapplied non-uniform scale (up to 9.8x), and the scene still has the authoring
cameras and lights in it. Object types are filtered to `MESH`, so the cameras and
lights are dropped, but nothing else here can be fixed at export time without
editing the .blend — which is the user's file, not this script's.

So it lands under `Blockouts/` rather than `Structures/`, to keep it out of the
way of the finished buildings until someone renames the meshes, applies the
scales, and moves it onto palette materials. At 540 triangles it is a massing
study, not an asset.

    blender --background --python tower_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = HERE
while LIB != os.path.dirname(LIB) and not os.path.exists(os.path.join(LIB, "_exportlib.py")):
    LIB = os.path.dirname(LIB)
sys.path.insert(0, LIB)

from _exportlib import export, unity_path  # noqa: E402

print("tower (blockout)")
export(os.path.join(HERE, "tower.blend"),
       unity_path("Environment", "Blockouts", "tower.fbx"))
