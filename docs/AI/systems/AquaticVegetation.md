---
system: AquaticVegetation
layer: presentation
summary: 56 sea plants in eight ocean zones that rock on the swell, plus five paintable seabed grass biomes
paths:
  - Assets/Game/Art/Models/_Source~/models/nature/vegetation_aquatic.blend
  - Assets/Game/Art/Models/Environment/nature/Vegetation/Aquatic/
  - Assets/Game/Art/Materials/Environment/Foliage/Aquatic/
  - Assets/Game/Prefabs/Environment/Nature/Vegetation/Aquatic/
  - Assets/Game/Scenes/Tests/AquaticGallery.unity
  - Assets/Game/Scenes/Tests/AquaticGrassGallery.unity
  - Assets/Game/Terrain/AquaticGrassGallery/
  - Assets/Game/Editor/Environment/OceanShowcaseBuilder.cs
  - Assets/Game/Editor/Environment/OceanShowcaseTerrain.cs
  - Assets/Game/Editor/Environment/OceanShowcasePlanting.cs
  - Assets/Game/Scenes/Tests/OceanShowcase.unity
symptoms:
  - "a coral's grooves or base-to-tip shading are flat although its mesh shells vary"
  - "the kelp tops hang in mid-water instead of lying along the surface"
  - "a floating sargassum or rosette sits half buried in the sea floor"
  - "sea plants lean with the wind instead of rocking to and fro under water"
  - "kelp and sea grass stand completely still under water"
reads_with: [Foliage, Grass, GrassBiomes, VegetationFamilies, StylizedWater, BarkMaps]
updated: 2026-10-28
---

# Aquatic Vegetation

56 sea plants (50 species, 6 kelps added later) plus 3 colour variants, one prefab each, sized for the 3 m
player and grouped by where they grow, and 5 seabed grass biomes painted like any other grass. Same shader,
wind, LODs and per-vertex contract as [Foliage.md](Foliage.md); new are the shapes, the **surge** (the swell
rocking everything to and fro) and a performance budget.

## Model

- **Source**: [`vegetation_aquatic.blend`](Assets/Game/Art/Models/_Source~/models/nature/vegetation_aquatic.blend),
  one `Coll_<Prefab>` per plant with `<Prefab>_LOD0/1/2`, slots `Foliage_Bark/Leaves/Fruit`, UV0–UV3 as in
  Foliage.md, custom normals. It is the only copy; the generator lives in no repo.
- **FBX** `Environment/nature/Vegetation/Aquatic/<lowercase>.fbx`; **materials** `Foliage/Aquatic/Mat_Foliage_Aqua_<name>`
  (96); **prefabs** `Prefabs/Environment/Nature/Vegetation/Aquatic/<Prefab>`.
- **Origin**: the sea floor, except the two **surface** plants (origin on the **water line**). Kelp heights are
  fixed: `Tree_Giantkelp_A` ~32 m of water, `_C` (storm-bent) ~28 m, `_B` ~22 m, `Tree_Bullkelp_A` ~20 m,
  `Tree_Elkkelp_A` ~18 m, or the canopies stop short of (or poke through) the surface.
- **Budget** (LOD0 triangles): small plants 0.7–4.4k, hero corals and fans ≤ 6k, giant kelp 5–7.3k; mean
  3k / LOD1 1.2k / LOD2 0.35k. Down ~50 % from the first build: every primitive's resolution is scaled
  (grids 0.55, blade rows 0.6, rope sides 0.75) and the heaviest species carry fewer parts.

