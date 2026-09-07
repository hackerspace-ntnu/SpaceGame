"""Ship the strap-on booster to Unity — two FBXs, live and spent.

`Coll_StrapOnBooster_Armed` is the carried item and the thing that flies: jaws
open, arming light lit. `Coll_StrapOnBooster_Spent` is what drops off after the
two-second burn: light out behind a cracked cover, soot flared back over the
casing. Both are geometry, not a material swap, so a spent booster in the sand
reads as spent from across the dune.

`Coll_StrapOnBooster_Clamped` deliberately does NOT ship. It is the same objects
as Armed with one jaw turned shut, and a prefab that has both states in one
transform is better served by rotating `Mesh_DeviceClamp_JawMoving` about its own
local X — which is exactly what its origin on the hinge pin is for. The collection
stays in the .blend as the authored shut pose to match that rotation against.

The rig is dropped; there is none. The jaws carry their pivots in their object
origins.

Markers: `Marker_Muzzle` is the bell exit and therefore the thrust exit,
`Marker_Mount` is the centre of the saddle's contact face and the model's own
origin, `Marker_Clamp` is the nose throat, `Marker_Grip` is on the bore axis where
a hand wraps the casing.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/strap_on_booster_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

MODEL = os.path.join(HERE, "strap_on_booster.blend")

TARGETS = [
    ("Coll_StrapOnBooster_Armed", "strap_on_booster.fbx"),
    ("Coll_StrapOnBooster_Spent", "strap_on_booster_spent.fbx"),
]


def main():
    for coll, fbx in TARGETS:
        print("== %s ==" % coll)
        export(MODEL, unity_path("Items", fbx), keep_armature=False,
               keep_collection=coll)
        describe()


main()
