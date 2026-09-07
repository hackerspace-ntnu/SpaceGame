"""Ship the cryo sprayer to Unity.

Exports the whole model file — one collection, one item. Flags and their
reasons live in `_exportlib`; the two decisions here match the rest of the
sprayer family:

- `keep_armature=False`: nothing is skinned. `Mesh_CryoSprayer_Rime` is its own
  object so the prefab can grow the frost while spraying and clear it when
  idle, which is the moving part the design doc asks for — an object to scale,
  not a bone to pose.
- No `keep_empties`: every reference point is a 4 mm `Marker_*` MESH, which
  survives `object_types={"MESH"}` where an empty does not.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/cryo_sprayer_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "cryo_sprayer.blend")
DST = unity_path("Items", "cryo_sprayer.fbx")


def main():
    export(SRC, DST, keep_armature=False)
    describe()


main()
