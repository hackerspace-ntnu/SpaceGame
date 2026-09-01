---
system: ProceduralGeneration
layer: world
summary: Edit-time marching-cubes terrain features, SDF caves and tile settlements, plus the runtime site registry
paths:
  - Assets/Game/Scripts/World/ProceduralGeneration/
  - Assets/Game/Editor/Terrain/
  - Assets/Game/Scripts/World/Sites/
  - Assets/Game/Scripts/World/Caves/
symptoms:
  - "a mesa or cliff bakes at the wrong ground height, or off-screen entirely"
  - "a terrain feature spawner produces no mesh and only logs a warning"
  - "a flat ground apron appears around a generated rock"
  - "a cave regenerates on Start and stalls play mode for seconds"
  - "the settlement or cave comes out different every time I regenerate"
  - "NPCs cannot walk on a rock or mesa I just generated"
  - "surface detail I dialled up does not show in the meshed feature"
reads_with: [WorldStreaming, NavMeshSystem, Environment, SceneTransitions]
updated: 2026-09-01
---

# Procedural World Generation

Three independent edit-time generators — marching-cubes **terrain features**, SDF **caves**, and tile-based **settlements** — plus the runtime **site registry** NPCs navigate by.

**Scope:** [`Assets/Game/Scripts/World/ProceduralGeneration/`](Assets/Game/Scripts/World/ProceduralGeneration) (Terrain, Cave, Settlement), [`World/Sites/`](Assets/Game/Scripts/World/Sites), [`World/Caves/`](Assets/Game/Scripts/World/Caves), [`Assets/Game/Editor/Terrain/`](Assets/Game/Editor/Terrain).
**Related:** [WorldStreaming.md](WorldStreaming.md) · [NavMeshSystem.md](NavMeshSystem.md) · [Environment.md](Environment.md) · [InteriorScenes.md](InteriorScenes.md)

## Model

