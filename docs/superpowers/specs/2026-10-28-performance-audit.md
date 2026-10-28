# Performance audit — 2026-10-28

Whole-game review starting from `gameScene` (the Spillexo world), plus the Sky City and the
Striders' walking city. Six parallel read-only audits: scene content, vegetation, AI, runtime
scripts, rendering configuration, and the two cities. A read-only census of the live Editor
with `gameScene` loaded, in edit mode, backs up the scene numbers.

**What this is and is not.** Every number here was measured from assets, YAML and code, or
from the Editor census. **None of it is a Play Mode profiler capture.** The findings show where
cost sits and how much content drives it, not milliseconds per item, except where an existing
doc already records a measured timing (marked *measured timing*). Confidence tags:
**measured** = counted from files or the Editor; **code** = read from source; **inferred** =
reasoned, not counted.

The fix plan is in [2026-10-28-performance-cheap-fixes.md](../plans/2026-10-28-performance-cheap-fixes.md).
Each finding has a stable ID, which the plan uses.

## Context

- `gameScene.unity` is the Spillexo world: a single scene with no `WorldStreamer`, so
  **everything in it stays resident**. Editor census:
  - 21,238 transforms, 9,911 MeshRenderers, 386 SkinnedMeshRenderers.
  - 131 lights, 886 MeshColliders, 1,766 LODGroups, 6,258 MonoBehaviours.
  - 92 NetworkObjects and 74 agents.
- The streamed story world is `persistentScene` plus 48 chunk scenes (load radius 1, so 3×3
  chunks of 500 m each).
- Active pipeline: quality "High", `PC_RPAsset`, and `PC_Renderer`. Forward+, HDR, MSAA off,
  shadows 2048 at 50 m in 2 cascades with soft shadows High, SRP Batcher on, GPU Resident
  Drawer off. `ProjectConfig.md` records 23.8 ms (High) and 19.9 ms (Low) in the editor.

## GPU / rendering

| ID | Finding | Numbers | Conf. |
| --- | --- | --- | --- |
| PERF-R01 | SSAO forces a full-resolution DepthNormals prepass: every opaque, grass included, is drawn twice. Grass forward never samples SSAO. | `PC_Renderer` SSAO: Source DepthNormals, Downsample **off**, Blur **High**. Tall grass +3.0–3.6 ms (*measured timing*, Grass.md), dominated by overdraw (`Cull Off`, no depth priming). | measured / code |
| PERF-R02 | Walking city at close range. | About 9k material-slot draws within 160 m and 2.36 M tris. `dune_barge.fbx` embeds 611 materials, only 50 distinct, so merged barge LODs keep 334–442 submeshes. About 40 crew × 28 skinned slots, no LODGroup. | measured |
| PERF-R03 | Sky City stays at LOD0 out to 400 m, and its "far" LOD is heavier than LOD0. | 311 renderers, 1,738 submesh draws, 1.22 M tris, all casting shadows. `SkyCity_LOD1_Merged` has 2.2 M verts against 615 k in `sky_city.fbx`, drawn from 400 m to 8 km. Whether Mesh LOD actually switches inside a LODGroup is **unverified**. | measured |
| PERF-R04 | gameScene terrain. | 8 × 8 km, 4097² heightmap, 13 layers. `drawInstanced` off (required: `StylizedTerrain` has no instancing path). Shadows **TwoSided**. Pixel error 5. Drawn in the main pass, the prepass and 2 cascades. | measured |
| PERF-R05 | 8,718 terrain-painted trees; 4 of 6 prototypes are raw FBX on importer-default LODs, not the tuned prefab variants. | `bush_bonecoral_a` 6,574; `tree_starburst_b` 981; `Bush_Spoonleaf_A` 1,083. Densest: 1,514 within 150 m. | measured |
| PERF-R06 | Nomad-family characters are built from 43–68 SkinnedMeshRenderers each, with no LOD. | `Nomad.prefab` 68, `SkyNomad_*` 43. 79 agent prefabs have more than 10 SMRs. | measured |
| PERF-R07 | A second directional light in both world scenes, beside `Sun.prefab`. | Evaluated on every lit pixel and stays lit at night. Only `PersistentSceneVisibility` turns it off, and only for interiors. | measured |
| PERF-R08 | Realtime point lights clustered in one place. | `Workshop.prefab` 116 (range 18 m); the worst 3×3 chunk window has 178. None cast shadows. `DistanceCulledLights` turns them off beyond about 168 m. | measured |
| PERF-R09 | No occlusion culling baked in any scene. | `m_OcclusionCullingData: {fileID: 0}` in all 50 scenes, although about 10.6k objects are flagged Occluder/Occludee static. | measured |
| PERF-R10 | Walking city distant silhouette is never hidden by distance. | 17 MeshRenderers, about 1,380 submeshes, shadows on, no LODGroup. Visible to the 5 km far clip. The city parks 298 m from the gameScene spawn. | code |
| PERF-R11 | Planar water reflection renders with the full `PC_Renderer`, SSAO included. | Every 2 frames at 0.5 scale, 300 m (*measured timing* about 0.85 ms, StylizedWater.md). | code |
| PERF-R12 | Big and Massive rocks do shadow vertex work far beyond the shadow distance. | Shadows cast out to 300 m and 900 m; shadow distance is 50 m. | inferred |
| PERF-R13 | 2,555 unique materials in gameScene, nearly all URP/Lit, with instancing off. | SRP Batcher still batches them, but GPU Resident Drawer is off, so each renderer is submitted separately. | measured |
| PERF-R14 | 20 looping engine-smoke ParticleSystems use `cullingMode: AlwaysSimulate`. | On SkySkiff, SkyTug, SkyFreighter and SkyCityFleet; DesertCrawler adds 2. | measured |

