"""Ship the flamethrower to Unity.

Exports the whole model file. A model `.blend` here holds exactly the objects
that make up the model — one collection, no alternate variations — so
`_exportlib.export` is the right tool and its flags stay in one place.

No rig (`keep_armature=False`): nothing on this item is skinned. The moving
parts the design doc names — the trigger, the gauge fill, the pilot flame — are
separate objects the prefab drives, not bones.

`keep_empties` is not set, and does not need to be: every reference point on
this model is a 4 mm `Marker_*` MESH, which survives `object_types={"MESH"}`
where an empty does not. `Marker_Muzzle`, `Marker_Pilot`, `Marker_Grip`,
`Marker_GripFore` and `Marker_Gauge` come across with the geometry for the
wave-2 prefab builder to read and strip.

Exports are the one kind of script here meant to be re-run; this only ever
reads the .blend.

    blender --background --python models/gear/flamethrower_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "flamethrower.blend")
DST = unity_path("Items", "flamethrower.fbx")


def main():
    export(SRC, DST, keep_armature=False)
    describe()


main()
