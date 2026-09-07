# Foam blob — build record

The lump the foam gun leaves in the world: dabs at six a second, 24 live, each
swelling over 0.4 s into a 0.45 m blob that merges with its neighbours into one
mass you can stand on.
Design: [`docs/AI/systems/Artifacts/FoamGun.md`](../../../../../../docs/AI/systems/Artifacts/FoamGun.md).

Written as the decomposition was decided, not proposed for approval. It records
one reversal, and the reversal is the interesting part.

## What shipped

**One mesh: `Mesh_FoamBlob_Unit`, a 320-triangle unit sphere.** No variations,
no lobing, radius exactly 1.

## Why, and what it replaced

The first version of this file shipped three lumpy metaball surfaces — 4 to 6
inverse-square wells each, solved radially from the origin so the result was
star-convex, closed and could not self-intersect. The reasoning was the design's
hard requirement that overlapping blobs read as *one substance*: a lump built
from intersecting spheres has hard creases where the shells cross, and a hard
crease is the line the eye uses to count objects. Each blob carried a `core`
channel giving the crease depth so a shader could pool thickness in the valleys.

`Assets/Game/Art/Shaders/Artifacts/FoamSurface.shader` shipped in the same wave
and answers the question better than geometry can:

- It expects **a unit sphere in object space** and reads POSITION and NORMAL
  only. The real 0.45 m comes from the transform, and the 0.4 s growth *is* that
  transform's scale.
- Its surface detail is **world-space triplanar**, so neighbouring blobs share
  one bubble field and pick up where each other left off — continuous across
  blob boundaries in a way per-blob modelling cannot reach.
- It unions overlapping neighbours **analytically**, from `_FoamBlobs[32]`
  (world centre in `xyz`, world radius in `w`) uploaded by gameplay code.

That last point is what forced the geometry. The union is computed against
spheres of radius `w`; a mesh that departs from radius 1 puts its silhouette
where the analytic field is not, and the weld between two blobs grows a seam
around it. The lumps were **deleted rather than shipped alongside**, because a
second mesh that quietly violates the shader's contract is a trap for whoever
wires the prefab next.

**So variation now comes from the shader, not from this file**, and it is better
variation: the detail is world-space, so two blobs in different places do not
look alike, and no player ever sees the same lump twice. That is past what three
authored meshes would have managed. It is also the one place this model departs
from the `blender-model` skill's "three distinct variations minimum" — recorded
here rather than worked around.

## Reused from the library

`components/props/flask_kit.py` — `emit`, `marker`, `ramp`. No component
`.blend` was needed: the mesh is `bmesh.ops.create_icosphere`.

`_buildlib.Part` was deliberately not used. It merges scratch bmeshes and
forgets which vertex came from where, which is fine for a bevelled bracket and
wrong for a mesh whose per-vertex channel tables have to line up with it.

## Poly budget

320 triangles, and the subdivision number is a trap worth writing down:
`bmesh.ops.create_icosphere` counts the icosahedron itself as **subdivision 1**,
so `subdivisions=3` is two rounds of splitting. At 2 the blob comes out at 80
triangles and its own facets read as the silhouette; at 4 it costs 1280 for no
visible gain on a surface that is entirely shader.

24 live blobs per player with several players spraying is the first performance
question this artifact raises, and the design says so. A sphere whose entire
surface is a shader does not need vertices (`GDC-L1-PERF-0004`).

## The mesh is closed, and that matters

`FoamSurface.shader` is opaque with a `DepthOnly` pass, deliberately, so the
ink/outline post pass silhouettes the foam for free — an alpha-blended version
would silently lose the outline. That wants a closed manifold, which an
icosphere is.

## Marker

| Marker | Blender | What it is |
| --- | --- | --- |
| `Marker_EffectOrigin` | (0, 0, 0) | The sphere's centre: the dab's placement point, and the pivot its growth scales about |

It sits in `Coll_FoamBlob_Markers`, its own collection, so it is obviously the
file's rather than the mesh's. Shipped by `keep_empties=True`.

An origin at the *contact point* was considered and rejected: the blob would
then grow upward out of the floor rather than outward around the point it stuck
to, and the design explicitly wants the growth to push bodies out gently rather
than launch them.

## Shader channels

`FoamSurface.shader` reads **none of these**. They are carried to the shared
prop convention in `components/props/flask_kit.py` because they cost a few bytes
on a 162-vertex mesh, `channel_report()` measures them on every export, and the
next shader that wants a height ramp will not need a re-export.

| Channel | Where | Value |
| --- | --- | --- |
| `UV0` | `uv0` / TEXCOORD0 | Spherical unwrap, `u` azimuth and `v` elevation, both 0..1, seam at −X fixed per-face |
| `core` | `uv2`.x / TEXCOORD1.x, and `Col.r` | 1 everywhere — a sphere has no thin edge to dissolve at |
| `up` | `uv2`.y / TEXCOORD1.y, and `Col.g` | 0 at the south pole, 1 at the north |
| `lobe` | `Col.b` | A smooth per-azimuth random, for noise phase |

UV1 is the authoritative copy and the colour attribute the convenience one: a
second UV set crosses FBX and Unity as exact float2 with no colour management in
the path, where an 8-bit vertex colour may or may not be gamma-converted
depending on project colour space.

## Articulation

None, and none is possible: it grows (a scale) and it expires (a despawn).

## Verified

- Built dimensions measured off the file: **2.000 × 2.000 × 2.000 m** — a unit
  sphere, as the shader requires. **This is not a metre figure to be corrected.**
- FBX re-imported and checked: 162 vertices, `uv=['UVMap', 'Data']`,
  `col=[('Col', 'BYTE_COLOR', 'CORNER')]`, `Marker_EffectOrigin` present.

## Principles cited

`GDC-L1-PERF-0004` (budget the frame — 24 live objects per player is the stated
first question), `GDC-L1-CONTENT-0003` (naming and organisation conventions).
