"""Ship the grapple bracer to Unity.

Exports the whole model file, like `item_scanner_export.py` and unlike
`components/props/item_devices_export.py` — a model file holds exactly the
objects that make up the model, so `_exportlib.export` is the right tool and
its flags stay in one place.

No rig (`keep_armature=False`): nothing on this device articulates. The drum
could spin and nothing in the game spins it; the one part that moves is the
harpoon, and it moves by being destroyed here and instantiated as
`hookHeadPrefab` out in the world.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/grapple_bracer_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "grapple_bracer.blend")
DST = unity_path("Items", "grapple_bracer.fbx")


def main():
    export(SRC, DST, keep_armature=False)

    # The prefab wires the seated harpoon by serialized reference so it can be
    # hidden while the hook is in flight, and it puts the rope's muzzle on the
    # fairlead. Both need to know where things landed, and printing it here
    # beats measuring it in the editor afterwards. `describe` prints the Unity
    # figures with the X flip the BUILD record measured.
    describe(worn_scale=2.1)


main()
