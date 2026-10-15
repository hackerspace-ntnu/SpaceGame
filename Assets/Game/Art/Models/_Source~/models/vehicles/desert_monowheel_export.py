"""Ship the five desert monowheels to Unity.

| .blend | collection | .fbx |
| --- | --- | --- |
| `desert_monowheel` | `Coll_Monowheel_Runner` | `Vehicles/Monowheel/desert_monowheel_runner.fbx` |
| `desert_monowheel_double` | `Coll_Monowheel_Double` | `Vehicles/Monowheel/desert_monowheel_double.fbx` |
| `desert_monowheel_double` | `Coll_Monowheel_DoubleWide` | `Vehicles/Monowheel/desert_monowheel_double_wide.fbx` |
| `desert_monowheel` | `Coll_Monowheel_Hauler` | `Vehicles/Monowheel/desert_monowheel_hauler.fbx` |
| `desert_monowheel` | `Coll_Monowheel_Patched` | `Vehicles/Monowheel/desert_monowheel_patched.fbx` |

The Hauler and Patched are the Runner plus luggage or wear (`desert_monowheel_variants.py`).

**The rig is kept** (`keep_armature=True`): the wheels spin in game by turning the
`Bone_Ring*` transforms, and every paddle rides on its ring. **Empties are kept**:
`Socket_Rider_*` and `Socket_Passenger_*` are where players attach.
**`fix_inverted`** because these files are hand-edited, and a part mirrored by a
negative scale renders inside-out in Unity with nothing reporting it.

On the double variants the ring bones are cambered (`camber_out_deg` on the
armature), so a spinner must turn each ring bone about ITS OWN axle axis, never
about the vehicle's X axis -- that would wobble the wheel.

Exports are meant to be re-run; this only ever reads the .blend files.

    blender --background --python models/vehicles/desert_monowheel_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

JOBS = [
    ("desert_monowheel", "Coll_Monowheel_Runner", "desert_monowheel_runner"),
    ("desert_monowheel_double", "Coll_Monowheel_Double", "desert_monowheel_double"),
    ("desert_monowheel_double", "Coll_Monowheel_DoubleWide", "desert_monowheel_double_wide"),
    ("desert_monowheel", "Coll_Monowheel_Hauler", "desert_monowheel_hauler"),
    ("desert_monowheel", "Coll_Monowheel_Patched", "desert_monowheel_patched"),
]


def ship(stem, collection, out):
    dst = unity_path("Vehicles", "Monowheel", "%s.fbx" % out)
    print("\n=== %s / %s" % (stem, collection))
    export(os.path.join(HERE, "%s.blend" % stem), dst, keep_armature=True,
           keep_empties=True, fix_inverted=True, keep_collection=collection)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    rigs = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    if len(rigs) != 1:
        raise SystemExit("Expected one rig in %s, found %d" % (collection, len(rigs)))
    loose = sorted(o.name for o in meshes if o.parent is None)
    if loose:
        raise SystemExit("Unrigged parts would not follow the vehicle: %s" % ", ".join(loose))
    rings = sorted(b.name for b in rigs[0].data.bones if b.name.startswith("Bone_Ring"))
    sockets = sorted(o.name for o in bpy.data.objects if o.type == 'EMPTY')
    lo = [min((o.matrix_world @ v.co)[i] for o in meshes for v in o.data.vertices) for i in range(3)]
    hi = [max((o.matrix_world @ v.co)[i] for o in meshes for v in o.data.vertices) for i in range(3)]
    print("  %d meshes, spin bones %s, sockets %s" % (len(meshes), rings, sockets))
    print("  SIZE %.2f wide x %.2f long x %.2f tall (m)" % tuple(hi[i] - lo[i] for i in range(3)))


for job in JOBS:
    ship(*job)
print("\nShipped %d monowheels." % len(JOBS))
