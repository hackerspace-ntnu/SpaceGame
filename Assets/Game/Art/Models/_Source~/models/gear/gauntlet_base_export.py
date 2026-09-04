"""Ship the gauntlet base to Unity as the bracer the player wears permanently.

Since 2026-09-04 the base is not part of a gauntlet. The player wears one on
each forearm from the moment they exist, a gauntlet is only the device that
clamps onto it, and this is the model of that bracer:
`Assets/Game/Art/Models/Items/gauntlet_base.fbx`, seated by
`ForearmBracers` through the same `ForearmSeat.Apply` every gauntlet goes
through, so a device lands on the deck it was authored against.

**The Mount variation, not Plain.** Mount is Plain plus `Deck` and `Bosses` —
the flat hardpoint and its four bolt heads. Those stay on the arm because they
are what a device bolts *to*: an empty forearm should show a bare deck waiting
for something, not a smooth shell with nowhere to put anything. Plain and Rail
stay in the component file as the variations they always were; nothing ships
them any more.

This replaced `ghost_gauntlet_export.py`, which shipped the Plain variation as
the body screen's empty-slot placeholder. That placeholder's premise died with
this change — a ghost bracer is pointless when the real bracer is always on the
arm — and the empty slot now shows `ghost_device.fbx` standing on this deck.

Exported from the component file rather than a model file, so `keep` names the
objects (see `_exportlib.export`). Re-running only ever reads the .blend.

    blender --background --python models/gear/gauntlet_base_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from _exportlib import describe, export, unity_path  # noqa: E402
from _gauntlet import BASE, base_object_names  # noqa: E402

DST = unity_path("Items", "gauntlet_base.fbx")


def main():
    export(BASE, DST, keep_armature=False, keep=base_object_names("Mount"))
    describe(worn_scale=1.0)


main()
