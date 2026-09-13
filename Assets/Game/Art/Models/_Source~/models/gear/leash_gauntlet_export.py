"""Ship the leash gauntlet to Unity.

Exports the whole model file, like `grapple_bracer_export.py`. The hand-held
`leash_emitter.fbx` still comes out of `components/props/item_devices_export.py`
from `leash_device.blend`'s `Spool` collection; this is a second FBX, not a
replacement, so anything still wiring the emitter keeps working.

`keep_empties=True` because the prefab pays the rope out from the `muzzle`
child, which is an empty; the default export ships meshes only.

No rig: nothing on this device articulates.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/leash_gauntlet_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "leash_gauntlet.blend")
DST = unity_path("Items", "leash_gauntlet.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe(worn_scale=2.1)


main()
