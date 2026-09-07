# Storm cloud — build record

The thing a storm flask leaves parked over the ground: a cloud 15 m up, 30 s
long, raining, throwing a bolt at the tallest body under it every 2.5 s.
Design: [`docs/AI/systems/Artifacts/StormFlask.md`](../../../../../../docs/AI/systems/Artifacts/StormFlask.md),
which calls this "the real art asset".

Written as the decomposition was decided, not proposed for approval.

## The frame, and it is the whole story

`Assets/Game/Art/Shaders/Artifacts/StormCloud.shader` reads POSITION only and
works in **object space, centred on the origin, XZ radius 1, +Y up** (Unity
axes). Geometry at `y >= 0` is shaded as cloud; geometry at `y < 0` is shaded as
the rain veil, hanging to `y = -1`.

`_exportlib` maps Blender `(x, y, z)` to Unity `(-x, z, -y)`, so in the frame
this file is authored in: **Blender +Z is Unity +Y**, XY radius 1, body at
`z >= 0`, veil in `z = -1 .. 0`.

Two consequences the first version of this file got wrong in both directions,
and both are recorded because neither errors:

- **The whole body sits at or above z = 0, belly included.** The first version
  put the origin at the underside *centre* and let the lumpy belly dip to
  −1.67 m. Under this contract that is not a belly — it is a metre and a half of
  cloud shaded as rain. The origin is now the **base plane**: the lowest point
  of the deepest belly lobe touches exactly z = 0, enforced by measuring the
  built mesh and translating, not by trusting the belly parameter.
- **Nothing is authored in metres.** The design's 12 m radius and 15 m height
  live in the prefab's transform.

### The one number that does not fit, and whose call it is

At XZ radius 1 → 12 m, a **uniform** scale of 12 hangs the veil 12 m below the
cloud, not the design's 15. A **`(12, 15, 12)`** scale hangs it the design's
15 m and stretches the body 25% taller, which is invisible on vapour. Both are
defensible; the geometry is normalised for either. **That is the wave-2 assets
agent's decision, and it is flagged here rather than silently picked in a mesh.**

## Decomposition

| Object | Why it is separate |
| --- | --- |
| `Mesh_StormCloud_{Anvil,Disc,Ragged}` | Three cloud bodies. Variations, stacked at the origin as every variation file in this library is. |
| `Mesh_StormCloud_RainVolume` | One veil, shared by all three. Rain hanging off a disc looks the same whatever the disc's crown is doing, and three copies would be three chances to let them drift apart. It is a separate object so it can be turned off on its own. |

The shader permits cloud and veil in one mesh or two; each object here obeys the
contract by itself — a body has nothing below 0 and simply gets no rain, a veil
has nothing above 0 and simply is not a cloud.

**The FBX root is not the prefab.** Unity reads the four as separate Mesh
assets; the prefab takes one body plus the veil.

## Variations

All three were needed: several flasks can be thrown at once, and the design
already flags multiple live clouds as a thing to cap rather than discover. They
differ in the three things a flat disc can differ in at 50 m:

| | Crown | Belly | Rim |
| --- | --- | --- | --- |
| **Anvil** | 0.383 | 0.100 | 0.84 ± 0.16, 6 lobes |
| **Disc** | 0.233 | 0.075 | 0.88 ± 0.12, 9 lobes |
| **Ragged** | 0.300 | 0.158 | 0.72 ± 0.28, 5 lobes |

All in units of the radius.

## What the model must get right

The cloud is a hazard telegraph before it is scenery: a player has to read
"there is a storm over there, and it is over *there* and not *here*" from 50 m,
which makes the readable extent a gameplay property, not a decoration
(`GDC-L1-UX-0003`, `GDC-L1-ANIM-0003`).

- **The rim is inside the gameplay radius.** It sits at 0.72–0.94 measured
  against the shader's 1.0, so what the player sees is inside where the bolts
  fall. A cloud drawn generously beyond its own trigger volume kills people
  standing outside it, which reads as the game cheating.
- **It is flat, not fluffy.** Crown 0.21–0.34 of the radius. A cumulus of
  proportionate height would be the biggest thing on the horizon and would read
  as weather rather than as a thing someone threw.

## Three things that build successfully and look wrong

- **One octave of noise is not a cloud.** The first pass used a single 5-lobe
  rim noise and came out as a smooth lens that read as a flying saucer: five
  gentle lobes across the whole disc is a wide slow curve, and the eye sees an
  ellipse. Every term now carries a second, higher octave — a fine rim noise at
  `2n+3` lobes and a 13-lobe billow on the height.
