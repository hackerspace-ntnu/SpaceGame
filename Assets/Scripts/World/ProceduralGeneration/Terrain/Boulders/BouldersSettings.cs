using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Per-feature settings for <see cref="BouldersFeature"/>. Serializable so a designer can override
    /// every knob on the spawner while the four shared <see cref="TerrainFeatureTuning"/> knobs (noise,
    /// overlap, height, jaggedness) still apply uniformly across all features.
    ///
    /// This is the single "one feature, many ways to customise it" surface: scatter density,
    /// clustering, size distribution, per-boulder shape erosion, flattening and how deep the boulders
    /// sink into the ground are all independently tunable. Every value drives deterministic maths —
    /// nothing frame-dependent — so the same (seed, settings, footprint) always bakes the same field.
    /// </summary>
    [System.Serializable]
    public class BouldersSettings
    {
        // -------------------------------------------------------------------------
        // Scatter / placement
        // -------------------------------------------------------------------------

        [Header("Scatter")]
        [Tooltip("Overall boulder density — boulders per 100 m² of footprint, before clustering and " +
                 "edge falloff thin the field. Higher = a denser rock field.")]
        [Range(0.05f, 8f)]
        public float density = 1.2f;

        [Tooltip("0 = even, blue-noise-like scatter. 1 = strong clumps — boulders gather in clusters " +
                 "with bare gaps between, driven by a low-frequency density noise field.")]
        [Range(0f, 1f)]
        public float clustering = 0.55f;

        [Tooltip("Spatial frequency of the clustering density-noise field. Lower = a few large " +
                 "cluster patches; higher = many small tight clumps.")]
        [Range(0.005f, 0.12f)]
        public float clusterFrequency = 0.03f;

        [Tooltip("Fades boulder density toward the footprint polygon edge, in metres. 0 = boulders " +
                 "right up to the boundary; larger = a clear margin that thins out near the rim.")]
        [Range(0f, 30f)]
        public float edgeFalloff = 6f;

        // -------------------------------------------------------------------------
        // Size distribution
        // -------------------------------------------------------------------------

        [Header("Size")]
        [Tooltip("Min / max boulder radius in metres. Each boulder picks a deterministic random " +
                 "radius in this range (skewed by sizeBias).")]
        public Vector2 sizeRange = new Vector2(0.6f, 3.5f);

        [Tooltip("Skews the size distribution. 0 = uniform; negative skews toward small rocks; " +
                 "positive skews toward large boulders.")]
        [Range(-1f, 1f)]
        public float sizeBias = -0.25f;

        // -------------------------------------------------------------------------
        // Per-boulder shape
        // -------------------------------------------------------------------------

        [Header("Shape")]
        [Tooltip("How lumpy / eroded each boulder is — amplitude of the domain-warp surface " +
                 "displacement as a fraction of the boulder radius. 0 = smooth ovoid, 1 = heavily " +
                 "weathered, faceted-but-rounded rock.")]
        [Range(0f, 1f)]
        public float irregularity = 0.4f;

        [Tooltip("Spatial scale of the per-boulder surface erosion noise. Higher = finer, busier " +
                 "facets; lower = a few broad lumps.")]
        [Range(0.05f, 1.2f)]
        public float shapeNoiseFrequency = 0.35f;

        [Tooltip("Number of SmoothMin-blended sub-spheres forming a boulder's lumpy core. 1 = a " +
                 "single ovoid; higher = a more cobbled, multi-lobed rock.")]
        [Range(1, 4)]
        public int lumpiness = 2;

        [Tooltip("How squashed boulders are. 0 = round; 1 = flat slab-like rocks (radius kept, " +
                 "height crushed). Each boulder also gets per-axis stretch for non-uniform shapes.")]
        [Range(0f, 0.85f)]
        public float flattenAmount = 0.35f;

        [Tooltip("Per-axis stretch variety — 0 = every boulder uniformly proportioned; 1 = boulders " +
                 "range from squat to elongated, each different.")]
        [Range(0f, 1f)]
        public float shapeVariety = 0.5f;

        // -------------------------------------------------------------------------
        // Grounding
        // -------------------------------------------------------------------------

        [Header("Grounding")]
        [Tooltip("How far each boulder sinks INTO the ground, as a fraction of its radius, so it " +
                 "rests partially buried rather than balancing on a point. 0 = sits on the surface; " +
                 "1 = a boulder is half-buried at its centre.")]
        [Range(0f, 0.7f)]
        public float embedDepth = 0.3f;

        [Tooltip("SmoothMin blend radius (metres) used when boulders overlap and when a boulder " +
                 "meets the ground fill, so touching rocks fuse softly instead of intersecting hard.")]
        [Range(0f, 2f)]
        public float blendRadius = 0.5f;
    }
}
