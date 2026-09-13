"""Ship the repulsor gauntlet to Unity.

Exports the whole model file, like `ruin_scanner_export.py`: a model file
holds exactly the objects that make up the model — the device alone, since the
bracer under it is worn separately — so `_exportlib.export` is the right tool and its flags stay in one
place.

No rig (`keep_armature=False`): nothing on this device articulates. The blast
is spawned out in the world, rooted on `Marker_Emitter`, and the capacitor
ball is scaled and toggled by the prefab about its own origin.

`keep_empties=True` because the prefab reads `Marker_Emitter` (the blast
origin) and `Marker_Grip` (the wrist joint) by reference; `_exportlib` ships
meshes only unless told otherwise, and without them the blast would start
from the prefab root in the middle of the forearm.

`describe(worn_scale=1.0)`: the family is authored at true suit scale, so the
gauntlet is worn at 1.0 and the printed Unity-frame pivots are the ones to
type into the prefab.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/gauntlet_repulsor_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "gauntlet_repulsor.blend")
DST = unity_path("Items", "gauntlet_repulsor.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe(worn_scale=1.0)


main()
