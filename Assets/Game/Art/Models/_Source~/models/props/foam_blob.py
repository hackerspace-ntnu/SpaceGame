"""Foam blob — the lump the foam gun leaves in the world.

Design: `docs/AI/systems/Artifacts/FoamGun.md`. Holding Use lays dabs along the
aim ray at six a second with a budget of 24 live; each dab lands, swells over
0.4 s into a 0.45 m radius blob, and merges with its neighbours into one lumpy
mass you can stand on. On the ground it is terrain for 60 s; on a body it is
`Foamed` for 10 s.

**This is a shell for a shader, not a detailed mesh**, and the shader owns more
of it than usual.

## The contract, and why the mesh is a plain sphere

`Assets/Game/Art/Shaders/Artifacts/FoamSurface.shader` reads POSITION and NORMAL
only, expects **a unit sphere in object space**, and takes the real 0.45 m radius
from the transform — growth over 0.4 s is that transform's scale. Its surface
detail is world-space triplanar, so neighbouring blobs share one bubble field
and pick up where each other left off, and it unions overlapping neighbours
analytically from `_FoamBlobs[32]` (world centre in `xyz`, world radius in `w`).

That last part is what decides the geometry. The union is computed against
*spheres of radius w*; a mesh that departs from radius 1 puts its silhouette
somewhere the analytic field is not, and the weld reads as a lump with a seam
around it. So the mesh is exactly a unit sphere, and **the radius must not be
baked in**.

This file first shipped three lumpy metaball surfaces solved against four to six
wells — real geometric lobing, on the reasoning that a mass built from
intersecting spheres has hard creases and a hard crease is what the eye counts
objects by. The shader answers that question better than geometry can: its union
is smooth by construction and its field is continuous across blob boundaries,
which no amount of per-blob modelling achieves. The lumps were dropped rather
than shipped alongside, because a second mesh that violates the shader's
contract is a trap for whoever wires the prefab next.

**Variation therefore comes from the shader, not from this file.** One mesh,
world-space detail: two blobs in different places do not look the same, which is
past what three authored variations could have managed.

The shader is opaque with a `DepthOnly` pass so the ink/outline post pass
silhouettes the foam for free. This mesh is closed and manifold, which is what
that needs.

## Poly budget

320 triangles. 24 live blobs per player, several players spraying, is the first
performance question this artifact raises (`GDC-L1-PERF-0004`), and the answer
is that a sphere whose entire surface is a shader does not need vertices.

## Shader channels

Present, unread by `FoamSurface.shader`, and kept because they are free and the
next shader may want them. They follow the shared prop convention from
`components/props/flask_kit.py`:

  `UV0`                  spherical unwrap: `u` azimuth, `v` elevation, both
                         0..1, seam fixed per-face.
  `core` (UV1.x, Col.r)  1 everywhere. A sphere has no thin edge to dissolve at.
  `up`   (UV1.y, Col.g)  0 at the south pole, 1 at the north.
  `lobe` (Col.b)         a smooth per-azimuth random, for noise phase.

Origin is the **sphere's centre**, which is both the dab's placement point and
the pivot its growth scales about.

No armature: nothing on a lump of foam articulates. It grows, which is a scale,
and it expires, which is a despawn.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import random
import sys

import bmesh

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "props"))

from _buildlib import *  # noqa: E402,F403
from flask_kit import emit, marker, ramp  # noqa: E402

# Nothing in the palette is foam, and nothing was added for it: the surface is
# `Mat_FoamSurface.mat`, which the wave-2 assets agent assigns. The slot here
# exists so the mesh is not untextured in Blender. Arctic is the palette's
# off-white and the design asks for off-white.
MATS = ["Mat_Paint_White_Arctic"]

# Radius 1, not the design's 0.45 m. FoamSurface.shader takes the world radius
# from the transform and from `_FoamBlobs[].w`; baking 0.45 into the mesh would
# put the silhouette where the analytic union is not.
UNIT = 1.0
SUBDIV = 3              # 320 triangles. `create_icosphere` counts the
                        # icosahedron itself as subdivision 1, so this is two
                        # rounds of splitting: at 2 the facets read as the
                        # silhouette, at 4 it costs 1280 for no visible gain.


def circle_noise(seed, k=9):
    """A smooth periodic 0..1 noise around the circle, for the `lobe` channel."""
    rng = random.Random(seed)
    vals = [rng.random() for _ in range(k)]

    def sample(theta):
        x = ((theta / (2 * math.pi)) % 1.0) * k
        i = int(x)
        t = 0.5 - 0.5 * math.cos(math.pi * (x - i))
        return vals[i % k] + (vals[(i + 1) % k] - vals[i % k]) * t

    return sample


def blob(coll, mats, name, seed):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=SUBDIV, radius=UNIT)
    bm.verts.ensure_lookup_table()
    phase = circle_noise(seed)

    uv0, data = [], []
    for v in bm.verts:
        p = v.co
        theta = math.atan2(p.y, p.x)
        length = max(p.length, 1e-9)
        uv0.append((theta / (2 * math.pi) + 0.5,
                    math.asin(max(-1.0, min(1.0, p.z / length))) / math.pi + 0.5))
        data.append((1.0, ramp(p.z, -UNIT, UNIT), phase(theta)))

    for f in bm.faces:
        f.smooth = True
    return emit(name, bm, coll, mats, uv0=uv0, data=data, wrap_u=True)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    blob(collection("Coll_FoamBlob"), mats, "Mesh_FoamBlob_Unit", 11)

    # The sphere's centre: where the dab was placed, and what the growth scales
    # about. In its own collection so it is obviously the file's, not the mesh's.
    marker(collection("Coll_FoamBlob_Markers"), "Marker_EffectOrigin",
           (0.0, 0.0, 0.0), size=0.2)

    report()
    save(out)


if __name__ == "__main__":
    main()
