"""Ship the Sucker Puncher gauntlet to Unity.

Exports the whole model file, like `ruin_scanner_export.py`: a model file holds
exactly the objects that make up the model, so `_exportlib.export` is the right
tool and its flags stay in one place.

No rig (`keep_armature=False`): the four ram objects move as one rigid group
along one axis and already share their origin (`RAM_PIVOT`), so
`SuckerPuncherArtifact` slides them by a single local offset — the same
capability as a one-bone rig without a hierarchy for Unity to unpick.

`keep_empties=True`: the builder adopts `Marker_Grip` as the GripPoint and
`Marker_Vent` as the steam burst's origin; without the flag both are dropped
and the prefab's references point at nothing.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/gauntlet_puncher_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "gauntlet_puncher.blend")
DST = unity_path("Items", "gauntlet_puncher.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe(worn_scale=1.0)


main()
