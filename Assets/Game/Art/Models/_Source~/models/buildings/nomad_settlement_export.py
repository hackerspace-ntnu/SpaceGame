"""Ship the forty nomad buildings to Unity, one FBX per building.

`nomad_settlement.blend` is a contact sheet: forty finished buildings laid out on
a 17 m grid, 8137 objects in one file. Unity wants them as forty separate props
that can be placed, so this exports each `Coll_NomadBuilding_NN` on its own, moved
onto its own origin by the collection's `instance_offset` — the ground centre the
generator set. See `nomad_settlement_BUILD.md`.

36 of the kit parts the generator copies carry a negative scale axis, and every
copy of those inherits it — 2538 objects over the forty buildings. Blender draws
them correctly and Unity renders them inside-out with nothing reporting a
problem.

**That is fixed in Unity, not here.** `_exportlib`'s `fix_inverted` bakes the
transform out and repairs the winding, and on this file it also moved three
buildings' mirrored parts up to 137 m away: these collections share one mesh
datablock between as many as 75 objects, and the .blend stays correct while the
FBX does not. Measured both ways — with it off, all forty buildings match the
source bounds to under 10 mm. `NomadSettlementBuilder` renders the mirrored
parts double-sided instead, which it finds by the sign of each renderer's
determinant.

An export, not a generator — it never writes back to the .blend.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python models/buildings/nomad_settlement_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)

from _exportlib import export_collections, unity_path  # noqa: E402

COUNT = 40

jobs = [("Coll_NomadBuilding_%02d" % n,
         unity_path("Environment", "Structures", "NomadSettlement",
                    "nomad_building_%02d.fbx" % n))
        for n in range(1, COUNT + 1)]

print("nomad settlement (%d buildings)" % COUNT)
export_collections(os.path.join(HERE, "nomad_settlement.blend"), jobs)
