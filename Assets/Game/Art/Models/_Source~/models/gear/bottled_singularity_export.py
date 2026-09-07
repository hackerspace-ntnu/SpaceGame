"""Ship the bottled singularity to Unity.

One FBX: the whole model file, which is exactly the objects that make up the
bottle. No `keep=` filter — that flag is for pulling one variation out of a
component file, and this is a model.

`keep_armature=False` because there is none. Everything that moves — the six
iris leaves, the core — is a rigid object turning or scaling about its own
origin, which the FBX hands Unity as a plain Transform the artifact drives
directly.

`keep_empties=True` is the flag this export cannot do without. `Marker_Grip`,
`Marker_ThrowPivot` and `Marker_Cap` are empties, and `_exportlib` ships meshes
only unless told otherwise — without it the prefab has no idea where the hand
closes or what the throw arc turns about, and every effect plays from the
prefab root. Same reason `ruin_scanner_export.py` carries the flag.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/bottled_singularity_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "bottled_singularity.blend")
DST = unity_path("Items", "bottled_singularity.fbx")

# The Dragon Bazooka's authored model measures 1.3685 m and is worn at
# `holdSize` 1.25; this bottle is authored at its real size, so the wear factor
# is roughly 1. The number below is what the ladder's Consumable bracket would
# give if the wave-2 assets agent puts it there — see the build record.
CONSUMABLE_BRACKET = 0.50


def main():
    print("== Bottled singularity ==")
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe()
    print("  Consumable bracket on ItemScaleLadder would be %.2f m"
          % CONSUMABLE_BRACKET)


main()
