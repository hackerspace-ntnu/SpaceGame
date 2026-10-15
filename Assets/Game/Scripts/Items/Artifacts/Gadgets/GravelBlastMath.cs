using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The gravel blaster's own math on top of <see cref="PelletShotMath"/>: the backfire. Pure
    /// and seeded for the same reason — the shot's seed decides whether it backfires, so the
    /// server bills exactly the outcome every machine draws.
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
