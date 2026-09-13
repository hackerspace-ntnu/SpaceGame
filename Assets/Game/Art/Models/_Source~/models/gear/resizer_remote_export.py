"""Ship the resizer remote to Unity.

One FBX: the handset is one item with one state. Its three moving parts ride
across as plain transforms, so the rig is dropped — there is none, and three
rigid parts each turning or sliding on one axis do not earn an armature:

  `Mesh_ResizerRemote_Whip`   telescopes along its own +Z from the collar
  `Mesh_ResizerRemote_Dial`   turns about its own spindle between the settings
  `Mesh_ResizerRemote_Lamp`   does not move; Unity tints it per instance

Markers ship as 4 mm meshes, the way the rest of the library ships sockets.
`Marker_Emitter` is the extra one: it is the whip's TIP, not its collar, because
the signal leaves the far end of the antenna — see the build script.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/resizer_remote_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

MODEL = os.path.join(HERE, "resizer_remote.blend")


def main():
    print("== Resizer remote ==")
    export(MODEL, unity_path("Items", "resizer_remote.fbx"),
           keep_armature=False, keep_collection="Coll_ResizerRemote")
    describe()


main()
