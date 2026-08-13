// The bounce. What the neck does when the body underneath it moves and the neck, being long and
// heavy at the top, does not want to.
//
// This is a SECOND-order system -- a mass on a spring -- and that distinction is the whole point.
// The standing rule on this rig is that nothing at stride frequency may go through a filter,
// because a first-order smooth costs both amplitude and phase at exactly the frequency it is meant
// to pass, and how much it costs varies with speed. A spring is not a filter: it is allowed to lag
// and overshoot, because the lag and the overshoot ARE the effect being modelled. The drive is
// taken raw and is never pre-smoothed.
//
// Pure state, no components, so an EditMode test can shake it and watch what comes out.
using UnityEngine;

namespace SpaceGame.Creatures.Ostrich
{
    /// Tunables for the ride bounce.
    [System.Serializable]
    public struct OstrichNeckSpringSettings
    {
        [Tooltip("Stiffness. Higher makes a shorter, faster bob; this sets the frequency the neck " +
                 "rings at.")]
        public float stiffness;
        [Tooltip("Damping ratio. Below 1 the neck overshoots and rings, which is what makes it read " +
                 "as whippy rather than as a rod. Around 0.35 is a long neck carrying a heavy head.")]
        public float damping;
        [Tooltip("Degrees of bend per unit of body acceleration.")]
        public float gain;
        [Tooltip("Largest bend the bounce may produce, in degrees. Stops a hard landing folding the " +
                 "neck through the body.")]
        public float maxAngle;
        [Tooltip("Largest drive accepted, in m/s^2, before it is clamped. A gait peaks near 54 at a " +
                 "full sprint; anything far above that is a snap-to-ground or a scene migration, not " +
                 "a stride, and must not be allowed to slam the neck against its clamp.")]
        public float maxDrive;

        /// Gain was measured on the real rig rather than guessed: driven by the body transform's own
        /// acceleration it gives about 2.6 deg of bend at a walk, 7.3 at a canter and 9.3 at a full
        /// sprint, which stays clear of maxAngle so the bounce never sits pinned against its clamp.
        public static OstrichNeckSpringSettings Default => new OstrichNeckSpringSettings
        {
            stiffness = 42f,
            damping = 0.35f,
            gain = 32f,
            maxAngle = 14f,
            maxDrive = 70f,
        };
    }

    public sealed class OstrichNeckSpring
    {
        /// Largest step the integrator will take in one go. A long frame is split into several of
        /// these instead of being integrated in one jump, because a stiff spring integrated over a
        /// big dt goes unstable and throws the head across the map.
        private const float MaxSubStep = 1f / 120f;

        private OstrichNeckSpringSettings s;
        private Vector2 value;          // x = pitch bend, y = roll bend, degrees
        private Vector2 velocity;

        /// Current bend, in degrees. x pitches the neck fore/aft, y rolls it side to side.
        public Vector2 Value => value;
        public Vector2 Velocity => velocity;

        public OstrichNeckSpring(OstrichNeckSpringSettings settings) => s = settings;

        public void Configure(OstrichNeckSpringSettings settings) => s = settings;

        public void Reset()
        {
            value = Vector2.zero;
            velocity = Vector2.zero;
        }

        /// One frame, driven by the body's acceleration already resolved into the body's own axes:
        /// `drive.x` is fore/aft, `drive.y` is vertical, `drive.z` is lateral.
        ///
        /// The neck lags the body, so the drive enters NEGATED: accelerate the body upward and the
        /// neck is left behind and below, which is the pitch-down you feel at the bottom of a stride.
        public void Step(float deltaTime, Vector3 drive)
        {
            if (deltaTime <= 0f) return;

            // Clamp the drive before it is scaled. A teleport -- snap-to-ground on spawn, a scene
            // migration, a streamed chunk arriving -- shows up as a single frame of effectively
            // infinite acceleration, and without this the neck slams to its limit and rings for two
            // seconds every time one happens.
            float limit = Mathf.Max(s.maxDrive, 1e-3f);
            drive = Vector3.ClampMagnitude(drive, limit);

            Vector2 forcing = new Vector2(-(drive.y + drive.x) * s.gain, -drive.z * s.gain);

            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / MaxSubStep), 1, 16);
            float h = deltaTime / steps;
            // Damping coefficient for the requested ratio, from c = 2 * zeta * sqrt(k) for unit mass.
            float c = 2f * s.damping * Mathf.Sqrt(Mathf.Max(s.stiffness, 1e-4f));

            for (int i = 0; i < steps; i++)
            {
                // Semi-implicit Euler: velocity first, then position from the NEW velocity. Stable
                // where explicit Euler quietly gains energy and rings louder every cycle.
                Vector2 accel = forcing - s.stiffness * value - c * velocity;
                velocity += accel * h;
                value += velocity * h;
            }

            if (value.magnitude > s.maxAngle)
            {
                // Clamp the position and kill the outward velocity with it, or the spring keeps
                // pushing against the clamp and snaps the moment the drive lets up.
                Vector2 direction = value.normalized;
                value = direction * s.maxAngle;
                float outward = Vector2.Dot(velocity, direction);
                if (outward > 0f) velocity -= direction * outward;
            }
        }
    }
}
