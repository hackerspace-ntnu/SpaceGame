"""Ship the foam gun to Unity.

Exports the whole model file — one collection, one item. Flags and their
reasons live in `_exportlib`; the two decisions here are the same as the rest
of the sprayer family's:

- `keep_armature=False`: nothing is skinned. The moving parts the design doc
  names — the trigger, the gauge fill, the bell's iris — are separate objects
  the prefab drives, not bones. `Mesh_FoamGun_Iris` is its own object precisely
  so it can be turned.
- No `keep_empties`: every reference point is a 4 mm `Marker_*` MESH, which
  survives `object_types={"MESH"}` where an empty does not.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/foam_gun_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "foam_gun.blend")
DST = unity_path("Items", "foam_gun.fbx")


def main():
    export(SRC, DST, keep_armature=False)
    describe()


main()
