"""Storm cloud — the thing a storm flask leaves parked over the ground.

Design: `docs/AI/systems/Artifacts/StormFlask.md`. A cloud forms 15 m above the
aimed point and stays 30 s. Under it, rain: fires go out, ground turns `Wet`.
Every 2.5 s it picks the tallest body beneath it and hits it with a bolt. The
design calls the cloud "the real art asset — a small, flat, angry disc of vapour
with rain streaking out of it, readable from the ground at 50 m so nobody
wanders under one by accident."

**This is a shell for a shader, not a detailed mesh.** The vapour, the churn and
the rain streaks are the shader's job. This file's job is the silhouette that
does the warning, in the object space the shader insists on.

## The contract — and it is entirely about object space

`Assets/Game/Art/Shaders/Artifacts/StormCloud.shader` reads POSITION only and
works in **object space, centred on the origin, XZ radius 1, +Y up** (Unity's
axes). Geometry at `y >= 0` is shaded as the cloud body; geometry at `y < 0` is
shaded as the rain veil, hanging to `y = -1`.

`_exportlib` maps Blender `(x, y, z)` to Unity `(-x, z, -y)`, so in the frame
this file is authored in that reads:

  **Blender +Z is Unity +Y.** XY radius 1, cloud body at `z >= 0`, rain veil in
  `z = -1 .. 0`.

Two consequences that are not obvious and that the first version of this file
got wrong in both directions:

- **The whole cloud body sits at or above z = 0**, belly included. An earlier
  version put the origin at the underside *centre* and let the lumpy belly dip
  to −1.67 m, which under this contract is not a belly at all — it is a metre
  and a half of cloud shaded as rain. The origin is therefore the **base plane
  of the body**: the lowest point of the deepest belly lobe touches z = 0.
- **Nothing is authored at metres.** The design's 12 m radius and 15 m height
  live in the prefab's transform, not here.

Cloud and veil may be one mesh or two; they are two objects here, sharing the
one material, so the veil can be turned off on its own. Each obeys the contract
by itself — a cloud body has nothing below 0 and simply gets no rain, a veil has
nothing above 0 and simply is not a cloud.

## The transform the prefab needs, and the one number that does not fit

At XZ radius 1 → 12 m, a uniform scale of 12 puts the veil's foot 12 m below the
cloud, not the design's 15. Either the prefab scales uniformly by 12 and the
storm hangs 12 m (and the cloud sits 12 m up), or it scales `(12, 15, 12)` and
the cloud body stretches 25% taller — invisible on vapour, and it puts the rain
exactly on the aimed point. **That is the assets agent's call, not this file's**;
the geometry supports either because it is normalised.

## What the model must get right

The cloud is a hazard telegraph before it is scenery. A player has to read
"there is a storm over there, and it is over *there* and not *here*" from 50 m
away, which makes the readable extent a gameplay property rather than a
decoration (`GDC-L1-UX-0003`, `GDC-L1-ANIM-0003`).

- **The rim is the gameplay radius.** It sits at 0.72–1.00 against the shader's
  1.0, so what the player sees is inside where the bolts fall. A cloud drawn
  generously beyond its own trigger volume kills people standing outside it,
  which reads as the game cheating.
- **It is flat, not fluffy.** The crown is 0.21–0.34 of the radius. A cumulus of
  proportionate height would be the biggest object on the horizon and would read
  as weather rather than as a thing someone threw.

## Shader channels

Present, unread by `StormCloud.shader`, and kept because they are free. They
follow the shared prop convention from `components/props/flask_kit.py`:

  `UV0`                  body: top-down planar projection normalised to the
                         diameter, centre (0.5, 0.5) — the natural domain for a
                         coverage mask. Veil: cylindrical, `u` around, `v` 0 at
                         the foot and 1 at the cloud, seam fixed per-face.
  `core` (UV1.x, Col.r)  body: 1 on the axis, 0 at the ragged rim. Veil: 1
                         everywhere; a curtain has no rim to dissolve at.
  `up`   (UV1.y, Col.g)  body: 0 at the base plane, 1 at the crown. Veil: 0 at
                         the foot, 1 at the cloud.
  `lobe` (Col.b)         body: a smooth per-azimuth random, for churn phase.
                         Veil: per-column rain density, taken from the cloud's
                         own rim noise.

## Variations

Three cloud bodies differing in crown height, belly depth and rim raggedness,
and **one** veil they share: rain hanging off a disc looks the same whatever the
disc's crown is doing, and three copies would be three chances to let them
drift. The variations sit at the origin as every variation file in this library
does — **the FBX root is not the prefab**; Unity reads them as separate Mesh
assets and the prefab takes one body plus the veil.

No armature: the churn is a shader and the drift is a transform.

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

# Two greys, both already in the palette. The runtime surface is
# `Mat_StormCloud.mat`, which the wave-2 assets agent assigns; these exist so
# the mesh is not untextured in Blender and so the belly and the crown are
# distinguishable while it is being looked at. Slate is the near-black underside
# a bolt comes out of, Panel_Grey the lighter crown. Nothing was added — a cloud
# is a shader, and inventing two more greys is exactly the palette drift the
# library guards against.
BELLY, CROWN = range(2)
MATS = ["Mat_Neutral_Slate_Dark",      # the underside, seen from below
        "Mat_Neutral_Panel_Grey"]      # the crown, seen from a ridge

RADIUS = 1.0            # StormCloud.shader's object space: XZ radius 1
DROP = 1.0              # the veil's foot, at Unity y = -1
AZ = 48                 # azimuth segments — 24 m across in the world, at 50 m
# The innermost band is 0.14, not 0. At 0 the whole ring collapses onto the
# axis and the lump term gives 48 coincident vertices at different heights,
# which builds without complaint and shades like a shattered cone.
BANDS = (0.14, 0.30, 0.55, 0.75, 0.90, 1.0)


def circle_noise(seed, k=7):
    """A smooth periodic 0..1 noise around the circle.

    Same generator as `frozen_statue_base.py`, and for the same reason: a rim
    has to be irregular but continuous. Per-vertex randomness gives a sawtooth
    edge that reads as a broken mesh rather than as vapour.
    """
    rng = random.Random(seed)
    vals = [rng.random() for _ in range(k)]

    def sample(theta):
        x = ((theta / (2 * math.pi)) % 1.0) * k
        i = int(x)
        t = 0.5 - 0.5 * math.cos(math.pi * (x - i))
        return vals[i % k] + (vals[(i + 1) % k] - vals[i % k]) * t

    return sample


def cloud(coll, mats, name, spec, seed):
    # Two frequencies on every term. One octave on a disc this wide gives a
    # shape that is irregular but not lumpy — the first pass at this file came
    # out as a smooth lens that read as a flying saucer, because five gentle
    # lobes across the whole disc is a wide slow curve and the eye sees an
    # ellipse. The billow octave is what makes it a cloud.
    rim = circle_noise(seed, spec["rim_lobes"])
    rim_fine = circle_noise(seed + 3, spec["rim_lobes"] * 2 + 3)
    swirl = circle_noise(seed + 1, 5)
    billow = circle_noise(seed + 4, 13)
    phase = circle_noise(seed + 2, 6)
    crown_h, belly_h = spec["crown"], spec["belly"]

    def radius_at(theta):
        ragged = 0.66 * rim(theta) + 0.34 * rim_fine(theta)
        return RADIUS * (spec["rim_base"] + spec["rim_amp"] * ragged)

    def height(theta, s, up):
        """Top and bottom surface heights at fraction `s` of the local radius.

        Measured from the *rim plane*; the whole body is lifted onto z = 0
        afterwards, because the shader reads anything below zero as rain.

        The `s` term inside each noise argument shears the pattern as it goes
        out, so the billows spiral instead of running in straight radial
        stripes. Azimuthal variation is faded out toward the axis: without that
        fade the 48 triangles of the hub fan each arrive at a different height
        and the crown gets a starburst pinch at its exact centre — the pole
        problem every radial mesh has, loudest on a surface this smooth.
        """
        azimuthal = ((0.62 + 0.38 * swirl(theta + 2.1 * s))
                     * (0.74 + 0.26 * billow(3.0 * theta + 4.0 * s)))
        w = min(1.0, s / 0.38)
        w = w * w * (3.0 - 2.0 * w)
        lump = 0.88 * (1.0 - w) + azimuthal * w
        if up:
            return crown_h * (max(0.0, 1.0 - s * s) ** 0.62) * lump
        return -belly_h * (max(0.0, 1.0 - s * s) ** 1.1) * lump

    bm = bmesh.new()
    chan = {}

    def ring(s, up):
        row = []
        for i in range(AZ):
            theta = 2 * math.pi * i / AZ
            r = radius_at(theta) * s
            z = height(theta, s, up)
            v = bm.verts.new((r * math.cos(theta), r * math.sin(theta), z))
            chan[v] = (theta, s, phase(theta))
            row.append(v)
        return row

    tops = [ring(s, True) for s in BANDS[:-1]]
    bottoms = [ring(s, False) for s in BANDS[:-1]]
    edge = ring(1.0, True)          # top and bottom meet here: (1-s^2) is 0
    tops.append(edge)
    bottoms.append(edge)

    # Faces are stamped as they are made, by which surface they belong to. The
    # alternative — deciding from a face's height against the midline — puts the
    # crown's own outer ring, which is near the rim plane, on the belly's dark
    # grey and leaves a dark halo round a light cloud.
    for rows, up in ((tops, True), (bottoms, False)):
        for a, b in zip(rows, rows[1:]):
            for i in range(AZ):
                j = (i + 1) % AZ
                quad = (a[i], a[j], b[j], b[i])
                face = bm.faces.new(quad if up else tuple(reversed(quad)))
                face.material_index = CROWN if up else BELLY

    # Fan the two hubs rather than leaving a 48-gon at each pole.
    for rows, up in ((tops, True), (bottoms, False)):
        hub = bm.verts.new((0.0, 0.0, height(0.0, 0.0, up)))
        chan[hub] = (0.0, 0.0, phase(0.0))
        for i in range(AZ):
            j = (i + 1) % AZ
            tri = (hub, rows[0][i], rows[0][j])
            face = bm.faces.new(tri if up else tuple(reversed(tri)))
            face.material_index = CROWN if up else BELLY

    # Lift the body so the deepest belly lobe touches z = 0. This is the
    # contract, not a nicety: anything left below zero is shaded as rain.
    lift = -min(v.co.z for v in bm.verts)
    bmesh.ops.translate(bm, vec=(0.0, 0.0, lift), verts=bm.verts)
    zhi = max(v.co.z for v in bm.verts)

    bm.verts.index_update()
    bm.verts.ensure_lookup_table()
    uv0, data = [], []
    for v in bm.verts:
        _, s, tag = chan[v]
        uv0.append((v.co.x / (2 * RADIUS) + 0.5, v.co.y / (2 * RADIUS) + 0.5))
        data.append((1.0 - s, ramp(v.co.z, 0.0, zhi), tag))

    for f in bm.faces:
        f.smooth = True

    return emit(name, bm, coll, mats, uv0=uv0, data=data, wrap_u=False)


def veil(coll, mats, name, seed, rings=8):
    """The rain column: an open cylinder from the cloud's base plane to y = -1.

    One-sided, and the storm's victims are standing inside it — so the material
    has to render with `Cull Off` or the rain is drawn for everyone except the
    people it is raining on.
    """
    density = circle_noise(seed, 7)
    r = RADIUS * 0.92

    bm = bmesh.new()
    chan = {}
    rows = []
    for k in range(rings + 1):
        z = -DROP * (1.0 - k / rings)
        row = []
        for i in range(AZ):
            theta = 2 * math.pi * i / AZ
            v = bm.verts.new((r * math.cos(theta), r * math.sin(theta), z))
            chan[v] = ((theta / (2 * math.pi), (z + DROP) / DROP),
                       (1.0, (z + DROP) / DROP, density(theta)))
            row.append(v)
        rows.append(row)

    for a, b in zip(rows, rows[1:]):
        for i in range(AZ):
            j = (i + 1) % AZ
            bm.faces.new((a[i], a[j], b[j], b[i]))

    bm.verts.index_update()
    bm.verts.ensure_lookup_table()
    for f in bm.faces:
        f.material_index = BELLY
        f.smooth = True
    uv0 = [chan[v][0] for v in bm.verts]
    data = [chan[v][1] for v in bm.verts]
    return emit(name, bm, coll, mats, uv0=uv0, data=data, wrap_u=True)


# --------------------------------------------------------------------------
# Three clouds. Crown height, belly depth and rim raggedness — the three things
# a flat disc can differ in at 50 m. All in units of the radius.
# --------------------------------------------------------------------------

ANVIL = {"crown": 0.383, "belly": 0.100, "rim_base": 0.84, "rim_amp": 0.16,
         "rim_lobes": 6}
DISC = {"crown": 0.233, "belly": 0.075, "rim_base": 0.88, "rim_amp": 0.12,
        "rim_lobes": 9}
RAGGED = {"crown": 0.300, "belly": 0.158, "rim_base": 0.72, "rim_amp": 0.28,
          "rim_lobes": 5}

CLOUDS = (("Anvil", ANVIL, 401), ("Disc", DISC, 509), ("Ragged", RAGGED, 617))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for tag, spec, seed in CLOUDS:
        cloud(collection("Coll_StormCloud_%s" % tag), mats,
              "Mesh_StormCloud_%s" % tag, spec, seed)

    veil(collection("Coll_StormCloud_Rain"), mats,
         "Mesh_StormCloud_RainVolume", 733)

    common = collection("Coll_StormCloud_Markers")
    # The base plane on the axis: where the storm is, and where the veil starts.
    marker(common, "Marker_EffectOrigin", (0.0, 0.0, 0.0), size=0.2)
    # A bolt leaves from just under the base plane. It cannot start *at* it: the
    # belly lobes come down to exactly z = 0 and a bolt born on that plane has
    # its first pixels inside the cloud it is supposed to be striking out of.
    marker(common, "Marker_BoltOrigin", (0.0, 0.0, -0.03), size=0.2)

    report()
    save(out)


if __name__ == "__main__":
    main()
