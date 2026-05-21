# Procedural Terrain Feature System

Architecture foundation for placing procedural terrain features (dunes, mesas, buttes, cliffs,
canyons, canyon paths, ridges, natural bridges, stone arches, cave entrances) onto the existing
Unity Terrain of the desert ancient-planet open world.

This document is the brief for **feature-implementing agents**. Read the "Adding a New Feature"
section — it is the exact contract you fill.

---

## 1. Overview

A designer drops a `TerrainFeatureSpawner` on a GameObject, drags a Scene-view gizmo (a resizable
**box** for area features, an editable **spline path** for linear features), picks a feature type,
and tunes shared sliders (noise, overlap, height, jaggedness). The system generates a smooth
marching-cubes mesh that blends onto the existing terrain with no floating gap and no hard seam.

The foundation reuses the cave system's proven pieces: the marching-cubes lookup tables
(`MarchingCubesTables`), the SDF primitives (`SdfPrimitives`), the noise functions
(`NoiseDistortion`), and the mesh smoothing (`MeshSmoothingUtility`).

It is **editor-baked**: an editor button bakes the mesh to a saved asset; runtime just instantiates
it — near-zero runtime cost, exactly like `CaveSpawner`'s baked path.

---

## 2. Pipeline

`TerrainFeatureGenerator.Generate(feature, context, meshSettings)` runs four passes:

1. **`TerrainFeature.BuildDensity(context)`** — the feature describes its solid volume as an
   `ITerrainDensity` field (negative = solid, positive = air, zero = surface).
2. **`TerrainMarchingCubesMesher.Build(density, meshSettings)`** — voxelises and extracts the
   iso-surface. For a *heightfield* density it walks only a thin band straddling the surface
   (cost scales with footprint **area**); for a *voxel* density it walks the full volume.
3. **`TerrainSkirtBlend.Apply(mesh, context, …)`** — snaps the mesh's lower band of vertices down
   onto the underlying terrain so there is no floating edge and no hard seam.
4. **`TerrainFeature.PostProcess(mesh, context)`** — optional per-feature final mesh tweak.

The feature only does step 1. Everything else is shared and identical for all features.

---

## 3. Density model — heightfield vs voxel

A feature declares which density model it needs via `TerrainFeature.DensityKind`:

| Kind          | Implementation        | Use for                                              | Cost            |
|---------------|-----------------------|------------------------------------------------------|-----------------|
| `Heightfield` | `HeightfieldDensity`  | dunes, mesas, buttes, cliffs, canyons, ridges, paths | scales w/ area  |
| `Voxel`       | `VoxelSdfDensity`     | natural bridges, stone arches, cave entrances        | scales w/ volume|

**Choose `Heightfield` unless the feature has a genuine overhang.** It is far cheaper and the
performance mandate requires it where possible. A heightfield's surface is a single height
function `f(x, z)`; a voxel SDF's surface can fold back over itself.

---

## 4. The footprint — one context, both shapes

