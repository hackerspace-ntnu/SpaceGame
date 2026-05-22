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

## 4. The footprint — one authority, two modes

There is **one** `FeatureContext` for all nine features (no area/linear class split — maximum
interchangeability is the #1 design rule). It carries:

- `Area` — the AREA footprint authority (`FeatureFootprint`): box dimensions, mode and outline.
- `Footprint` — convenience accessor onto `Area.polygon`, the editable **closed polygon**
  (`FeaturePolygon`) in feature-local space.
- `Path` — the editable `FeaturePath` poly-line (linear features sweep along this; wrap it in a
  `FeatureSpline` to sample a smooth Catmull-Rom curve).
- `LocalBounds` — axis-aligned bounding box of whichever footprint is active. Used by the mesher
  for its voxel-walk extent; features should NOT read this for their silhouette.
- `Tuning` — the shared `TerrainFeatureTuning` knobs.
- `Ground` — an `ITerrainHeightSampler` over the underlying Unity Terrain.
- `LocalToWorld`, `Seed`, `VoxelSize`.

**Area features must shape their outline with `context.FootprintDistanceInside(x, z)`** — it
returns the signed distance to the footprint boundary (positive metres inside, negative outside).
Feed it straight into `TerrainNoiseHelper.OverlapWeight`. Do NOT recompute a box edge with
`Mathf.Min(dx, dz)` — that hardcodes a rectangle and ignores the designer's footprint.

A linear feature reads `Path` and uses `LocalBounds` only as an overall clamp.
`TerrainFeatureSpawner.UsesPath` decides which gizmo the editor shows (polygon vs spline).

### Authoring the area footprint

`FeatureFootprint` is the single authority. It owns the box — **Width (X) / Height (Y) /
Breadth (Z)** in metres — the `FootprintMode`, the outline `FeaturePolygon`, and the
`FootprintNoise` knob block. Every feature only ever calls `context.FootprintDistanceInside(x, z)`;
it never branches on the mode. The two modes:

- **`Polygon`** — hand-edited. In the Scene view: drag the blue vertex dots, **click anywhere on
  an edge** to insert a vertex there, use the **✕** button beside a dot to delete it. A green
  up-arrow sets the feature Height.
- **`Noise`** — the outline is *generated* by `FootprintNoise.Generate` from the Width × Breadth
  box plus a set of **explicit knobs**: `lobeFrequency`, `lobeAmplitude`, `detailOctaves`,
  `detailGain`, `irregularity`, `cornerSharpness`, `resolution`. All knobs minimal → a clean
  rounded blob; all knobs high → a wild, messy, multi-armed silhouette. No vertex editing; resize
  with the box handles and the outline regenerates live and deterministically off the seed. The
  **Bake to Polygon** button freezes the generated outline into editable Polygon-mode vertices.

`FeatureFootprint.Refresh(seed)` keeps the outline consistent with the mode (regenerates in Noise
mode, seeds an ellipse in Polygon mode); `MigrateFromLegacy` upgrades scenes authored before this
rewrite (old `boxHalfExtents` + `FootprintShape` + complexity dial).

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
- **Surface detail** — `detailStrength`, `detailScale`, `detailOctaves`, `detailRoughness`,
  `detailLacunarity`, `detailRidged`, `detailWarp`. An **independent high-frequency detail layer**
  added on top of every feature's surface — the central "how bumpy is the rock" control. It is
  SEPARATE from `noiseAmount` (the macro shape noise): a feature can be smooth-macro + jagged-detail
  or any mix. `detailStrength` is an **absolute metre amplitude** — 0 = glassy smooth, crank it for
  violently broken rock. See §7.

> **Smooth vs jagged terrain — how to tune it.** The detail layer is owned by the shared
> `Surface detail` knobs and applied centrally in `TerrainNoiseHelper.DetailLayer` (added by every
> feature, heightfield AND voxel), so the same dials reach all 14 features.
> - *Glassy smooth rock* — `detailStrength` 0 (the layer is fully off).
> - *Naturally bumpy rock* — `detailStrength` 1–3, `detailRoughness` ~0.5, `detailRidged` ~0.3.
> - *Harsh broken badlands* — `detailStrength` 6–15, `detailRoughness` 0.8+, `detailRidged` 0.6+,
>   `detailOctaves` 5–6, plus `jaggedness`.
>
> `detailStrength` is the master amplitude — the fractal is normalised, so adding octaves enriches
> the shape without silently changing the displacement. The post-marching-cubes Laplacian smoothing
> would wash out fine crags, so `TerrainFeatureGenerator` **auto-eases the smoothing** in proportion
> to `detailStrength` (`AdaptSmoothingToDetail`) — strong detail survives to the final mesh without
> the designer touching `TerrainMeshSettings`. Detail finer than ~2× `voxelSize` still cannot exist;
> shrink the voxel size for very fine crags.

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
- `SurfaceNoise(p, tuning, seed)` — total surface displacement in metres = the macro shape noise
  (`noiseAmount`) **plus** `DetailLayer`. Heightfield features add this straight to their height.
- `DetailLayer(p, tuning, seed)` — **the independent surface-detail layer, in metres.** Driven by
  the `Surface detail` knobs; `detailStrength` is the absolute amplitude (0 ⇒ returns 0). This is
  the one central place surface bumpiness lives — every feature adds it, so smooth-vs-jagged is
  tuned once.
- `DetailUnit(p, tuning, seed)` — the same shaped field normalised to ~[-1,1] (before the
  `detailStrength` metre scaling). Voxel features call this and apply their own **capped** amplitude
  so erosion can never punch through thin rock.
- `DetailedNoise(p, tuning, seed)` — back-compat alias of `DetailUnit`.
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
    FeatureFootprint.cs         THE area-footprint authority — box dims, mode, outline, noise knobs
    FootprintMode.cs            Polygon (hand-edited) vs Noise (generated) enum
    FootprintNoise.cs           explicit-knob noise → outline-polygon generator
    FeaturePolygon.cs           closed-polygon geometry — signed distance, containment, edge ops
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
    TerrainFeatureMeshSpawn.cs  mesh -> GameObject helper (single + multi-mesh)
  ArchingCave/                  the ArchingCave composite feature (see section 11)
    ArchingCaveSettings.cs      designer knobs
    ArchingCavePlan.cs          plan data structures (chambers, passages, hints)
    ArchingCavePlanner.cs       STAGE 1 — chamber graph plan
    ArchingCavePlacer.cs        STAGE 2 — keep-solid pillar hints + skylight holes
    ArchingCaveSdf.cs           STAGE 3 — one global carve-based signed distance field
    ArchingCaveChunker.cs       STAGE 3b — internal chunking into seamless sub-meshes
    ArchingCaveFeature.cs       the feature class (multi-mesh)
  BadlandsMaze/                 the BadlandsMaze composite feature (see section 12)
    BadlandsMazeSettings.cs     designer knobs + per-section feature toggles
    BadlandsMazePlan.cs         plan data structures (chambers, channels, mesas, boulders)
    BadlandsMazePlanner.cs      STAGE 1 — channel graph plan
    BadlandsMazePlacer.cs       STAGE 2 — mesa & boulder placement
    BadlandsMazeSdf.cs          STAGE 3 — one global SDF (massif MINUS carved channels)
    BadlandsMazeChunker.cs      STAGE 3b — internal chunking into seamless sub-meshes
    BadlandsMazeFeature.cs      the feature class (multi-mesh)
Editor/
  TerrainFeatureSpawnerEditor.cs  inspector: live preview + bake & save
  TerrainFeatureBakeUtility.cs    bake-asset writing (single + multi-mesh)
  TerrainFeatureHandles.cs        Scene-view box + spline footprint handles
```

---

## 11. ArchingCave — a large composite feature + the multi-mesh capability

`ArchingCave` (`TerrainFeatureType.ArchingCave = 11`) is a monumental CAVE-LIKE rock COMPLEX —
a cave whose ceiling has been opened up in places. It is an AREA feature — it uses the footprint
polygon as the site extent and is deliberately NOT in `TerrainFeatureSpawner.UsesPath`.

**The model — carve the cavity out of solid rock (not unioned blobs).** The ArchingCave is built
the SAME way as the cave system's `CaveSdfField`, and the OPPOSITE of the old (rejected) model.
The old model unioned solid pillar/arch/canopy primitives floating in air — a blobby kit of
parts. The new model:

1. starts from a SOLID ROCK MASSIF — everything from a ceiling height down is rock;
2. CARVES the open walkable space out of it — chambers (spheres / vertical capsules of open
   space) joined by passages (capsules), all `SmoothMin`-unioned into ONE connected cavity, then
   SUBTRACTED from the rock (`SmoothMax(rockBlock, -cavity)`);
3. the rock that SURVIVES the carve is the geometry — cave-like walls, emergent PILLARS (rock
   left standing between adjacent cavities) and emergent ARCHES (rock spanning over a passage).
   Pillars and arches are leftover rock, exactly like mesa overhangs — never placed solids.

**The four-stage "careful planning" pipeline** (plan the top-order structure first, then place):

1. **Chamber graph plan** (`ArchingCavePlanner`) — a guided random walk (like `CaveGraphGenerator`)
   drops irregular chambers across the footprint and joins them into ONE connected graph of
   walkable passages. Each chamber carries CONTINUOUS parameters (openness, pillar density,
   canopy amount, height scale, radius) — no discrete zone types, no uniform sizes. It also fixes
   `FloorY` (terrain height under the footprint centre) and `CeilingY` (rock-roof height).
2. **Structure placement** (`ArchingCavePlacer`) — for each chamber it scatters KEEP-SOLID pillar
   HINTS (spots the carve avoids, so rock columns survive — count/size driven by the chamber's
   continuous params) and SKYLIGHT holes for canopied chambers. No solids are placed.
3. **Global carve SDF** (`ArchingCaveSdf`) — solid rock block, minus the chamber/passage cavity,
   minus skylight shafts; pillar hints intersect the cavity so columns survive; the cavity floor
   is `ApplyFloorFlatten`-ed walkable and the walls are domain-warp eroded.
4. **Internal chunking** (`ArchingCaveChunker`) — the site is split into a grid of sub-tiles;
   each tile's portion of the SDF is meshed separately into its own sub-mesh. All tiles sample
   the SAME global SDF on a SHARED voxel lattice and are padded one voxel, so the sub-meshes seam
   together perfectly. (Unchanged from the old model — it only ever needed `sdf.Sample`/`Bounds`.)

**Open vs canopied ceilings** — each chamber's cavity ceiling is interpolated by its `Openness`:
an OPEN chamber's cavity is carved straight up PAST `CeilingY` so the roof rock is fully removed
and sky/light pour in; a CANOPIED chamber's cavity stops below `CeilingY` so a rock roof survives,
then skylight shafts are carved up through that roof for dappled light. The mix is preserved.

**Walkability** — the carved cavity floor is flattened (`ApplyFloorFlatten`, depth 0) to a level
plane at `FloorY` across every chamber and passage, so the cavity is one connected slope-limited
walkable surface a NavMeshAgent can cross end to end. The rock block is solid all the way down,
so there is always solid rock beneath the floor. Surviving pillars are NavMesh obstacles routed
around; surviving arches are overhead — never walked on.

**The multi-mesh capability** — ArchingCave is the first feature to emit MORE than one mesh. The
feature system was extended minimally and backward-compatibly:

- `TerrainFeature` gained `virtual bool ProducesMultipleMeshes => false` and
  `virtual List<Mesh> BuildMeshes(...)  => null`. The eleven other features override neither and
  keep the unchanged single-mesh `BuildDensity` path.
- `TerrainFeatureResult` gained an optional `List<Mesh> SubMeshes` (+ `IsMultiMesh`). The single
  `Mesh` field and the single-mesh `IsValid` path are unchanged.
- `TerrainFeatureGenerator` checks `ProducesMultipleMeshes`: when true it takes a `GenerateMultiMesh`
  branch (calls `BuildMeshes`, collects sub-meshes); otherwise it runs the original 4-pass pipeline
  verbatim.
- `TerrainFeatureSpawner` spawns one child GameObject (MeshFilter+MeshRenderer+MeshCollider) per
  sub-mesh — every sub-mesh collider feeds the world NavMesh. Baked sub-meshes live in a separate
  `bakedSubMeshes[]` field; the single `bakedMesh` field is untouched for the other features.
- `TerrainFeatureSpawnerEditor` / `TerrainFeatureBakeUtility` save one mesh asset per sub-mesh
  for a multi-mesh bake, and the unchanged single asset for everything else.

---

## 12. BadlandsMaze — a labyrinth of mesas carved by a wide eroded river system

`BadlandsMaze` (`TerrainFeatureType.BadlandsMaze = 12`) is a monumental desert rock COMPLEX. Like
`ArchingCave` it works by carving — it starts from a SOLID rock massif and SUBTRACTS a wide,
branching channel network — the "wide river system that ran through here". (ArchingCave carves an
enclosed-but-breached cave cavity; BadlandsMaze carves open sunken channels.) The rock that
survives between the channels is a dynamic maze of mesas with undercut overhangs and stratified
cliff bands. It is
meant to be VIEWED FROM THE BOTTOM: the player walks the sunken channel floors and looks up at
the towering walls; boulders and small rocks litter the floor alongside the massif.

It is an AREA feature — it uses the footprint polygon as the maze extent and is deliberately NOT
in `TerrainFeatureSpawner.UsesPath`. It is a MULTI-MESH feature (overrides `ProducesMultipleMeshes`
+ `BuildMeshes`), reusing the same internally-chunked sub-mesh capability ArchingCave introduced.

**The four-stage "careful planning" pipeline** (same shape as ArchingCave):

1. **Channel graph plan** (`BadlandsMazePlanner`) — a guided random walk drops open CHAMBERS
   (pools / junctions) across the footprint and joins them into ONE connected graph of meandering
   walkable CHANNELS. Chamber radii are scattered non-uniformly; each channel gets a meander
   control point so the river beds snake. This graph is the CARVED VOID, not the rock.
2. **Structure placement** (`BadlandsMazePlacer`) — scatters MESA anchors into the rock that
   survives BETWEEN the channels (an anchor is kept only if it lands on solid rock, clear of every
   carved void), each with a varied top height and footprint radius; then litters BOULDERS across
   the open channel floors.
3. **Global SDF** (`BadlandsMazeSdf`) — realises the plan as one signed distance field: the union
   of the mesa lumps (the massif) MINUS the carved chamber/channel void, plus a walkable floor
   slab and the boulders, all `SmoothMin`/`SmoothSubtract`-blended and domain-warp eroded into one
   cohesive water-eroded sandstone mass.
4. **Internal chunking** (`BadlandsMazeChunker`) — identical seamless sub-tile chunker to
   `ArchingCaveChunker`: the site is split into a grid, each tile meshed from the SAME global SDF
   on a SHARED voxel lattice with one-voxel overlap padding, so the sub-meshes seam perfectly.

**The mesas are real Mesa rock-bodies** — every mesa in the maze is shaped by the EXACT same
model `MesaFeature` uses for its overhang branch: `BadlandsMazeSdf.MesaDensity` mirrors
`RockBodySdf.Sample` (radial tower) step-for-step, driving the radius with
`RockBodyProfile.RadiusMultiplier`. The body's cross-section varies with height and angle, so each
mesa bulges, pinches, leans and undercuts all the way around as a consequence of its body shape —
genuine overhanging mesas, not a single placed shelf. The maze only supplies each mesa's axis,
footprint radius and top height; the shaping is the shared Mesa code. The knobs live in the
`mesaBody` field of `BadlandsMazeSettings` — an `OverhangSettings` block, the same type
`MesaSettings` embeds, so a designer tunes maze mesas with the identical controls as a standalone
Mesa feature.

**Walkability** — the carved channel floor is one continuous solid slab at a single `FloorY`
(`channelDepth` below the surrounding terrain rim), threading every chamber disc and channel
ribbon, so it is a slope-free walkable surface a NavMeshAgent can cross end to end. The mesas rise
from it as obstacles routed around; overhanging rims are elevated scenery.

**Feature toggle** — `BadlandsMazeSettings.enableBoulders` gates the scattered small-rock field;
`mesaBody.enableOverhangs` (on by default) gates the rock-body overhang model on every mesa. Each
gates a clearly delimited block of the SDF / placer.

---

## 13. Boulders — a noise-scattered field of natural rock boulders

`Boulders` (`TerrainFeatureType.Boulders = 13`) scatters a field of natural, eroded rock BOULDERS
across its footprint. ONE feature with many knobs (`BouldersSettings`) — the designer customises
scatter density, clustering, size distribution, per-boulder shape and grounding. It is an AREA
feature: it uses the footprint polygon as the scatter region and is deliberately NOT in
`TerrainFeatureSpawner.UsesPath`. It is a normal single-mesh feature — `BuildDensity` returns one
voxel `BouldersSdf` and the shared pipeline meshes / skirt-blends it.

**Voxel SDF model.** A boulder is a rounded 3D rock that bulges over its contact point — a genuine
overhang a heightfield cannot represent — so `DensityKind` is `Voxel`. The field is the
`SmoothMin` union of N individual boulders. The volume's Y extent is kept modest (ground span to
the tallest boulder + padding) since boulders are not tall.

**Per-boulder shape** (`BouldersSdf.BoulderSdf`) — reads as weathered rock, not a sphere:
a lumpy core of `lumpiness` `SmoothMin`-blended sub-spheres, evaluated in the boulder's own space
after un-yawing and un-squashing (per-axis flatten + shape-variety stretch), then domain-warp /
fbm erosion (`NoiseDistortion.DomainWarpedFbm`, sharpened by the shared `jaggedness`) added to the
distance so the surface is faceted-but-rounded. Each boulder gets a deterministic random size,
stretch, yaw and noise phase, and sinks into the ground by `embedDepth` so it rests partially
buried.

**Noise-driven scatter** (`BouldersScatter`) — a jittered grid thresholded by a low-frequency
density-noise field: cells sized off `density`, one jittered candidate per cell, kept only when a
per-cell random roll beats a threshold that tracks the cluster-noise. `clustering` sets the
contrast (0 = even scatter, 1 = tight clumps with bare gaps). Candidates outside the polygon or
thinned by `edgeFalloff` near the rim are dropped. All deterministic off `context.Seed`.

**Spatial acceleration** — a naive union is O(N) per `Sample`. The `BouldersSdf` constructor
buckets every boulder into a uniform XZ grid (cell ≈ the largest boulder reach); `Sample` tests
only the 3×3 cell neighbourhood of the query point, so it stays ~O(boulders in the neighbourhood)
no matter how large the field. A ground-fill half-space (gated to below-ground voxels under a
boulder) grounds each rock without meshing a flat apron.

**Knobs** (`BouldersSettings`): `density`, `clustering`, `clusterFrequency`, `edgeFalloff`,
`sizeRange`, `sizeBias`, `irregularity`, `shapeNoiseFrequency`, `lumpiness`, `flattenAmount`,
`shapeVariety`, `embedDepth`, `blendRadius`. The shared tuning still applies — `height` scales
every boulder's size, `jaggedness` sharpens the erosion.
