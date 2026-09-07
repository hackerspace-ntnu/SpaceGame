"""Ship the storm flask to Unity.

One FBX: the whole model file. See `bottled_singularity_export.py` — the two
bottles are the same shape of export and the reasoning is identical.

`keep_armature=False`: the stopper pops and the core drains, both rigid
transforms about their own origins. `keep_empties=True`: `Marker_Grip`,
`Marker_ThrowPivot` and `Marker_Cap` are empties and vanish without it.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/storm_flask_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "storm_flask.blend")
DST = unity_path("Items", "storm_flask.fbx")

CONSUMABLE_BRACKET = 0.50


def main():
    print("== Storm flask ==")
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe()
    print("  Consumable bracket on ItemScaleLadder would be %.2f m"
          % CONSUMABLE_BRACKET)


main()
