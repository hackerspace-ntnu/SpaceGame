---
system: GrassBiomes
layer: presentation
summary: The 25 shipped grass biomes — profiles, materials and Ground_ layers — and how to add or tune one
paths:
  - Assets/Game/ScriptableObjects/Environment/Grass/
  - Assets/Game/Art/Materials/Environment/Grass/
  - Assets/Game/Terrain/GrassBiomes/
symptoms:
  - "a grass biome looks right up close but the wrong colour from far away"
  - "a new grass biome grows nothing where I painted it"
  - "the grass is too short for the player"
reads_with: [Grass, NoiseHeightmap]
updated: 2026-10-25
---

# Grass Biomes

A biome is three assets: a `GrassProfile_<Biome>` (shape, size, density, distances), its own
`Mat_Grass_<Biome>` (every colour), and a `Ground_<Biome>` TerrainLayer that is painted to grow it.
[GrassField.prefab](Assets/Game/Prefabs/Environment/Nature/GrassField.prefab) pairs all 25. How
they are grown and drawn is [Grass.md](Grass.md).

## Model

| Biome | Height (m) | Tufts/m² | Draw (m) | Look |
| --- | --- | --- | --- | --- |
| Meadow | 0.90–1.35 | 9 | 300 | lush green, rare pink/lilac flowers |
| MeadowGolden / MeadowAutumn | 0.90–1.50 | 9 | 300 | meadow shape; yellow / orange-rust |
| MeadowBrown / MeadowDark / MeadowSage | 0.75–1.65 | 9 | 300 | meadow shape; dried brown / deep green / grey-green |
| DrySteppe | 0.45–0.96 | 6 | 300 | thin straw-tan blades |
| TallGrass | 2.4–4.2 | 5 | 350 | golden savanna, strong sway, tall clumps |
| Turf | 0.24–0.48 | 16 | 150 | dense cool blue-green, white flowers |
| RedDesertScrub | 0.36–0.90 | 2.5 | 300 | sparse ochre wisps on red sand |
| RedDesertBunch | 1.5–2.7 | 1.2 | 350 | spinifex-like straw hummocks on red sand |
| RockyTufts | 0.45–1.05 | 3 | 300 | hardy grey-green tufts on stone, slopes to 55° |
| RockyMoss | 0.15–0.30 | 10 | 150 | moss/lichen cushions on stone, slopes to 65° |

Shape variants (same generator, own material each; heights in m): NeedleFountain 1.2–1.8 hair-thin
spray · LeafRosette 1.05–1.5 broad creased leaves · Clover 0.24–0.39 splayed leaves · Reeds 3.3–6.0
stiff spears · FlowerMeadow 0.9–1.5, 20 % flowering · PomPom 0.75–1.2 round puffs · Ferns 1.5–2.25
arching fronds · Agave 1.2–1.95 folded spikes · CrystalShards 0.9–3.0 shards · ShaggyMops 1.8–2.7
long arching blades · VelvetLawn 0.18–0.30 tiny dense lawn · Mycelium (VelvetLawn shape) pale
lilac fungal carpet with rare cyan spore tips, for the fungal kits in [Foliage.md](Foliage.md).

- Heights are for the **3 m player** (tripled 2026-10-25): meadows reach the hip, TallGrass and Reeds
  overtop the player.
- **Distance tiers** (all: falloff 2.6, widen up to 3×, sink from 80 %): draw 300 m, full density to
  30 m, detail switches at 8 / 20 / 40 m. Tall: 350 m, 35 m, 10 / 24 / 48 m. Tiny: 150 m, 18 m,
  5 / 12 / 24 m. Tall = TallGrass, Reeds, RedDesertBunch, ShaggyMops, Ferns, CrystalShards; tiny =
  Turf, VelvetLawn, Clover, RockyMoss.
- **Natural patches**: `edgeWarp` (texels the painted outline wanders) and `densityVariation` /
  `densityVariationSize` (clumps and clearings inside a field).
- **Ground colour** (`groundTint`): ~0.85 for carpets; 0.2–0.35 for sparse grass over sand or stone
  (desert, rocky, Agave, CrystalShards). Dense biomes' `Ground_*` texture is the **average** grass
  colour, which is what Terrain/Lit terrains show under the blades.

## Flows

N/A — data only; the flows are in [Grass.md](Grass.md).

## Multiplayer

N/A — presentation assets.

## Persistence

N/A — authored assets, no runtime state.

## Gotchas

- A biome is matched to paint by its **`Ground_*` asset**; a copy of the layer grows nothing.
- From afar a biome reads as `GrassGroundTint`'s estimate of its colour, built from the material
  (root → tips 80 %, variation, patch, a third of the warm blade tint). A material with a strong
  per-blade tint or gust brighten can read a shade off; tune the material, not the estimate.
- Tall biomes cost most where the camera stands in them: TallGrass +3.6 ms filling the view.

## Extending

- **New biome**: copy a `Mat_Grass_*` material and a `GrassProfile_*`, point the profile at the new
  material, tune colours/heights/distances. Make a `Ground_<Biome>` TerrainLayer (copy a ground
  PNG). Add `{terrainLayer, profile}` to the prefab's `biomes`.
- Profile edits in the Inspector rebuild the grass live; material edits apply at once, except the
  far ground colour, which follows on the next repaint or `GrassField.Regenerate()`.
