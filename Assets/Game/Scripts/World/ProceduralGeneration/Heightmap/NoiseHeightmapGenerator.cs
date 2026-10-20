using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Edit-time generator that writes a whole Unity <see cref="Terrain"/> heightmap from stacked
    /// <see cref="NoiseLayer"/>s, after the procedural-playground terrain:
    ///
    ///   • <b>Continentalness</b> — the broadest layer; a curve maps it to the base height, so it
    ///     decides lowland basins versus raised highlands.
    ///   • <b>Erosion</b> — a curve maps it to <i>relief</i> (0..1). High erosion flattens everything
    ///     on top of the base into plains; low erosion lets mountains and hills through. This is what
    ///     makes some regions flat and others steep.
    ///   • <b>Peaks and valleys</b> — ridge-folded noise, the mountain ranges. Scaled by relief and by
    ///     how far inland continentalness says the point is.
    ///   • <b>Base elevation</b> / <b>detail</b> — rolling hills and small bumps, both damped by relief.
    ///   • <b>Warp</b> — bends the sample point of the large layers so they read as natural shapes
    ///     instead of round Perlin blobs.
    ///
    /// Sampling is in world metres, so two terrains with the same settings side by side meet without
    /// a seam. Inert at runtime: the result is the <see cref="TerrainData"/> asset, written from the
    /// inspector's Generate button.
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    [DisallowMultipleComponent]
    public class NoiseHeightmapGenerator : MonoBehaviour
    {
        [Tooltip("Shared by every layer; each layer mixes in its own salt.")]
        [SerializeField] private int seed = 1;

        [Header("Continentalness — basins vs. highlands")]
        [SerializeField] private NoiseLayer continentalness = new NoiseLayer
            { wavelength = 1800f, persistence = 0.5f, contrast = 1.8f, seedSalt = 1 };
        [Tooltip("Continentalness (0..1) → base height in metres.")]
        [SerializeField] private AnimationCurve continentalToHeight = SmoothCurve(
            new Keyframe(0f, 35f), new Keyframe(0.3f, 50f), new Keyframe(0.5f, 75f),
            new Keyframe(0.7f, 115f), new Keyframe(1f, 160f));

        [Header("Erosion — flat plains vs. steep ground")]
        [SerializeField] private NoiseLayer erosion = new NoiseLayer
            { wavelength = 1000f, persistence = 0.5f, contrast = 1.8f, seedSalt = 2 };
        [Tooltip("Erosion (0..1) → relief (0 = flat plain, 1 = full mountains and hills).")]
        [SerializeField] private AnimationCurve erosionToRelief = SmoothCurve(
            new Keyframe(0f, 1f), new Keyframe(0.3f, 0.9f), new Keyframe(0.5f, 0.35f),
            new Keyframe(0.65f, 0.06f), new Keyframe(1f, 0f));

        [Header("Peaks and valleys — ridged mountain ranges")]
        [SerializeField] private NoiseLayer peaksAndValleys = new NoiseLayer
            { wavelength = 650f, persistence = 0.5f, contrast = 1.3f, seedSalt = 3 };
        [Tooltip("Height of a full ridge at full relief, metres.")]
        [Min(0f)] [SerializeField] private float mountainHeight = 200f;
        [Tooltip("Power on the folded ridge. Higher = narrower, sharper crests.")]
        [Min(0.1f)] [SerializeField] private float ridgeSharpness = 2f;
        [Tooltip("Continentalness where mountains start to rise; below it the ground stays low.")]
        [Range(0f, 1f)] [SerializeField] private float mountainInlandStart = 0.35f;
        [Tooltip("Continentalness where mountains reach full height.")]
        [Range(0f, 1f)] [SerializeField] private float mountainInlandEnd = 0.65f;

        [Header("Base elevation — rolling hills")]
        [SerializeField] private NoiseLayer baseElevation = new NoiseLayer
            { wavelength = 300f, persistence = 0.45f, contrast = 1.4f, seedSalt = 4 };
        [Tooltip("± metres of hill height at full relief.")]
        [Min(0f)] [SerializeField] private float hillHeight = 30f;
        [Tooltip("Fraction of the hill height kept on the flattest plains, so they still undulate.")]
        [Range(0f, 1f)] [SerializeField] private float plainHillFraction = 0.2f;

        [Header("Detail — small bumps")]
        [SerializeField] private NoiseLayer detail = new NoiseLayer
            { wavelength = 70f, persistence = 0.45f, contrast = 1.2f, seedSalt = 5 };
        [Tooltip("± metres of bump height at full relief.")]
        [Min(0f)] [SerializeField] private float detailHeight = 4f;
        [Tooltip("Fraction of the bump height kept on the flattest plains.")]
        [Range(0f, 1f)] [SerializeField] private float plainDetailFraction = 0.35f;

        [Header("Warp — bends the large layers into natural shapes")]
        [SerializeField] private NoiseLayer warp = new NoiseLayer
            { wavelength = 900f, persistence = 0.5f, contrast = 1f, seedSalt = 6 };
        [Tooltip("How far, in metres, the large layers' sample point is pushed.")]
        [Min(0f)] [SerializeField] private float warpDistance = 250f;

        [Header("Slope texturing")]
        [Tooltip("Terrain layer painted on flat ground.")]
        [Min(0)] [SerializeField] private int flatTerrainLayer = 0;
        [Tooltip("Terrain layer painted on steep ground.")]
        [Min(0)] [SerializeField] private int steepTerrainLayer = 1;
        [Tooltip("Slope in degrees where the steep layer starts to blend in.")]
        [Range(0f, 90f)] [SerializeField] private float steepBlendStart = 22f;
        [Tooltip("Slope in degrees where the ground is fully the steep layer.")]
        [Range(0f, 90f)] [SerializeField] private float steepBlendEnd = 38f;

        // Second warp axis samples the same layer far enough away to be uncorrelated with the first.
        const float WarpAxisSeparation = 7919f;

        public int Seed { get => seed; set => seed = value; }

        /// <summary>Ground height in metres above the terrain's base at world XZ.</summary>
        public float SampleHeight(float x, float z)
        {
            float warpX = (warp.Sample(x, z, seed) - 0.5f) * 2f * warpDistance;
            float warpZ = (warp.Sample(x + WarpAxisSeparation, z - WarpAxisSeparation, seed) - 0.5f) * 2f * warpDistance;
            float wx = x + warpX;
            float wz = z + warpZ;

            float continental = continentalness.Sample(wx, wz, seed);
            float relief = Mathf.Clamp01(erosionToRelief.Evaluate(erosion.Sample(wx, wz, seed)));
            float inland = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mountainInlandStart, mountainInlandEnd, continental));

            float ridge = 1f - Mathf.Abs(2f * peaksAndValleys.Sample(wx, wz, seed) - 1f);
            float mountains = Mathf.Pow(ridge, ridgeSharpness) * mountainHeight * relief * inland;

            float hills = Signed(baseElevation.Sample(x, z, seed)) * hillHeight * Mathf.Lerp(plainHillFraction, 1f, relief);
            float bumps = Signed(detail.Sample(x, z, seed)) * detailHeight * Mathf.Lerp(plainDetailFraction, 1f, relief);

            return continentalToHeight.Evaluate(continental) + mountains + hills + bumps;
        }

        /// <summary>
        /// Rewrites the attached terrain's heightmap and slope splat. Returns the highest sampled
        /// height in metres so the caller can report clipping against <c>TerrainData.size.y</c>.
        /// </summary>
        public float Generate()
        {
            TerrainData data = GetComponent<Terrain>().terrainData;
            Vector3 origin = transform.position;
            Vector3 size = data.size;
            int resolution = data.heightmapResolution;

            var heights = new float[resolution, resolution];
            float highest = float.MinValue;
            for (int row = 0; row < resolution; row++)
            {
                float z = origin.z + row / (float)(resolution - 1) * size.z;
                for (int col = 0; col < resolution; col++)
                {
                    float x = origin.x + col / (float)(resolution - 1) * size.x;
                    float h = SampleHeight(x, z);
                    highest = Mathf.Max(highest, h);
                    heights[row, col] = Mathf.Clamp01(h / size.y);
                }
            }
            data.SetHeights(0, 0, heights);

            if (highest > size.y)
                Debug.LogWarning($"[NoiseHeightmap] '{name}': peaks reach {highest:F0} m but the terrain is only " +
                                 $"{size.y:F0} m tall, so they are clipped flat. Raise the terrain height or lower " +
                                 "mountainHeight / continentalToHeight.", this);

            PaintSlopes(data);
            return highest;
        }

        void PaintSlopes(TerrainData data)
        {
            int layers = data.alphamapLayers;
            if (flatTerrainLayer >= layers || steepTerrainLayer >= layers)
            {
                Debug.LogWarning($"[NoiseHeightmap] '{name}': terrain has {layers} terrain layer(s); slope texturing " +
                                 $"needs layers {flatTerrainLayer} and {steepTerrainLayer}. Heights were written, the splat was not.", this);
                return;
            }

            int resolution = data.alphamapResolution;
            var alpha = new float[resolution, resolution, layers];
            for (int row = 0; row < resolution; row++)
            {
                float v = (row + 0.5f) / resolution;
                for (int col = 0; col < resolution; col++)
                {
                    float u = (col + 0.5f) / resolution;
                    float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(steepBlendStart, steepBlendEnd, data.GetSteepness(u, v)));
                    alpha[row, col, flatTerrainLayer] += 1f - steep;
                    alpha[row, col, steepTerrainLayer] += steep;
                }
            }
            data.SetAlphamaps(0, 0, alpha);
        }

        static float Signed(float value01) => value01 * 2f - 1f;

        static AnimationCurve SmoothCurve(params Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }
    }
}