- **Nothing generates the base heightmap.** Unity `Terrain` heightmaps are authored/sliced by the chunker ([`WorldChunkerEditor`](Assets/Game/Editor/World/WorldChunkerEditor.cs), `Tools/World Streaming/Chunk World`) — see [WorldStreaming.md](WorldStreaming.md). Everything here *adds* geometry on top of that terrain.
- **Terrain features** = designer drops a spawner, drags a footprint polygon, picks Mesa or Cliff → a density field → marching cubes → skirt-blended onto the terrain → **baked to a `.asset` mesh**. Runtime only instantiates.
- **Caves** = seeded random-walk room/corridor graph → smooth-min SDF → marching cubes → NavMesh. Baked mesh + `NavMeshData`; decoration/liquid/lights still run at spawn.
- **Settlements** = seeded height-map of grid cells → tile placements → prefab instantiation. Edit-time only, output lives in the scene.
- **Determinism**: every generator is a pure function of one `int` seed plus serialized settings. `System.Random(seed)` (cave graph, settlement) or hash/Perlin off the seed (terrain features). One exception — [`RobotSettlementGenerator`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Spawning/RobotSettlementGenerator.cs) uses global `UnityEngine.Random` and only calls `Random.InitState` when `useSeed` is ticked.
- **Authored, not generated**: [`WorldSiteMarker`](Assets/Game/Scripts/World/Sites/WorldSiteMarker.cs) components are hand-placed; they publish `WorldSite` records into a static registry NPCs query.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `TerrainFeature` | [Terrain/Core/TerrainFeature.cs](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Core/TerrainFeature.cs) | Abstract feature contract: `FeatureType`, `DensityKind`, `BuildDensity` (+ optional settings hooks) |
| `TerrainFeatureType` / `TerrainFeatureRegistry` | [Core/TerrainFeatureType.cs](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Core/TerrainFeatureType.cs), [Core/TerrainFeatureRegistry.cs](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Core/TerrainFeatureRegistry.cs) | Only two live entries: `Mesa = 2`, `Cliff = 4`. Enum→instance switch |
| `MesaFeature` / `CliffFeature` | [Features/](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Features) | Plateau silhouette; escarpment step. Both heightfield unless `overhang.enableOverhangs` |
| `FeatureContext` | [Core/FeatureContext.cs](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Core/FeatureContext.cs) | Immutable input bundle: `Seed`, `LocalBounds`, `Area`, `Tuning`, `Ground`, `LocalToWorld`, `VoxelSize`, `FootprintDistanceInside`, `LocalGroundHeight` |
| `TerrainFeatureTuning` | [Core/TerrainFeatureTuning.cs](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Core/TerrainFeatureTuning.cs) | Shared knobs: noise / overlap / height / jaggedness + 7 surface-detail dials + `keepWalkable`, `maxWalkableSlope` |
| `FeatureFootprint`, `FeaturePolygon`, `FootprintNoise` | [Terrain/Footprint/](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Footprint) | Box (width/height/breadth, default 80/50/80 m) + `FootprintMode.Polygon` (hand-edited) or `.Noise` (generated outline) |
| `ITerrainDensity`, `HeightfieldDensity`, `RockBodySdf` | [Terrain/Density/](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Density) | Signed field (`<0` solid). Heightfield = thin surface band; `RockBodySdf` = full 3D, real overhangs |
| `TerrainNoiseHelper`, `TerrainProfiles`, `RockBodyProfile` | [Terrain/Density/](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Density) | `SurfaceNoise`/`DetailLayer`/`OverlapWeight`/`Hash01`/`VariedHeight`; `Plateau`, `CliffStep`; `OverhangSettings` + radius modulation |
| `TerrainMarchingCubesMesher`, `TerrainMeshSettings`, `TerrainSkirtBlend` | [Terrain/Meshing/](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Meshing) | Iso-surface at 0; `voxelSize` 2 m, `surfaceBandVoxels` 3, 3 Laplacian iters, gradient normals; seam closer |
| `TerrainFeatureSpawner` / `TerrainGenManager` | [Terrain/Spawner/](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Spawner) | Per-feature scene driver; folder-level bulk conductor |
| `CaveGenerator`, `CaveSpawner`, `CaveProfile` | [Cave/Generation/](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Generation), [Cave/Profiles/CaveProfile.cs](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Profiles/CaveProfile.cs) | Entry point; scene driver; ScriptableObject bundling `shape`/`decoration`/`liquid`/`material` |
| `CaveGraphGenerator`, `CaveSdfField`, `MarchingCubesMesher` | [Cave/Graph/](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Graph), [Cave/Density/](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Density), [Cave/Meshing/](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Meshing) | Rooms+corridors random walk; spheres/capsules smooth-min + noise + floor flatten; shared MC tables |
| `DecorationScatterer`, `LiquidPoolFinder`, `CaveExitCover`, `AlgaePulse` | [Cave/Decoration/](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Decoration), [Cave/Liquid/](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Liquid), [World/Caves/AlgaePulse.cs](Assets/Game/Scripts/World/Caves/AlgaePulse.cs) | Triangle-surface prop scatter; flood-fill pools; entrance cap interactable; shader wave globals |
| `SettlementGenerator`, `SettlementLayout`, `SettlementBuilder` | [Settlement/Core/](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core) | 4-pass tile pipeline; heights + block roles; prefab instantiation |
| `SettlementPrefabConfig`, `SettlementGenerationSettings` | [Settlement/Config/](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Config) | ~25 prefab-variant arrays (tileSize 1, prefabs at 3× scale); footprint/height/density knobs |
| `WorldSite`, `SiteKind`, `WorldSiteRegistry`, `WorldSiteMarker` | [World/Sites/](Assets/Game/Scripts/World/Sites) | Position+radius+id record; 8 kinds; static registry with nearest/random queries |

## Flows

**Terrain feature (edit time → bake):**
1. `TerrainFeatureSpawner.BuildContext()` — resolves the terrain sampler (`targetTerrain` else `Terrain.activeTerrain`), calls `FeatureFootprint.Refresh(seed)`, builds the `FeatureContext`.
2. `TerrainFeatureRegistry.Create(type)` → `feature.ApplySettings(SyncFeatureSettings())`.
3. [`TerrainFeatureGenerator.Generate`](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Core/TerrainFeatureGenerator.cs): `AdaptSmoothingToDetail` (eases Laplacian smoothing as `detailStrength` rises) → `BuildDensity` → `TerrainMarchingCubesMesher.Build` → `TerrainSkirtBlend.Apply(embed: 1)`. Mesh named `<Type>_seed<N>`.
4. Editor **Bake & Save Mesh** writes `<scene folder>/TerrainFeatureBakes/<Type>_seed_<N>_<globalObjectId hex>_Mesh.asset` and assigns `bakedMesh`. Save the scene.

