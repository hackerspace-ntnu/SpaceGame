"""Ship the slick can to Unity.

Exports the whole model file — one collection, one item. Flags and their
reasons live in `_exportlib`; the two decisions here match the rest of the
sprayer family:

- `keep_armature=False`: nothing is skinned. The trigger and the gauge fill are
  separate objects the prefab drives, not bones.
- No `keep_empties`: every reference point is a 4 mm `Marker_*` MESH, which
  survives `object_types={"MESH"}` where an empty does not.

**One thing to read off `describe()` before wiring the prefab:** the can's long
axis is Z, not Y. Its Blender bounds are 0.395 m tall and about 0.12 m in the
other two directions, which the export maps onto Unity's Y — so
`ItemGrip.holdSize`, which is the longest axis, is the can's *height* here
while it is the *length* on the other three sprayers. The number is printed
below; take it from there rather than assuming the family's 0.5.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/slick_can_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "slick_can.blend")
DST = unity_path("Items", "slick_can.fbx")


def main():
    export(SRC, DST, keep_armature=False)
    describe()


main()
