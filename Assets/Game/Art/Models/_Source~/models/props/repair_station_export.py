"""Ship the repair station to Unity.

The model file holds exactly the assembled station — every appended part plus
the gauge marker — so it exports whole, no `keep` list. Re-run whenever
`repair_station.blend` changes; it never writes back to the .blend.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python models/props/repair_station_export.py
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))
from _exportlib import export, unity_path  # noqa: E402

export(os.path.join(_HERE, "repair_station.blend"),
       unity_path("Props", "repair_station.fbx"))
