using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// The single input bundle handed to every <see cref="TerrainFeature"/> when it builds. One context
    /// type for all features, so they stay interchangeable — a feature simply reads the parts it needs,
    /// principally <see cref="LocalBounds"/> (the box) and <see cref="Area"/> (the outline).
    ///
    /// All geometry in here is in the spawner's LOCAL space unless a field name says "World". Working
    /// local keeps the feature deterministic regardless of where the spawner sits, and lets the bake
    /// pipeline place the mesh as a child of the spawner.
    ///
    /// This object is pure immutable data — construct it once, hand it to <see cref="TerrainFeature.Build"/>,
    /// never mutate it mid-build. Determinism depends on that.
    /// </summary>
    public sealed class FeatureContext
    {
        /// <summary>Deterministic seed. Same seed + same tuning + same footprint = identical output.</summary>
        public readonly int Seed;

        /// <summary>Axis-aligned local-space bounds of the footprint — the bounding box of
        /// <see cref="Area"/>. The mesher uses this for its voxel-walk extent; features should
        /// prefer <see cref="FootprintDistanceInside"/> over reading this box directly for their
        /// silhouette.</summary>
        public readonly Bounds LocalBounds;

        /// <summary>The resolved footprint — box dimensions, mode and outline polygon. Features read it
        /// only through <see cref="FootprintDistanceInside"/>. See <see cref="FeatureFootprint"/>.</summary>
        public readonly FeatureFootprint Area;

        /// <summary>The four shared tuning knobs (noise / overlap / height / jaggedness) plus
        /// walkability controls. A feature may also carry its own extra per-feature settings.</summary>
        public readonly TerrainFeatureTuning Tuning;

        /// <summary>Samples the underlying ground height/normal. Density adds the feature on top of
        /// this; the skirt-blend helper snaps the mesh down onto it. Coordinates passed to the sampler
        /// are WORLD space — convert with <see cref="LocalToWorld"/> first.</summary>
        public readonly ITerrainHeightSampler Ground;

        /// <summary>Local→world transform of the spawner at bake time. Lets a feature convert a local
        /// sample point to world space to query <see cref="Ground"/>.</summary>
        public readonly Matrix4x4 LocalToWorld;

        /// <summary>Voxel edge length (metres) the marching-cubes mesher will use. Smaller = more
        /// detail, much slower. A feature may read this to scale its detail to the resolution.</summary>
        public readonly float VoxelSize;

        public FeatureContext(
            int seed,
            Bounds localBounds,
            FeatureFootprint area,
            TerrainFeatureTuning tuning,
            ITerrainHeightSampler ground,
            Matrix4x4 localToWorld,
            float voxelSize)
        {
            Seed = seed;
            LocalBounds = localBounds;
            Area = area;
            Tuning = tuning ?? new TerrainFeatureTuning();
            Ground = ground ?? new UnityTerrainHeightSampler(null);
            LocalToWorld = localToWorld;
            VoxelSize = Mathf.Max(0.1f, voxelSize);
        }

        /// <summary>
        /// Signed distance from a feature-local (x, z) point to the footprint boundary: POSITIVE
        /// metres inside, NEGATIVE outside. This is the single shared call every feature should
        /// feed into <see cref="TerrainNoiseHelper.OverlapWeight"/> — it makes the feature's silhouette
        /// follow the designer-drawn <see cref="Area"/> polygon instead of a hardcoded rectangle.
        ///
        /// Falls back to the axis-aligned <see cref="LocalBounds"/> rectangle when the polygon has
        /// fewer than 3 vertices, so a feature works even before a footprint is drawn.
        /// </summary>
        public float FootprintDistanceInside(float localX, float localZ)
        {
            if (Area != null)
                return Area.SignedDistanceInside(localX, localZ);

            // Box fallback (no footprint yet): distance into the axis-aligned bounds rectangle.
            float dx = LocalBounds.extents.x - Mathf.Abs(localX - LocalBounds.center.x);
            float dz = LocalBounds.extents.z - Mathf.Abs(localZ - LocalBounds.center.z);
            return Mathf.Min(dx, dz);
        }

        /// <summary>Convert a feature-local point to world space (for querying <see cref="Ground"/>).</summary>
        public Vector3 LocalToWorldPoint(Vector3 local) => LocalToWorld.MultiplyPoint3x4(local);

        /// <summary>Ground height (in feature-LOCAL Y) directly under a feature-local (x, z) point.
        /// Convenience wrapper so a feature can stay entirely in local space.</summary>
        public float LocalGroundHeight(float localX, float localZ)
        {
            Vector3 world = LocalToWorldPoint(new Vector3(localX, 0f, localZ));
            float worldH = Ground.SampleHeight(world.x, world.z);
            // Invert just the Y of the world point back into local space.
            return LocalToWorld.inverse.MultiplyPoint3x4(new Vector3(world.x, worldH, world.z)).y;
        }
    }
}
