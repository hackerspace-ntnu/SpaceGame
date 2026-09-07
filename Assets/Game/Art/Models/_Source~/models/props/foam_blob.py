"""Foam blob — the lump the foam gun leaves in the world.

Design: `docs/AI/systems/Artifacts/FoamGun.md`. Holding Use lays dabs along the
aim ray at six a second with a budget of 24 live; each dab lands, swells over
0.4 s into a 0.45 m radius blob, and merges with its neighbours into one lumpy
mass you can stand on. On the ground it is terrain for 60 s; on a body it is
`Foamed` for 10 s.

**This is a shell for a shader, not a detailed mesh.** The foam surface — the
off-white translucency, the rough normal, the way a mass of blobs reads as one
substance — is the shader's job. The geometry's job is to give that shader
something with sensible topology, a clean unwrap and a silhouette that reads,
and then get out of the way.

## Why a solved implicit surface rather than a cluster of spheres

The design's hard requirement is that overlapping blobs read as *one substance*.
Geometry can lose that fight before the shader starts: a lump built as five
intersecting spheres has hard creases where the shells cross, and a hard crease
is exactly the line the eye uses to count objects. So each blob is one closed
surface solved against a metaball field of four to six centres — the crease
between two of its own lobes is a smooth valley, never an intersection.

Solving *radially* from the blob's own origin keeps the surface star-convex,
which buys three things for free: it cannot self-intersect, the icosphere's even
triangle distribution survives the displacement, and the spherical unwrap below
stays injective.

## Why one mesh at three variations rather than 24 unique blobs

24 live objects per player is the first performance question this artifact
raises (`GDC-L1-PERF-0004`), so the geometry is 320 triangles and three meshes
that a spawner picks from at a random yaw. Three lumps at four rotations each
is twelve silhouettes, which is well past the point where a player stops seeing
repeats, at a twelfth of the cost of authoring them.

**The FBX root is not the prefab.** All three variations sit at the origin, as
every component file in this library does, so exported whole they arrive as one
interpenetrating lump. Unity reads them as three separate Mesh assets inside
`foam_blob.fbx`; the prefab takes one.

## Shader channels

`UV0` is a spherical unwrap from the blob's own centre: `u` is the azimuth, `v`
the elevation, both 0..1, seam at −X and fixed per-face so no triangle samples
the map backwards. It is continuous and low-distortion away from the poles,
which is what a tiling rough-normal detail map wants.

`UV1` and the `Col` attribute carry the shared prop convention from
`components/props/flask_kit.py`:

  `core` (UV1.x, Col.r)  0 on a lobe crown, 1 in the deepest crease between
                         lobes. **This is the channel that sells the merge** —
                         a crease is where foam pools, so it is where the shader
                         should go thicker, darker and less translucent.
  `up`   (UV1.y, Col.g)  0 at the bottom of the lump, 1 at the top.
  `lobe` (Col.b)         a smooth per-lobe random, so neighbouring lobes can
                         carry different noise phase without a texture.

Origin is the **blob centre**, on purpose: the dab grows from nothing to full
size, and a uniform scale about the centre is that growth. An origin at the
contact point would make the blob grow upward out of the floor instead of
outward around the point it stuck to.

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
from mathutils import Vector

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "props"))

from _buildlib import *  # noqa: E402,F403
from flask_kit import emit, marker, ramp  # noqa: E402

# Nothing in the palette is foam, and nothing was added for it: the surface is
# a shader, and the material slot here exists so the mesh is not untextured in
# Blender and so Unity has something to override. Arctic is the palette's
# off-white and the design asks for off-white.
MATS = ["Mat_Paint_White_Arctic"]

BLOB_R = 0.45           # the design's blob radius; the mesh's widest half-extent
SUBDIV = 3              # 320 triangles — see the performance note above.
                        # `create_icosphere` counts the icosahedron itself as
                        # subdivision 1, so this is two rounds of splitting,
                        # not three. At 2 the lump comes out at 80 triangles
                        # and its own facets read as the silhouette.
THRESHOLD = 1.0         # the field value the surface sits on


def field(p, centres):
    """Sum of inverse-square wells. Smooth everywhere off the centres."""
    total = 0.0
    for c, w, _ in centres:
        total += w / max((p - c).length_squared, 1e-6)
    return total


def surface_radius(direction, centres):
    """Distance from the origin to the iso-surface along `direction`.

    Bisection rather than a closed form: the field is a sum of wells with no
    analytic inverse, and 40 halvings of a 4 m bracket lands inside a micron.
    """
    lo, hi = 1e-4, 4.0
    for _ in range(40):
        mid = 0.5 * (lo + hi)
        if field(direction * mid, centres) > THRESHOLD:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def lobe_mix(p, centres):
    """The field-weighted average of the centres' random tags at `p`.

    Weighted rather than nearest, so the value is continuous: a nearest-centre
    lookup puts a hard seam down the middle of every valley, which is the one
    place this mesh must not have a line in it.
    """
    num = den = 0.0
    for c, w, tag in centres:
        k = w / max((p - c).length_squared, 1e-6)
        num += k * tag
        den += k
    return num / den if den else 0.0


def blob(coll, mats, name, centres, squash):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=SUBDIV, radius=1.0)
    bm.verts.ensure_lookup_table()

    # Solve every vertex out onto the iso-surface, keeping the field-space
    # position so the lobe tag can be sampled where the wells actually are.
    solved = []
    for v in bm.verts:
        d = v.co.normalized()
        r = surface_radius(d, centres)
        v.co = d * r
        solved.append((r, lobe_mix(v.co, centres)))

    # Silhouette variation comes first from the centre layout and second from
    # one anisotropic squash — a splat is not a dollop at another size.
    bmesh.ops.scale(bm, vec=Vector(squash), verts=bm.verts)
    # Normalised last, so every variation fills the same 0.9 m box however its
    # centres were laid out and the spawner never has to know which it got.
    half = max(max(abs(c) for c in v.co) for v in bm.verts)
    bmesh.ops.scale(bm, vec=Vector((BLOB_R / half,) * 3), verts=bm.verts)

    radii = [r for r, _ in solved]
    rlo, rhi = min(radii), max(radii)
    zlo = min(v.co.z for v in bm.verts)
    zhi = max(v.co.z for v in bm.verts)

    uv0, data = [], []
    for v, (r, tag) in zip(bm.verts, solved):
        p = v.co
        length = max(p.length, 1e-9)
        uv0.append((math.atan2(p.y, p.x) / (2 * math.pi) + 0.5,
                    math.asin(max(-1.0, min(1.0, p.z / length))) / math.pi + 0.5))
        # `core` is inverted radius: the smallest solved radius is the deepest
        # point of a valley between two lobes, and the largest is a crown.
        data.append((1.0 - ramp(r, rlo, rhi), ramp(p.z, zlo, zhi), tag))

    for f in bm.faces:
        f.smooth = True
    return emit(name, bm, coll, mats, uv0=uv0, data=data, wrap_u=True)


def centres(spec, seed):
    """Attach a stable random tag to each centre, so `lobe` is reproducible."""
    rng = random.Random(seed)
    return [(Vector(c), w, rng.random()) for c, w in spec]


# --------------------------------------------------------------------------
# Three lumps. They differ in where the wells sit and in one squash, which is
# to say in silhouette — a dollop, a splat and a column are three shapes, not
# one shape at three sizes.
# --------------------------------------------------------------------------

# An isolated well of weight w meets the threshold at radius sqrt(w), so the
# weights below are lobe radii squared and the offsets are how far apart those
# lobes sit. Getting that relationship wrong is the failure mode this file
# already hit once: five wells of weight ~0.7 (radius 0.84) offset by 0.3 sit
# entirely inside one another and solve to a single smooth egg — a shape with no
# creases in it, and therefore no merge for the shader to sell. A lobe has to
# reach further out than its neighbours do.

DOLLOP = [((0.00, 0.00, 0.00), 0.20), ((0.42, 0.14, 0.16), 0.13),
          ((-0.30, 0.36, -0.08), 0.12), ((0.07, -0.45, 0.10), 0.11),
          ((-0.14, -0.11, 0.42), 0.10)]

SPLAT = [((0.00, 0.00, -0.05), 0.18), ((0.60, 0.08, -0.12), 0.13),
         ((-0.52, 0.28, -0.10), 0.12), ((0.14, -0.58, -0.08), 0.12),
         ((-0.26, -0.32, 0.08), 0.09), ((0.34, 0.44, 0.02), 0.09)]

# Piled rather than stacked. A genuinely column-shaped dab was tried and read
# as a rock rather than as foam: the design's dab swells into a *sphere*, so the
# three variations differ in how they lobe, not in aspect ratio. Only the splat,
# which is what a dab landing flat on the ground does, departs from round.
COLUMN = [((0.00, 0.00, -0.32), 0.16), ((0.08, -0.10, 0.02), 0.18),
          ((-0.13, 0.08, 0.34), 0.13), ((0.18, 0.15, 0.60), 0.09)]

LUMPS = (("Dollop", DOLLOP, (1.00, 1.00, 1.00), 11),
         ("Splat", SPLAT, (1.00, 1.00, 0.62), 23),
         ("Column", COLUMN, (0.95, 0.95, 1.08), 37))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for tag, spec, squash, seed in LUMPS:
        blob(collection("Coll_FoamBlob_%s" % tag), mats,
             "Mesh_FoamBlob_%s" % tag, centres(spec, seed), squash)

    # One marker for the file: all three lumps share the origin, and the origin
    # is where the dab was placed and what the growth scales about. One marker
    # serves all three because they share that origin exactly; it sits in its
    # own collection so it is obvious it belongs to the file rather than to any
    # one lump.
    marker(collection("Coll_FoamBlob_Markers"), "Marker_EffectOrigin",
           (0.0, 0.0, 0.0), size=0.08)

    report()
    save(out)


if __name__ == "__main__":
    main()
