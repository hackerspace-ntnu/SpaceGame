using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Environment
{
    /// <summary>
    /// Bakes the patterns SpaceGame/StylizedWater draws with into two tiling textures, so the
    /// shader pays a fetch per pattern instead of evaluating noise for every pixel —
    /// procedurally the water cost ~5.5 ms full screen at 1080p on an M1 Pro; baked, ~0.6 ms.
    ///
    /// WaterNoise3D (RGBAHalf, NoiseSize³, wraps on every axis). Its third axis is time: the
    /// shader moves through it as the clock runs, so patterns change shape rather than scroll.
    ///   R  gradient noise A, NoiseCells cells per tile on every axis
    ///   GB noise A's slope along x and y, per cell (the ripple normal, and even-width streaks)
    ///   A  gradient noise B, independent of A (breaks up the shore lines)
    ///
    /// WaterCaustics (R8 with mips, CausticSize², wraps): the distance to the nearest cell edge
    /// of a static Voronoi pattern with CausticCells cells per tile. Static and sharp on
    /// purpose — a web moving through a time axis smears into double lines between slices, so
    /// the shader animates it by warping two layers of it with noise A instead.
    ///
    /// The shader's WATER_NOISE_CELLS and WATER_CAUSTIC_CELLS must match the constants here.
    /// </summary>
    internal static class WaterNoiseBaker
    {
        public const string NoisePath = "Assets/Game/Art/Textures/Environment/WaterNoise3D.asset";
        public const string CausticsPath = "Assets/Game/Art/Textures/Environment/WaterCaustics.asset";
        public const string NoiseProperty = "_WaterNoise";
        public const string CausticsProperty = "_WaterCaustics";
        private const string ShaderPath = "Assets/Game/Art/Shaders/Environment/StylizedWater.shader";

        private const int NoiseSize = 64;
        private const int NoiseCells = 8;
        private const int CausticSize = 256;
        private const int CausticCells = 4;
        // Edge distances reach ~1.4 cells; stored divided by this to fit 8 bits.
        private const float CausticRange = 1.5f;

        private static readonly Vector3 SecondNoiseSeed = new Vector3(37f, 11f, 23f);

        [MenuItem("SpaceGame/Environment/Bake Water Noise")]
        public static void Bake()
        {
            Texture3D noise = SaveAsset(BakeNoise(), NoisePath);
            Texture2D caustics = SaveAsset(BakeCaustics(), CausticsPath);

            // New materials on the shader pick the textures up without anyone assigning them.
            var importer = (ShaderImporter)AssetImporter.GetAtPath(ShaderPath);
            importer.SetDefaultTextures(new[] { NoiseProperty, CausticsProperty }, new Texture[] { noise, caustics });
            importer.SaveAndReimport();
            Debug.Log($"[WaterNoise] Baked {NoisePath} and {CausticsPath}.");
        }

        private static Texture3D BakeNoise()
        {
            var pixels = new Color[NoiseSize * NoiseSize * NoiseSize];
            for (int z = 0; z < NoiseSize; z++)
            {
                for (int y = 0; y < NoiseSize; y++)
                {
                    for (int x = 0; x < NoiseSize; x++)
                    {
                        Vector3 cells = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * NoiseCells / NoiseSize;
                        Vector4 a = GradientNoise(cells, Vector3.zero);
                        Vector4 b = GradientNoise(cells, SecondNoiseSeed);
                        pixels[x + y * NoiseSize + z * NoiseSize * NoiseSize] = new Color(a.x, a.y, a.z, b.x);
                    }
                }
            }

            var texture = new Texture3D(NoiseSize, NoiseSize, NoiseSize, TextureFormat.RGBAHalf, false)
            {
                name = "WaterNoise3D",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels(pixels);
            // Kept readable: a texture asset is serialized from its CPU copy, so releasing it
            // here would save the asset empty.
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D BakeCaustics()
        {
            var pixels = new Color[CausticSize * CausticSize];
            for (int y = 0; y < CausticSize; y++)
            {
                for (int x = 0; x < CausticSize; x++)
                {
                    Vector2 cells = new Vector2(x + 0.5f, y + 0.5f) * CausticCells / CausticSize;
                    float edge = VoronoiEdge(cells) / CausticRange;
                    pixels[x + y * CausticSize] = new Color(edge, edge, edge, 1f);
                }
            }

            var texture = new Texture2D(CausticSize, CausticSize, TextureFormat.R8, true, true)
            {
                name = "WaterCaustics",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
            };
            texture.SetPixels(pixels);
            // Mips average the distance, so far away the web thins out instead of shimmering.
            texture.Apply(true, false);
            return texture;
        }

        private static T SaveAsset<T>(T texture, string path) where T : Texture
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(texture, path);
                return texture;
            }
            EditorUtility.CopySerialized(texture, existing);
            Object.DestroyImmediate(texture);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
        }

        // Dave Hoskins' sine-free hash33 / hash22.
        private static Vector3 Hash33(Vector3 p)
        {
            p = new Vector3(Frac(p.x * 0.1031f), Frac(p.y * 0.1030f), Frac(p.z * 0.0973f));
            float d = Vector3.Dot(p, new Vector3(p.y, p.x, p.z) + Vector3.one * 33.33f);
            p += Vector3.one * d;
            return new Vector3(Frac((p.x + p.y) * p.z), Frac((p.x + p.x) * p.y), Frac((p.y + p.x) * p.x));
        }

        private static Vector2 Hash22(Vector2 p)
        {
            var p3 = new Vector3(Frac(p.x * 0.1031f), Frac(p.y * 0.1030f), Frac(p.x * 0.0973f));
            float d = Vector3.Dot(p3, new Vector3(p3.y, p3.z, p3.x) + Vector3.one * 33.33f);
            p3 += Vector3.one * d;
            return new Vector2(Frac((p3.x + p3.y) * p3.z), Frac((p3.x + p3.z) * p3.y));
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        private static int Wrap(int v, int period) => ((v % period) + period) % period;

        /// <summary>Gradient at a lattice corner, wrapped so the noise tiles every NoiseCells.</summary>
        private static Vector3 Gradient(int x, int y, int z, Vector3 seed)
        {
            Vector3 h = Hash33(new Vector3(Wrap(x, NoiseCells), Wrap(y, NoiseCells), Wrap(z, NoiseCells)) + seed);
            return h * 2f - Vector3.one;
        }

        /// <summary>Quilez's gradient noise with analytic derivatives: (value, d/dx, d/dy, d/dz).</summary>
        private static Vector4 GradientNoise(Vector3 p, Vector3 seed)
        {
            int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
            var w = new Vector3(p.x - ix, p.y - iy, p.z - iz);
            var u = new Vector3(Quintic(w.x), Quintic(w.y), Quintic(w.z));
            var du = new Vector3(QuinticSlope(w.x), QuinticSlope(w.y), QuinticSlope(w.z));

            Vector3 ga = Gradient(ix, iy, iz, seed), gb = Gradient(ix + 1, iy, iz, seed);
            Vector3 gc = Gradient(ix, iy + 1, iz, seed), gd = Gradient(ix + 1, iy + 1, iz, seed);
            Vector3 ge = Gradient(ix, iy, iz + 1, seed), gf = Gradient(ix + 1, iy, iz + 1, seed);
            Vector3 gg = Gradient(ix, iy + 1, iz + 1, seed), gh = Gradient(ix + 1, iy + 1, iz + 1, seed);

            float va = Vector3.Dot(ga, w);
            float vb = Vector3.Dot(gb, w - new Vector3(1, 0, 0));
            float vc = Vector3.Dot(gc, w - new Vector3(0, 1, 0));
            float vd = Vector3.Dot(gd, w - new Vector3(1, 1, 0));
            float ve = Vector3.Dot(ge, w - new Vector3(0, 0, 1));
            float vf = Vector3.Dot(gf, w - new Vector3(1, 0, 1));
            float vg = Vector3.Dot(gg, w - new Vector3(0, 1, 1));
            float vh = Vector3.Dot(gh, w - new Vector3(1, 1, 1));

            float k0 = va, k1 = vb - va, k2 = vc - va, k3 = ve - va;
            float k4 = va - vb - vc + vd, k5 = va - vc - ve + vg, k6 = va - vb - ve + vf;
            float k7 = -va + vb + vc - vd + ve - vf - vg + vh;
            float value = k0 + u.x * k1 + u.y * k2 + u.z * k3 + u.x * u.y * k4 + u.y * u.z * k5
                        + u.z * u.x * k6 + u.x * u.y * u.z * k7;

            Vector3 g0 = ga, g1 = gb - ga, g2 = gc - ga, g3 = ge - ga;
            Vector3 g4 = ga - gb - gc + gd, g5 = ga - gc - ge + gg, g6 = ga - gb - ge + gf;
            Vector3 g7 = -ga + gb + gc - gd + ge - gf - gg + gh;
            Vector3 gradient = g0 + u.x * g1 + u.y * g2 + u.z * g3 + u.x * u.y * g4 + u.y * u.z * g5
                             + u.z * u.x * g6 + u.x * u.y * u.z * g7;
            gradient.x += du.x * (k1 + u.y * k4 + u.z * k6 + u.y * u.z * k7);
            gradient.y += du.y * (k2 + u.z * k5 + u.x * k4 + u.z * u.x * k7);
            gradient.z += du.z * (k3 + u.x * k6 + u.y * k5 + u.x * u.y * k7);

            return new Vector4(value, gradient.x, gradient.y, gradient.z);
        }

        private static float Quintic(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static float QuinticSlope(float t) => 30f * t * t * (t * (t - 2f) + 1f);

        /// <summary>Edge distance of a Voronoi pattern that wraps every CausticCells cells.</summary>
        private static float VoronoiEdge(Vector2 p)
        {
            int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y);
            var local = new Vector2(p.x - cx, p.y - cy);
            float nearest = 8f, second = 8f;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    var cell = new Vector2(Wrap(cx + x, CausticCells), Wrap(cy + y, CausticCells));
                    // Points kept off the cell borders give rounder, more even cells.
                    Vector2 site = Vector2.one * 0.15f + Hash22(cell) * 0.7f;
                    float distanceSq = (new Vector2(x, y) + site - local).sqrMagnitude;
                    if (distanceSq < nearest)
                    {
                        second = nearest;
                        nearest = distanceSq;
                    }
                    else if (distanceSq < second)
                    {
                        second = distanceSq;
                    }
                }
            }
            return Mathf.Sqrt(second) - Mathf.Sqrt(nearest);
        }
    }
}