- **The pole pinches.** With azimuthal variation running all the way to the
  axis, the 48 triangles of the hub fan each arrive at a different height and
  the crown grows a starburst at its exact centre. Variation is faded out over
  the inner 0.38 of the radius, smoothstepped.
- **A degenerate inner ring.** The innermost band is 0.14, not 0. At 0 the ring
  collapses onto the axis and the lump term gives 48 coincident vertices at
  different heights, which builds without complaint and shades like a shattered
  cone.

## The veil must render with `Cull Off`

It is a one-sided cylinder and the storm's victims are standing inside it.
Back-face culled, the rain is drawn for everyone except the people it is raining
on. This is a material setting, not something the mesh can fix — a double-sided
cylinder would double the triangles and still need the shader to handle inward
normals.

## Markers

| Marker | Blender | Unity | What it is |
| --- | --- | --- | --- |
| `Marker_EffectOrigin` | (0, 0, 0) | (0, 0, 0) | The base plane on the axis: where the storm is, and where the veil starts |
| `Marker_BoltOrigin` | (0, 0, −0.03) | (0, −0.03, 0) | Where a bolt leaves the belly |

They are not the same point. The belly lobes come down to exactly z = 0, so a
bolt born on that plane has its first pixels inside the cloud it is supposed to
be striking out of. Both in `Coll_StormCloud_Markers`; shipped by
`keep_empties=True`.

## Shader channels

`StormCloud.shader` reads **none of these**. They are carried to the shared prop
convention in `components/props/flask_kit.py` because they are free at 432 to
530 vertices and the next shader may want them.

| Channel | Where | Value |
| --- | --- | --- |
| `UV0` | `uv0` / TEXCOORD0 | **Body:** top-down planar projection normalised to the diameter, centre (0.5, 0.5) — the natural domain for a coverage mask, since the storm's shape is a function of where you are under it. **Veil:** cylindrical, `u` around, `v` 0 at the foot and 1 at the cloud, seam fixed per-face. |
| `core` | `uv2`.x / TEXCOORD1.x, and `Col.r` | **Body:** 1 on the axis, 0 at the ragged rim — the coverage mask. Fade the cloud out on it and the silhouette stays ragged without a texture. **Veil:** 1 everywhere; a curtain has no rim to dissolve at, its fade is `up`. |
| `up` | `uv2`.y / TEXCOORD1.y, and `Col.g` | **Body:** 0 at the base plane, 1 at the crown. **Veil:** 0 at the foot, 1 at the cloud. One expression drives both the lit top and the fall. |
| `lobe` | `Col.b` | **Body:** a smooth per-azimuth random, for churn phase. **Veil:** per-column rain density taken from the cloud's own rim noise, so heavier rain falls under thicker cloud. |

UV1 is the authoritative copy; the colour attribute is 8-bit and may be
gamma-converted by Unity depending on project colour space.

## Articulation

None. The churn is a shader and the drift is a transform.

## Palette

Nothing added. `Mat_Neutral_Slate_Dark` for the belly a bolt comes out of and
`Mat_Neutral_Panel_Grey` for the crown — enough to tell the two apart while the
model is being looked at in Blender. The runtime surface is
`Assets/Game/Art/Shaders/Artifacts/Materials/Mat_StormCloud.mat`, assigned by
the wave-2 assets agent. Inventing two more greys for a shader-driven cloud is
exactly the palette drift the library guards against.

Faces are stamped as they are built, by which surface they belong to. Deciding
from a face's height against the midline was tried and puts the crown's own
outer ring — which is near the rim plane — on the belly's dark grey, leaving a
dark halo round a light cloud.

## Verified

- Per-object z ranges measured off the built file, which is the check that
  matters here: bodies **0.0000 .. 0.4250 / 0.2710 / 0.4055**, veil
  **−1.0000 .. 0.0000**. Nothing of the cloud is below zero; the veil's foot is
  exactly −1.
- File bounds 1.8969 × 1.9024 × 1.4250 (unit-radius object space, **not
  metres**).
- FBX re-imported and checked: four meshes carry `uv=['UVMap', 'Data']` and
  `col=[('Col', 'BYTE_COLOR', 'CORNER')]`; both empties present with their
  coordinates.

## Principles cited

`GDC-L1-UX-0003` (readability and hierarchy — the cloud is a telegraph, so its
extent is a gameplay property), `GDC-L1-ANIM-0003` (the warning must exist to be
read), `GDC-L1-CONTENT-0003` (naming and organisation conventions).
