"""Ship the gauntlet ruin scanner to Unity.

Exports the whole model file, like `leash_gauntlet_export.py`: a model file
holds exactly the objects that make up the model — the device alone, since the
bracer under it is worn separately — so `_exportlib.export` is the right tool and its flags stay in one
place.

No rig (`keep_armature=False`): nothing on this device articulates. The
sight frame has its origin on its hinge pin so it could be folded later, but
nothing in the game folds it, and the cone of light is spawned by
`RuinScannerPulse` out in the world, rooted on the `Emitter` empty.

`keep_empties=True` is the one flag this export needs that `gauntlet_base_export`
does not: the prefab's `muzzle` field points at `Emitter`, and an FBX without the
empty leaves the cone starting from the prefab root at the wrist.

`describe(worn_scale=1.0)` because the family is modelled at true suit scale
against the bracer's deck and `GauntletFit` wears it at 1 — the Unity-local figures it prints are
the ones to type into the prefab.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/gauntlet_ruin_scanner_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "gauntlet_ruin_scanner.blend")
DST = unity_path("Items", "gauntlet_ruin_scanner.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe(worn_scale=1.0)


main()
