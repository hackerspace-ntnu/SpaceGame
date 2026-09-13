"""Ship the oxygen generator to Unity.

The model file holds exactly the assembled machine — every locally built block,
both appended docks, and the two dock markers — so it exports whole, no `keep`
list. The markers arrive as 6 mm cubes named `Marker_OxyGen_TankDock` and
`Marker_OxyGen_CellDock`; a builder reads their transforms to seat a bottle or a
cell, and should hide or delete the cubes themselves.

Re-run whenever `oxygen_generator.blend` changes; it never writes back to the
.blend.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python models/props/oxygen_generator_export.py
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))
from _exportlib import export, unity_path  # noqa: E402

# `fix_inverted`: a hand edit left `Mesh_OxyGen_HatchDrum` on a negative Z
# scale (determinant -1.85, world signed volume -0.190). Blender draws it
# correctly, the FBX carries the negative scale straight through, and Unity
# renders the drum inside-out. Repaired at export time so the .blend — which is
# hand-edited and the source of truth — is never written to.
export(os.path.join(_HERE, "oxygen_generator.blend"),
       unity_path("Props", "oxygen_generator.fbx"),
       fix_inverted=True)
