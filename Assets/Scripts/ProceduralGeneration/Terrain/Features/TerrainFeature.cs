using UnityEngine;

/// <summary>
/// ====================================================================================
/// THE FEATURE CONTRACT — read this before implementing one of the nine terrain features.
/// ====================================================================================
///
/// One footprint-agnostic abstract base class for ALL terrain features — area and linear alike.
/// There is deliberately NO split into "AreaFeature" / "LinearFeature": every feature receives the
/// same <see cref="FeatureContext"/> and reads the parts it needs (the box bounds for area
/// features, the spline path for linear features). Maximum interchangeability is the #1 priority.
///
/// A concrete feature subclass implements exactly THREE members:
///
///   1. <see cref="FeatureType"/>      — the <see cref="TerrainFeatureType"/> enum entry it builds.
///   2. <see cref="DensityKind"/>      — Heightfield (cheap, surface = f(x,z): dunes, mesas,
///                                       buttes, cliffs, canyons, ridges, paths) or Voxel (real
///                                       overhangs only: arches, bridges, cave entrances).
///   3. <see cref="BuildDensity"/>     — return the <see cref="ITerrainDensity"/> describing the
///                                       feature's solid volume. For Heightfield, build a
///                                       <see cref="HeightfieldDensity"/> from a height lambda.
///                                       For Voxel, build a <see cref="VoxelSdfDensity"/> from an
///                                       SDF lambda (use the shared <see cref="SdfPrimitives"/>).
///
/// A feature NEVER touches marching cubes, smoothing, the skirt blend or asset saving — the shared
/// <see cref="TerrainFeatureGenerator"/> pipeline does all of that. The feature only describes its
/// SHAPE as a density field. This is what keeps the nine features small and interchangeable.
///
/// HELPERS available to every feature (use these, do not reinvent):
///   • <see cref="TerrainNoiseHelper"/>  — SurfaceNoise, ApplyJaggedness, Fbm, OverlapWeight,
///                                          VariedHeight, Hash01.
///   • <see cref="TerrainProfiles"/>     — Dune, Plateau, Ridge, CliffStep, CanyonCrossSection,
///                                          LimitSlope (walkability).
///   • <see cref="FeatureSpline"/>       — Evaluate, Tangent, ClosestParam (for linear features).
///   • <see cref="SdfPrimitives"/>       — Sphere, Capsule, SmoothMin, ApplyFloorFlatten (voxel).
///   • <see cref="FeatureContext.Ground"/> + LocalGroundHeight — sample the underlying terrain.
///
/// DETERMINISM: a feature must produce identical output for identical (seed, tuning, footprint).
/// Seed everything off <see cref="FeatureContext.Seed"/>; never read Time, Random.value, etc.
/// </summary>
public abstract class TerrainFeature
{
    /// <summary>Which <see cref="TerrainFeatureType"/> registry entry this subclass builds.
    /// Must be unique across features — the <see cref="TerrainFeatureRegistry"/> keys on it.</summary>
    public abstract TerrainFeatureType FeatureType { get; }

    /// <summary>Whether this feature needs the cheap 2D heightfield density or the full 3D voxel
    /// SDF. Choose Heightfield unless the feature has a genuine overhang — it is far cheaper and
    /// the performance mandate requires it where possible.</summary>
    public abstract TerrainDensityKind DensityKind { get; }

    /// <summary>
    /// THE method a feature implements. Given the context, return the density field describing the
    /// feature's solid volume.
    ///
    /// Heightfield feature pattern:
    ///   return new HeightfieldDensity(
    ///       (x, z) => groundHeight + profile(...) * height + TerrainNoiseHelper.SurfaceNoise(...),
    ///       footprintBounds, minY, maxY, bandPadding);
    ///
    /// Voxel feature pattern:
    ///   return new VoxelSdfDensity(
    ///       p => SdfPrimitives.SmoothMin(solidBlock(p), -tunnel(p), k),
    ///       volumeBounds);
    ///
    /// The returned density is fed straight to <see cref="TerrainMarchingCubesMesher"/>.
    /// </summary>
    public abstract ITerrainDensity BuildDensity(FeatureContext context);

    /// <summary>
    /// Optional hook run AFTER meshing + smoothing + skirt-blend, before the result is returned.
    /// Default does nothing. Override only if a feature needs a final mesh tweak (e.g. carving a
    /// flat walkable strip into a canyon path). Most features leave this alone.
    /// </summary>
    public virtual void PostProcess(Mesh mesh, FeatureContext context) { }

    /// <summary>
    /// Human-readable name for logs / inspector. Defaults to the feature type name; override only
    /// for a friendlier label.
    /// </summary>
    public virtual string DisplayName => FeatureType.ToString();

    // -------------------------------------------------------------------------
    // Per-feature settings hook.
    //
    // A feature may expose its OWN extra knobs (e.g. CliffFeatureSettings.faceWidthFraction) via a
    // small [System.Serializable] settings class. The spawner stores one such object, draws its
    // fields in the inspector, and injects it here before BuildDensity is called. Features with no
    // extra knobs simply leave these two members alone.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a fresh default instance of this feature's per-feature settings class, or null if
    /// the feature has no extra knobs. The spawner uses this to know the settings type and to
    /// seed a new settings object when the designer switches feature type.
    /// </summary>
    public virtual object CreateDefaultSettings() => null;

    /// <summary>
    /// Injects the designer-tuned per-feature settings object (produced earlier by
    /// <see cref="CreateDefaultSettings"/>) into this feature instance, before
    /// <see cref="BuildDensity"/> runs. A feature overrides this to cast the object to its own
    /// settings type and store it. Called with null when the spawner has no settings yet — the
    /// feature should fall back to its code defaults in that case.
    /// </summary>
    public virtual void ApplySettings(object settings) { }
}
