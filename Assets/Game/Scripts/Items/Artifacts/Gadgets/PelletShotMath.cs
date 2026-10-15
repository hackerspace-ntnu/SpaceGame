using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Pure math for a <see cref="PelletGunArtifact"/>'s shot — kept free of scene and network
    /// state so it is unit-testable and so the authority and the cosmetic shot provably agree. One
    /// seed, rolled by the owner and carried in the use message, decides where every pellet goes:
    /// the server bills exactly the shot every machine draws.
    /// </summary>
    public static class PelletShotMath
    {
        /// <summary>
        /// The pellet directions for a shot, deterministic in the seed. Directions are drawn
        /// uniformly over the solid angle of the cone around <paramref name="aim"/>'s forward —
        /// uniform in yaw alone would bunch the pellets on the axis and the spread would read
        /// tighter than the number says.
        /// </summary>
        public static Vector3[] PelletDirections(int seed, Quaternion aim, int count,
                                                 float spreadDeg)
        {
            var pellets = new Vector3[Mathf.Max(0, count)];
            var rng = new System.Random(seed);
            float minCos = Mathf.Cos(spreadDeg * Mathf.Deg2Rad);

            for (int i = 0; i < pellets.Length; i++)
            {
                float yaw = (float)(rng.NextDouble() * 2.0 * Mathf.PI);
                float cos = Mathf.Lerp(1f, minCos, (float)rng.NextDouble());
                float sin = Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos));
                pellets[i] = aim * new Vector3(Mathf.Cos(yaw) * sin,
                                               Mathf.Sin(yaw) * sin, cos);
            }

            return pellets;
        }

        /// <summary>
        /// How much of a pellet's damage survives the flight to <paramref name="distance"/>: all of
        /// it inside <paramref name="fullDamageRange"/>, then falling linearly to
        /// <paramref name="farFraction"/> at <paramref name="range"/> and no lower.
        ///
        /// <para>
        /// What gives each gun built on this its niche (GDC-L1-BAL-0004). The gravel blaster
        /// reaches seventy metres but tapers hard, so it is devastating in a corridor and a
        /// nuisance across a valley; the basic gun keeps most of its damage out to hundreds of
        /// metres but lands one light round a shot. Range and pellet count make a shot LOOK
        /// violent; the falloff decides where it is actually the right tool (GDC-L1-BAL-0002).
        /// </para>
        /// </summary>
        public static float DamageFalloff(float distance, float fullDamageRange, float range,
                                          float farFraction)
        {
            if (distance <= fullDamageRange) return 1f;

            // A taper with no room to run means the full-damage band covers the whole reach, so
            // everything the shot can get to is worth all of it. Reachable from the Inspector:
            // OnValidate clamps fullDamageRange to range, and the two being equal is a legitimate
            // way to say "no falloff at all".
            float taper = range - fullDamageRange;
            if (taper <= 0f) return 1f;

            float t = Mathf.Clamp01((distance - fullDamageRange) / taper);
            return Mathf.Lerp(1f, Mathf.Clamp01(farFraction), t);
        }
    }
}
