"""Ship the foam blob to Unity.

One FBX, one mesh: `Mesh_FoamBlob_Unit`, a **unit sphere**.

That radius is the contract, not a placeholder.
`Assets/Game/Art/Shaders/Artifacts/FoamSurface.shader` takes the real 0.45 m
from the transform and unions overlapping neighbours analytically against
spheres of the radius in `_FoamBlobs[].w`. **Do not scale the mesh in the FBX
importer and do not bake a radius into it** — a mesh that departs from radius 1
puts its silhouette where the analytic field is not, and the weld between two
blobs grows a seam.

`keep_armature=False`: nothing on a lump of foam articulates. `keep_empties=True`
ships `Marker_EffectOrigin` — the sphere's centre, which is the dab's placement
point and the pivot its 0.4 s growth scales about.

`channel_report()` is the point of running this rather than exporting by hand.
`FoamSurface.shader` reads POSITION and NORMAL only, so the UV sets and the
colour attribute are carried for the next shader rather than this one; printing
them here is how a silently lost channel gets noticed before someone needs it.
Measure, do not trust the log.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/props/foam_blob_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)
sys.path.insert(0, os.path.join(LIB, "components", "props"))

from _exportlib import describe, export, unity_path  # noqa: E402
from flask_kit import channel_report  # noqa: E402

SRC = os.path.join(HERE, "foam_blob.blend")
DST = unity_path("Props", "foam_blob.fbx")


def main():
    print("== Foam blob ==")
    export(SRC, DST, keep_armature=False, keep_empties=True)
    describe()
    channel_report()


main()
