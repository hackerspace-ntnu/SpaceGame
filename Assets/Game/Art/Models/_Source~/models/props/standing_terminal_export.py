"""Ship the standing terminal to Unity.

The terminal IS the hand-built `Coll_CrtMonitor_Kiosk` variation in
`components/props/crt_monitor.blend` — the user took the generated kiosk head
and reworked it into a whole leaning cabinet, key strip and all — so this
exports that one collection out of the component file (`keep_collection`),
the way `item_devices_export.py` ships one variation of a device. Everything
the user adds to that collection ships; nothing is named here.

There is no model file to assemble any more: `standing_terminal.blend`, the
pedestal-and-deck assembly this script used to export, was retired on
2026-09-05 (see `standing_terminal_BUILD.md`). The FBX keeps its name so the
prefab that references it keeps its GUID.

No markers and no rig. `StandingTerminalBuilder` measures the screen plate's
own mesh for the glass centre and normal, and stands the model on its lowest
point, so the .blend's origin can sit wherever the user left it.

Re-run whenever the Kiosk collection changes; it never writes back.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python models/props/standing_terminal_export.py
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
from _exportlib import describe, export, unity_path  # noqa: E402

export(os.path.join(_LIB, "components", "props", "crt_monitor.blend"),
       unity_path("Props", "standing_terminal.fbx"),
       keep_collection="Coll_CrtMonitor_Kiosk")
describe()