**Terrain feature (runtime):** `Awake` → if playing, enqueue on `ChunkActivationQueue.Shared` (spreads PhysX collider cooks across frames); `SpawnBaked()` when a baked mesh exists, else `GenerateNow()` (slow, iteration only). One child `TerrainFeature_<Type>_seed<N>` with MeshFilter/Renderer/MeshCollider on `featureLayer`.

**Cave:** `CaveGraphGenerator.Generate(settings, rng)` → `CaveSdfField` → `MarchingCubesMesher.Build` → (profile path) `LiquidPoolFinder.Find`. `CaveSpawner.Awake` uses baked mesh + `NavMeshData` when present, else generates live; then always spawns liquid pools, cluster lights, entrance tube + `CaveExitCover`, and runs `DecorationScatterer`.

**Settlement:** `SettlementGenerator.GenerateFull(seed, settings)` → `SettlementLayout.Build` (heights, block roles, colonnades, monolith base) → `EmitStructural` (floors, terrace edges, roofs, exterior-only corner pillars) → `SettlementInterior.Emit` (interior slabs + stairs) → `SettlementDetailPlacer.PlaceDetails` (walls, arches, colonnades, obelisks, clutter) → `SettlementBuilder.Build` instantiates, groups by material/chunk, optionally combines colliders.

**Editor entry points:**

| Trigger | Where | Effect |
| --- | --- | --- |
| `SpaceGame/Terrain/Apply Selected Material to All Terrains in Scene` | [ApplyTerrainMaterial.cs](Assets/Game/Editor/Terrain/ApplyTerrainMaterial.cs) | Only `MenuItem` in the terrain tooling |
| `Tools/World Streaming/Chunk World` | [WorldChunkerEditor.cs](Assets/Game/Editor/World/WorldChunkerEditor.cs) | Slices terrain into chunk scenes/`TerrainData` (streaming doc) |
| Inspector buttons: Regenerate Preview, Bake & Save Mesh, Clear Baked, Regenerate/Bake to Polygon/Reset Outline | [TerrainFeatureSpawnerEditor.cs](Assets/Game/Editor/Terrain/TerrainFeatureSpawnerEditor.cs) + [TerrainFeatureHandles.cs](Assets/Game/Editor/Terrain/TerrainFeatureHandles.cs) | Per feature; scene-view box + polygon handles |
| Inspector buttons: Bake All Meshes, Clear All Baked, Regenerate All, Spawn All Baked, Clear All, Apply Terrain/Layer To All, Auto-Assign Terrains | [TerrainGenManagerEditor.cs](Assets/Game/Editor/Terrain/TerrainGenManagerEditor.cs) | Whole folder of spawners |
| Inspector button: Bake & Save (mesh + navmesh) | [CaveSpawnerEditor.cs](Assets/Game/Editor/Terrain/CaveSpawnerEditor.cs) | Writes `<scene folder>/CaveBakes/seed_<N>_Mesh.asset` + `_NavMesh.asset` |
| Context menus: Generate / Clear / Reroll | `SettlementSpawner`, `RobotSettlementGenerator`, `CaveSpawner`, `TerrainFeatureSpawner`, `TerrainGenManager` | Component header right-click |

## Multiplayer

**Baked and shipped, not replicated.** Terrain-feature meshes and cave meshes/NavMeshData are `.asset` files referenced from chunk scenes; settlement output is plain scene GameObjects. Every machine loads the same bytes, so no netcode is involved and nothing here is a `NetworkBehaviour`. Cave decoration/liquid/light spawns run per machine but are seeded off `ctx.Seed ^ rule.seedSalt`, so they agree. Divergence risks: an *unbaked* `TerrainFeatureSpawner` or `CaveSpawner` regenerates locally (identical only because the seed is serialized — `CaveSpawner.randomSeedOnStart` breaks that outright), and `RobotSettlementGenerator` is an edit-time tool whose global-`Random` output must be committed to the scene, never rolled at runtime.

## Persistence

- **Nothing generated is saved.** Meshes are assets; placements are scene content. Regeneration is a designer action, not a load-time one.
- `WorldSiteRegistry` is pure runtime state, cleared on play start and on world unload. It is *not* serialized — but `WorldSiteMarker.id` is, and NPC saves reference it (`NpcGroup.Record.lastSiteId`), so the id is derived from scene+hierarchy via `SaveableEntity.DeriveAuthoredId` rather than a fresh GUID. Changing a marker's hierarchy path orphans saved references.
- See [Persistence.md](Persistence.md); the feature/cave/settlement systems register no savers.

