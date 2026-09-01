// The arc the paint travels.
//
// The gun used to be hitscan: a straight raycast to the crosshair, and paint landed wherever you
// were pointing, out to 120 m and later 30. That is a paint GUN. This is a hose — the stream leaves
// the nozzle at a speed, gravity pulls it down, and where it lands is decided by the arc, not by
// the crosshair. Pointing at a far wall no longer reaches it; you have to lift the nozzle and lob
// the stream, and close range is where the thing is precise.
//
// It is a static function over plain arguments, with no MonoBehaviour and no state, for two
// reasons. The obvious one is that it can be tested without a scene. The one that matters more is
// that the PARTICLES have to agree with it: the jet's droplets are an ordinary Unity
// ParticleSystem, and a system whose start speed and gravity modifier match the numbers below
// traces the same parabola for free. If these two ever disagree the paint lands somewhere the
// player did not see the stream go, which is the single worst thing a hose can do.
using UnityEngine;

namespace SpaceGame.Portals
{
    public static class PortalJet
    {
        /// <summary>
        /// How many straight segments the arc is tested as.
        ///
        /// The path is a parabola and the test is a series of chords across it, so this trades
        /// accuracy for casts. Twenty over a second and a half of flight puts the chords a few
        /// centimetres off the true curve at the fastest part of the arc, which is far below the
        /// radius of the blob that lands there.
        /// </summary>
        public const int Segments = 20;

        /// <summary>
        /// Where the stream is <paramref name="time"/> seconds after leaving the nozzle.
        ///
        /// Plain projectile motion. Shared by the trace below and by anything that wants to draw
        /// the arc, so there is one definition of the curve rather than two that drift.
        /// </summary>
        public static Vector3 Sample(Vector3 origin, Vector3 direction, float speed,
                                     float gravityScale, float time) =>
            origin
            + direction * (speed * time)
            + Physics.gravity * (gravityScale * 0.5f * time * time);

        /// <summary>
        /// Follow the stream until it hits something or runs out of flight.
        ///
        /// <paramref name="flightTime"/> comes back as the time to the impact, which is what the
        /// paint's landing is delayed by — a hose's far end arrives noticeably later than its near
        /// end, and using a straight-line distance for that would make lobbed paint land early.
        /// </summary>
        public static bool Trace(Vector3 origin, Vector3 direction, float speed, float gravityScale,
                                 float maxTime, LayerMask mask,
                                 out RaycastHit hit, out float flightTime)
        {
            hit = default;
            flightTime = maxTime;

            if (direction.sqrMagnitude < 1e-6f) return false;
            direction.Normalize();

            Vector3 previous = origin;
            float step = maxTime / Segments;

            for (int i = 1; i <= Segments; i++)
            {
                float time = step * i;
                Vector3 point = Sample(origin, direction, speed, gravityScale, time);

                Vector3 leg = point - previous;
                float length = leg.magnitude;

                if (length > 1e-5f &&
                    Physics.Raycast(previous, leg / length, out hit, length, mask,
                                    QueryTriggerInteraction.Ignore))
                {
                    // Interpolated inside the segment rather than snapped to its end, so a droplet
                    // that hits early in a long chord is not reported as arriving a whole step late.
                    flightTime = step * (i - 1) + step * (hit.distance / length);
                    return true;
                }

                previous = point;
            }

            return false;
        }

        /// <summary>
        /// The furthest the stream reaches on flat ground, for tuning and for tests.
        ///
        /// Not used at runtime. It exists so "little reach" is a number somebody can check rather
        /// than a feeling — at 13 m/s the answer is about 17 m thrown at 45 degrees, and nearer 9
        /// held level at chest height.
        /// </summary>
        public static float BallisticRange(float speed, float gravityScale) =>
            speed * speed / Mathf.Max(Physics.gravity.magnitude * gravityScale, 1e-4f);
    }
}