## CPU

| ID | Finding | Numbers | Conf. |
| --- | --- | --- | --- |
| PERF-C01 | `AgentGroundConform` casts 5 multi-hit rays per agent per frame. It runs while dormant, off-screen and on clients, and its `groundMask` is Everything. | About 150 agents × 5 ≈ 750 `RaycastNonAlloc` per frame on every machine. [AgentGroundConform.cs:166](../../../Assets/Game/Scripts/Agents/animation/AgentGroundConform.cs) | code |
| PERF-C02 | Walking city unfolds and folds in one frame. | About 6,600 GameObjects and 59 NetworkObjects, spawned synchronously (`NpcWorldSim.cs:748`). Spawn radius 250 / despawn 360 m, with the city 298 m from spawn. | code |
| PERF-C03 | No AI level of detail inside the 360 m awake radius. | Dormancy parks only `AgentController`, `AgentTargeting` and `PerceptionModule`. Group members and vehicles never sleep. | code |
| PERF-C04 | 41 agent prefabs use `AlwaysAnimate`. 37–42 Shellback villagers in Chunk_2_1 have no `DistanceDormant`. | All 28 wild creatures, plus Appa, Esko, the Caraxoid family, RobotHorse, PatrolRobot, ClankerOutrider and the Shellback villager and mechanic. | measured |
| PERF-C05 | Pathing requests. | `SetDestination` fires on every 0.2 m of target movement (`NavMeshAgentMotor.cs:698`). Legged and vehicle motors run a synchronous `CalculatePath` plus 2 × 20 m `SamplePosition` every 0.5–1 s (`NavPathFollower.cs:160`). | code |
| PERF-C06 | Legged locomotion raycasts. | Up to legs + 6 rays per frame (12 for six-legged bodies), plus 8 per step. About 80 per frame for the walking city. | code |
| PERF-C07 | Moving collider compounds. | SkyCityFleet: one non-interpolated kinematic body with 666 colliders. RigWalker: 33 BoxColliders with **no Rigidbody**, moved every frame, which counts as moving static colliders. Barges: 71–117 convex MeshColliders each. | measured |
| PERF-C08 | About 11 non-agent callbacks per NPC per frame. `Network.Owns` / `Simulates` call `GetComponentInParent<NetworkObject>()` on every call. | RagdollRig ×3, UnderTerrainGuard ×2 (calls `Simulates` *before* its 0.25 s throttle), EyeBlink, CharacterActions, IdleVariation, and others. 239 `Network.*` call sites. | code |
| PERF-C09 | Autosave stalls the main thread every 300 s. | `BuildDocument` plus pretty-printed `ToJson` run on the main thread. `TryReadHeader` does a full `File.ReadAllText` + `JObject.Parse` of the old save just to read its version. Saves are 0.9–1.2 MB. World select parses all 46 saves the same way. | code |
| PERF-C10 | Physics project settings are fully permissive. | Layer collision matrix is all `ff`, so UI, Hologram, PackItem and Schematic collide with everything. `queriesHitTriggers` is on. | measured |
| PERF-C11 | Netcode defaults. | 305 of 325 NetworkTransforms use full floats, reliable deltas, scale sync and a 1 mm threshold. `NetworkMessageMetrics` and `NetworkProfilingMetrics` are on. | measured |
| PERF-C12 | HUD churn. | One HUD canvas re-batched every frame by `VisorSway`. `HelmetDangerVignette` makes about 72 string-keyed `SetFloat` calls per frame, even at zero alpha. `VisorGauge` calls `ToString` × 3 per frame. `DuneFoilHUD` formats speed every frame. | code |
| PERF-C13 | Spikes. | Ragdoll built lazily on the death frame. Grass `Rebuild` about 90 ms plus 13.6 MB of allocations on the first frame (*measured timing*). Streaming leaves `backgroundLoadingPriority` Low, even during the loading screen. | code |
| PERF-C14 | Small per-frame waste. | `SuitOxygen.Publish` writes 3 NetworkVariables every frame. `VisorOverlayController` calls `FindObjectsByType<Light>` every frame while none is found. `Interactor` calls `LayerMask.GetMask` every frame. `ShipDriftProbe` (F9 debug) is installed in every build. `VolumeTrigger.OnTriggerStay` logs. `AgentAnimatorDriver` / `AggressionTelegraphModule` hash parameter strings every frame. | code |

