"""Ship the oxygen tank to Unity.

The file holds one collection and nothing else — this model has no variations by
design — so it exports whole, no `keep` list.

`oxygen_tank.blend` is HAND-EDITED and is the source of truth. Never regenerate
it from `oxygen_tank.py`; edit it in Blender and re-run this.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python components/props/oxygen_tank_export.py
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))
from _exportlib import export, unity_path  # noqa: E402

export(os.path.join(_HERE, "oxygen_tank.blend"),
       unity_path("Props", "oxygen_tank.fbx"))
