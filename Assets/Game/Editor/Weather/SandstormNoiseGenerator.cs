// Bakes the tiling 3D noise the storm shaders erode their shape with.
//
// This is the Perlin-Worley volume from Guerrilla's Nubis, at a size this project can afford. Two
// things make it worth baking instead of evaluating noise in the shader: one texture fetch with
// hardware trilinear filtering replaces about twenty-four hash-and-lerp operations per sample, and
// a raymarch takes dozens of samples per pixel — so the difference is the whole effect being
// affordable or not.
//
// Every octave is TILEABLE: the lattice wraps at each frequency, so the volume can be scrolled
// forever without a seam. That is what lets the storm drift.
//
// Channels, following Nubis:
//   R — Perlin-Worley. The billowing base shape. Puffy rather than cloudy because it is remapped
//       against inverted Worley, which is what gives dust its cauliflower edges.
//   G, B, A — Worley at rising frequencies, used to erode the base into detail.
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class SandstormNoiseGenerator
    {
        private const string AssetPath = "Assets/Game/Art/Textures/Environment/SandstormNoise.asset";
        private const int Resolution = 64;

        [MenuItem("Tools/World/Bake Sandstorm Noise")]
        public static void Bake()
        {
            var texture = new Texture3D(Resolution, Resolution, Resolution, TextureFormat.RGBA32, false)
            {
                name = "SandstormNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[Resolution * Resolution * Resolution];

            for (int z = 0; z < Resolution; z++)
            {
                EditorUtility.DisplayProgressBar("Sandstorm noise", $"slice {z + 1}/{Resolution}",
                                                 z / (float)Resolution);

                for (int y = 0; y < Resolution; y++)
                for (int x = 0; x < Resolution; x++)
                {
                    var p = new Vector3(x / (float)Resolution, y / (float)Resolution, z / (float)Resolution);

                    float perlin = PerlinFbm(p, 4, 4, 0);
                    float worleyBase = WorleyFbm(p, 4, 1);

                    // Nubis's remap: pulling Perlin up by inverted Worley turns smooth billows into
                    // the packed puffs that read as a dense body of dust rather than as fog.
                    float perlinWorley = Remap01(perlin, worleyBase - 1f, 1f);

                    pixels[Index(x, y, z)] = new Color32(
                        ToByte(perlinWorley),
                        ToByte(WorleyFbm(p, 6, 2)),
                        ToByte(WorleyFbm(p, 11, 3)),
                        ToByte(WorleyFbm(p, 19, 4)));
                }
            }

            EditorUtility.ClearProgressBar();

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            AssetDatabase.CreateAsset(texture, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Sandstorm] Baked {Resolution}^3 noise to {AssetPath}");
        }

        private static int Index(int x, int y, int z) => x + y * Resolution + z * Resolution * Resolution;

        private static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);

        private static float Remap01(float value, float low, float high) =>
            Mathf.Clamp01((value - low) / Mathf.Max(1e-4f, high - low));

        // ── Perlin-ish value noise, tiling at `frequency` ─────────────────────────

        private static float PerlinFbm(Vector3 p, int frequency, int octaves, int seed)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;

            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * ValueNoise(p, frequency, seed + i * 71);
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2;
            }

            return sum / total;
        }

        private static float ValueNoise(Vector3 p, int frequency, int seed)
        {
            Vector3 scaled = p * frequency;
            int x0 = Mathf.FloorToInt(scaled.x);
            int y0 = Mathf.FloorToInt(scaled.y);
            int z0 = Mathf.FloorToInt(scaled.z);

            float fx = Ease(scaled.x - x0);
            float fy = Ease(scaled.y - y0);
            float fz = Ease(scaled.z - z0);

            float c000 = Hash(x0, y0, z0, frequency, seed);
            float c100 = Hash(x0 + 1, y0, z0, frequency, seed);
            float c010 = Hash(x0, y0 + 1, z0, frequency, seed);
            float c110 = Hash(x0 + 1, y0 + 1, z0, frequency, seed);
            float c001 = Hash(x0, y0, z0 + 1, frequency, seed);
            float c101 = Hash(x0 + 1, y0, z0 + 1, frequency, seed);
            float c011 = Hash(x0, y0 + 1, z0 + 1, frequency, seed);
            float c111 = Hash(x0 + 1, y0 + 1, z0 + 1, frequency, seed);

            return Mathf.Lerp(
                Mathf.Lerp(Mathf.Lerp(c000, c100, fx), Mathf.Lerp(c010, c110, fx), fy),
                Mathf.Lerp(Mathf.Lerp(c001, c101, fx), Mathf.Lerp(c011, c111, fx), fy),
                fz);
        }

        // ── Worley (cellular), tiling at `frequency` ──────────────────────────────
        // Inverted so cell centres are bright: that is what makes a puff rather than a crack.

        private static float WorleyFbm(Vector3 p, int frequency, int seed) =>
            Worley(p, frequency, seed) * 0.625f +
            Worley(p, frequency * 2, seed + 17) * 0.25f +
            Worley(p, frequency * 4, seed + 31) * 0.125f;

        private static float Worley(Vector3 p, int frequency, int seed)
        {
            Vector3 scaled = p * frequency;
            int cx = Mathf.FloorToInt(scaled.x);
            int cy = Mathf.FloorToInt(scaled.y);
            int cz = Mathf.FloorToInt(scaled.z);

            float nearest = float.MaxValue;

            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                int nz = cz + dz;

                // The feature point is placed from the WRAPPED cell id but measured against the
                // unwrapped one, which is what makes the result tile without a visible seam.
                var feature = new Vector3(
                    nx + Hash(nx, ny, nz, frequency, seed),
                    ny + Hash(nx, ny, nz, frequency, seed + 101),
                    nz + Hash(nx, ny, nz, frequency, seed + 211));

                nearest = Mathf.Min(nearest, (feature - scaled).sqrMagnitude);
            }

            // Distances beyond one cell never win, so normalising by 1 keeps the range sane.
            return 1f - Mathf.Clamp01(Mathf.Sqrt(nearest));
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);

        private static float Hash(int x, int y, int z, int period, int seed)
        {
            // Wrapping before hashing is the entire tiling trick: the lattice point one past the
            // edge is literally the same point as the one at the origin.
            uint ux = (uint)(((x % period) + period) % period);
            uint uy = (uint)(((y % period) + period) % period);
            uint uz = (uint)(((z % period) + period) % period);

            unchecked
            {
                uint h = ux * 374761393u + uy * 668265263u + uz * 2246822519u + (uint)seed * 3266489917u;
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h >> 8) / 16777216f;
            }
        }
    }
}