There is **one** `FeatureContext` for all nine features (no area/linear class split — maximum
interchangeability is the #1 design rule). It carries:

- `LocalBounds` — the resizable box, in feature-local space (area features mesh inside this).
- `Path` — the editable `FeaturePath` poly-line (linear features sweep along this; wrap it in a
  `FeatureSpline` to sample a smooth Catmull-Rom curve).
- `Tuning` — the shared `TerrainFeatureTuning` knobs.
- `Ground` — an `ITerrainHeightSampler` over the underlying Unity Terrain.
- `LocalToWorld`, `Seed`, `VoxelSize`.

An area feature reads `LocalBounds` and ignores `Path`. A linear feature reads `Path` and uses
`LocalBounds` only as an overall clamp. `TerrainFeatureSpawner.UsesPath` decides which gizmo the
editor shows.

---

## 5. Tuning parameters

`TerrainFeatureTuning` is the shared serializable block on every spawner. The four mandated knobs:

- **Noise** — `noiseType`, `noiseAmount`, `noiseScale`, `domainWarpStrength`. How much organic
  surface variation and at what scale.
- **Overlap** — `overlap`, `overlapFalloff`. Width and curve of the edge falloff band; drives how
  features blend into the terrain and merge with neighbours.
- **Height** — `height`, `heightVariation`. Primary vertical extent plus deterministic per-feature
  variation.
- **Jaggedness** — `jaggedness`. Sharpens ridges/cliffs vs soft rounded forms.

Plus **walkability**: `keepWalkable`, `maxWalkableSlope` — slope control so NavMeshAgents can
traverse climbable ridges, narrow canyon paths and natural bridges. Use `TerrainProfiles.LimitSlope`
in a feature's height function to enforce these.

A feature may add its own extra serialized fields, but must read the four core knobs from here.

---

## 6. Adding a new feature — THE CONTRACT

Implement a subclass of `TerrainFeature` (see `FlatPadFeature.cs` as the reference template).
You implement exactly three members:

```csharp
public sealed class SandDunesFeature : TerrainFeature
{
    public override TerrainFeatureType FeatureType => TerrainFeatureType.SandDunes;
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Heightfield;

    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        // Build and return a HeightfieldDensity (or VoxelSdfDensity).
        // ... see helpers below ...
    }
}
```

Then register it with **one line** in `TerrainFeatureRegistry.RegisterBuiltIns()`:

```csharp
Register(() => new SandDunesFeature());
```

Nothing else — spawner, editor, gizmo and pipeline all work automatically.

**Heightfield feature pattern:**

```csharp
return new HeightfieldDensity(
    (x, z) => {
        float ground = context.LocalGroundHeight(x, z);
        float profile = TerrainProfiles.Dune(...);          // shape
        float noise   = TerrainNoiseHelper.SurfaceNoise(new Vector3(x,0,z), context.Tuning, context.Seed);
        float weight  = TerrainNoiseHelper.OverlapWeight(distInside, context.Tuning);
        return ground + (profile * height + noise) * weight;
    },
    footprintBounds, minY, maxY, bandPadding);
```

**Voxel feature pattern (overhangs):**

```csharp
return new VoxelSdfDensity(
    p => SdfPrimitives.SmoothMin(solidBlock(p), -tunnelCapsule(p), k),
    volumeBounds);
```

**Rules:**
- Be deterministic — seed everything off `context.Seed`; never read `Time`, `Random.value`, etc.
- Do not touch marching cubes, smoothing, skirt-blend or asset saving — the pipeline owns those.
- Reserve your `TerrainFeatureType` enum entry (already present; do not reorder existing entries).

---

## 7. Shared helpers — use these, do not reinvent

**`TerrainNoiseHelper`** (`Density/`)
- `SurfaceNoise(p, tuning, seed)` — noise displacement in metres, jaggedness applied.
- `ApplyJaggedness(n, jaggedness)` — sharpen noise into ridges/crags.
- `Fbm(p, frequency, seed, octaves)` — multi-octave fractal noise.
- `OverlapWeight(distanceInside, tuning)` — 0→1 edge falloff for blending/overlap.
- `VariedHeight(baseHeight, tuning, seed)` — apply deterministic per-feature height variation.
- `Hash01(seed, salt)` — deterministic per-feature random value in [0,1].

**`TerrainProfiles`** (`Density/`)
- `Dune(t, crestBias)` — asymmetric barchan dune profile.
- `Plateau(edgeDistance, wallFraction)` — flat-topped mesa/butte silhouette.
- `Ridge(t, sharpness)` — ridge / canyon-wall cross-section.
- `CliffStep(t, edge, width)` — smooth escarpment step.
- `CanyonCrossSection(lateralFraction, floorFlat)` — U-shaped canyon depth profile.
- `LimitSlope(height, neighbourHeight, stepDistance, maxSlope)` — walkability slope clamp.

**`FeatureSpline`** (`Spline/`) — for linear features
- `Evaluate(t)`, `Tangent(t)` — sample the smooth Catmull-Rom curve.
- `ClosestParam(point, out lateralDistance, out closestPoint)` — for heightfield linear features:
  distance-to-centre-line so the feature can apply its cross-section profile.

**`SdfPrimitives`** (cave system, reused) — for voxel features
- `Sphere`, `Capsule`, `SmoothMin`, `ApplyFloorFlatten`.

**`TerrainSkirtBlend`** (`Meshing/`) — the pipeline calls `Apply` for you; `SealOpenBottom` is
available if a feature needs a closed underside.

**`FeatureContext.LocalGroundHeight(x, z)`** — underlying terrain height in feature-local Y.

---

## 8. Bake workflow

On a `TerrainFeatureSpawner`'s inspector (`TerrainFeatureSpawnerEditor`):

1. Pick the feature type. A banner warns if it has no implementation registered.
2. Drag the Scene-view box/spline handles to define the footprint, tune the sliders.
3. **Live preview** toggle — regenerates the in-scene mesh on every change.
4. **Bake & Save Mesh** — generates at edit time and writes the mesh to
   `TerrainFeatureBakes/<Type>_seed_<N>_Mesh.asset` next to the scene. The spawner's `bakedMesh`
   field is auto-assigned. Save the scene; runtime then skips generation entirely.

At runtime, `TerrainFeatureSpawner.Awake` calls `SpawnBaked()` when a baked mesh is assigned
(near-zero cost) or `GenerateNow()` otherwise (slow — for iteration only).

---

## 9. NavMesh integration

Feature meshes **contribute to the shared world NavMesh** — they do **not** get an isolated
`NavMeshData`. The spawner gives the spawned mesh a `MeshCollider` on the configured `featureLayer`.
The world's `NavMeshSourceCache` collects colliders on the streamed chunks (with
`NavMeshCollectGeometry.PhysicsColliders`) and feeds them into the world `NavMeshSurface` rebuild,
so the feature becomes one source among many in a single unified walkable surface.

