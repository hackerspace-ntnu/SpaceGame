"""Frozen statue base — the ice a `Frozen` body is planted in.

Design: `docs/AI/systems/Artifacts/CryoSprayer.md` and
`docs/AI/systems/Artifacts/StatusEffects.md`. Spray a creature or a player for
1.5 s and it freezes solid for 10 s, pose held exactly as it was; a hard hit
while frozen shatters it and kills it.

**This is a shell for a shader, not a detailed mesh**, and it is deliberately
*only the base*.

## Why there is no shell around the body

The design's hard requirement is that "the original silhouette [is] intact so
you can tell what you froze". Geometry cannot deliver that and an encasing
capsule actively destroys it: any shell wide enough to clear a Vrescal's legs is
wide enough to erase a player's, and the moment the outline belongs to the ice
rather than to the creature the artifact stops telling the player anything
(`GDC-L1-UX-0003`). The design already assigns encasement to the *material* —
"pale blue, faintly translucent, with the original silhouette intact" — which is
the statue shader on the body's own skinned mesh, and that is the only place it
can live without a shape.

So the world prop is what the design's other half asks for: the ice the statue
*stands in*. It reads from the feet up, it never crosses the horizon of the
creature above it, and it is the same object whatever is standing in it. The
tallest shard measures 0.47 m at nominal scale — below the knee of a 1.8 m
player — so the recognisable part of the silhouette is untouched by
construction, not by tuning.

`Assets/Game/Art/Shaders/Artifacts/FrozenStatue.shader` settled the same
question from the other side and independently: it runs on **any** mesh, skinned
or not, reads POSITION and NORMAL only, and does its detail in object-space
triplanar — so it shades a creature's own skinned body correctly with whatever
UVs that body happens to have. The encasement is therefore the frozen body
wearing that material, and this prop is the ice at its feet. Nothing here has to
match a shape.

## Scale

Authored at a **1.0 m nominal footprint** and scaled at runtime to the frozen
body's own footprint. The three variations therefore differ in *shape*, never in
size — a size variation would fight the runtime scale and read as an authoring
mistake.

## Shader channels

`UV0` wraps `u` around and runs `v` up in **metres at nominal scale**, so a
frost detail map tiles at world density: on the plinth, `u` is the azimuth about
the prop's axis; on a shard, `u` runs around that shard's own four faces. Both
seams are fixed per-face, so no triangle samples the map backwards.

`FrozenStatue.shader` reads none of the channels below — they are carried to
the shared prop convention from `components/props/flask_kit.py` because they are
free at 96 to 256 vertices and the next shader may want them:

  `core` (UV1.x, Col.r)  1 in solid ice, 0 where the ice thins to an edge — the
                         plinth's outer rim and every shard tip. This is the
                         channel a translucency or refraction term should drive:
                         thin ice passes light, a block of it does not.
  `up`   (UV1.y, Col.g)  0 at the ground plane, 1 at the tallest shard tip of
                         *this variation*, so a rime gradient reaches the top of
                         whichever base it is drawn on.
  `lobe` (Col.b)         constant per shard, smooth around the plinth. Enough to
                         give neighbouring shards different noise phase without
                         a texture, which is what stops nine spikes reading as
                         nine copies.

Origin is the **ground plane on the axis**, which is where the frozen body's
feet are and where the effect emits from.

No armature: nothing here moves. It appears, it holds, it goes.

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

# Index 0 is the ice itself. `Mat_Glass_Canopy_Tinted` is the palette's only
# glazing and at #AEC4CC roughness 0.05 it is already the pale cold blue the
# design asks for — nothing was added, because a second near-identical glass is
# exactly the drift the palette guards against.
ICE, RIME = range(2)
MATS = ["Mat_Glass_Canopy_Tinted",     # the ice
        "Mat_Paint_White_Arctic"]      # rime frost crusting the plinth top

FOOTPRINT = 1.0         # nominal diameter; the runtime scales to the body
R0 = FOOTPRINT / 2.0
AZ = 32                 # azimuth segments around the plinth


def circle_noise(seed, k=7):
    """A smooth periodic 0..1 noise around the circle.

    Cosine-interpolated between `k` random samples rather than a hash per
    vertex: the plinth's rim has to be irregular but *continuous*, and per-vertex
    randomness gives a sawtooth rim that reads as a modelling error.
    """
    rng = random.Random(seed)
    vals = [rng.random() for _ in range(k)]

    def sample(theta):
        x = ((theta / (2 * math.pi)) % 1.0) * k
        i = int(x)
        t = 0.5 - 0.5 * math.cos(math.pi * (x - i))
        return vals[i % k] + (vals[(i + 1) % k] - vals[i % k]) * t

    return sample


def plinth(coll, mats, name, spec, seed, zmax):
    """The block of ice at the feet. `zmax` is the tallest shard tip."""
    noise = circle_noise(seed, spec["lobes"])
    base, amp = spec["base"], spec["amp"]

    bm = bmesh.new()
    chan = {}
    rings = []
    for z, k in spec["profile"]:
        row = []
        for i in range(AZ):
            theta = 2 * math.pi * i / AZ
            radius = R0 * (base + amp * noise(theta)) * k
            v = bm.verts.new((radius * math.cos(theta),
                              radius * math.sin(theta), z))
            # `core` falls only across the outer quarter: a plinth is a block of
            # ice, and calling three quarters of it "thin" would wash the whole
            # prop out.
            chan[v] = ((theta / (2 * math.pi), z),
                       (1.0 - ramp(k, 0.75, 1.0), z, noise(theta)))
            row.append(v)
        rings.append(row)

    crown = []
    for n, (a, b) in enumerate(zip(rings, rings[1:])):
        for i in range(AZ):
            j = (i + 1) % AZ
            face = bm.faces.new((a[i], a[j], b[j], b[i]))
            if n == len(rings) - 2:
                crown.append(face)

    # Fan caps rather than 32-gons, top and bottom. An n-gon survives export and
    # then triangulates however Unity feels like, which on a domed cap is a
    # visible star of shading seams.
    for row, z, top in ((rings[0], spec["profile"][0][0], False),
                        (rings[-1], spec["profile"][-1][0], True)):
        hub = bm.verts.new((0.0, 0.0, z))
        chan[hub] = ((0.5, z), (1.0, z, noise(0.0)))
        for i in range(AZ):
            j = (i + 1) % AZ
            tri = (hub, row[i], row[j]) if top else (hub, row[j], row[i])
            face = bm.faces.new(tri)
            if top:
                crown.append(face)

    for f in bm.faces:
        f.material_index = ICE
    # Rime, opaque white, on the crown alone. A block of ice reads as ice
    # because part of it is *not* transparent — an all-glass plinth reads as a
    # rendering error rather than as frost.
    for f in crown:
        f.material_index = RIME

    return finish(bm, chan, name, coll, mats, zmax, mat=None)


def spike(bm, chan, origin, axis, length, width, twist, tag):
    """One tapered four-sided shard, base to near-point."""
    axis = Vector(axis).normalized()
    side = axis.cross(Vector((0.0, 0.0, 1.0)))
    if side.length < 1e-4:
        side = Vector((1.0, 0.0, 0.0))
    side.normalize()
    other = axis.cross(side).normalized()

    rings = []
    for t, k in ((0.0, 1.0), (0.45, 0.55), (0.85, 0.22), (1.0, 0.04)):
        row = []
        for c in range(4):
            a = twist + math.pi / 4.0 + c * math.pi / 2.0
            p = (Vector(origin) + axis * (length * t)
                 + (side * math.cos(a) + other * math.sin(a)) * (width * k))
            v = bm.verts.new(p)
            chan[v] = ((c / 4.0, length * t),
                       (1.0 - t, p.z, tag))
            row.append(v)
        rings.append(row)

    for a, b in zip(rings, rings[1:]):
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(tuple(reversed(rings[0])))
    bm.faces.new(tuple(rings[-1]))


def shards(coll, mats, name, spec, seed):
    """The ring of shards. Returns the tallest tip, which is the `up` ceiling.

    Built before the plinth for exactly that reason. `up` is documented as
    reaching 1.0 at the tallest shard tip of *this* variation, and the tip
    height is a draw from a seeded RNG, not a number that can be written down
    beside the spec: hand-guessing it left every base's `up` topping out around
    0.75, which is a gradient that silently never finishes.
    """
    noise = circle_noise(seed, spec["lobes"])
    base, amp = spec["base"], spec["amp"]
    rng = random.Random(seed + 1)

    bm = bmesh.new()
    chan = {}
    for j in range(spec["shards"]):
        theta = 2 * math.pi * (j + rng.uniform(-0.28, 0.28)) / spec["shards"]
        radius = R0 * (base + amp * noise(theta)) * spec["seat_r"]
        lean = math.radians(rng.uniform(*spec["lean"]))
        axis = (math.cos(theta) * math.sin(lean),
                math.sin(theta) * math.sin(lean), math.cos(lean))
        spike(bm, chan,
              (radius * math.cos(theta), radius * math.sin(theta),
               spec["seat_z"]),
              axis, rng.uniform(*spec["length"]), rng.uniform(*spec["width"]),
              rng.uniform(0.0, math.pi / 2.0), rng.random())

    zmax = max(v.co.z for v in bm.verts)
    finish(bm, chan, name, coll, mats, zmax, mat=ICE)
    return zmax


def finish(bm, chan, name, coll, mats, zmax, mat=ICE):
    """Order the channel tables by vertex index and hand the mesh to `emit`.

    The channel table carries a raw world height where `up` belongs; it is
    normalised here, once, against the height the caller measured. `mat=None`
    leaves the material indices the caller already stamped alone.
    """
    bm.verts.index_update()
    bm.verts.ensure_lookup_table()
    if mat is not None:
        for f in bm.faces:
            f.material_index = mat
    uv0 = [chan[v][0] for v in bm.verts]
    data = [(c[0], ramp(c[1], 0.0, zmax), c[2]) for c in
            (chan[v][1] for v in bm.verts)]
    return emit(name, bm, coll, mats, uv0=uv0, data=data, wrap_u=True)


# --------------------------------------------------------------------------
# Three bases. They differ in silhouette — a ring of spires, a low crust and a
# broken slab are three shapes, and only the first is the obvious one.
# --------------------------------------------------------------------------

SPIRE = {                       # the default: a body planted in a spiked collar
    "profile": [(0.000, 0.94), (0.030, 1.00), (0.075, 0.93),
                (0.115, 0.72), (0.135, 0.40)],
    "base": 0.86, "amp": 0.14, "lobes": 7,
    "shards": 9, "seat_r": 0.80, "seat_z": 0.062,
    "lean": (14.0, 34.0), "length": (0.30, 0.46), "width": (0.055, 0.085),
}

CRUST = {                       # ice that spread instead of climbing
    "profile": [(0.000, 0.96), (0.025, 1.00), (0.055, 0.96),
                (0.085, 0.80), (0.095, 0.45)],
    "base": 0.92, "amp": 0.16, "lobes": 9,
    "shards": 16, "seat_r": 0.86, "seat_z": 0.048,
    "lean": (38.0, 62.0), "length": (0.10, 0.20), "width": (0.040, 0.070),
}

SHATTERED = {                   # already cracking: the thaw, and the kill
    "profile": [(0.000, 0.92), (0.022, 1.00), (0.060, 0.88),
                (0.088, 0.58), (0.100, 0.28)],
    "base": 0.62, "amp": 0.44, "lobes": 4,
    "shards": 6, "seat_r": 0.74, "seat_z": 0.052,
    "lean": (30.0, 58.0), "length": (0.14, 0.28), "width": (0.070, 0.110),
}

BASES = (("Spire", SPIRE, 101), ("Crust", CRUST, 211),
         ("Shattered", SHATTERED, 307))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for tag, spec, seed in BASES:
        coll = collection("Coll_FrozenBase_%s" % tag)
        # Shards first: they measure the height the plinth's `up` ramp shares.
        zmax = shards(coll, mats, "Mesh_FrozenBase_%s_Shards" % tag, spec, seed)
        plinth(coll, mats, "Mesh_FrozenBase_%s_Plinth" % tag, spec, seed, zmax)

    # One marker for the file: every base shares the ground plane on the axis,
    # which is where the body stands and where the freeze effect emits from.
    marker(collection("Coll_FrozenBase_Markers"), "Marker_EffectOrigin",
           (0.0, 0.0, 0.0), size=0.12)

    report()
    save(out)


if __name__ == "__main__":
    main()
