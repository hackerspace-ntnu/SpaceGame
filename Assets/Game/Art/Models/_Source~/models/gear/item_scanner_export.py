"""Ship the item scanner to Unity.

Exports the whole model file — unlike `components/props/item_devices_export.py`,
which has to pick one collection out of a component holding three stacked
variations. A model file holds exactly the objects that make up the model, so
`_exportlib.export` is the right tool and its flags stay in one place.

The rig is dropped (`keep_armature=False`) because there is none: the dial and
the antenna are separate objects with their origins on their axes rather than
bones. See the model's docstring.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/item_scanner_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "item_scanner.blend")
DST = unity_path("Items", "item_scanner.fbx")


def main():
    export(SRC, DST, keep_armature=False)
    # The Unity prefab wires the dial, antenna and screen by serialized
    # reference and needs to know where each pivot landed. Printing it here
    # beats measuring it in the editor afterwards.
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for obj in sorted(meshes, key=lambda o: o.name):
        loc = obj.location
        uv = "uv" if obj.data.uv_layers else "--"
        print("  PIVOT %-34s (%.4f, %.4f, %.4f)  %s"
              % (obj.name, loc.x, loc.y, loc.z, uv))


main()
