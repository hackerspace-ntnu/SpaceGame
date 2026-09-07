"""Ship the inflator nozzle to Unity.

One FBX: the pump is one item with one state. Its two moving parts —
`Mesh_InflatorNozzle_Plunger`, which strokes along −Y from its own origin on the
barrel's rear face, and `Mesh_DeviceGauge_Needle`, which turns about its own
local Y on the dial spindle — ride across as plain transforms, so the rig is
dropped: there is none, and one rigid part turning about one axis does not earn
an armature.

Markers ship as 4 mm meshes. `Marker_Dial` is the extra one: the needle reads the
TARGET's inflation rather than this device's tank, so whatever drives it is not
the same code that drives `Marker_Gauge`'s supply bar.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/inflator_nozzle_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

MODEL = os.path.join(HERE, "inflator_nozzle.blend")


def main():
    print("== Inflator nozzle ==")
    export(MODEL, unity_path("Items", "inflator_nozzle.fbx"),
           keep_armature=False, keep_collection="Coll_InflatorNozzle")
    describe()


main()
