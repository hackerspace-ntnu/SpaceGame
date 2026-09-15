"""Ship the eighteen nomad shade sails to Unity, one FBX per sail.

`tents.blend` is a contact sheet the same way `nomad_settlement.blend` is: the
eighteen sails sit on a 12 m grid, each in its own `Coll_NomadSail_<Name>` with
the collection's `instance_offset` on its ground anchor point. See `TENTS.md`.

The ten `Wall*` sails are authored against a wall in the **y = 0** plane and hang
away from it towards −y, which arrives in Unity as +z: their wall face is the
model's z = 0 plane and the sail reaches out along +z. `NomadSettlementBuilder`
relies on that to seat them against a building.

An export, not a generator — it never writes back to the .blend.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python components/nomad_settlement/tents_export.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)

from _exportlib import export_collections, unity_path  # noqa: E402

SAILS = ["QuadSmall", "QuadLarge", "Tri", "TriTall", "Penta", "HexLow", "Ribbon",
         "Kite", "WallQuad", "WallTri", "WallStrip", "WallLean", "WallPorch",
         "WallCorner", "WallFan", "WallCanopyLong", "WallBillow", "WallSpur"]


def snake(name):
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


jobs = [("Coll_NomadSail_" + s,
         unity_path("Environment", "Structures", "NomadSettlement", "Tents",
                    "nomad_sail_%s.fbx" % snake(s)))
        for s in SAILS]

print("nomad sails (%d)" % len(SAILS))
export_collections(os.path.join(HERE, "tents.blend"), jobs)
