# Cryo sprayer — build record

The finned-barrel one-hander of the sprayer kit. Built 2026-09-07.
Design: [`docs/AI/systems/Artifacts/CryoSprayer.md`](../../../../../../docs/AI/systems/Artifacts/CryoSprayer.md).

| File | |
|---|---|
| `cryo_sprayer.py` | generator — historical record, never re-run over the `.blend` |
| `cryo_sprayer.blend` | **source of truth** |
| `cryo_sprayer_export.py` | re-runnable export |
| `Assets/Game/Art/Models/Items/cryo_sprayer.fbx` | what Unity imports |

**0.5080 m** long, 8 408 tris, 12 objects (9 meshes + 3 markers), 9 palette
materials. +Z up, −Y forward, 1 unit = 1 m. `_zverify`: **0 clashing pairs**.

## Scale

Bracketed against `models/gear/dragon_bazooka.blend` (1.3685 m in Blender, worn
at `holdSize` 1.25 — the anchor of `ItemScaleLadder.cs`). Authored at 0.508 m,
the one-handed sprayer bracket, matching the foam gun so the two read as issued
peers.

## Told apart from the foam gun without colour

Same length, same kit, same grip. Three things separate them and none of them
is a hue, which is the point — `GDC-L1-UX-0006` says never to encode
information in colour alone, and a player who cannot tell pale blue from yellow
still has to pick the right tool under pressure:

1. **The bottle is on the spine, not under the barrel.** That is the silhouette
   difference and it is visible at any distance. It is also the honest place for
   it — a cryogen bottle is the heaviest thing on the item and sitting it over
   the hand is what a designer would do.
2. **The barrel is finned.** Seven chrome annular fins, tapering forward. Fins
   say "this sheds heat"; nothing else in the nine-piece set is about
   temperature.
3. **The last few centimetres are rimed.** Five overlapping white lumps over
   the tip and the front fins — lumpy where everything else on the item is
   machined.

The corrugated line arcing from the bottle's valve to the barrel throat is the
fourth cue and the one that reads at icon size.

## Reuse

From `components/props/sprayer_kit.py` (`sprayer_kit_BUILD.md`): `shell`,
`tank`, `nozzle_finned`, `hose(ribbed=True)`, `arc`, `clamp_band`,
`grip_moulded`, `gauge_plate`, `marker`, and the eight-entry `KIT_MATS` table.
One material beyond it — `Mat_Paint_Blue_Station`, the palette's cold enamel.
**No material added to the palette.**

### Rime, and why it is `Mat_Paint_White_Arctic`

The palette has no ice or frost material and this build does not add one.
Arctic white is a chalky cool off-white at roughness 0.58, which is what frost
looks like; what makes the part read as *rime* rather than as paint is its
shape. Adding a fifty-sixth near-white to say the same thing is exactly the
"eleventh grey" the palette rules exist to prevent.

## Decomposition

**Nothing is joined.** Nine meshes:

| Object | Origin (blender) | Dimensions (m) | Tris | Why separate |
|---|---|---|---|---|
| `Mesh_CryoSprayer_Body` | (0, 0, 0) | 0.076 × 0.233 × 0.070 | 712 | the moulded shell, blue flank plates and barrel collar |
| `Mesh_CryoSprayer_Barrel` | (0, 0, 0) | 0.073 × 0.253 × 0.074 | 1860 | tube plus seven fins, and the recessed bore |
| `Mesh_CryoSprayer_Rime` | (0, −0.310, 0.010) | 0.076 × 0.061 × 0.076 | 1280 | **the frost** — pivot at the nozzle tip on the bore axis, so scaling local Y grows it *back* along the barrel from the coldest point |
| `Mesh_CryoSprayer_Bottle` | (0, 0, 0) | 0.092 × 0.157 × 0.092 | 816 | the cryogen bottle and its valve block |
| `Mesh_CryoSprayer_Saddle` | (0, 0, 0) | 0.104 × 0.116 × 0.105 | 560 | two cradles and the strap holding it on the spine |
| `Mesh_CryoSprayer_Line` | (0, 0, 0) | 0.044 × 0.094 × 0.104 | 1104 | the corrugated cryogenic line |
| `Mesh_CryoSprayer_Grip` | (0, 0, 0) | 0.053 × 0.132 × 0.132 | 1172 | the kit's moulded grip, trigger and guard |
| `Mesh_CryoSprayer_Breech` | (0, 0, 0) | 0.052 × 0.064 × 0.052 | 424 | rear boss and regulator cap |
| `Mesh_CryoSprayer_Gauge` | (0, 0, 0) | 0.013 × 0.090 × 0.030 | 444 | the kit's supply gauge, on the bottle's outboard flank |

**No armature.** The two moving parts the design doc names are the fins turning
white and the frost creeping back along the barrel; both are the rime object,
which is already separate with its origin on the axis of growth. The trigger is
44 mm of blade inside the grip and the gauge fill is built on the Unity side by
`OxygenGearBuilder`.

## Markers — what the wave-2 assets agent needs

| Marker | Blender | Unity (`−x, z, −y`) | For |
|---|---|---|---|
| `Marker_Muzzle` | (0.000, −0.310, 0.010) | (0.000, 0.010, 0.310) | the vapour plume's origin — the bore mouth |
| `Marker_Grip` | (0.000, 0.040, −0.072) | (0.000, −0.072, −0.040) | `ItemGrip` — the palm, inside the grip on its core axis |
| `Marker_Gauge` | (0.052, 0.058, 0.098) | (−0.052, 0.098, −0.058) | the gauge face, buried in the lit strip |

`holdSize` from the export: the model's longest axis is **0.5080**.

`GAUGE_SIDE = +1`, `PLUMB_SIDE = −1`: the gauge is on Blender +X (Unity −X, the
flank a right-handed hold turns toward the camera) and the cryogenic line and
the bottle's valve on the other, so neither occludes the other.

## Things that had to be redone

- **The rime was five short cylinders and is now five tori.** Every ring put a
  flat disc perpendicular to the barrel, and one of them landed half a
  millimetre from a fin's own flat disc; nudging the rings apart only moved the
  clash to the next fin. A torus has no flat face anywhere on it, so no
  placement can produce one — and lumps read as frost better than rings do.
- **The blue accent was too quiet as a single flank plate.**
  `Mat_Paint_Blue_Station` (#9FB8CE) against `Mat_Paint_White_Arctic` (#D6DAD9)
  is a small step. The accent is repeated on the barrel collar, the bottle caps
  and the regulator, which is what makes "the blue one" readable at a glance
  (`GDC-L1-UX-0003`).
- **`nozzle_finned` grew an `at` parameter.** It first built the barrel on the
  model's centreline; this bore runs at z +0.010, and every other builder in the
  kit takes an absolute placement.

## Principles cited

`GDC-L1-UX-0006` (accessibility — the sprayer is told from the foam gun by
silhouette, surface and plumbing, with hue as the fourth and least of the
cues), `GDC-L1-UX-0003` (readability and hierarchy — the accent repeated until
it carries, the gauge on the flank the player sees), `GDC-L1-UX-0004`
(affordances — fins state "cold", a bell states "wide", a lance states "far").
