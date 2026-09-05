"""Ship the gauntlet flashlight to Unity.

Exports the whole model file, like `gauntlet_ruin_scanner_export.py`: a model
file holds exactly the objects that make up the model — the device alone, since
the bracer under it is worn separately — so `_exportlib.export` is the right tool and its flags stay in
one place.

No rig (`keep_armature=False`): nothing on this lamp articulates.

`keep_empties=True` is the flag this export cannot lose. The prefab hangs the
`Flashlight` lamp — a URP spot plus its beam volume — on the `Emitter` empty,
and an FBX without it leaves the torch shining out of the wrist bone instead
of the horn.

`describe(worn_scale=1.0)` because the family is modelled at true suit scale
against the bracer's deck and `GauntletFit` wears it at 1 — the Unity-local figures it prints are
the ones to type into the prefab.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/gauntlet_flashlight_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "gauntlet_flashlight.blend")
DST = unity_path("Items", "gauntlet_flashlight.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe(worn_scale=1.0)


main()
