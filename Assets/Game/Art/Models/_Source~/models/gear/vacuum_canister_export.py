"""Ship the vacuum canister to Unity — two FBXs, because it is two items.

`VacuumCanister` and `VacuumCanisterFull` are separate item identities in the
design, the way an empty and a charged bottle are, so they cannot be one model
with a toggled child: the full one carries geometry the empty one does not
(the containment rings and the captive) and a different lid.

`Coll_VacuumCanister_Cracked` deliberately does NOT ship. It is set dressing and
loot built ahead of any code that spawns it; a third FBX in `Assets/` that nothing
references is landfill. Adding it is one row in `TARGETS`.

The rig is dropped — there is none. The lid, and each of the six iris leaves,
carry their pivots in their own object origins, so Unity gets plain transforms it
can drive directly. Each leaf's LOCAL +Z is its hinge axis, and the leaves keep
their object rotations through the FBX for exactly that reason.

Markers ship as 4 mm meshes (`Marker_Muzzle`, `Marker_Grip`, `Marker_Gauge`); the
prefab reads their positions and deletes them.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/vacuum_canister_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

MODEL = os.path.join(HERE, "vacuum_canister.blend")

TARGETS = [
    ("Coll_VacuumCanister_Empty", "vacuum_canister.fbx"),
    ("Coll_VacuumCanister_Full", "vacuum_canister_full.fbx"),
]


def main():
    for coll, fbx in TARGETS:
        print("== %s ==" % coll)
        export(MODEL, unity_path("Items", fbx), keep_armature=False,
               keep_collection=coll)
        describe()


main()
