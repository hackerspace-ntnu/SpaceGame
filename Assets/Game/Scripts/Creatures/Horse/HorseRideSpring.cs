// The bounce: what a long neck and a heavy head do when the body underneath them moves and they
// would rather not.
//
// This is a SECOND-ORDER system -- a mass on a spring -- and that is the whole point. The standing
// rule on these machines is that nothing at stride frequency may go through a filter, because a
// first-order smooth costs both amplitude and phase at exactly the frequency it is meant to pass,
// and how much it costs varies with speed. A spring is not a filter: it is allowed to lag and to
// overshoot, because the lag and the overshoot ARE the effect being modelled. The drive is taken
// raw and is never pre-smoothed.
//
// Pure state, no components, so an EditMode test can shake it and watch what comes out.
using UnityEngine;

namespace SpaceGame.Creatures.Horse
{
    /// Tunables for the ride bounce.
    [System.Serializable]
    public struct HorseRideSpringSettings
    {
        [Tooltip("Stiffness. Higher makes a shorter, faster bob; this sets the frequency the neck " +
                 "rings at.")]
        public float stiffness;
        [Tooltip("Damping ratio. Below 1 the neck overshoots and rings, which is what makes it read " +
                 "as a neck rather than a rod.")]
        public float damping;
        [Tooltip("Degrees of bend per unit of body acceleration.")]
        public float gain;
        [Tooltip("Largest bend the bounce may produce, in degrees.")]
        public float maxAngle;
        [Tooltip("Largest drive accepted, in m/s^2, before it is clamped. A gait peaks well under " +
                 "this; anything far above it is a snap-to-ground or a scene migration, not a stride, " +
                 "and must not be allowed to slam the neck against its clamp.")]
        public float maxDrive;

        public static HorseRideSpringSettings Default => new HorseRideSpringSettings
        {
            stiffness = 38f,
            damping = 0.4f,
            gain = 26f,
            maxAngle = 12f,
            maxDrive = 70f,
        };
    }

    public sealed class HorseRideSpring
    {
        /// Largest step the integrator will take in one go. A long frame is split into several of
        /// these rather than integrated in one jump: a stiff spring over a big dt goes unstable and
        /// throws the head across the map.
        private const float MaxSubStep = 1f / 120f;

        private HorseRideSpringSettings s;
        private Vector2 value;          // x = pitch bend, y = roll bend, degrees
        private Vector2 velocity;

        /// Current bend in degrees. x pitches the neck fore/aft, y rolls it side to side.
        public Vector2 Value => value;
        public Vector2 Velocity => velocity;

        public HorseRideSpring(HorseRideSpringSettings settings) => s = settings;

        public void Configure(HorseRideSpringSettings settings) => s = settings;

        public void Reset()
        {
            value = Vector2.zero;
            velocity = Vector2.zero;
        }

        /// One frame, driven by the body's acceleration already resolved into the body's own axes:
        /// `drive.x` is lateral, `drive.y` is vertical, `drive.z` is fore/aft.
        ///
        /// The neck LAGS the body, so the drive enters negated: accelerate the body upward and the neck
        /// is left behind and below, which is the pitch-down a rider feels at the bottom of a stride.
        public void Step(float deltaTime, Vector3 drive)
        {
            if (deltaTime <= 0f) return;

            // Clamp the drive before it is scaled. A teleport -- snap-to-ground on spawn, a streamed
            // chunk arriving, a scene migration -- shows up as one frame of effectively infinite
            // acceleration, and without this the neck slams to its limit and rings for two seconds.
            drive = Vector3.ClampMagnitude(drive, Mathf.Max(s.maxDrive, 1e-3f));

            Vector2 forcing = new Vector2(-(drive.y + drive.z) * s.gain, -drive.x * s.gain);

            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / MaxSubStep), 1, 16);
            float h = deltaTime / steps;
            // c = 2 * zeta * sqrt(k), for unit mass.
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
