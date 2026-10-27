---
system: Foliage
layer: presentation
summary: Stylized trees, bushes and plants in biome kits — vegetation.blend models, one wind-shared shader
paths:
  - Assets/Game/Art/Shaders/Environment/StylizedFoliage.shader
  - Assets/Game/Art/Shaders/Environment/StylizedFoliage.hlsl
  - Assets/Game/Art/Models/_Source~/models/nature/vegetation.blend
  - Assets/Game/Art/Models/Environment/nature/Vegetation/
  - Assets/Game/Art/Materials/Environment/Foliage/
  - Assets/Game/Prefabs/Environment/Nature/Vegetation/
  - Assets/Game/Scenes/Tests/VegetationGallery.unity
  - Assets/Game/Scenes/Tests/VegetationGallery2.unity
  - Assets/Game/Terrain/VegetationGallery/
  - Assets/Game/Terrain/VegetationGallery2/
symptoms:
  - "trees and bushes stand perfectly still while the grass around them waves"
  - "a tree's branches drift away from its leaves in the wind"
  - "a statically batched tree leans from the world origin or swings metres sideways"
  - "a painted palm tree has no collision"
  - "a tree crown has a dark, almost black core showing between the leaves"
  - "foliage looks dithered or speckled in an editor screenshot"
  - "a terrain painted from code shows only its first layer everywhere"
  - "a newly painted biome has no grass in an editor screenshot"
  - "a mushroom cap, petal or pod renders almost black although its material is pale"
  - "the plant in a planter composite floats on top of a little tree"
  - "bushes never move when the player walks through them"
reads_with: [Grass, GrassInteraction, ArtPipeline, LevelOfDetail, BarkMaps, VegetationKit, VegetationFamilies]
updated: 2026-10-27
---

# Foliage

Trees and bushes in the grass's style: ordinary prefabs (drag them in, or paint them with the
terrain's **Paint Trees** tool), drawn by `SpaceGame/StylizedFoliage`, which leans them on the same
wind wave as the meadow around them. Leaves are opaque geometry, not textures, lit by the normal of
the canopy blob they sit on, so a crown shades as a few soft round masses.

## Model

- **Source**: [`vegetation.blend`](Assets/Game/Art/Models/_Source~/models/nature/vegetation.blend), a contact sheet: one `Coll_<model>`
  per plant with `<model>_LOD0/1/2`, slots `Foliage_Bark` (0), `Foliage_Leaves` (1), `Foliage_Fruit` (2); an unused
  slot is empty and Unity drops it. The `.blend` is the only copy — no generator in the repo.
- **Per-vertex data** (baked by the .blend, read by the shader — the layout is a contract):

