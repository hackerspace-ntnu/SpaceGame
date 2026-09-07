"""Ship the storm cloud to Unity.

One FBX carrying **four meshes**: three cloud bodies and the single rain veil
they share. All at the origin, so the FBX root is not the prefab — the cloud
prefab takes one `Mesh_StormCloud_*` plus `Mesh_StormCloud_RainVolume`.

Everything below is `StormCloud.shader`'s object-space contract, which the
geometry already satisfies and the prefab must not break:

- **XZ radius 1, +Y up, origin on the cloud's base plane.** The bodies occupy
  `y = 0 .. 0.27–0.43`, belly lobes touching zero; the veil occupies `y = −1..0`.
  The shader shades `y >= 0` as cloud and `y < 0` as rain, so **nothing may be
  scaled or offset in a way that pushes cloud geometry below zero**.
- **The design's metres live in the transform.** 12 m radius and 15 m height are
  the prefab's scale and position, not the mesh's. A uniform scale of 12 hangs
  the rain 12 m; a `(12, 15, 12)` scale hangs it the design's 15 m and stretches
  the body 25% taller, which is invisible on vapour. Either is defensible and
  it is the assets agent's call — the geometry is normalised for both.
- **The veil must render with `Cull Off`.** It is a one-sided cylinder and the
  storm's victims are standing inside it. Back-face culled, the rain is drawn
  for everyone except the people it is raining on.

`keep_armature=False`: the churn is a shader, the drift is a transform.
`keep_empties=True` ships `Marker_EffectOrigin` and `Marker_BoltOrigin`, which
are not the same point — the belly lobes come down to exactly y = 0, so a bolt
born on that plane has its first pixels inside the cloud.

`channel_report()` measures the shader channels off the built file rather than
trusting the build log. `StormCloud.shader` reads POSITION only; the channels
are carried for whatever comes next.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/props/storm_cloud_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)
sys.path.insert(0, os.path.join(LIB, "components", "props"))

from _exportlib import describe, export, unity_path  # noqa: E402
from flask_kit import channel_report  # noqa: E402

SRC = os.path.join(HERE, "storm_cloud.blend")
DST = unity_path("Props", "storm_cloud.fbx")


def main():
    print("== Storm cloud ==")
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe()
    channel_report()


main()
