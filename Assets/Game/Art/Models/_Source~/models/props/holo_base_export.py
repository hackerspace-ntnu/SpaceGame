"""Ship the holo bases to Unity — one FBX per variation.

`holo_base.blend` holds four base variations stacked at the origin, which is
right for the library and useless as a single FBX; exported whole they arrive
in Unity as one interpenetrating lump. So each variation ships alone, mesh plus
its `Marker_HoloAnchor_*` (the empty that `MapHologramTerrain.projectorAnchor`
wires to).

Exports are meant to be re-run whenever the .blend changes.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python models/props/holo_base_export.py
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))
from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(_HERE, "holo_base.blend")

VARIATIONS = {
    "holo_base_puck": ["Mesh_HoloBase_Puck", "Marker_HoloAnchor_Puck"],
    "holo_base_pedestal": ["Mesh_HoloBase_Pedestal",
                           "Marker_HoloAnchor_Pedestal"],
    "holo_base_table": ["Mesh_HoloBase_Table", "Marker_HoloAnchor_Table"],
    "holo_base_tripod": ["Mesh_HoloBase_Tripod", "Marker_HoloAnchor_Tripod"],
}

for name, keep in VARIATIONS.items():
    export(SRC, unity_path("Props", name + ".fbx"), keep=keep)
