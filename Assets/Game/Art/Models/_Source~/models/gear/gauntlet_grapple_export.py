"""Ship the gauntlet grapple to Unity.

Exports the whole model file, like `ruin_scanner_export.py`: a model file
holds exactly the objects that make up the model, so `_exportlib.export` is
the right tool and its flags stay in one place.

No rig (`keep_armature=False`): nothing on this device articulates. The
harpoon that leaves the arm is a separate prefab spawned at `muzzle`; the
seated `Mesh_GrappleHarpoon` is a plain mesh the artifact hides and shows.

`keep_empties=True` because the prefab's `muzzle` field points at the
`muzzle` empty. `_exportlib` ships meshes only unless told otherwise, and an
FBX without the empty leaves the rope paying out of the prefab root, in the
middle of the forearm.

`describe(worn_scale=1.0)`: the family is authored at true suit scale, so the
gauntlet is worn at scale 1 — the printed pivots are the ones to type into
the prefab.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/gauntlet_grapple_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "gauntlet_grapple.blend")
DST = unity_path("Items", "gauntlet_grapple.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe(worn_scale=1.0)


main()
