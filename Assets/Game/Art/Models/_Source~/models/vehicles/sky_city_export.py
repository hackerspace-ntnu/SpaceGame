"""Ship the sky city to Unity as one FBX for `SkyCityBuilder` to build from.

One file, not one per collection: the four collections in `sky_city.blend`
(`Structure`, `Town`, `Lift`, `Rig`) are parts of a single 125 m place, not a
contact sheet of separable props. The builder tells them apart by object name.

An export, not a generator — it never writes back to the `.blend`.

`fix_inverted` is deliberately **off**, and there is nothing for it to do:
`sky_city.py` asserts at build time that no object carries a negative-determinant
transform, because the port sail's mirror is baked into its own mesh data rather
than left as a scale. That assertion exists so this stays true. Turning
`fix_inverted` on here would be actively dangerous — this file shares mesh
datablocks between objects (43 meshes across 109 objects), which is the exact
shape of file on which it threw three of the forty nomad buildings up to 137 m.
See `nomad_settlement_export.py` and ArtPipeline.md.

No armature to keep (the model is static) and no empties to ship (nothing in
Unity reads a socket off it — the prefab's collision is authored in the builder
against the model's own constants).

    blender --background --python models/vehicles/sky_city_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)

from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "sky_city.blend")
DST = unity_path("Environment", "Structures", "sky_city.fbx")

print("sky city -> %s" % DST)
export(SRC, DST)
