using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Environment
{
    /// <summary>
    /// The ocean showcase's seabed: one 1.2 km terrain that runs from a beach in the north down through
    /// a lagoon, a reef crest and fore-reef, a kelp plateau (west) beside a rocky reef (east), a drop-off
    /// and a deep basin with a vent field. Heights are world metres with the water surface at y 0.
    /// Painted with sand, rock, a dune grass and the five seabed grass biomes, by zone, depth and slope.
    /// </summary>
    internal static class OceanShowcaseTerrain
    {
        public const float Size = 1200f;
        public const float Bottom = -70f;          // terrain origin height: the deepest the bed can go
        public const float HeightRange = 90f;      // so the highest dune can reach y 20
        private const int HeightmapResolution = 513;
        private const int AlphamapResolution = 512;

        // Zone edges along world z (north is land).
        public const float BeachZ = 480f;
        public const float LagoonZ = 440f;
        public const float CrestZ = 255f;
        public const float ForeReefZ = 215f;
        public const float PlateauZ = 60f;
        public const float DropOffZ = -150f;
        public const float BasinZ = -330f;
        public static readonly Vector2 VentField = new Vector2(180f, -470f);
        public const float VentFieldRadius = 70f;

        // Paint layer order in TerrainData.terrainLayers.
        public const int Sand = 0, Rock = 1, Dune = 2, Seagrass = 3, AlgaeTurf = 4, RedFringe = 5, KelpSprouts = 6, VentFloc = 7;

        public enum Zone { Land, Intertidal, Lagoon, ReefCrest, ForeReef, KelpPlateau, RockyReef, DropOff, Basin, VentField }

        public static Zone ZoneAt(float x, float z)
        {
            if (z > BeachZ) return Zone.Land;
            if (z > LagoonZ) return Zone.Intertidal;
            if (z > CrestZ) return Zone.Lagoon;
            if (z > ForeReefZ) return Zone.ReefCrest;
            if (z > PlateauZ) return Zone.ForeReef;
            if (z > DropOffZ) return x < 0f ? Zone.KelpPlateau : Zone.RockyReef;
            if (z > BasinZ) return Zone.DropOff;
            return (new Vector2(x, z) - VentField).magnitude < VentFieldRadius ? Zone.VentField : Zone.Basin;
        }

        /// <summary>The bed's world height at (x, z).</summary>
        public static float HeightAt(float x, float z)
        {
            float n = Fbm(x * 0.006f, z * 0.006f);
            float detail = Fbm(x * 0.03f + 40f, z * 0.03f + 40f) - 0.5f;
            // The broad profile from land to basin, each zone blended into the one above it.
            float land = Band(z, BeachZ, Size * 0.5f, 0.3f, 7f + 6f * n);
            float y = Mathf.Lerp(Band(z, LagoonZ, BeachZ, -1.2f, 0.3f), land, Smooth(BeachZ - 10f, BeachZ + 10f, z));
            y = Mathf.Lerp(-4f - 2.5f * n, y, Smooth(LagoonZ - 25f, LagoonZ + 5f, z));
            // Reef crest: a ridge almost at the surface, cut by a few channels.
            float channel = Mathf.Abs(Mathf.Sin(x * 0.012f + 1.3f)) < 0.18f ? 1f : 0f;
            float crest = Mathf.Lerp(-1.8f - 1.2f * n, -6f, channel);
            y = Mathf.Lerp(crest, y, Smooth(CrestZ - 10f, CrestZ + 20f, z));
            y = Mathf.Lerp(Mathf.Lerp(-18f, -6f, Smooth(PlateauZ, ForeReefZ, z)), y, Smooth(ForeReefZ - 10f, ForeReefZ + 15f, z));
            // Kelp plateau (west) and rocky reef (east), deepening seaward.
            float plateau = Mathf.Lerp(-33f, -22f, Smooth(DropOffZ, PlateauZ, z)) + 3f * (n - 0.5f);
            float rocky = Mathf.Lerp(-26f, -15f, Smooth(DropOffZ, PlateauZ, z)) + 6f * (n - 0.5f);
            float shelf = Mathf.Lerp(plateau, rocky, Smooth(-60f, 60f, x));
            y = Mathf.Lerp(shelf, y, Smooth(PlateauZ - 15f, PlateauZ + 20f, z));
            // Drop-off to the basin.
            float slope = Mathf.Lerp(-60f, -34f, Smooth(BasinZ, DropOffZ, z));
            y = Mathf.Lerp(slope, y, Smooth(DropOffZ - 10f, DropOffZ + 10f, z));
            y = Mathf.Lerp(-62f + 2f * n, y, Smooth(BasinZ - 15f, BasinZ + 10f, z));

            y += Features(x, z, ZoneAt(x, z));
            return y + detail * Mathf.Lerp(0.6f, 2.5f, Smooth(0f, -40f, y));
        }

        /// <summary>Bommies on the reef, outcrops in the shallows, boulders on the rocky reef, cones in the vent field.</summary>
        private static float Features(float x, float z, Zone zone)
        {
            switch (zone)
            {
                case Zone.Intertidal:
                    return 2.2f * Ridge(x * 0.04f, z * 0.04f, 0.62f);
                case Zone.ForeReef:
                case Zone.ReefCrest:
                    return 3.5f * Ridge(x * 0.03f + 7f, z * 0.03f, 0.6f);
                case Zone.RockyReef:
                    return 5f * Ridge(x * 0.025f + 3f, z * 0.025f + 9f, 0.5f);
                case Zone.KelpPlateau:
                    return 2.5f * Ridge(x * 0.02f + 11f, z * 0.02f, 0.55f);
                case Zone.VentField:
                    float cone = 0f;
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 1.7f;
                        var c = VentField + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (12f + 7f * i);
                        float d = (new Vector2(x, z) - c).magnitude;
                        cone = Mathf.Max(cone, (5f - 0.5f * i) * Mathf.Clamp01(1f - d / 14f));
                    }
                    return cone;
                default:
                    return 0f;
            }
        }

        public static TerrainData Create(string folder, TerrainLayer[] layers)
        {
            var data = new TerrainData { heightmapResolution = HeightmapResolution, alphamapResolution = AlphamapResolution };
            data.size = new Vector3(Size, HeightRange, Size);
            // The asset first: alphamaps set before CreateAsset come back as layer 0 everywhere (Foliage.md).
            string path = folder + "/OceanShowcase_Terrain.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            data.terrainLayers = layers;

            var heights = new float[HeightmapResolution, HeightmapResolution];
            for (int j = 0; j < HeightmapResolution; j++)
            {
                for (int i = 0; i < HeightmapResolution; i++)
                {
                    Vector2 w = HeightmapToWorld(i, j);
                    heights[j, i] = Mathf.Clamp01((HeightAt(w.x, w.y) - Bottom) / HeightRange);
                }
            }
            data.SetHeights(0, 0, heights);
            Paint(data);
            EditorUtility.SetDirty(data);
            return data;
        }

        private static void Paint(TerrainData data)
        {
            var weights = new float[AlphamapResolution, AlphamapResolution, data.terrainLayers.Length];
            for (int j = 0; j < AlphamapResolution; j++)
            {
                for (int i = 0; i < AlphamapResolution; i++)
                {
                    // Texel centres sit at (i + 0.5) / res (Grass.md).
                    float x = (i + 0.5f) / AlphamapResolution * Size - Size * 0.5f;
                    float z = (j + 0.5f) / AlphamapResolution * Size - Size * 0.5f;
                    float y = HeightAt(x, z);
                    float steep = Vector3.Angle(Normal(x, z), Vector3.up);
                    weights[j, i, LayerAt(x, z, y, steep)] = 1f;
                }
            }
            data.SetAlphamaps(0, 0, weights);
        }

        /// <summary>One layer per texel: patches come from noise, never from hard rectangles.</summary>
        public static int LayerAt(float x, float z, float y, float steep)
        {
            float patch = Fbm(x * 0.02f + 17f, z * 0.02f + 5f);
            Zone zone = ZoneAt(x, z);
            if (steep > 28f && zone != Zone.Land)
                return Rock;
            switch (zone)
            {
                case Zone.Land:
                    return y > 1.2f && patch > 0.45f ? Dune : Sand;
                case Zone.Intertidal:
                    return y > -0.4f && patch > 0.5f ? Rock : Sand;
                case Zone.Lagoon:
                    if (patch > 0.52f) return Seagrass;
                    return patch < 0.25f ? AlgaeTurf : Sand;
                case Zone.ReefCrest:
                case Zone.ForeReef:
                    // Mostly bare sand between the corals; turf only on the higher ground.
                    if (patch > 0.7f) return RedFringe;
                    return patch > 0.55f ? AlgaeTurf : Sand;
                case Zone.KelpPlateau:
                    if (patch > 0.55f) return KelpSprouts;
                    return patch > 0.35f ? Rock : AlgaeTurf;
                case Zone.RockyReef:
                    if (patch > 0.6f) return RedFringe;
                    return patch > 0.3f ? Rock : AlgaeTurf;
                case Zone.DropOff:
                    return patch > 0.5f ? Rock : Sand;
                case Zone.VentField:
                    return patch > 0.3f ? VentFloc : Rock;
                default:
                    return Sand;
            }
        }

        public static Vector3 Normal(float x, float z)
        {
            const float e = 1.5f;
            float dx = HeightAt(x + e, z) - HeightAt(x - e, z);
            float dz = HeightAt(x, z + e) - HeightAt(x, z - e);
            return new Vector3(-dx, 2f * e, -dz).normalized;
        }

        private static Vector2 HeightmapToWorld(int i, int j) =>
            new Vector2(i / (float)(HeightmapResolution - 1) * Size - Size * 0.5f,
                        j / (float)(HeightmapResolution - 1) * Size - Size * 0.5f);

        private static float Band(float z, float z0, float z1, float y0, float y1) =>
            Mathf.Lerp(y0, y1, Mathf.Clamp01((z - z0) / (z1 - z0)));

        private static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public static float Fbm(float x, float z) =>
            Mathf.PerlinNoise(x, z) * 0.6f + Mathf.PerlinNoise(x * 2.1f + 13f, z * 2.1f + 7f) * 0.3f
            + Mathf.PerlinNoise(x * 4.3f + 3f, z * 4.3f + 21f) * 0.1f;

        /// <summary>Rounded lumps where the noise rises past <paramref name="cut"/>: 0 outside, up to 1 on top.</summary>
        private static float Ridge(float x, float z, float cut)
        {
            float v = Mathf.PerlinNoise(x, z);
            float t = Mathf.Clamp01((v - cut) / (1f - cut) * 2.2f);
            return t * t * (3f - 2f * t);
        }
    }
}
