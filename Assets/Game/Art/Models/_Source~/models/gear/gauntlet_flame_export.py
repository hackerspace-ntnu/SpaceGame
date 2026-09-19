"""Ship the Flame Gauntlet to Unity.

Whole-file export through `_exportlib.export`, no rig (nothing moves; the fire is Unity's),
`keep_empties=True` because the builder adopts `Marker_Grip`, `Marker_Muzzle`,
`Marker_Pilot` and `Marker_Gauge`.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/gauntlet_flame_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "gauntlet_flame.blend")
DST = unity_path("Items", "gauntlet_flame.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe(worn_scale=1.0)


main()
