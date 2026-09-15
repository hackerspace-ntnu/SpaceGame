"""Ship the Wrist Blade gauntlet to Unity.

Exports the whole model file, like `gauntlet_puncher_export.py`: a model file
holds exactly the objects that make up the model, so `_exportlib.export` is the
right tool and its flags stay in one place.

No rig (`keep_armature=False`): the one moving object slides along one axis
from an origin already on it (`BLADE_ROOT`), so `WristBladeArtifact` slides it
by a single local offset.

`keep_empties=True`: the builder adopts `Marker_Grip` as the GripPoint and
`Marker_Mouth` as the spark burst's origin.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/gauntlet_blade_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "gauntlet_blade.blend")
DST = unity_path("Items", "gauntlet_blade.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe(worn_scale=1.0)


main()