| Zone | Prefabs (LOD0 k tris) |
| --- | --- |
| **Shore / intertidal** | Plant_Glasswort_A 4.2, Plant_Seapalm_A 2.8, Plant_Wrack_A 3.4, Plant_Sealettuce_A 2.0, Plant_Cordgrass_A 3.1, Plant_Coralline_A 3.4, Plant_Tidebell_A 2.5 (alien), Plant_Wrackmat_A 2.0 |
| **Lagoon** | Plant_Seagrass_A 2.0, Plant_Seagrass_Patch_A 4.1 (6 m tile), Plant_Turtlegrass_A 2.2, Plant_Seagrapes_A 3.3, Plant_Mermaidcup_A 1.4, Plant_Coinweed_A 0.7, Plant_Shoalfern_A 3.0 (alien) |
| **Coral reef** | Bush_Staghorn_A 3.2, Bush_Staghorn_B 4.1, Plant_Braincoral_A 3.6, Plant_Tablecoral_A 3.0, Plant_Scrollcoral_A 0.8, Plant_Seafan_A 6.0, Plant_Seafan_Lace_A 5.9, Plant_Pillarcoral_A 4.1, Plant_Leathercoral_A 1.4, Plant_Anemone_A 3.9, Plant_Anemone_Carpet_A 2.8, Plant_Seawhip_A 2.5, Plant_Barrelsponge_A 1.2 |
| **Kelp forest** | Tree_Giantkelp_A 7.3, _B 5.2, _C 6.9 (storm-bent), Tree_Bullkelp_A 3.0, Tree_Elkkelp_A 0.7 (antlers + hanging blades), Plant_Kelp_Young_A 2.5, Plant_Oarweed_A 1.3, Plant_Featherboa_A 4.0, Plant_Sugarkelp_A 1.4, Plant_Kelp_Spiny_A 1.3, Plant_Kelp_Palm_A 1.3 (tree-like), Plant_Kelp_Ribbon_A 1.2, Plant_Kelp_Strap_A 0.7 (surge-zone straps), Plant_Kelpholdfast_A 3.7; variants Tree_Giantkelp_A_Golden, Tree_Giantkelp_B_Cold, Plant_Kelp_Young_A_Cold |
| **Rocky reef** | Plant_Redweed_A 4.4, Plant_Tubesponge_A 1.7, Plant_Seapen_A 1.4, Plant_Sealily_A 3.7 |
| **Twilight slope** | Plant_Glasssponge_A 5.8, Plant_Glassrope_A 3.1, Plant_Bamboocoral_A 4.2, Plant_Blackcoral_A 2.1, Plant_Harpsponge_A 2.5 |
| **Abyssal vents** | Plant_Ventfleece_A 3.8, Plant_Chimneyfern_A 3.3 (alien), Plant_Abyssbulb_A 3.1 (alien) |
| **Surface** | Plant_Sargassum_A 3.6, Plant_Floatrosette_A 3.9 (alien) |

- **Seabed grass** ([GrassBiomes.md](GrassBiomes.md)): paint `Ground_Seagrass` (1–1.8 m buoyant blades),
  `Ground_AlgaeTurf` (0.2–0.35 m olive turf), `Ground_RedFringe` (0.6–1.1 m fine red-brown hair algae),
  `Ground_KelpSprouts` (0.6–1.1 m juvenile kelp), `Ground_VentFloc` (0.15–0.3 m pale bacterial floc). Their
  profiles set `growsUnderwater`, draw only 120–200 m (the underwater haze hides more) and carry surge.
- **Wind profiles** (lean, limb sway m, limb speed, flutter m, flutter speed, bender push m | surge strength m per m,
  period s, wave length m): rigid (0.008, 0.03, 0.7, 0.03, 4, 0.1 | 0.003, 8, 30), ground and the shore's bush /
  stem (Foliage.md values | 0), **kelp** (0.03, 0.4, 0.8, 0.15, 3, 0.4 | 0.1, 6, 35), **giantkelp** (0.015, 0.5, 0.6,
  0.15, 2.5, 0 | 0.05, 8, 45), **seagrass** (0.03, 0.15, 1, 0.1, 3.5, 0.8 | 0.14, 5, 30), **seaweed** (0.02, 0.12, 0.8,
  0.08, 3, 0.4 | 0.09, 6, 35), **current** (0.01, 0.22, 0.6, 0.05, 2.5, 0.2 | 0.12, 6, 35), **floating** (0.01, 0.15,
  0.5, 0.05, 2, 0 | 0.04, 5, 25). Submerged profiles lean little on the wind: the swell moves them. Stony corals and sponges (rigid) and floor debris
  (ground) stay still on purpose; the shore's glasswort and cordgrass stand in air (wind only); sea palm and tidebell are seaweed.
- **Materials**: copies of `Bark_Ridged` / `Broadleaf` / `Fruit_Coral`, muted colours, no emission; bark maps
  ([BarkMaps.md](BarkMaps.md)) Pored on corals and sponges at 3 tiles/m strength ~0.45, Beaded on fans and whips,
  Ringed on stipes, `Cap_Mottled` on the leather coral; moss as algae turf or vent sulfur.
- **LODs**: thresholds from `Tree_Broadleaf_A` (`Tree_*`) or `Bush_Round_A`; LOD1 halves resolution and drops
  polyps, tertiary fan branches, lace links and most floats; LOD2 keeps the silhouette.
