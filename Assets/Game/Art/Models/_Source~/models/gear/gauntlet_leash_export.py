"""Ship the leash gauntlet to Unity.

Exports the whole model file through `_exportlib.export`: a model file holds
exactly the objects that make up the model — the device alone, since the bracer
under it is worn separately — so no `keep` filter is needed.

No rig (`keep_armature=False`): nothing on the device articulates in Unity.
The rope itself is `LeashRope`, spawned in the world, not part of the mesh.

`keep_empties=True` because the prefab's `LeashArtifact.muzzle` points at the
`muzzle` empty at the fairlead's centre; without it the rope would pay out
from the prefab root at the wrist bone.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/gauntlet_leash_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "gauntlet_leash.blend")
DST = unity_path("Items", "gauntlet_leash.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe(worn_scale=1.0)


main()
