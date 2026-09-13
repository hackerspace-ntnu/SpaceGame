# Frozen statue base — build record

The ice a `Frozen` body is planted in. 1.5 s of cryo spray freezes a creature or
a player solid for 10 s, pose held; a hard hit shatters it.
Design: [`docs/AI/systems/Artifacts/CryoSprayer.md`](../../../../../../docs/AI/systems/Artifacts/CryoSprayer.md)
and [`StatusEffects.md`](../../../../../../docs/AI/systems/Artifacts/StatusEffects.md).

Written as the decomposition was decided, not proposed for approval.

## The decision that shaped everything: there is no shell around the body

The design's hard requirement is that "the original silhouette [is] intact so
you can tell what you froze". **Geometry cannot deliver that, and an encasing
shell actively destroys it.** Any capsule wide enough to clear a Vrescal's legs
is wide enough to erase a player's, and the moment the outline belongs to the
ice rather than to the creature the artifact stops telling the player anything —
which is a readability failure, not a taste one (`GDC-L1-UX-0003`).

The design already assigns encasement to the *material*: "pale blue, faintly
translucent, with the original silhouette intact". That is the statue shader on
the body's own skinned mesh, and it is the only place the effect can live
without having a shape.

`Assets/Game/Art/Shaders/Artifacts/FrozenStatue.shader` settled the same
question independently from the other side: it runs on **any** mesh, skinned or
not, reads POSITION and NORMAL only, and does its detail in object-space
triplanar, so it shades a creature's own body correctly whatever UVs that body
happens to have.

So the world prop is the design's other half — the ice the statue *stands in*.
It reads from the feet up, it never crosses the horizon of the creature above
it, and it is the same object whatever is standing in it. The tallest shard
measures **0.47 m** at nominal scale, below the knee of a 1.8 m player, so the
recognisable part of the silhouette is untouched **by construction rather than
by tuning**.

## Reused from the library

`components/props/flask_kit.py` — `emit`, `marker`, `ramp`. Nothing in
`components/organic/` served: `scute_plate.blend` is keratin armour with a hide
material and a scute's own curvature, not a mineral shard.

`_buildlib.Part` was deliberately not used, for the same reason as the foam
blob: it merges scratch bmeshes and forgets vertex identity, and every vertex
here carries channel data that has to stay attached to it.

## Decomposition

Two objects per variation, six in the file.

| Object | Why it is separate |
| --- | --- |
| `Mesh_FrozenBase_<V>_Plinth` | The block. Its own object because it is the part that can be swapped for a thicker or thinner one without touching the spikes. |
| `Mesh_FrozenBase_<V>_Shards` | The spikes, as one object. They never move independently and they are one visual event; the same call `dragon_bazooka.py` makes for `Mesh_DragonBazooka_SlingLoops`. |

### Variations

All three were needed — a frozen body appears repeatedly and three shapes is the
floor. They differ in silhouette, not size:

- **Spire** — 9 tall shards leaning out 14–34°, the default: a body planted in a
  spiked collar. Tallest tip 0.47 m.
- **Crust** — 16 short teeth at 38–62°, on a flatter, wider plinth. Ice that
  spread instead of climbing.
- **Shattered** — a deeply notched plinth (rim noise at 0.62 ± 0.44 against
  Spire's 0.86 ± 0.14) with 6 low splayed shards. For the thaw, and for the
  kill.

## Scale

Authored at a **1.0 m nominal footprint** and scaled at runtime to the frozen
body's own footprint. The three variations therefore differ in *shape only* — a
size variation would fight the runtime scale and read as an authoring mistake.

Measured bounds of the whole file: **1.1243 × 1.0901 × 0.4663 m**.

## Two things that are easy to get wrong here

- **The `up` ramp has to be measured, not guessed.** `up` is documented as
  reaching 1.0 at the tallest shard tip of *this* variation, and the tip height
  is a draw from a seeded RNG, not a number that can be written beside the spec.
  Hand-guessing it left every base's `up` topping out around 0.75 — a gradient
  that silently never finishes, with nothing in any log. The shards are
  therefore built **first**, they measure their own maximum, and the plinth
  shares it.
- **Fan the caps, do not leave an n-gon.** A 32-gon cap survives the FBX and
  then triangulates however Unity feels like, which on a domed cap is a visible
  star of shading seams.

## Marker

| Marker | Blender | What it is |
| --- | --- | --- |
| `Marker_EffectOrigin` | (0, 0, 0) | The ground plane on the axis: where the body's feet are, and where the freeze effect emits from |

In `Coll_FrozenBase_Markers`, its own collection. Shipped by
`keep_empties=True`.

## Shader channels

`FrozenStatue.shader` reads **none of these**. They are carried to the shared
prop convention in `components/props/flask_kit.py` because they are free at 96
to 256 vertices and the next shader may want them.

| Channel | Where | Value |
| --- | --- | --- |
| `UV0` | `uv0` / TEXCOORD0 | `u` wraps around, `v` runs up in **metres at nominal scale** so frost detail tiles at world density. On the plinth `u` is the azimuth about the prop's axis; on a shard `u` runs around that shard's own four faces. Both seams fixed per-face. |
| `core` | `uv2`.x / TEXCOORD1.x, and `Col.r` | 1 in solid ice, 0 where it thins to an edge — the plinth's outer quarter and every shard tip. The channel a translucency or refraction term wants: thin ice passes light, a block of it does not. |
| `up` | `uv2`.y / TEXCOORD1.y, and `Col.g` | 0 at the ground plane, 1 at the tallest shard tip of this variation. Plinths therefore top out at 0.31–0.58 and shards reach 1.0, which is correct — the plinth is the bottom of the assembly. |
| `lobe` | `Col.b` | Constant per shard, smooth around the plinth. Enough to give neighbouring shards different noise phase without a texture, which is what stops nine spikes reading as nine copies. |

UV1 is the authoritative copy; the colour attribute is 8-bit and may be
gamma-converted by Unity depending on project colour space.

## Articulation

None. It appears, it holds, it goes.

## Palette

Nothing added. `Mat_Glass_Canopy_Tinted` for the ice — the palette's only
glazing, and at `#AEC4CC` roughness 0.05 already the pale cold blue the design
asks for. A second near-identical glass is exactly the drift the palette guards
against. `Mat_Paint_White_Arctic` for the rime crust on the plinth's crown: a
block of ice reads as ice because part of it is *not* transparent, and an
all-glass plinth reads as a rendering error rather than as frost.

## Verified

- Built dimensions measured off the file: 1.1243 × 1.0901 × 0.4663 m; the six
  meshes are 96–256 vertices, 168–448 triangles.
- FBX re-imported and checked: all six meshes carry `uv=['UVMap', 'Data']` and
  `col=[('Col', 'BYTE_COLOR', 'CORNER')]`; `up` reaches 1.000 on every shard
  cluster; `Marker_EffectOrigin` present.

## Principles cited

`GDC-L1-UX-0003` (readability — the silhouette is the information, so the shell
was cut), `GDC-L1-CONTENT-0003` (naming and organisation conventions).
