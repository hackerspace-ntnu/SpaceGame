using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// ====================================================================================
    /// THE FEATURE CONTRACT — read this before implementing a terrain feature.
    /// ====================================================================================
    ///
    /// One abstract base class for all terrain features. Every feature receives the same
    /// <see cref="FeatureContext"/> and reads the parts it needs, which keeps features
    /// interchangeable.
    ///
    /// A concrete feature subclass implements exactly THREE members:
    ///
    ///   1. <see cref="FeatureType"/>      — the <see cref="TerrainFeatureType"/> enum entry it builds.
    ///   2. <see cref="DensityKind"/>      — Heightfield (cheap, surface = f(x,z)) or Voxel (only when
    ///                                       the surface genuinely folds back on itself, i.e. real
    ///                                       overhangs).
    ///   3. <see cref="BuildDensity"/>     — return the <see cref="ITerrainDensity"/> describing the
    ///                                       feature's solid volume. For Heightfield, build a
    ///                                       <see cref="HeightfieldDensity"/> from a height lambda.
    ///                                       For Voxel, build a <see cref="RockBodySdf"/>.
    ///
    /// A feature NEVER touches marching cubes, smoothing, the skirt blend or asset saving — the shared
    /// <see cref="TerrainFeatureGenerator"/> pipeline does all of that. The feature only describes its
    /// SHAPE as a density field. This is what keeps features small and interchangeable.
    ///
    /// HELPERS available to every feature (use these, do not reinvent):
    ///   • <see cref="TerrainNoiseHelper"/>  — SurfaceNoise, ApplyJaggedness, Fbm, OverlapWeight,
    ///                                          VariedHeight, Hash01.
    ///   • <see cref="TerrainProfiles"/>     — Plateau, CliffStep.
    ///   • <see cref="RockBodySdf"/>         — eroded rock-body SDF shared by mesa and cliff.
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
        /// Voxel feature pattern (real overhangs):
        ///   return new RockBodySdf(lineA, lineB, reach, ground, summit,
        ///       context.LocalGroundHeight, volumeBounds, overhangSettings, seed);
        ///
        /// The returned density is fed straight to <see cref="TerrainMarchingCubesMesher"/>.
        /// </summary>
        public abstract ITerrainDensity BuildDensity(FeatureContext context);

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

        /// <summary>
        /// Repairs a previously-serialized settings object in place, returning a usable instance.
        ///
        /// <para><b>Why this exists.</b> The spawner stores the settings object in a
        /// <c>[SerializeReference]</c> field. When a feature's settings class later gains a NEW
        /// reference-type field, Unity reconstructs already-serialized instances through the
        /// serializer — NOT the constructor — so that field's <c>= new T()</c> initialiser never runs
        /// and it deserialises as <c>null</c>. A feature whose settings class has nested blocks should
        /// override this to null-fill those blocks, so old scenes pick up the new knobs. The default
        /// returns the object unchanged. Returns null only if <paramref name="settings"/> is null.</para>
        /// </summary>
        public virtual object HealSettings(object settings) => settings;
    }
}
