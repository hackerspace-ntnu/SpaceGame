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
3. **`TerrainSkirtBlend.Apply(mesh, context, embed)`** — closes the geometric SEAM where the mesh
   meets the terrain (lifts punch-through vertices, buries a thin contact band). It uses a small
   FIXED band and is NOT driven by `overlap` — the feature's own `OverlapWeight` owns the soft edge.
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

- `Footprint` — the editable **closed polygon** (`FeaturePolygon`), in feature-local space. AREA
  features get their organic outline from this.
- `Path` — the editable `FeaturePath` poly-line (linear features sweep along this; wrap it in a
  `FeatureSpline` to sample a smooth Catmull-Rom curve).
- `LocalBounds` — axis-aligned bounding box of whichever footprint is active. Used by the mesher
  for its voxel-walk extent; features should NOT read this for their silhouette.
- `Tuning` — the shared `TerrainFeatureTuning` knobs.
- `Ground` — an `ITerrainHeightSampler` over the underlying Unity Terrain.
- `LocalToWorld`, `Seed`, `VoxelSize`.

**Area features must shape their outline with `context.FootprintDistanceInside(x, z)`** — it
returns the signed distance to the polygon boundary (positive metres inside, negative outside).
Feed it straight into `TerrainNoiseHelper.OverlapWeight`. Do NOT recompute a box edge with
`Mathf.Min(dx, dz)` — that hardcodes a rectangle and ignores the designer's polygon.

A linear feature reads `Path` and uses `LocalBounds` only as an overall clamp.
`TerrainFeatureSpawner.UsesPath` decides which gizmo the editor shows (polygon vs spline).

### Authoring the area footprint

The polygon is edited **visually in the Scene view** — never as raw vertex fields. The spawner's
`footprintShape` picks how the outline is defined:

- **`Polygon`** — hand-edited. Drag the blue vertex dots; the `+`/`-` buttons beside each add or
  remove vertices. A green up-arrow sets feature height.
- **`Noise`** — the outline is *generated* by `FeaturePolygon.GenerateFromNoise` from two knobs:
  `footprintNoiseScale` (lobe frequency — how wiggly) and `footprintIrregularity` (the
  "rectangleness" — 0 stays near the box, 1 is wildly organic). No vertex editing; resize with the
  box handles and the outline regenerates live.

Either way a feature just calls `context.FootprintDistanceInside(x, z)` — it never cares which
mode produced the polygon.

### Per-feature settings

Beyond the four shared `TerrainFeatureTuning` knobs, a feature may expose its own knobs via a
small `[System.Serializable]` settings class (e.g. `CliffFeatureSettings.faceWidthFraction`). To
make those reachable from the spawner inspector, override two members on `TerrainFeature`:

```csharp
public override object CreateDefaultSettings() => new MyFeatureSettings();
public override void ApplySettings(object settings) => Settings = settings as MyFeatureSettings;
```

The spawner stores the settings object (`[SerializeReference]`), the editor draws its fields
automatically, and the spawner injects it via `ApplySettings` before `BuildDensity` runs. A
feature with no extra knobs simply omits both overrides. Always null-guard the injected settings
(`ApplySettings(null)` is valid) and fall back to code defaults.

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
        // Outline + edge falloff come from the polygon footprint — NOT a box edge.
        float distInside = context.FootprintDistanceInside(x, z);
        float weight  = TerrainNoiseHelper.OverlapWeight(distInside, context.Tuning);
        return ground + (profile * height + noise) * weight;
    },
    context.LocalBounds, minY, maxY, bandPadding);
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
