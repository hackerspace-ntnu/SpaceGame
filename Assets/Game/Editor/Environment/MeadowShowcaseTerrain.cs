using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Environment
{
    /// <summary>
    /// The showcase's ground: rolling meadow, a hill behind, and an irregular pond with a sandy
    /// beach. Layer 0 is grass, layer 1 is sand, and the pond bed is sand so nothing grows under
    /// the water. Pure shaping maths plus the asset writes it needs; the scene is assembled by
    /// <see cref="MeadowShowcaseBuilder"/>.
    /// </summary>
    internal static class MeadowShowcaseTerrain
    {
        public const float Size = 160f;
        public const float MaxHeight = 40f;
        public const float WaterLevel = 7.2f;
        public static readonly Vector2 PondCentre = new Vector2(4f, 10f);

        private const int HeightResolution = 257;
        private const int AlphaResolution = 256;
        private const float PondRadius = 21f;
        private const float GroundHeight = 10.5f;

        public static TerrainData Create(string folder, TerrainLayer grass, TerrainLayer sand)
        {
            var data = new TerrainData
            {
                heightmapResolution = HeightResolution,
                alphamapResolution = AlphaResolution,
                size = new Vector3(Size, MaxHeight, Size),
            };

            var heights = new float[HeightResolution, HeightResolution];
            for (int y = 0; y < HeightResolution; y++)
                for (int x = 0; x < HeightResolution; x++)
                    heights[y, x] = HeightAt(ToWorld(x, HeightResolution), ToWorld(y, HeightResolution)) / MaxHeight;
            data.SetHeights(0, 0, heights);

            data.terrainLayers = new[] { grass, sand };
            var alpha = new float[AlphaResolution, AlphaResolution, 2];
            for (int y = 0; y < AlphaResolution; y++)
            {
                for (int x = 0; x < AlphaResolution; x++)
                {
                    float sandWeight = SandAt(ToWorld(x, AlphaResolution), ToWorld(y, AlphaResolution));
                    alpha[y, x, 0] = 1f - sandWeight;
                    alpha[y, x, 1] = sandWeight;
                }
            }
            data.SetAlphamaps(0, 0, alpha);

            AssetDatabase.CreateAsset(data, Path.Combine(folder, "MeadowShowcase_TerrainData.asset"));
            return data;
        }

        /// <summary>World X/Z of a heightmap or alphamap sample; the terrain is centred on 0.</summary>
        private static float ToWorld(int index, int resolution) => index / (float)(resolution - 1) * Size - Size * 0.5f;

        private static float HeightAt(float x, float z)
        {
            float hills = (Mathf.PerlinNoise(x * 0.025f + 11f, z * 0.025f + 3f) - 0.5f) * 5f
                        + (Mathf.PerlinNoise(x * 0.07f + 5f, z * 0.07f + 9f) - 0.5f) * 1.2f;
            float backHill = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 75f, z)) * 10f;
            float ground = GroundHeight + hills + backHill;

            float d = PondDistance(x, z);
            float bed = WaterLevel - 0.35f - 2.2f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.2f, d));
            float shore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.4f, 0.8f, d));
            return Mathf.Lerp(ground, Mathf.Min(ground, bed), shore);
        }

        private static float SandAt(float x, float z)
        {
            float d = PondDistance(x, z);
            float fray = (Mathf.PerlinNoise(x * 0.3f, z * 0.3f) - 0.5f) * 0.12f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.22f, 1.05f, d + fray));
        }

        /// <summary>Distance from the pond centre in pond radii, with a wobbly outline.</summary>
        private static float PondDistance(float x, float z)
        {
            float dx = x - PondCentre.x;
            float dz = z - PondCentre.y;
            float angle = Mathf.Atan2(dz, dx);
            float radius = PondRadius * (1f + 0.18f * Mathf.Sin(angle * 3f + 1f) + 0.09f * Mathf.Sin(angle * 5f + 2.3f));
            return Mathf.Sqrt(dx * dx + dz * dz) / radius;
        }
    }
}
