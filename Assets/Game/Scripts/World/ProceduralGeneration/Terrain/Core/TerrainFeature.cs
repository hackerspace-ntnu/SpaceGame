using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
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
        /// True if this feature deliberately authors geometry BELOW the terrain surface (e.g. a cave
        /// entrance whose tunnel descends underground). The generator then SKIPS the terrain skirt
        /// blend for this feature — the skirt lifts every below-ground vertex up to the ground line,
        /// which would collapse a descending tunnel and reseal its mouth. Default false: ordinary
        /// features sit on top of the terrain and want the skirt seam-fix.
        /// </summary>
        public virtual bool HasSubTerrainGeometry => false;

        /// <summary>
        /// Human-readable name for logs / inspector. Defaults to the feature type name; override only
        /// for a friendlier label.
        /// </summary>
        public virtual string DisplayName => FeatureType.ToString();

        // -------------------------------------------------------------------------
        // Multi-mesh capability (opt-in).
        //
        // The default pipeline is SINGLE-mesh: a feature returns one ITerrainDensity from
        // BuildDensity, the generator meshes it into one Mesh, the spawner spawns one GameObject. All
        // eleven other features use exactly that path and these two members untouched.
        //
        // A large feature (e.g. ArchingCave) whose site must be split into internally-chunked
        // sub-meshes overrides ProducesMultipleMeshes to true and implements BuildMeshes. The
        // generator then takes the multi-mesh branch: it ignores BuildDensity and calls BuildMeshes,
        // and the result carries every sub-mesh. The change is fully backward-compatible — a feature
        // that does not override these keeps the original single-mesh behaviour.
        // -------------------------------------------------------------------------

        /// <summary>
        /// True if this feature produces MULTIPLE sub-meshes instead of one. Default false — the
        /// generator then uses the standard single-mesh <see cref="BuildDensity"/> path. Override to
        /// true and implement <see cref="BuildMeshes"/> for an internally-chunked feature.
        /// </summary>
        public virtual bool ProducesMultipleMeshes => false;

        /// <summary>
        /// Builds the feature as a list of finished sub-meshes (each in feature-local space). Called by
        /// the generator INSTEAD of <see cref="BuildDensity"/> when <see cref="ProducesMultipleMeshes"/>
        /// is true. A multi-mesh feature owns its own meshing here (typically via an internal chunker)
        /// because the shape it produces does not fit one voxel volume. Default returns null —
        /// single-mesh features never implement this.
        /// </summary>
        public virtual List<Mesh> BuildMeshes(FeatureContext context, TerrainMeshSettings meshSettings) => null;

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
