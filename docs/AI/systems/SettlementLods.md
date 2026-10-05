---
system: SettlementLods
layer: vehicles
summary: "Generated far levels for the Strider city and Sky fleet: one merged mesh per material, Mesh LODs, culled"
paths:
  - Assets/Game/Scripts/Vehicles/Lod/
  - Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs
  - Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs
  - Assets/Game/Settings/SettlementLodSettings.asset
  - Assets/Game/Editor/Tests/SettlementLodBakerTests.cs
  - Assets/Game/Editor/Tests/SettlementLodPrefabTests.cs
symptoms:
  - "the walking houses or the sky fleet cost hundreds of draw calls even far away"
  - "a merged far level shows parts of a machine inside out"
  - "a dead crab outrider or monowheel stands upright again when seen from a distance"
  - "Unity crashes with 'Could not allocate memory: System out of memory!' in MeshLodUtility.GenerateMeshLods while baking"
  - "a dune barge's merged level still has hundreds of submeshes"
  - "a merged level draws its parts in the wrong materials, or has one more material than submeshes"
reads_with: [VehicleDust, Striders, SkyTribe, ArtPipeline]
updated: 2026-10-05
---

# Settlement LODs

The Strider city's vehicles and the Sky fleet cost **draw calls**, not triangles (a walking house: 1024 renderers, 1024 material slots). Each prefab gets a generated `LODGroup`: LOD0 its own renderers, LOD1 one rest-pose mesh with **one submesh per material** that carries Unity 6.3 **Mesh LODs**, culled past a distance. Nothing is modelled by hand.

## Model

