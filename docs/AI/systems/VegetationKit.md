---
system: VegetationKit
layer: presentation
summary: 40 Quaternius Nature MegaKit plants converted to StylizedFoliage — opaque traced leaves, wind data, LODs
paths:
  - Assets/Game/Art/Models/_Source~/models/nature/vegetation_kit.blend
symptoms:
  - "a kit tree's crown is the original flat leaf cards instead of leaves"
  - "kit mushroom caps come out white like the stalks"
  - "a wildflower's petals are green"
  - "rebuilding the kit models gives slightly different leaves every time"
reads_with: [Foliage, BarkMaps, Rocks]
updated: 2026-10-25
---

# Vegetation Kit

The plants of the Quaternius **Stylized Nature MegaKit** (CC0, free tier) rebuilt in our foliage
style: same shader, wind, colours and LODs as every other plant in [Foliage.md](Foliage.md). Their
alpha-cut leaf cards are gone — each card became **opaque leaf polygons traced from its texture**.
The kit's rocks and pebbles are not here: [Rocks.md](Rocks.md) uses them directly.

## Model

- **Source**: [`vegetation_kit.blend`](Assets/Game/Art/Models/_Source~/models/nature/vegetation_kit.blend),
  same contract as `vegetation.blend`: one `Coll_<Prefab>` per plant with `<file>_LOD0/1/2`, three
  slots (`Foliage_Bark/Leaves/Fruit`), UV0–UV3 wind/colour data, custom normals. It is the only copy;
  the converter that built it lives in no repo (it read the kit's FBX + textures from outside).
- **Converted** (FBX `Environment/nature/Vegetation/<lowercase>.fbx`, prefab `Vegetation/<Prefab>`):

| Kit model | Prefab | Scale | Bark / leaves / fruit material | LOD0 tris |
| --- | --- | --- | --- | --- |
| CommonTree_1–5 | Tree_Common_A–E | 1.7 | Bark_Ridged / Broadleaf | 12–15k |
| TwistedTree_1–5 | Tree_Twisted_A–E | 1.45 | Bark_Twisted / BroadleafAutumn | 17–18k |
| Pine_1–5 | Tree_Spruce_A–E | 2.0 | Bark_Plated / Fir | 10–17k |
| DeadTree_1–5 | Tree_Snag_A–E | 1.45 | Bark_Weathered (bark only) | 5.6–6.6k |
| Bush_Common, _Flowers | Bush_Leafy_A–B | 1.8 | Bush / Bush / Fruit_BushBloom | 4.9–7k |
| Fern_1, Plant_1(_Big), Plant_7(_Big), Clover_1–2 | Plant_Fern_B, Plant_Hosta_A–B, Plant_Rosette_A–B, Plant_Clover_A–B | 0.45–1.6 | Bark_Stem / Fern | 0.5–1.5k |
| Flower_3/4 Single, Group | Plant_Wildflower_A–D | 0.6 | Fern / Fruit_FlowerSky (A, blue), Fruit_FlowerSun (B), Fruit_FlowerViolet (C–D) | 0.7–2.2k |
| Grass_Common/Wispy Short, Tall | Plant_GrassClump_A–D | 1.0 | Fern | 155–622 |
| Petal_3 | Plant_Blossom_C (ground flower) | 1.4 | Fruit_Bloom | 60 |
| Mushroom_Common, _Laetiporus | Plant_Mushrooms_C, Plant_Shelffungus_B | 1.5 | Bark_Mushroom / MushroomCap; Bark_Log / Bracket | 0.9k, 3.2k |

- **New materials** (all copies of an existing material of the same wind profile, so wind matches):
  `Mat_Foliage_Bark_Ridged`, `_Bark_Twisted` (Furrowed map + moss) from `Bark_Tree`; `_Bark_Plated`
  from `Bark_Pine`; `_Bark_Weathered` from `Bark_Dead`; `_Fruit_BushBloom` (pink/peach) from `Fruit_Berry`.
  The barks use [BarkMaps](BarkMaps.md) with brightened colours.
- **Prefabs**: variants of the FBX; LODGroup thresholds, fade and renderer flags copied from
  `Tree_Broadleaf_A` (trees, snags) or `Bush_Round_A` (everything else). Trees and snags get a root
  `CapsuleCollider` fitted to the bark below 1.5 m, up to the fork or 12 m; the rest none.

## Key types

| Asset | Role |
| --- | --- |
| [vegetation_kit.blend](Assets/Game/Art/Models/_Source~/models/nature/vegetation_kit.blend) | The 40 converted plants; export with `_exportlib.export_collections`. |
| `StylizedFoliage` (see [Foliage.md](Foliage.md)) | Draws them; nothing kit-specific in the shader. |

## Flows

How a kit model was converted (per island of its mesh; the kit's textures decide):

1. **Opaque island** (mean texture alpha > 0.85: bark, stems, grass blades, solid petals,
   mushrooms) → geometry kept. Bark → slot 0; petals (`Flowers.png`) → fruit; the rest → leaves.
   Mushrooms: islands more saturated than the model's mid-range → caps (leaves slot), others → stalk.
2. **Cluster card** (`Leaves_*` texture, 1 quad) → the texture's leaf blobs (connected alpha
   components, 7-ray outlines), a budget's worth per card, enlarged to keep the crown's coverage.
3. **Pine bough strip** (`Leaf_Pine`) → needle sprigs scattered inside the spray's alpha, pointing
   along the bough.
4. **Single-leaf / flower card** (`Leaves.png`, `Flowers.png`) → its own traced outline (20–28 rays
   + an inner ring), mapped through the card's triangles so a curved leaf stays curved.
5. **Data**: sway = height01¹·¹; tree limb seed is a *smooth* field (azimuth × 3 + 0.6 height01) so
   bark and leaves never tear; plants sway per leaf. Leaves of trees get crown-clump normals.
6. **LODs**: trees — LOD1 35 % of the leaves ×1.45 + bark decimated 50 %, LOD2 one lumpy ball per
   leaf clump + bark at 20 %; snags 40 % / 12 %; plants 50 % / 20 %.

## Multiplayer

N/A — static scene props, like all foliage.

## Persistence

N/A — no runtime state.

## Gotchas

- **`Leaves_NormalTree_C.png` contains the word "Normal"** — a filter skipping normal maps by name
  dropped the tree's leaf texture and every card came through as a flat opaque quad. Match the
  `_Normal.` suffix.
- **Blender 5.1 reports `material.use_nodes` False on imported FBX materials** even though the node
  tree is there; test `node_tree`.
- **Seed per model with a stable checksum (crc32), not `hash()`** — Python salts string hashes per
  process, so every rebuild scattered different leaves.
- **Bush meshes have no bark**: a woody-only LOD piece is empty; guard empty pieces.
- **Solid petals are opaque islands**: route `Flowers.png` islands to the fruit slot or they turn green.
- Tree LOD0s are heavier than our own (Twisted ~17.6k vs 8–16k); not yet profiled in a scene.
- Not seen in play mode, with wind (no GrassField in the preview scene), or on Windows.

## Extending

- **Tune a family**: change its materials (e.g. a `_Spring` leaves variant) — the meshes are shared.
- **Rebuild a model**: edit `vegetation_kit.blend`, export its collection, reimport; the prefab keeps
  its GUID (re-save through `PrefabUtility.LoadPrefabContents`).
- **The 10 kit path-stone pieces (`RockPath_*`) are not converted** — they need a stone material
  that works outside RockField's instanced shader.