For this to work a feature mesh must be: a valid non-empty mesh, on a layer the world
NavMeshSurface collects, and `isReadable` (procedural meshes are readable by default). Use the
`keepWalkable` / `maxWalkableSlope` tuning plus `TerrainProfiles.LimitSlope` so agents can actually
climb the walkable parts.

---

## 10. File map

```
Terrain/
  Core/
    TerrainFeatureType.cs       enum registry of the 9 features + FlatPad stub
    TerrainDensityKind.cs       Heightfield vs Voxel enum
    TerrainFeatureTuning.cs     shared noise/overlap/height/jaggedness + walkability
    FeatureContext.cs           footprint-agnostic input bundle for every feature
    TerrainFeatureResult.cs     pure-data output bundle
    ITerrainHeightSampler.cs    underlying-terrain height/normal sampler (+ Unity impl)
    TerrainFeatureGenerator.cs  the orchestration entry point (4-pass pipeline)
  Density/
    ITerrainDensity.cs          signed density field interface
    HeightfieldDensity.cs       cheap 2D-heightfield density
    VoxelSdfDensity.cs          full 3D voxel SDF density (overhangs)
    TerrainNoiseHelper.cs       shared noise + overlap-falloff helpers
    TerrainProfiles.cs          shared dune/plateau/ridge/cliff/canyon profile functions
  Meshing/
    TerrainMeshSettings.cs      MC resolution + smoothing settings
    TerrainMarchingCubesMesher.cs  MC adapted for terrain (surface-band walk, outward normals)
    TerrainSkirtBlend.cs        snaps the mesh's lower band onto the terrain
  Spline/
    FeaturePath.cs              serializable editable poly-line path
    FeatureSpline.cs            Catmull-Rom evaluation of a FeaturePath
  Features/
    TerrainFeature.cs           THE abstract base class — the feature contract
    TerrainFeatureRegistry.cs   type -> feature factory; feature agents register here
    FlatPadFeature.cs           reference stub feature — copy as a template
  Spawner/
    TerrainFeatureSpawner.cs    scene-level MonoBehaviour driver
Editor/
  TerrainFeatureSpawnerEditor.cs  inspector: live preview + bake & save
  TerrainFeatureHandles.cs        Scene-view box + spline footprint handles
```
