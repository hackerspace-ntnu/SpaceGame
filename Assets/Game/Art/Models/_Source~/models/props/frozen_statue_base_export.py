"""Ship the frozen statue bases to Unity.

One FBX carrying **six meshes** — a plinth and a shard cluster for each of the
three bases — all stacked at the origin, as every variation file in this library
is. The FBX root is not the prefab: the statue prefab takes one `_Plinth` and
its matching `_Shards`, and scales the pair to the frozen body's footprint. The
bases are authored at a 1.0 m nominal footprint for exactly that reason.

`keep_armature=False`: nothing here moves. `keep_empties=True` ships
`Marker_EffectOrigin`, the ground plane on the axis, which is where the body
stands and where the freeze effect emits from.

`channel_report()` measures the shader channels off the exported file rather
than trusting the build log — the statue shader steers on UV1, and a lost UV set
is invisible until someone writes the shader and it draws a flat lump.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/props/frozen_statue_base_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)
sys.path.insert(0, os.path.join(LIB, "components", "props"))

from _exportlib import describe, export, unity_path  # noqa: E402
from flask_kit import channel_report  # noqa: E402

SRC = os.path.join(HERE, "frozen_statue_base.blend")
DST = unity_path("Props", "frozen_statue_base.fbx")


def main():
    print("== Frozen statue base ==")
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe()
    channel_report()


main()
