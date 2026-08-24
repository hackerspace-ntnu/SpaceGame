using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Pure math for the gravel blaster's shot — kept free of scene and network state so it is
    /// unit-testable and so the authority and the cosmetic blast provably agree. One seed, rolled
    /// by the owner and carried in the use message, decides both the pellet spread and whether
    /// the gun backfires: the server bills exactly the shot every machine draws.
    /// </summary>
    public static class GravelBlastMath
    {
        /// <summary>
        /// Does this shot blow back into the holder? Exactly one seed in <paramref name="chance"/>
        /// does, uniformly across the seed space. A chance of zero or less never backfires.
        /// </summary>
        public static bool Backfires(int seed, int chance)
            => chance > 0 && unchecked((uint)seed) % (uint)chance == 0;

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
        /// Velocity handed to the holder when the gun backfires: horizontally opposite the aim,
        /// tilted upward. The tilt is load-bearing, not flavour — PlayerMovement never touches
        /// vertical velocity, so the up-component survives unconditionally and un-grounds the
        /// victim, which is what lets CarryMomentum protect the horizontal half (see FlungBody).
        /// Aiming straight down resolves to straight up: a degenerate horizontal is not a reason
        /// for the kick to vanish.
        /// </summary>
        public static Vector3 BackfireVelocity(Vector3 aimDir, float speed, float upwardTiltDeg)
        {
            Vector3 flat = Vector3.ProjectOnPlane(-aimDir, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) return Vector3.up * speed;

            float rad = upwardTiltDeg * Mathf.Deg2Rad;
            return (flat.normalized * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad)) * speed;
        }
    }
}