## Gotchas

- `TerrainFeatureType` integers are **deliberately non-contiguous** (`Mesa = 2`, `Cliff = 4`) — twelve unused entries were deleted and scenes store the int. Never renumber; only append. A stale int makes `TerrainFeatureRegistry.Create` return null → warning, no mesh.
- **This doc's predecessor described features that no longer exist** (ArchingCave, BadlandsMaze, Boulders, dunes, spline/path features, `VoxelSdfDensity`, multi-mesh `BuildMeshes`, `FlatPadFeature`). Only Mesa and Cliff remain, single-mesh only.
- A null `targetTerrain` silently falls back to `Terrain.activeTerrain` — during a bulk bake that skirt-blends features onto the **wrong ground height** and they bake off-screen. Run `TerrainGenManager.AutoAssignTerrains` first.
- Bake filenames include a `GlobalObjectId` because two same-type/same-seed spawners previously baked to the same path and the second bake deleted the first's mesh asset.
- `featureSettings` is `[SerializeReference] object`; a newly-added nested reference field deserialises as `null` on old scenes — that is what `TerrainFeature.HealSettings` exists to repair.
- `TerrainFeatureSpawner.OnAfterDeserialize` migrates pre-rewrite fields; it tests the legacy polygon with `IsValid` (≥3 verts), because a non-null empty `[Serializable]` object would re-run migration forever and revert footprint edits.
- `HeightfieldDensity` without a `coverageFn` meshes the **whole box** — you get a flat ground apron around the feature. Uncovered columns must report `HeightfieldDensity.NoColumn` (`-1e9`).
- `TerrainSkirtBlend` uses a fixed 1.5 m `ContactBand` and deliberately ignores `overlap`; `overlap` means only the feature's own edge falloff (`TerrainNoiseHelper.OverlapWeight`).
- Surface detail finer than ~2× `voxelSize` cannot survive meshing; `AdaptSmoothingToDetail` already relaxes smoothing, so don't hand-tune both.
- `CaveSpawner` generates in `Awake`, not `Start`, so its entrance `InteriorAnchor` exists before `InteriorManager` places the player — moving it drops the player at the origin.
- `WorldSiteMarker` **never unregisters on disable** (a caravan may be walking to a site whose chunk unloaded); only `WorldSiteRegistry.Clear()` removes sites.
- Feature/cave meshes feed the shared world NavMesh through their `MeshCollider` layer — they get no isolated `NavMeshData` (except a baked cave's). The layer must be one the world surface collects; see [NavMeshSystem.md](NavMeshSystem.md).

## Extending

**New terrain feature type**
1. Append an entry to `TerrainFeatureType` with a fresh integer (do not reuse or reorder).
2. Subclass `TerrainFeature`: implement `FeatureType`, `DensityKind` (prefer `Heightfield`), `BuildDensity`. Copy [`MesaFeature`](Assets/Game/Scripts/World/ProceduralGeneration/Terrain/Features/MesaFeature.cs) as the template.
3. Shape the silhouette with `context.FootprintDistanceInside(x, z)` → `TerrainNoiseHelper.OverlapWeight`; never hardcode a box edge. Pass a `coverageFn` to `HeightfieldDensity`.
4. Seed everything off `context.Seed` (`Hash01`, `Fbm`); no `Time`, no `UnityEngine.Random`.
5. Optional knobs: a `[System.Serializable]` settings class + `CreateDefaultSettings` / `ApplySettings` (+ `HealSettings` if it nests reference blocks).
6. Add one arm to `TerrainFeatureRegistry.Create`. Spawner, inspector, handles and bake pipeline need no changes.
7. Bake in a chunk scene, verify the NavMesh rebuild, commit the mesh asset and the scene.

**New cave/settlement pass** — add a static pass class in the matching folder and call it from `CaveGenerator.Generate` / `SettlementGenerator.GenerateFull` after the existing passes; take `System.Random rng` (or the seed) as a parameter rather than creating your own, so the whole pipeline stays one seeded sequence.

**New site kind** — add a `SiteKind` entry, give it a gizmo colour in `WorldSiteMarker.KindColour`, place markers, and query it via `WorldSiteRegistry.TryFindRandom` (prefer random over `TryFindNearest`, which makes every NPC walk the same route).
