"""Ship the gauntlet item scanner to Unity.

Exports the whole model file through `_exportlib.export`, like the other
gauntlets: a model file holds exactly the objects that make up the model.

No rig (`keep_armature=False`): the dial and the antenna are rigid parts
with their origins on their own axis of motion, and `ItemScannerArtifact`
rotates them as Transforms. No empties (`keep_empties=False`): nothing in
the prefab binds to one — the screen, dial and antenna are bound by mesh
object name.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/gauntlet_item_scanner_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "gauntlet_item_scanner.blend")
DST = unity_path("Items", "gauntlet_item_scanner.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=False)
    describe(worn_scale=1.0)


main()