## Memory

| ID | Finding | Numbers | Conf. |
| --- | --- | --- | --- |
| PERF-M01 | Merged far-LOD meshes are Read/Write, which keeps a CPU copy as well as the GPU copy. | 16 `*_LOD1_Merged.asset`, about 300 MB on the GPU plus the same again on the CPU. 12 are in `DefaultNetworkPrefabs`, so they load at boot. | measured |
| PERF-M02 | `MeshReadablePostprocessor` forces Read/Write on all 1,881 FBX. Deliberate (runtime NavMesh, grass bases, ropes). | Roughly doubles mesh RAM. | measured |
| PERF-M03 | `Settlement.terrainBackup` is serialized into player builds. | 7.5 MB of Chunk_5_3's 7.9 MB; about 0.7–0.8 MB in each of gameScene, Chunk_2_1 and Chunk_6_3. | measured |
| PERF-M04 | `Resources.UnloadUnusedAssets` never runs after a chunk unloads. | Chunk-unique TerrainData and textures stay resident. | code |
| PERF-M05 | Rock visible buffers are sized for every segment. | About 40× their real need, about 28 MB in total. | inferred |

## Editor-only distortion

- Grass, rocks and the volumetric features also run for `SceneView` cameras, so a Play Mode
  profile taken with the Scene view open roughly doubles their cost.
- At audit time `AquaticGrassGallery.unity` was loaded additively next to `gameScene`.

## Found broken (not performance)

- Chunk_5_3 references a missing prefab, GUID `e5ccf4995d6294533a19fa920e647845`.
- `Assets/Game/Scenes/References/Spillexo.asset` has `sceneName: NoiseTerrain`, but the scene
  is now `gameScene.unity` (same GUID).

## Next measurement

Take a Play Mode profile with the Scene view closed at four places: the gameScene spawn, beside
the walking city, under the Sky City, and inside tall grass. Add one capture of an autosave
frame. That turns the ranking above into milliseconds.
