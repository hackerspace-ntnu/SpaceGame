"""Ship the ghost mount frame to Unity. Reads the .blend, never writes it.

No rig (`keep_armature=False`) and no empties: the frame is one static mesh the
body screen draws translucent, and the prefab binds nothing by name.

    blender --background --python models/gear/ghost_mount_frame_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "ghost_mount_frame.blend")
DST = unity_path("Items", "ghost_mount_frame.fbx")


def main():
    export(SRC, DST, keep_armature=False)
    describe(worn_scale=1.0)


main()
