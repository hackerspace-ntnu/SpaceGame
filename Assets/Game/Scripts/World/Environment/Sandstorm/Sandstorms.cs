// The one thing the rest of the game talks to.
//
// Damage, AI perception, audio and all three render layers are consumers of this facade and know
// nothing about each other or about the manager. Adding a new way for a storm to affect the world
// is a new file that calls one function here — that is the whole reason it exists.
//
// Every query answers sensibly with no manager, no session and no storms: zero intensity, full
// visibility. A scene without weather is not a broken scene.
using SpaceGame.Core;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    /// <summary>Everything worth knowing about the storm at one point, in one read.</summary>
    public struct StormSample
    {
        public int Id;
        public SandstormProfile Profile;
        public StormFootprint Footprint;

        /// <summary>Sand density at the point, 0 to 1. Shape and lifecycle, before shelter.</summary>
        public float Density;

        /// <summary>
        /// The storm's lifecycle intensity, 0 to 1, with no dependence on where it was sampled.
        /// <see cref="Density"/> is this multiplied by the shape — the renderer needs the two
        /// apart, because the shader computes the shape itself for every pixel.
        /// </summary>
        public float Intensity;

        /// <summary>How much cover the point has, 0 to 1. See <see cref="SandstormShelter"/>.</summary>
        public float Shelter;

        /// <summary>What actually hurts: density with shelter taken out.</summary>
        public float Exposure;

        /// <summary>How far you can see here, in metres.</summary>
        public float Visibility;

        /// <summary>Unit XZ the storm is travelling. What the sand is streaking toward.</summary>
        public Vector2 WindDirection;
    }

    public static class Sandstorms
    {
        /// <summary>
        /// Sight range in clear air, in metres. Storms interpolate down from this, so it only has
        /// to be further than anyone can meaningfully see across this desert.
        /// </summary>
        public const float ClearVisibility = 1500f;

        /// <summary>
        /// The clock every machine agrees on. Server time when there is a session — including for
        /// clients, which estimate it — and plain game time when there is not.
        ///
        /// <para>
        /// Read by the sky as well as the weather (<c>DayNightCycle.Now</c>), which is why it is
        /// still the RAW reading and not <see cref="WeatherTime"/>: the two systems anchor
        /// themselves against it independently, and a clock one of them could move underneath the
        /// other would let restoring a saved storm swing the sun.
        /// </para>
        /// </summary>
        public static double Now => StormClock.Shared;

        /// <summary>
        /// What time the WEATHER thinks it is. Every storm's <c>StartTime</c> is a reading of this.
        ///
        /// <para>
        /// Not the raw clock, because the raw clock restarts at zero every session and a saved
        /// storm's StartTime read back against a fresh zero is a storm that has not begun yet. See
        /// <see cref="StormClock"/>: an anchor over the shared clock, so restoring a world is a
        /// matter of re-stating which reading counts as now rather than replaying elapsed time.
        /// Every machine still derives the same number — the anchor is replicated by
        /// <c>SandstormManager</c>, exactly as the sky's is by <c>SkyNetwork</c>.
        /// </para>
        /// </summary>
        public static double WeatherTime => StormClock.Now;

        /// <summary>
        /// Restore-only. Puts the weather clock back to a saved reading. Called by the save system;
        /// do not call from gameplay.
        /// </summary>
        public static void RestoreClock(double weatherTime) => StormClock.RestoreNow(weatherTime);

        /// <summary>
        /// Restore-only. Starts the weather clock over, for a world that has no saved weather.
        /// Called by the save system; do not call from gameplay.
        /// </summary>
        public static void ResetClock() => StormClock.Reset();

        /// <summary>
        /// Every live storm as a record, for the save system. Empty when there is no manager.
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<StormInstance> Records =>
            SandstormManager.Instance != null
                ? SandstormManager.Instance.Records
                : System.Array.Empty<StormInstance>();

        /// <summary>
        /// Restore-only. Replaces every live storm with the saved set and resumes id allocation from
        /// <paramref name="nextId"/>. Server-only, and false when there is no manager — same
        /// contract as <see cref="TrySpawn"/>. Called by the save system; do not call from gameplay.
        /// </summary>
        public static bool RestoreStorms(System.Collections.Generic.IReadOnlyList<StormInstance> storms, int nextId)
        {
            SandstormManager manager = SandstormManager.Instance;
            return manager != null && manager.RestoreRecords(storms, nextId);
        }

        /// <summary>The id the next storm would be given. Saved so ids stay unique across a reload.</summary>
        public static int NextId => SandstormManager.Instance != null ? SandstormManager.Instance.NextId : 1;

        /// <summary>True when at least one storm exists anywhere in the world.</summary>
        public static bool Any => SandstormManager.Instance != null &&
                                 SandstormManager.Instance.Resolved.Count > 0;

        /// <summary>Sand density at a point, 0 to 1, ignoring shelter.</summary>
        public static float IntensityAt(Vector3 worldPos)
        {
            SandstormManager manager = SandstormManager.Instance;
            if (manager == null)
                return 0f;

            float strongest = 0f;
            var storms = manager.Resolved;
            for (int i = 0; i < storms.Count; i++)
                strongest = Mathf.Max(strongest, storms[i].DensityAt(worldPos));

            // Overlapping storms take the strongest rather than adding up. Two storms on top of
            // each other are not twice as opaque as one — they are one bad afternoon.
            return strongest;
        }

        /// <summary>How sheltered a point is, 0 (open desert) to 1 (fully enclosed).</summary>
        public static float ShelterAt(Vector3 worldPos) => SandstormShelter.ShelterAt(worldPos);

        /// <summary>Density after shelter. This is the number damage is scaled by.</summary>
        public static float ExposureAt(Vector3 worldPos)
        {
            float intensity = IntensityAt(worldPos);
            return intensity <= 0f ? 0f : intensity * (1f - ShelterAt(worldPos));
        }

        /// <summary>How far anything can see from this point, in metres.</summary>
        public static float VisibilityAt(Vector3 worldPos)
        {
            if (!TrySample(worldPos, out StormSample sample))
                return ClearVisibility;

            return sample.Visibility;
        }

        /// <summary>
        /// What an agent's sight range should be multiplied by here, 0 to 1. Shelter is
        /// deliberately not applied: a robot standing in a doorway still cannot see through the
        /// storm outside it.
        /// </summary>
        public static float SightFactorAt(Vector3 worldPos)
        {
            if (!TrySample(worldPos, out StormSample sample))
                return 1f;

            return Mathf.Lerp(1f, sample.Profile.aiSightFactorAtFullIntensity, sample.Density);
        }

        /// <summary>
        /// The strongest storm at a point and everything derived from it. False when the air is
        /// clear, in which case <paramref name="sample"/> is left at its default.
        /// </summary>
        public static bool TrySample(Vector3 worldPos, out StormSample sample)
        {
            sample = default;

            SandstormManager manager = SandstormManager.Instance;
            if (manager == null)
                return false;

            var storms = manager.Resolved;
            float bestDensity = 0f;
            int best = -1;

            for (int i = 0; i < storms.Count; i++)
            {
                float density = storms[i].DensityAt(worldPos);
                if (density <= bestDensity)
                    continue;

                bestDensity = density;
                best = i;
            }

            if (best < 0)
                return false;

            ResolvedStorm storm = storms[best];
            float shelter = ShelterAt(worldPos);

            sample = new StormSample
            {
                Id = storm.Id,
                Profile = storm.Profile,
                Footprint = storm.Footprint,
                Density = bestDensity,
                Intensity = storm.Intensity,
                Shelter = shelter,
                Exposure = bestDensity * (1f - shelter),
                Visibility = Mathf.Lerp(ClearVisibility, storm.Profile.visibilityAtFullIntensity, bestDensity),
                WindDirection = storm.Footprint.Heading,
            };

            return true;
        }

        /// <summary>
        /// Starts a storm. Server-only, and false when there is no manager — so a quest or a debug
        /// key can call it anywhere without first checking whether this world has weather.
        /// </summary>
        public static bool TrySpawn(SandstormProfile profile, Vector3 origin, float headingDegrees, out int id)
        {
            id = 0;
            SandstormManager manager = SandstormManager.Instance;
            return manager != null && manager.TrySpawn(profile, origin, headingDegrees, out id);
        }

        /// <summary>Ends a storm early. Server-only.</summary>
        public static bool Despawn(int id)
        {
            SandstormManager manager = SandstormManager.Instance;
            return manager != null && manager.Despawn(id);
        }
    }
}
