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
symptoms:
  - "the walking houses or the sky fleet cost hundreds of draw calls even far away"
  - "a merged far level shows parts of a machine inside out"
  - "a dead crab outrider or monowheel stands upright again when seen from a distance"
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
- The merged mesh is saved beside the prefab as `<Prefab>_LOD1_Merged.asset` and **overwritten in place** (`CopySerialized`), so its GUID survives every rebuild.

## Key types

| Type | File | Role |
|---|---|---|
| `SettlementLodBaker` | [SettlementLodBaker.cs](Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs) | `Bake(root, prefabPath, profile)` (builders, before saving), `BakeAll` = `Tools/SpaceGame/Art/Bake Settlement LODs` (in place), `MergedMeshPath`, `ScreenHeightAt`, `StriderPrefabPaths`, `SkyPrefabPaths` |
| `SettlementLodSettings` | [SettlementLodSettings.cs](Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs) | `Profile { mergedBeyondMetres, cullBeyondMetres, meshLodLimit, referenceFovDegrees }`, `strider`, `sky`; `Load()` creates the asset with defaults |
| `MergedLod` | [MergedLod.cs](Assets/Game/Scripts/Vehicles/Lod/MergedLod.cs) | On the baked root: `Group`, `Mesh`, `Materials`, `MergedRenderer`; holds LOD0 while dead (`ForcedLevel`) |

## Flows

- **Bake:** refuse a `LODGroup` below the root → delete the old `LOD1_Merged` → merge → generate Mesh LODs → save over the old asset → new child → `LODGroup` (the root's own, reused) with LOD0/LOD1 → heights from the profile → `MergedLod.Configure`.

## Multiplayer

N/A for the wire: a `LODGroup` switches renderers on each machine from its own camera. `MergedLod`'s dead/alive hold reads health, which already replicates.

## Persistence

N/A: generated assets and serialized prefab data only; no runtime state.

## Gotchas

- **A screen height above 1 is never reached.** A large prefab (the Sky city) can need a "height" over 1 at its merge distance; the baker clamps it to 0.999, which merges it a little *farther* out than asked, never nearer.
- **Never rewind a mirrored part yourself.** In Unity 6000.3 `Mesh.CombineMeshes` already reverses the winding of an instance whose matrix has a negative determinant (and transforms its normals), so a negative-scale part comes out the right way round as is; reversing its triangles first double-flips it inside out (`AMirroredPart_IsNotTurnedInsideOut` pins this).
- **A renderer in two `LODGroup`s draws twice**, so the baker throws on a group below the root. Bake the nested prefab instead (the Sky fleet's city).

## Extending

- **Another prefab:** call `SettlementLodBaker.Bake(root, prefabPath, SettlementLodSettings.Load().<profile>)` as the builder's last step before `SaveAsPrefabAsset` (after anything that adds renderers), add its path to `StriderPrefabPaths`/`SkyPrefabPaths`, and it joins `SettlementLodPrefabTests`.
- **Retune:** edit `SettlementLodSettings.asset`, then run `Tools/SpaceGame/Art/Bake Settlement LODs`.
