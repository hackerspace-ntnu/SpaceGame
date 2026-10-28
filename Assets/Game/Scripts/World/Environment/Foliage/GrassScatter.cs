using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Decides where every tuft in one chunk stands. A jittered grid rather than pure random
    /// points, so the coverage is even and there are no bald spots or clumps at any density.
    ///
    /// Every grid cell is seeded from the chunk, the cell and the seed, so every machine and every
    /// reload scatters the same meadow from the same terrain — nothing about the grass is networked
    /// or saved — and a thinned build is exactly a subset of a full one. Runs on a
    /// worker thread: it reads only its <see cref="Settings"/> and the captured
    /// <see cref="PaintedGroundPatch"/>es, never a Unity object.
    /// </summary>
    public static class GrassScatter
    {
        // Unity's Perlin noise mostly stays within this band; stretching it to 0..1 lets the
        // clumps reach both ends of the height range.
        private const float ClumpNoiseLow = 0.3f;
        private const float ClumpNoiseHigh = 0.7f;

        // How far apart, in noise space, two seeds put their clump patterns.
        private const float ClumpSeedSpread = 1000f;

        // A plant with no base and no reach would be a division by zero.
        private const float MinContactSpan = 0.01f;

        /// <summary>A profile's placement settings, copied on the main thread for a worker.</summary>
        public readonly struct Settings
        {
            public readonly float ChunkSize;
            public readonly float Spacing;
            public readonly int Cells;
            public readonly float MinimumLayerWeight;
            public readonly float MinSlopeCos;
            public readonly Vector2 HeightRange;
            public readonly float HeightClumping;
            public readonly float HeightClumpSize;
            public readonly float FlowerChance;
            public readonly float DensityVariation;
            public readonly float DensityVariationSize;
            public readonly float ContactGrowth;
            public readonly float ContactClearing;
            public readonly float ContactReach;

            /// <param name="densityScale">The field's scale on the profile's tufts per square metre.</param>
            /// <param name="contactClearing">The fraction of a plant's base kept clear of grass.</param>
            /// <param name="contactReach">How far beyond a plant's base, in metres, it shades and shelters the grass.</param>
            public Settings(GrassFieldProfile profile, float densityScale, float contactClearing, float contactReach)
            {
                ChunkSize = profile.ChunkSize;
                Spacing = 1f / Mathf.Sqrt(profile.TuftsPerSquareMetre * densityScale);
                Cells = Mathf.CeilToInt(ChunkSize / Spacing);
                MinimumLayerWeight = profile.MinimumLayerWeight;
                MinSlopeCos = Mathf.Cos(profile.MaxSlopeDegrees * Mathf.Deg2Rad);
                HeightRange = profile.HeightRange;
                HeightClumping = profile.HeightClumping;
                HeightClumpSize = profile.HeightClumpSize;
                FlowerChance = profile.FlowerChance;
                DensityVariation = profile.DensityVariation;
                DensityVariationSize = profile.DensityVariationSize;
                ContactGrowth = profile.ContactGrowth;
                ContactClearing = contactClearing;
                ContactReach = contactReach;
            }

            /// <summary>The most tufts one chunk can hold: one per grid cell.</summary>
            public int MaxTuftsPerChunk => Cells * Cells;
        }

        /// <summary>The tufts of one biome in one chunk.</summary>
        /// <param name="keptDensity">Only tufts whose rank is below this are kept: 1 builds the
        /// full chunk, 1/16 the sixteenth a distant camera needs. Each cell draws its own numbers,
        /// so a sparser build is exactly a subset of a denser one.</param>
        /// <param name="capacity">The most tufts the caller can store; a build over it (a
        /// many-sigma fluke) keeps the lowest ranks, which are the ones drawn farthest.</param>
        public static GrassInstance[] Build(Vector2Int chunk, Settings settings, PaintedGroundPatch[] ground,
                                            float minimumHeight, int seed, float keptDensity, int capacity)
        {
            // Sized once: growing it by doubling would leave several large dead arrays behind per
            // chunk, and the garbage collector pauses the main thread to clear them.
            var tufts = new List<GrassInstance>(capacity);
            float spacing = settings.Spacing;
            float fullWeight = settings.MinimumLayerWeight;
            Vector2 heights = settings.HeightRange;
            float clumpScale = 1f / settings.HeightClumpSize;
            float clumpOffset = Mathf.Repeat(seed * 0.618034f, 1f) * ClumpSeedSpread;
            float clearingScale = 1f / settings.DensityVariationSize;
            float clearingOffset = clumpOffset + ClumpSeedSpread * 0.5f;

            float originX = chunk.x * settings.ChunkSize;
            float originZ = chunk.y * settings.ChunkSize;
            float endX = originX + settings.ChunkSize;
            float endZ = originZ + settings.ChunkSize;

            for (int i = 0; i < settings.Cells; i++)
            {
                for (int j = 0; j < settings.Cells; j++)
                {
                    var random = new ScatterCellRandom(chunk, i, j, seed);
                    // The rank comes first, so the cells a sparse build skips cost one hash.
                    float rank = random.Next();
                    if (rank >= keptDensity)
                        continue;
                    float x = originX + (i + random.Next()) * spacing;
                    float z = originZ + (j + random.Next()) * spacing;
                    float keep = random.Next();
                    if (x >= endX || z >= endZ)
                        continue;
                    if (!TrySample(ground, x, z, out float y, out float slopeCos, out float weight,
                                   out GroundContact[] plants))
                        continue;
                    if (y < minimumHeight || slopeCos < settings.MinSlopeCos)
                        continue;
                    // Thinned by the paint toward its edge, and by noise into clumps and clearings.
                    float clearing = Mathf.InverseLerp(ClumpNoiseLow, ClumpNoiseHigh,
                        Mathf.PerlinNoise(x * clearingScale + clearingOffset, z * clearingScale + clearingOffset));
                    float coverage = Mathf.InverseLerp(fullWeight * 0.5f, fullWeight, weight)
                                   * (1f - settings.DensityVariation * clearing);
                    if (keep > coverage)
                        continue;
                    // Nothing grows through a plant's base; around it the grass is shaded and taller.
                    if (!TryContact(plants, x, z, settings, out float contact))
                        continue;

                    float clump = Mathf.InverseLerp(ClumpNoiseLow, ClumpNoiseHigh,
                        Mathf.PerlinNoise(x * clumpScale + clumpOffset, z * clumpScale + clumpOffset));
                    float height = Mathf.Lerp(heights.x, heights.y,
                                              Mathf.Lerp(random.Next(), clump, settings.HeightClumping))
                                 * (1f + contact * settings.ContactGrowth);
                    float yaw = random.Next() * Mathf.PI * 2f;
                    float tint = random.Next();
                    float flowerRoll = random.Next();
                    float flowerHue = random.Next();
                    float accent = flowerRoll < settings.FlowerChance ? 0.05f + flowerHue : 0f;
                    tufts.Add(new GrassInstance(new Vector3(x, y, z), height, yaw, tint, contact, accent, rank));
                }
            }
            if (tufts.Count > capacity)
            {
                tufts.Sort((a, b) => a.YawTintAccent.w.CompareTo(b.YawTintAccent.w));
                tufts.RemoveRange(capacity, tufts.Count - capacity);
            }
            return tufts.ToArray();
        }

        /// <summary>
        /// The most tufts a build at <paramref name="keptDensity"/> can return, short of a
        /// six-sigma fluke: every cell kept independently with that chance.
        /// </summary>
        public static int CapacityFor(Settings settings, float keptDensity) =>
            ScatterGrid.CapacityFor(settings.MaxTuftsPerChunk, keptDensity);

        private static bool TrySample(PaintedGroundPatch[] ground, float x, float z, out float height,
                                      out float slopeCos, out float weight, out GroundContact[] plants)
        {
            foreach (PaintedGroundPatch patch in ground)
            {
                if (patch.TrySample(x, z, out height, out Vector3 normal, out weight))
                {
                    slopeCos = normal.y;
                    plants = patch.Contacts;
                    return true;
                }
            }
            height = slopeCos = weight = 0f;
            plants = null;
            return false;
        }

        /// <summary>
        /// False inside the cleared part of a plant's base. Otherwise <paramref name="contact"/> is
        /// how close the nearest plant is: 1 at the cleared edge, easing to 0 at its reach.
        /// </summary>
        private static bool TryContact(GroundContact[] plants, float x, float z, in Settings settings,
                                       out float contact)
        {
            contact = 0f;
            foreach (GroundContact plant in plants)
            {
                float dx = x - plant.X;
                float dz = z - plant.Z;
                float cleared = plant.Radius * settings.ContactClearing;
                float span = plant.Radius - cleared + settings.ContactReach;
                float distanceSquared = dx * dx + dz * dz;
                if (distanceSquared > (cleared + span) * (cleared + span))
                    continue;
                float beyond = Mathf.Sqrt(distanceSquared) - cleared;
                if (beyond < 0f)
                    return false;
                contact = Mathf.Max(contact, Mathf.SmoothStep(0f, 1f, 1f - beyond / Mathf.Max(span, MinContactSpan)));
            }
            return true;
        }

    }
}
