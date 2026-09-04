"""Ship the ghost device to Unity as the body screen's empty-forearm placeholder.

    blender --background --python models/gear/ghost_device_export.py

No rig and no empties: the ghost is two boxes that never move and nothing binds
to a node inside it. `GearGhostBuilder` makes the prefab; `BodySite` copies that
prefab, strips it and repaints it translucent, then seats the copy through the
very call that wears a real gauntlet, so the ghost stands where a device would.

Exports are meant to be re-run; this only ever reads the .blend.
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "ghost_device.blend")
DST = unity_path("Items", "ghost_device.fbx")


def main():
    export(SRC, DST, keep_armature=False)
    describe(worn_scale=1.0)


main()
