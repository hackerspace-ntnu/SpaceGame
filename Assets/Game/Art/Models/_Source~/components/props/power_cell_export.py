"""Ship the power cell to Unity — one variation per FBX.

The component file stacks all three variations at the origin, so exporting it
whole would arrive in Unity as a single interpenetrating lump. `keep` names the
objects of one variation at a time, which is how the rest of the library ships a
component file.

`power_cell.fbx` (the Slab) is the one the oxygen generator's cell dock is cut
for; `power_cell_compact.fbx` is built ahead and nothing references it yet.

The Drum variation is NOT shipped. A hand edit removed its Shell, Collar and
Port, leaving `Coll_PowerCell_Drum` holding only `Mesh_PowerCell_Drum_Face` — a
readout plate with no cell behind it. Listing it here would hard-fail
`_keep_only`, which refuses to ship a name that is not in the file.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python components/props/power_cell_export.py
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))
from _exportlib import export, unity_path  # noqa: E402

VARIATIONS = (
    ("power_cell", "Slab",
     ("Shell", "Bumpers", "Face", "Port", "Strap", "Latch")),
    ("power_cell_compact", "Compact",
     ("Shell", "Bumpers", "Face", "Port", "Handle", "Latch")),
)

for _fbx, _variation, _parts in VARIATIONS:
    export(os.path.join(_HERE, "power_cell.blend"),
           unity_path("Props", _fbx + ".fbx"),
           keep=["Mesh_PowerCell_%s_%s" % (_variation, _p) for _p in _parts])
