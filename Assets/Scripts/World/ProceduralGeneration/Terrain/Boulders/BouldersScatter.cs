using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// One scattered boulder, fully resolved at placement time. Every field is deterministic off the
    /// feature seed plus the boulder's grid cell, so the same seed always produces the identical field.
    /// The <see cref="BouldersSdf"/> reads these structs to evaluate the boulder field.
    /// </summary>
    public struct BoulderInstance
    {
        /// <summary>Local-space centre of the boulder (X, Z = footprint position, Y = resting centre
        /// height — already sunk into the ground by the embed depth).</summary>
        public Vector3 Centre;

        /// <summary>Nominal radius in metres (the size before per-axis stretch and erosion).</summary>
        public float Radius;

        /// <summary>Per-axis scale applied inside the SDF — squashes / stretches the boulder so it is
        /// not a uniform sphere. Y is crushed by the flatten knob; X/Z carry the stretch variety.</summary>
        public Vector3 AxisScale;

        /// <summary>Yaw rotation (radians) about Y, so elongated boulders point in varied directions.</summary>
        public float Yaw;

        /// <summary>Per-boulder noise phase — offsets the erosion noise so no two boulders share the
        /// exact same lump pattern even at identical size.</summary>
        public Vector3 NoisePhase;

        /// <summary>The largest distance (metres) at which this boulder can still influence the SDF —
        /// radius plus the maximum erosion outset. The spatial grid uses it to bound neighbour tests.</summary>
        public float Reach;
    }

    /// <summary>
    /// Noise-driven, non-uniform scatter of boulders across the footprint polygon. Produces a
    /// deterministic list of <see cref="BoulderInstance"/> from the feature seed.
    ///
    /// MODEL — a jittered grid thresholded by a density-noise field:
    /// the footprint bounding box is divided into cells sized from the target <c>density</c>; each cell
    /// gets one candidate, jittered to a random position within the cell (so the field never reads as
    /// a grid). A candidate is KEPT only if a deterministic per-cell random value passes a threshold
    /// derived from a low-frequency density-noise field — so where the noise is high, nearly every
    /// candidate survives (a cluster), and where it is low, most are culled (a sparse gap). The
    /// <c>clustering</c> knob sets how much contrast that noise applies. Candidates outside the polygon,
    /// or thinned out by the polygon-edge falloff, are also dropped.
    /// </summary>
    public static class BouldersScatter
    {
        /// <summary>
        /// Builds the deterministic boulder field for a feature.
        /// </summary>
        /// <param name="context">The feature context (footprint, seed, ground sampler).</param>
        /// <param name="s">Resolved boulder settings (never null).</param>
        /// <param name="sizeScale">Multiplier on every boulder radius (folds in the shared height
        /// tuning so the designer can scale the whole field bigger / smaller).</param>
        public static List<BoulderInstance> Build(FeatureContext context, BouldersSettings s, float sizeScale)
        {
            var result = new List<BoulderInstance>(256);
            Bounds box = context.LocalBounds;
            int seed = context.Seed;

            float minR = Mathf.Max(0.1f, Mathf.Min(s.sizeRange.x, s.sizeRange.y)) * sizeScale;
            float maxR = Mathf.Max(minR + 0.05f, Mathf.Max(s.sizeRange.x, s.sizeRange.y) * sizeScale);

            // Cell size: scaled so the average candidate spacing matches the requested density
            // (density = boulders per 100 m²). One candidate per cell ⇒ cell area = 100 / density.
            float cellArea = 100f / Mathf.Max(0.05f, s.density);
            float cell = Mathf.Max(maxR * 1.2f, Mathf.Sqrt(cellArea));

            int cols = Mathf.Max(1, Mathf.CeilToInt(box.size.x / cell));
            int rows = Mathf.Max(1, Mathf.CeilToInt(box.size.z / cell));

            for (int cz = 0; cz < rows; cz++)
            for (int cx = 0; cx < cols; cx++)
            {
                // Deterministic per-cell hashes (distinct salts keep the streams independent).
                int cellSalt = cx * 73856093 ^ cz * 19349663;
                float jx = TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0x1111);
                float jz = TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0x2222);
                float keepRoll = TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0x3333);

                // Jittered candidate position inside the cell.
                float px = box.min.x + (cx + jx) * cell;
                float pz = box.min.z + (cz + jz) * cell;
                if (px > box.max.x || pz > box.max.z) continue;

                // --- Polygon test + edge falloff ---------------------------------------------
                float distInside = context.FootprintDistanceInside(px, pz);
                if (distInside <= 0f) continue;                       // outside the polygon
                float edgeKeep = s.edgeFalloff > 0.01f
                    ? Mathf.Clamp01(distInside / s.edgeFalloff)
                    : 1f;

                // --- Clustering: low-frequency density noise thresholds the candidate ---------
                // densityNoise in [0,1]; clustering scales how far the keep-threshold swings.
                float dn = NoiseDistortion.Sample(
                    new Vector3(px, 0f, pz), s.clusterFrequency, seed + 8101) * 0.5f + 0.5f;
                // High clustering ⇒ threshold strongly tracks the noise: sparse where dn is low.
                float threshold = Mathf.Lerp(0.15f, 1f - dn, s.clustering);
                if (keepRoll * edgeKeep < threshold) continue;        // culled

                result.Add(MakeBoulder(context, s, seed, cellSalt, px, pz, minR, maxR));
            }

            return result;
        }

        /// <summary>Resolves one surviving candidate into a fully-shaped <see cref="BoulderInstance"/>.</summary>
        static BoulderInstance MakeBoulder(
            FeatureContext context, BouldersSettings s, int seed, int cellSalt,
            float px, float pz, float minR, float maxR)
        {
            // --- Size: random in range, then skewed by sizeBias --------------------------------
            float rRoll = TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0x4444);
            // Bias < 0 pushes the roll toward 0 (small); bias > 0 pushes toward 1 (large).
            float biased = s.sizeBias >= 0f
                ? Mathf.Pow(rRoll, 1f - s.sizeBias * 0.85f)
                : 1f - Mathf.Pow(1f - rRoll, 1f + s.sizeBias * 0.85f);
            float radius = Mathf.Lerp(minR, maxR, biased);

            // --- Per-axis stretch: non-uniform boulders ----------------------------------------
            float sx = 1f + (TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0x5555) - 0.5f) * s.shapeVariety;
            float sz = 1f + (TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0x6666) - 0.5f) * s.shapeVariety;
            // Y is squashed by the flatten knob (plus a touch of its own variety).
            float syVar = (TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0x7777) - 0.5f) * s.shapeVariety;
            float sy = Mathf.Max(0.15f, (1f - s.flattenAmount) + syVar * 0.3f);
            Vector3 axisScale = new Vector3(Mathf.Max(0.35f, sx), sy, Mathf.Max(0.35f, sz));

            float yaw = TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0x8888) * Mathf.PI * 2f;

            Vector3 phase = new Vector3(
                TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0x9999) * 100f,
                TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0xAAAA) * 100f,
                TerrainNoiseHelper.Hash01(seed, cellSalt ^ 0xBBBB) * 100f);

            // --- Resting height: sit on the ground, then sink in by embedDepth -----------------
            float groundY = context.LocalGroundHeight(px, pz);
            float halfY = radius * axisScale.y;
            float centreY = groundY + halfY - radius * s.embedDepth;

            // Reach: how far the boulder can influence the field — its widest half-extent plus the
            // outward erosion bulge. Used by the spatial grid to bound Sample's neighbour search.
            float maxHalf = radius * Mathf.Max(axisScale.x, Mathf.Max(axisScale.y, axisScale.z));
            float reach = maxHalf + radius * s.irregularity + s.blendRadius + 0.5f;

            return new BoulderInstance
            {
                Centre = new Vector3(px, centreY, pz),
                Radius = radius,
                AxisScale = axisScale,
                Yaw = yaw,
                NoisePhase = phase,
                Reach = reach,
            };
        }
    }
}