| Channel | x | y |
| --- | --- | --- |
| UV0 | sway weight: 0 root → 1 top | flutter / along-leaf: 0 base → 1 tip; 0 on bark |
| UV1 | limb seed (phase of that limb's sway) | limb weight: 0 at its junction → 1 at its end |
| UV2 | height01 (colour gradient) | leaf seed (jitter, accents) |
| UV3 | shell: 0 deep inside the crown → 1 outside; 1 on bark and fruit | — |

- **Normals are custom** (leaves: their canopy blob's; fronds/blades: the crown ellipsoid's) — keep `Normals: Import`.
- **Building blocks**: tubes (trunks, limbs, roots; cactus **ribs** lit by their own normals), leaf shingles on
  (rotatable) canopy blobs, leaves along a branch, **fronds** — `pinnate` (stem + leaflets), `chain` (leaves on a
  hanging/rising curve), `blade`/`ribbon` (far LOD only: up close a ribbon reads as paper) — spike clusters, pads,
  fruit, petals, cactus **areoles** (spine tufts), cup blooms, **surfaces of revolution** (caps with lip and gills,
  puffballs, pods, bells), gill fins, flakes (scales, wool), faceted crystals, curved leaves. Spines and flakes thin at LOD1, vanish at LOD2.
- **129 models + 40 kit conversions ([VegetationKit.md](VegetationKit.md)) + 51 colour variants + 10 composites** (trees 10–30 m, bushes 2.5–4.5 m, for the 3 m
  player), prefab name = PascalCase of the FBX name. Four more biome families (Frill, Lattice, Starburst,
  Scalewood) live in their own source file: [VegetationFamilies.md](VegetationFamilies.md). Families by kit:

| Kit | Trees | Bushes / plants / props |
| --- | --- | --- |
| Meadow / old woods | Broadleaf A–C, Oak_Giant_A (30 m landmark), Birch A–B, Blossom A–B, Bamboo A–B, Common A–E, Twisted A–E (kit) | Bush_Round A–D, Bush_Leafy A–B, Plant_Hosta A–B, Plant_Rosette A–B, Plant_Clover A–B, Plant_Wildflower A–D, Plant_GrassClump A–D, Plant_Blossom_C (kit), Bush_Berry A–B, Bush_Hedge A–B (box collider: a wall), Log_Fallen A–B, Stump A–B, Plant_Flowers A–B |
| Winter | Broadleaf_Bare A–B, Birch_Bare_A, Blossom_Bare_A, Umbrella_Bare_A (bark-only models) | — |
| Highland | Pine A–B, Fir A–B, Larch A–B, Krummholz_A, Spire A–B, Ghost A–B (standing dead), Spruce A–E, Snag A–E (kit) | Bush_Juniper A–B, Bush_Juniper_Mat_A, Plant_Cushion A–B, Plant_Lichen A–B, Plant_Fern_A, Plant_Fern_B (kit) |
| Savanna | Umbrella A–B, Baobab A–B, Fever A–B, Whistling_Thorn_A, Sausage_A | Bush_Thorn_A |
| Desert | Joshua A–B, Dead A–B, Dragonblood_A | Plant_Saguaro_A, Plant_Organpipe_A, Plant_Pricklypear A–B, Plant_Agave A–B, Plant_Ocotillo_A, Bush_Scrub A–B, Bush_Creosote_A, Bush_Spiky_A, Plant_Tumbleweed A–B (static: rolling is gameplay) |
| Cactus line (v5) | Plant_Cactus_Candelabra_A (10 m), Plant_Cactus_Cardon_A (9 m) | Plant_Cactus_Barrel_A (golden) / _B (fishhook), _Hedgehog_A (purple), _Pincushion_A (deep green, wool), _Cholla_A (silver) / _B (chain fruit), _Star_A (dusty, flecked), _Creeping_A (bronze) — each species its own body, spine and flower colour |
| Oasis / wetland | Palm A–B, Datepalm A–B, Willow A–B, Mangrove_A, Swamp_Cypress_A, Strangler_Fig A–C (one braided trunk, flat weeping crown, free-hanging aerial roots; A wide, B tall column, C leaning) | Bush_Fan_A, Plant_Papyrus_A, Plant_Cattails_A, Plant_Lilypads_A, Plant_Waterlily_Giant_A, Plant_Seedpuffs_A (dandelion clocks) |
| Alien | Bulb_B, Lantern A–B (fruit clusters in the crowns), Spiral A–B, Jellyfish A–B (ribbed, scalloped bell), Dunekelp A–B | Bush_Coral A–B, Bush_Crystalflower A–B (golden-spiral crystal rosettes), Plant_Tubeworms_A, Plant_Balloonpods_A (papery lantern pods), Plant_Sporepuffer_A (puffballs and earthstars), Plant_Glowcaps A–B (fairy bonnets, no glow despite the name), Plant_Spiralfern_A |
| Fungal | Mushroom_A (parasol), Mushroom_B (bolete) | Plant_Mushrooms_A (honey tuft), _B (shaggy inkcaps), _C (kit), Plant_Shelffungus A–B, Plant_Lampmushroom A–B (nodding bells) |
| Salvage / farm | — | Plant_Crop_Grain_A (8 m rows) |

- **Colour variants** (material swaps only): `_Autumn`, `_Golden`, `_Spring`, `_Drought`, `_Dust`, `_Dry`, `_Salt`, `_Frost`,
  `_Charred`, `_NightGlow`, `_Ember` (Tree_Bulb_B only), `_Teal`, `_Glow`, `_Fungal`, `_Blossom` (weeping cherry), `_Green` (larch).
- **Composites** in `Vegetation/Composites/`: deco props + plants — `Salvage_Overgrown_*` (4), `Planter_*` (5:
  the bed's own plants hidden, ours on the soil top ~0.45 m), `Farm_Plot_Mixed` (four grain rows).
- **Mushroom caps** are the leaves slot (`_InnerColor` = gills/pores at shell 0); scales/warts the fruit slot;
  caps, puffballs and pale stalks use `Cap_*` maps ([BarkMaps.md](BarkMaps.md)); only `_Glow` variants glow.
  **Cacti**: per species a body `Mat_Foliage_Bark_Cactus<Look>`, a spine material in the leaves slot
  (`CactusSpine_<Gold|White|Red|Grey|Dark>`) and a flower/fruit material, all rigid wind. LOD0 budgets: small
  plants 3.5–11.5k, tree cacti/mushrooms 5–15k, figs 18–21k — spines are the cost.
- **Materials**: one bark + leaves (+ fruit) material per look, all `SpaceGame/StylizedFoliage`; fruit sets `_AccentChance` and
  `_AccentSize` 1 so the whole pod takes the accent colours (`_AccentEmission` glows); bark may add a map ([BarkMaps.md](BarkMaps.md)).
- **Wind profiles** (lean, limb sway m, limb speed, flutter m, flutter speed, bender push m) — a plant's
  bark, leaves and fruit share one: tree (0.025, 0.3, 1.0, 0.09, 6.5, 0), conifer (0.02, 0.22, 0.9, 0.05,
  5, 0), palm (0.02, 0.5, 1.2, 0.12, 5.5, 0), willow (0.025, 0.35, 0.9, 0.25, 3, 0), alien (0.03, 0.35,
  0.7, 0.07, 4.5, 0), bush (0.07, 0.1, 1.5, 0.06, 7.5, 0.9), stem (0.09, 0.15, 1.4, 0.06, 6, 0.7), spiky
  (0.04, 0.03, 1.4, 0.04, 6, 0.5), rigid (0.008, 0.03, 0.7, 0.03, 4, 0.1), hedge (0.015, 0.03, 1.2, 0.04,
  7, 0.25), ground (0, 0, 1, 0.02, 4, 0), bamboo (0.04, 0.4, 1, 0.08, 6, 0), kelp (0.06, 0.4, 0.8, 0.15, 3, 0.4).
  Wave length/speed and gust size/speed are the same on every material.
- **LODs**: LOD1 ~30 % of the leaves enlarged, every 2nd ring; LOD2 lumps per clump (20-tri past 16) + 5 % of the leaves
  on trees > 6 m, every 3rd ring, roots/twigs dropped. Thresholds — trees 1.0 / 0.25 / **cull 0.1**, everything else
  0.5 / 0.12 / **cull 0.05**, cross-faded. With the project's `lodBias` 2 a 20 m tree is LOD0 to ~35 m,
  LOD1 to ~140 m, culled past ~350 m; a 3 m bush is culled past ~200 m. 1.0 is the LODGroup maximum.
- **Colliders**: root capsule on the trunk; palms a tilted child capsule; hedges/logs a box; bushes none.
- **Galleries** (480 × 360 m test scenes, nine kit zones, own TerrainData in `Assets/Game/Terrain/<scene>/`):
  [VegetationGallery](Assets/Game/Scenes/Tests/VegetationGallery.unity) and [VegetationGallery2](Assets/Game/Scenes/Tests/VegetationGallery2.unity)
  (old woods, winter, larch fell, dry savanna, wetland, cactus flats, glow reef, mycelium hollow, salvage farm).

## Key types

| Type | Role |
| --- | --- |
| [StylizedFoliage.shader](Assets/Game/Art/Shaders/Environment/StylizedFoliage.shader) | ForwardLit (per-vertex light, one hard shadow tap), ShadowCaster, DepthOnly, DepthNormals; instancing + LOD cross-fade. `_Cull`: Off for leaves, Back for bark. |
| [StylizedFoliage.hlsl](Assets/Game/Art/Shaders/Environment/StylizedFoliage.hlsl) | `PlaceFoliageVertex` (lean, limb sway, flutter, benders), `FoliageAlbedo`, per-pixel tip accents, `BarkAlbedo` (triplanar bark map, groove tint, moss). |
| `WindPush` / `WindGust` in [GrassShared.hlsl](Assets/Game/Art/Shaders/Environment/GrassShared.hlsl) | The wave + gust formulas, shared with GrassCull.compute so trees lean and leaves flutter with the meadow. |
| [GrassField](Assets/Game/Scripts/World/Environment/Foliage/GrassField.cs) | Publishes `_FoliageWind`, `_FoliageTime`, `_FoliageBenders`, `_FoliageBenderCount` per camera. |
| [GrassBender](Assets/Game/Scripts/World/Environment/Foliage/GrassBender.cs) | Also parts bushes (`_BendStrength`, m) and shakes their leaves while moving (`_BendRustle`, m): [GrassInteraction](GrassInteraction.md). |

## Flows

1. **Per camera**: GrassField's command buffer sets the foliage globals (wind, clock, 8 nearest benders + motion).
2. **Per vertex**: the root is the object origin. Lean = the grass's tip-weighted lean at the root × sway weight ×
   height, so the plant moves as one body; each limb adds a figure-eight on its own phase × limb weight; leaves
   flutter about their base, harder in gusts; benders push sideways and shake leaves as they move. Height lost to the lean comes off the top.
3. **Colour**: inner → outer and bottom → top gradient (`_ShellWeight`, `_HeightWeight`), per-plant variation hashed
   from the root position, per-limb shade, per-leaf jitter and tint, accents on outer leaf tips, gust brightening.

## Performance (M1 Pro, 1080p, editor; same settings on Windows — one "PC" quality level)

| Measure | Result |
| --- | --- |
| GPU per plant, all passes (instanced, GPU-bound) | Fir LOD0 18k tris ~59 µs, LOD1 ~21 µs, LOD2 ~2 µs; oak 38 / 14 / ~0; bush 6 / 3 / ~0 µs. ≈ 3.3 µs per 1k triangles |
| CPU per GameObject plant (editor) | ~10 µs, independent of triangles — the dominant cost in big scenes |
| 3000-plant woodland, player height | before the perf pass **+5.0 ms**; after: below the ±1 ms noise floor; ~350 drawn, 0.42 M tris ≈ 1.4 ms GPU (hidden under CPU in the editor) |
| Camera inside a willow/bush/fir | not measurable in the editor (GPU hidden under CPU); needs a player build |

Shaders compile for D3D11/12, Vulkan and Metal. Method: toggle plants every other frame, medians of `Render` ×3 +
1-px readback; GPU slopes from `RenderMeshInstanced` 1000 vs 3000. Never run on Windows or in play mode.

## Multiplayer

N/A — pure presentation: static scene objects (no NetworkObject); wind from the shared clock, benders local.

## Persistence

N/A — plants hold no runtime state; their placement lives in the scene / TerrainData.

## Gotchas

- **Never mark foliage Batching Static**: batched meshes lose their object origin, so the lean is
  measured from the world origin and plants swing metres. GPU instancing is the batching that works.
- **A plant's bark, leaves and fruit need identical wind values** — tune one material and not the
  others and branches drift off their leaves. Always pick all three from one wind profile (above).
- **Bushes part only for bodies with a `GrassBender`; plant bases need Read/Write**: [GrassInteraction](GrassInteraction.md).
- **No GrassField → no wind.** The globals are only set by a GrassField for a camera whose culling
  mask has its layer (and only while it has a biome). Without one the foliage stands still.
- **Terrain-painted trees only collide through a collider on the prefab ROOT.** The palms' collider
  is a tilted child (the trunk leans), so a *painted* palm has no collision; a dragged-in one does.
- **A TerrainData built in code loses its alphamaps if `SetAlphamaps` runs before
  `AssetDatabase.CreateAsset`** (the whole terrain comes back as layer 0): create the asset first.
- **Freshly painted biomes look bald in editor renders**: scripted `cam.Render()` needs ~100 frames.
- **Dark crown cores** come from the inner shell (`shell` 0) times `_InnerOcclusion`; on thin clumps
  (conifer skirts, cloud pads) they read as black slabs. Raise `_InnerColor`/`_InnerOcclusion`, shrink
  `core_scale`, or turn the core off (the willow has none, its curtains are `Blob.core = False`).
- **Wind every face to its custom normal**: Blender stores custom normals in the face's normal space, so one
  against the winding is mangled and caps, petals and pods render near-black. Pass `facing` to `MeshBuilder.face`.
- **A `Deco_PlanterBed_*` already holds plants**; its `Face_Plant` marker (1–2 m up) sets ours on its little tree.
- **`Tree_Bulb_B` (+ `_Ember`) is kept on purpose** (an unsaved `gameScene` placed one when the other bulb trees
  were removed, 2026-10-25); delete prefabs, FBX and `Coll_tree_bulb_b` once nothing places them.
- Editor screenshots: set `LODGroup.crossFadeAnimationDuration` tiny (else mid-dither), and call
  `RecordPrefabInstancePropertyModifications` after scripted moves (else a reimport snaps them home).

## Extending

- **New variant or species**: edit `vegetation.blend` (each `Coll_*` is a model; keep the UV layout
  and the three slots), export with `_exportlib.export_collections` to
  `Environment/nature/Vegetation/<name>.fbx`, set the importer's three material remaps
  (`Foliage_Bark`, `Foliage_Leaves`, `Foliage_Fruit`), `Normals: Import`, `Tangents: None`, then make a
  prefab variant of the model with the LOD thresholds and collider rules above.
- **New look** (autumn, dry season, glowing): make the material(s) with the same wind profile and a
  prefab variant that swaps only materials — the `_Autumn`/`_Golden`/`_Dry`/`_Ember`/`_Teal`/`_Glow`
  prefabs are the pattern.
- **Mass placement**: terrain *Paint Trees* with these prefabs works (instanced, per-plant colour
  variation from position) and is cheaper per plant than scene GameObjects. A paint-driven scatter
  like the grass's does not exist yet; at gallery densities it is the next performance step.
