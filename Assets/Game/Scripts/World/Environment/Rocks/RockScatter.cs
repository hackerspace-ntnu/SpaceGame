using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Decides where every rock of one size class in one chunk lies, which model it is, how big
    /// and how it is turned. A jittered grid, one candidate per <see cref="RockSizeClass.Spacing"/>
    /// cell, kept by the paint, the class's chance and the profile's patch noise.
    ///
    /// Every cell is seeded from the chunk, the cell and the seed (<see cref="ScatterCellRandom"/>),
    /// so every machine and every reload scatters the same rocks from the same terrain — nothing
    /// about them is networked or saved. Runs on a worker thread: it reads only its
    /// <see cref="Settings"/> and the captured <see cref="PaintedGroundPatch"/>es, never a Unity
    /// object, and does its rotation maths in plain C# rather than through the engine.
    /// </summary>
    public static class RockScatter
    {
        // Unity's Perlin noise mostly stays within this band; stretching it to 0..1 lets the
        // patches reach both ends.
        private const float PatchNoiseLow = 0.3f;
        private const float PatchNoiseHigh = 0.7f;

        // How far apart, in noise space, two seeds put their patch patterns.
        private const float PatchSeedSpread = 1000f;

        /// <summary>A size class's placement settings and its models' measurements, copied on the main thread.</summary>
        public readonly struct Settings
        {
            public readonly float ChunkSize;
            public readonly float Spacing;
            public readonly int Cells;
            public readonly float Chance;
            public readonly float FullDensityPaint;
            public readonly float MinSlopeCos;
            public readonly Vector2 WidthRange;
            public readonly float Stretch;
            public readonly float Sink;
            public readonly float SlopeAlign;
            public readonly float MaxTiltRadians;
            public readonly float Clumping;
            public readonly float ClumpSize;
            public readonly float SizeClumping;
            public readonly int PatchSeed;

            // Per variant, from its mesh bounds (origin at the base, Y up).
            public readonly float[] CumulativeWeight;
            public readonly float[] Width;
            public readonly float[] Bottom;
            public readonly float[] Height;
            public readonly Vector3[] Centre;
            public readonly float[] Radius;

            public Settings(RockFieldProfile profile, RockSizeClass sizeClass, IReadOnlyList<Mesh> meshes,
                            IReadOnlyList<float> weights, int patchSeed)
            {
                ChunkSize = sizeClass.ChunkSize;
                Spacing = sizeClass.Spacing;
                Cells = Mathf.CeilToInt(ChunkSize / Spacing);
                Chance = sizeClass.Chance;
                FullDensityPaint = profile.FullDensityPaint;
                MinSlopeCos = Mathf.Cos(profile.MaxSlopeDegrees * Mathf.Deg2Rad);
                WidthRange = sizeClass.WidthRange;
                Stretch = sizeClass.Stretch;
                Sink = sizeClass.Sink;
                SlopeAlign = sizeClass.SlopeAlign;
                MaxTiltRadians = sizeClass.MaxTiltDegrees * Mathf.Deg2Rad;
                Clumping = profile.Clumping;
                ClumpSize = profile.ClumpSize;
                SizeClumping = profile.SizeClumping;
                PatchSeed = patchSeed;

                int count = meshes.Count;
                CumulativeWeight = new float[count];
                Width = new float[count];
                Bottom = new float[count];
                Height = new float[count];
                Centre = new Vector3[count];
                Radius = new float[count];
                float total = 0f;
                foreach (float weight in weights)
                    total += weight;
                float running = 0f;
                for (int i = 0; i < count; i++)
                {
                    running += weights[i];
                    CumulativeWeight[i] = total > 0f ? running / total : (i + 1f) / count;
                    Bounds bounds = meshes[i].bounds;
                    Width[i] = Mathf.Max(bounds.size.x, bounds.size.z);
                    Bottom[i] = bounds.min.y;
                    Height[i] = bounds.size.y;
                    Centre[i] = bounds.center;
                    Radius[i] = bounds.extents.magnitude;
                }
            }

            /// <summary>The most rocks one chunk can hold: one per grid cell.</summary>
            public int MaxRocksPerChunk => Cells * Cells;
        }

        /// <summary>The rocks of one size class in one chunk.</summary>
        /// <param name="capacity">The most rocks the caller can store; a build over it (a
        /// many-sigma fluke) drops the last ones.</param>
        public static RockInstance[] Build(Vector2Int chunk, Settings settings, PaintedGroundPatch[] ground,
                                           float minimumHeight, int seed, int capacity)
        {
            var rocks = new List<RockInstance>(capacity);
            float spacing = settings.Spacing;
            float patchScale = 1f / settings.ClumpSize;
            // Every class of a profile shares the patch pattern, so scree gathers around boulders.
            float patchOffset = Mathf.Repeat(settings.PatchSeed * 0.618034f, 1f) * PatchSeedSpread;
            float originX = chunk.x * settings.ChunkSize;
            float originZ = chunk.y * settings.ChunkSize;
            float endX = originX + settings.ChunkSize;
            float endZ = originZ + settings.ChunkSize;

            for (int i = 0; i < settings.Cells && rocks.Count < capacity; i++)
            {
                for (int j = 0; j < settings.Cells && rocks.Count < capacity; j++)
                {
                    var random = new ScatterCellRandom(chunk, i, j, seed);
                    // The keep roll comes first, so the cells that hold no rock cost one hash.
                    float keep = random.Next();
                    if (keep >= settings.Chance)
                        continue;
                    float x = originX + (i + random.Next()) * spacing;
                    float z = originZ + (j + random.Next()) * spacing;
                    if (x >= endX || z >= endZ)
                        continue;
                    if (!TrySample(ground, x, z, out float y, out Vector3 normal, out float paint))
                        continue;
                    if (y < minimumHeight || normal.y < settings.MinSlopeCos)
                        continue;

                    float patch = Mathf.InverseLerp(PatchNoiseLow, PatchNoiseHigh,
                        Mathf.PerlinNoise(x * patchScale + patchOffset, z * patchScale + patchOffset));
                    // Density follows the paint strength all the way down, so a faint stroke
                    // sprinkles a few rocks and a painted edge thins out as it blends away.
                    float coverage = Mathf.Clamp01(paint / settings.FullDensityPaint)
                                   * (1f - settings.Clumping * (1f - patch));
                    if (keep >= settings.Chance * coverage)
                        continue;

                    rocks.Add(Place(settings, ref random, new Vector3(x, y, z), normal, patch));
                }
            }
            return rocks.ToArray();
        }

        private static RockInstance Place(Settings settings, ref ScatterCellRandom random, Vector3 ground,
                                          Vector3 normal, float patch)
        {
            int variant = PickVariant(settings.CumulativeWeight, random.Next());
            float size = Mathf.Lerp(random.Next(), patch, settings.SizeClumping);
            float width = Mathf.Lerp(settings.WidthRange.x, settings.WidthRange.y, size);
            float scale = width / Mathf.Max(settings.Width[variant], 1e-4f);
            var stretch = new Vector3(StretchAxis(settings.Stretch, random.Next()),
                                      StretchAxis(settings.Stretch, random.Next()),
                                      StretchAxis(settings.Stretch, random.Next()));

            Vector3 up = Vector3.Lerp(Vector3.up, normal, settings.SlopeAlign).normalized;
            float tiltBearing = random.Next() * Mathf.PI * 2f;
            var tiltAxis = new Vector3(Mathf.Cos(tiltBearing), 0f, Mathf.Sin(tiltBearing));
            Quaternion rotation = FromUp(up)
                                * AxisAngle(tiltAxis, random.Next() * settings.MaxTiltRadians)
                                * AxisAngle(Vector3.up, random.Next() * Mathf.PI * 2f);
            Vector3 rockUp = rotation * Vector3.up;

            // The mesh's base sits on the ground, then sinks by a share of the rock's height.
            float height = settings.Height[variant] * scale * stretch.y;
            Vector3 pivot = ground - rockUp * (settings.Bottom[variant] * scale * stretch.y + settings.Sink * height);
            Vector3 centre = pivot + rotation * Vector3.Scale(settings.Centre[variant] * scale, stretch);
            float radius = settings.Radius[variant] * scale * Mathf.Max(stretch.x, Mathf.Max(stretch.y, stretch.z));
            return new RockInstance(pivot, scale, rotation, stretch, variant, random.Next(), centre, radius);
        }

        private static float StretchAxis(float stretch, float roll) => 1f + (roll * 2f - 1f) * stretch;

        private static int PickVariant(float[] cumulative, float roll)
        {
            for (int i = 0; i < cumulative.Length - 1; i++)
                if (roll < cumulative[i])
                    return i;
            return cumulative.Length - 1;
        }

        /// <summary>The rotation that turns world up onto <paramref name="up"/> (a unit vector).</summary>
        private static Quaternion FromUp(Vector3 up)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, up);
            float sin = axis.magnitude;
            if (sin < 1e-5f)
                return Quaternion.identity;
            return AxisAngle(axis / sin, Mathf.Atan2(sin, up.y));
        }

        private static Quaternion AxisAngle(Vector3 unitAxis, float radians)
        {
            float half = radians * 0.5f;
            float sin = Mathf.Sin(half);
            return new Quaternion(unitAxis.x * sin, unitAxis.y * sin, unitAxis.z * sin, Mathf.Cos(half));
        }

        private static bool TrySample(PaintedGroundPatch[] ground, float x, float z, out float height,
                                      out Vector3 normal, out float paint)
        {
            foreach (PaintedGroundPatch patch in ground)
                if (patch.TrySample(x, z, out height, out normal, out paint))
                    return true;
            height = paint = 0f;
            normal = Vector3.up;
            return false;
        }
    }
}