- **LOD0** = every `MeshRenderer`/`SkinnedMeshRenderer` under the root, untouched and animated. **LOD1** = child `LOD1_Merged`: those renderers' meshes (enabled, active, with a material) in the root's space, skinned ones `BakeMesh`ed in the prefab's pose, one submesh per distinct material; `MeshLodUtility.GenerateMeshLods(mesh, meshLodLimit)` adds simplified levels inside the same mesh, picked by screen size by the renderer itself.
- **Never in a level:** particle systems, trails, lines — smoke and dust run at every distance. Colliders, scripts and network components are untouched.
- **Distances, not heights.** `SettlementLodSettings.asset` holds a profile per settlement (`strider` 160 m merged / 1500 m culled; `sky` 400 / 8000) in metres; the baker converts each to a screen height from the prefab's own `LODGroup.size` × lossy scale, the reference FOV (60, the player default) and `QualitySettings.lodBias` (2): `ScreenHeightAt = size·0.5 / (d·tan(fov/2)) · lodBias`.
- **A wreck keeps full detail.** `MergedLod` forces LOD0 while the root's `HealthComponent` is dead (`OnDeath`/`OnRevive`/`OnRestored`), so a body lying where it fell is never drawn as its standing rest pose.
- **Coverage:** every Strider city vehicle (`StriderPrefabPaths`: habitat, crawler, crab, three barges, five Strider monowheels and the player's) and the Sky fleet (`SkyCity.prefab` — nested in `SkyCityFleet`, whose root has no group — and the escort hulls). Measured (renderers / material slots → merged submeshes; every one 5 Mesh LODs): habitat 1024/1024 → 49; crawler 112/382 → 18; crab 77/258 → 13; barges 428/1348 → 366, lookout 473/1453 → 442, compact 329/1028 → 334; monowheels 52–112/73–157 → 7–14; Sky city 310/1694 → 44 (2.2 M verts); freighter 12/55 → 20, skiff 10/61 → 24, tug 11/65 → 23.
- The merged mesh is saved beside the prefab as `<Prefab>_LOD1_Merged.asset` and **overwritten in place** (`CopySerialized`), so its GUID survives every rebuild.

## Key types

| Type | File | Role |
|---|---|---|
| `SettlementLodBaker` | [SettlementLodBaker.cs](Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs) | `Bake(root, prefabPath, profile)` (builders, before saving), `BakeAll` = `Tools/SpaceGame/Art/Bake Settlement LODs` (in place), `MergedMeshPath`, `ScreenHeightAt`, `StriderPrefabPaths`, `SkyPrefabPaths` |
| `SettlementLodSettings` | [SettlementLodSettings.cs](Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs) | `Profile { mergedBeyondMetres, cullBeyondMetres, meshLodLimit, referenceFovDegrees }`, `strider`, `sky`; `Load()` creates the asset with defaults |
| `MergedLod` | [MergedLod.cs](Assets/Game/Scripts/Vehicles/Lod/MergedLod.cs) | On the baked root: `Group`, `Mesh`, `Materials`, `MergedRenderer`; holds LOD0 while dead (`ForcedLevel`) |

## Flows

- **Builders:** each Strider builder adds far dust ([VehicleDust.md](VehicleDust.md)) and bakes right before `SaveAsPrefabAsset`; `SkyCityBuilder` after setting the root's 1.5 scale; `SkyFleetBuilder.BuildVessel` in place of the old single-level cull group. The Sky prefabs were baked in place, one per editor call (rebuilding them re-places the fleet in `persistentScene`).
- **Bake:** refuse a `LODGroup` below the root → delete the old `LOD1_Merged` → merge → generate Mesh LODs → save over the old asset → new child → `LODGroup` (the root's own, reused) with LOD0/LOD1 → heights from the profile → `MergedLod.Configure`.

## Multiplayer

N/A for the wire: a `LODGroup` switches renderers on each machine from its own camera. `MergedLod`'s dead/alive hold reads health, which already replicates.

## Persistence

N/A: generated assets and serialized prefab data only; no runtime state.

## Gotchas

- **A screen height above 1 is never reached.** A large prefab (the Sky city) can need a "height" over 1 at its merge distance; the baker clamps it to 0.999, which merges it a little *farther* out than asked, never nearer.
- **Never rewind a mirrored part yourself.** In Unity 6000.3 `Mesh.CombineMeshes` already reverses the winding of an instance whose matrix has a negative determinant (and transforms its normals), so a negative-scale part comes out the right way round as is; reversing its triangles first double-flips it inside out (`AMirroredPart_IsNotTurnedInsideOut` pins this).
- **A renderer in two `LODGroup`s draws twice**, so the baker throws on a group below the root. Bake the nested prefab instead (the Sky fleet's city). No Strider vehicle trips it: the crab's rider, the monowheels' drivers and gunners and the barges' nested hulls carry no group of their own.
- **Merging collapses shared materials only.** A submesh is one distinct `Material` asset, so the barges (one material per part: 1348 slots → 366–442 submeshes) gain far less than the habitat (1024 → 49). Sharing their materials is the lever, not the baker.
- **An empty part shifts every material after it.** Exported prefabs carry empty meshes (the crawler's `Circle`, the monowheels' bare `Mesh_Tube*`); `CombineMeshes` drops an empty part, so its material's slot pointed at the next material's submesh and the last slot at nothing. `Merge` skips a submesh with no indices (`AnEmptyPartsMaterial_GetsNoSlot_AndTheRestKeepTheirSubmeshes`).
- **Far dust stays outside every level.** `FX_FarDust` is a `ParticleSystemRenderer`, which `Bake` never puts in a level, so it keeps running while the vehicle draws its merged mesh; `SettlementLodPrefabTests` asserts no particle is in any level, and pins `FarDustCullDistance` to the `strider` cull (1500 m).
- **Mesh LOD selection inside a LODGroup: unverified.** Rendered off-screen in a preview scene (`Camera.Render`) at 700 m, the merged renderer drew exactly the same pixels as with `forceMeshLod = 0`, with the group forced to level 1 and with it free, while `forceMeshLod = 4` drew visibly coarser (habitat 271 px differ, Sky city 2612). So the levels exist, but the automatic pick stayed at 0 there; whether that is the LODGroup, the off-screen path or the threshold at 700 m needs a check in Play Mode (`lods_<Prefab>_merged700*.png`, 2026-10-05).
- **`GenerateMeshLods` is memory-hungry.** The Sky city's 2.2 M-vertex merge, or `BakeAll` over all 16 prefabs in one call, ran the editor out of memory on a 14 GB machine already near its limit (crash in `MeshLod::BuildClusterLodMeshFromMesh`, 2026-10-05). Bake one prefab per editor call when memory is short.

## Extending

- **Another prefab:** call `SettlementLodBaker.Bake(root, prefabPath, SettlementLodSettings.Load().<profile>)` as the builder's last step before `SaveAsPrefabAsset` (after anything that adds renderers), add its path to `StriderPrefabPaths`/`SkyPrefabPaths`, and it joins `SettlementLodPrefabTests`.
- **Retune:** edit `SettlementLodSettings.asset`, then run `Tools/SpaceGame/Art/Bake Settlement LODs`.