- **Colliders** (root capsule): brain, table, pillar and leather corals, barrel and tube sponges. Nothing else.
- **Galleries**: [AquaticGallery](Assets/Game/Scenes/Tests/AquaticGallery.unity) (every prefab in zone rows, own
  GrassField so they sway) and [AquaticGrassGallery](Assets/Game/Scenes/Tests/AquaticGrassGallery.unity) (a 150 m
  seabed terrain painted in five bands, one per seabed biome). Neither has water.
- **OceanShowcase** (`SpaceGame ▸ Environment ▸ Build Ocean Showcase`, scene in Build Settings so Play works): a 1.2 km
  seabed (beach → intertidal → lagoon → reef crest/fore-reef → kelp plateau | rocky reef → drop-off → basin + vent field),
  9 `Water_Ocean` tiles (400 m, 1 m cells, underwater depth 75 m), the GrassField, ~3,100 plants as **terrain trees**
  (instanced; `treeDistance` 320) placed per species by zone, depth band, ground and patch noise, canopy kelps scaled so
  their tops reach the surface, plus ~30 floating GameObjects. Play: `SpectatorCamera` (WASD, Space/Ctrl, Shift ×5).

## Key types

| Asset | Role |
| --- | --- |
| [vegetation_aquatic.blend](Assets/Game/Art/Models/_Source~/models/nature/vegetation_aquatic.blend) | The 56 models; export with `_exportlib.export_collections`. |
| `WaterSurge` in [GrassShared.hlsl](Assets/Game/Art/Shaders/Environment/GrassShared.hlsl) | The swell: `sin` of time/period minus distance-along-current/wave length, plus a crossing overtone. |
| `_SurgeStrength/_SurgePeriod/_SurgeLength` | On `StylizedFoliage` (added to the lean, × sway weight × height) and `StylizedGrass` (added to the compute's sway). 0 on every land material. |
| `GrassFieldProfile.growsUnderwater` | Lets a biome grow below the field's `minimumHeight` water line. |

## Flows

1. **Per vertex** (foliage) / **per tuft** (grass compute): `lean += currentDir × surge × WaterSurge(root.xz, …)`. The
   current is the wind direction; the clock is the GrassField's, so a bed of plants rocks in a ripple rolling along it.
2. **Shapes**: Blade (buoyant/drooping strap with fold, cup, ruffle, pucker, twist), Grid (any parametric surface),
   Rope (tapered tube, rounded cap), Bead; fans march primaries/secondaries/tertiaries inside an ellipse; families'
   lattice, pinnate fronds and bottlebrushes are reused.

## Multiplayer

N/A — static props; the swell is a pure function of the shared clock and position, identical on every machine.

## Persistence

N/A — no runtime state; placement lives in the scene or TerrainData.

## Gotchas

- **Play in a test scene outside Build Settings drops you at the main menu** (Bootstrapper); the builder adds
  OceanShowcase to the list. Its rebuild overwrites the scene — hand edits there are lost.
- **No GrassField, no motion**: wind, swell and clock all come from it. Galleries carry their own; opening one
  additively next to gameScene doubles the grass until it is closed (the field warns).
- **A bark-based material ignores the shell ramp** (`Bark_Ridged`: shell weight 0, height 1, no variation); the
  aquatic bark materials set 0.55 / 0.45 / 0.35 (brain coral 0.85 / 0.25), or corals shade flat.
- **Tubes must not flutter** (flutter runs along the normal: a radial-normal rope pulses); ropes move by limb sway only.
- **Everything in a sea fan shares one limb**, or the lace links pull away from their branches.
- **A Bead's LOD1 was finer than its LOD0** once LOD0 was scaled down; LOD1 is now clamped to LOD0.
- **The grass generator only makes pointed leaves**: a "sea lettuce" biome read as agave rosettes and became
  KelpSprouts. Round, ruffled sheets need the prefab (`Plant_Sealettuce_A`).
- **A seabed biome below a field's `minimumHeight` grows only with `growsUnderwater`**; its far ground colour is
  still cut at that height (`GrassGroundTint`), so a field with a water line shows bare bed far away.
- Surge seen moving (render diffs with wind zeroed) but never in play mode, under the underwater pass, on a client or
  on Windows; nothing is placed in a game scene.

## Extending

- **New species**: add a builder to the scratchpad generator (or hand-model in the .blend), export its collection,
  make a prefab with the LOD/collider rules above and three materials sharing one profile, surge included.
- **New look**: copy the leaves material, recolour, prefab variant swapping it (the `_Golden`/`_Cold` kelps).
- **Stronger or calmer sea**: tune `_SurgeStrength` / `_SurgePeriod` per material; keep a plant's three equal.
