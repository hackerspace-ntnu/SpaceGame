using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// Moves the craft. Sole owner of the hull transform — the same rule
    /// <c>SpiderWalkerLocomotion</c> follows, and for the same reason: two things writing one
    /// transform fight every frame.
    ///
    /// Each frame it asks the rig what the sails made, turns that into velocity, lets the foil
    /// decide how high to sit and how much leeway to allow, and writes one pose. There is no
    /// rudder and no throttle: everything the player does, they do to the rigging.
    ///
    /// Transform-driven rather than physics-driven, so a rider standing on the deck needs
    /// <c>WalkerPlatformCarrier</c> to come along with it.
    /// </summary>
    [DefaultExecutionOrder(100)]    // after SailRig (50), before WalkerPlatformCarrier (200)
    [RequireComponent(typeof(SailRig))]
    [RequireComponent(typeof(FoilLift))]
    public class DuneFoilLocomotion : MonoBehaviour
    {
        [Header("Mass")]
        [Tooltip("Craft mass in kg. Sail force divided by this is acceleration, so it sets how " +
                 "quickly the rig powers the hull up.")]
        [SerializeField, Min(1f)] private float mass = 1800f;

        [Header("Hull")]
        [Tooltip("Top speed the hull will reach however hard it is driven, m/s.")]
        [SerializeField, Min(1f)] private float maxSpeed = 45f;

        [Tooltip("Resistance that does not come from the sand: air on the hull, rig windage.")]
        [SerializeField, Min(0f)] private float parasiticDrag = 0.008f;

        [Header("Steering")]
        [Tooltip("Yaw inertia. Divides the rig's torque, so a bigger number turns more slowly.")]
        [SerializeField, Min(1f)] private float yawInertia = 9000f;

        [Tooltip("Damping on the turn rate. Without it the craft oscillates about its heading " +
                 "instead of settling on one.")]
        [SerializeField, Min(0f)] private float yawDamping = 1.6f;

        [Tooltip("Hard cap on turn rate, deg/s. A craft this size should never spin.")]
        [SerializeField, Min(1f)] private float maxTurnRate = 28f;

        [Header("Heel and trim")]
        [Tooltip("Righting moment. Larger means a stiffer craft that heels less.")]
        [SerializeField, Min(1f)] private float rightingMoment = 1500f;

        [Tooltip("Most the craft will lean, degrees.")]
        [SerializeField, Range(0f, 45f)] private float maxHeel = 22f;

        [Tooltip("How quickly it rolls to the heel the sails call for.")]
        [SerializeField, Min(0.1f)] private float heelSpeed = 2.2f;

        [Tooltip("Degrees of bow-down pitch per m/s² of acceleration.")]
        [SerializeField] private float pitchPerAcceleration = 0.6f;

        [Tooltip("Most it will pitch, degrees.")]
        [SerializeField, Range(0f, 20f)] private float maxPitch = 7f;

        [SerializeField, Min(0.1f)] private float pitchSpeed = 2.5f;

        [Header("Terrain")]
        [Tooltip("Also lean the hull to follow the slope of the dune underneath. Off by " +
                 "default: at full ride height the craft is flying and should stay level.")]
        [SerializeField] private bool followGroundSlope;

        private SailRig rig;
        private FoilLift foil;

        private Vector3 velocity;
        private float yawRate;
        private float heel;
        private float pitch;
        private float lastSpeed;

        /// <summary>World velocity, m/s.</summary>
        public Vector3 Velocity => velocity;

        /// <summary>Horizontal speed, m/s.</summary>
        public float Speed => new Vector3(velocity.x, 0f, velocity.z).magnitude;

        /// <summary>Speed as a fraction of the craft's maximum.</summary>
        public float Speed01 => Mathf.Clamp01(Speed / maxSpeed);

        /// <summary>Current heel, degrees. Positive to starboard.</summary>
        public float Heel => heel;

        /// <summary>Turn rate, deg/s.</summary>
        public float YawRate => yawRate;

        /// <summary>The rig driving this craft.</summary>
        public SailRig Rig => rig;

        /// <summary>The foil under it.</summary>
        public FoilLift Foil => foil;

        private void Awake()
        {
            rig = GetComponent<SailRig>();
            foil = GetComponent<FoilLift>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 heading = Heading();

            // Resolve the sails against the velocity we came into the frame with, so the
            // apparent wind and the force it produces belong to the same instant.
            rig.Tick(velocity, heading, dt);

            IntegrateVelocity(heading, dt);
            IntegrateHeading(dt);
            ApplyPose(dt);
        }

        private Vector3 Heading()
        {
            Vector3 h = SailAerodynamics.Flatten(transform.forward);
            return h.sqrMagnitude < 1e-6f ? Vector3.forward : h.normalized;
        }

        private void IntegrateVelocity(Vector3 heading, float dt)
        {
            Vector3 force = SailAerodynamics.Flatten(rig.TotalForce);
            velocity += force / mass * dt;

            // The foil is what makes sailing upwind possible: it refuses most of the sideways
            // motion, leaving the forward component of a mostly-sideways force to drive the
            // craft. Without this the sails would simply blow it to leeward.
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            float forward = Vector3.Dot(velocity, heading);
            float lateral = Vector3.Dot(velocity, right);
            lateral *= 1f - Mathf.Clamp01(foil.LateralGrip() * dt * 10f);

            float speed = Mathf.Abs(forward);
            float decel = (foil.DragDeceleration(speed) + parasiticDrag * speed * speed) * dt;
            forward -= Mathf.Sign(forward) * Mathf.Min(Mathf.Abs(forward), decel);

            // A sand craft does not sail backwards; a back-winded sail stops it instead.
            if (forward < 0f) forward = Mathf.Max(forward, -maxSpeed * 0.08f);
            forward = Mathf.Clamp(forward, -maxSpeed * 0.08f, maxSpeed);

            velocity = heading * forward + right * lateral;
        }

        private void IntegrateHeading(float dt)
        {
            // Sail balance is the entire helm: torque comes from where each sail sits relative
            // to the foil, so trimming main against jib is how the player steers.
            float angularAccel = rig.TotalTorque / yawInertia * Mathf.Rad2Deg;
            yawRate += angularAccel * dt;
            yawRate -= yawRate * Mathf.Clamp01(yawDamping * dt);
            yawRate = Mathf.Clamp(yawRate, -maxTurnRate, maxTurnRate);

            // A craft with no way on has no steerage: the foil needs flow over it to bite.
            float steerage = Mathf.Clamp01(Speed / 4f);
            transform.Rotate(Vector3.up, yawRate * steerage * dt, Space.World);
        }

        private void ApplyPose(float dt)
        {
            float speed = Speed;

            float targetHeel = SailAerodynamics.HeelAngle(rig.TotalForce, Heading(),
                                                          rightingMoment, maxHeel);
            heel = Mathf.Lerp(heel, targetHeel, 1f - Mathf.Exp(-heelSpeed * dt));

            float accel = (speed - lastSpeed) / Mathf.Max(dt, 1e-4f);
            lastSpeed = speed;
            float targetPitch = Mathf.Clamp(accel * pitchPerAcceleration, -maxPitch, maxPitch);
            pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-pitchSpeed * dt));

            float y = foil.Tick(speed, dt);

            Vector3 p = transform.position + SailAerodynamics.Flatten(velocity) * dt;
            transform.position = new Vector3(p.x, y, p.z);

            // Rebuild the whole rotation from yaw + heel + pitch each frame rather than
            // accumulating: incremental roll and pitch drift, and on a craft that leans this
            // far the drift shows up as a hull slowly winding over.
            float yaw = transform.eulerAngles.y;
            Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
            Quaternion lean = Quaternion.Euler(pitch, 0f, -heel);

            if (followGroundSlope && foil.HasGround)
            {
                // Only while the hull is down in the sand. Once it is up on the foil the craft
                // is flying and should hold its own attitude, not copy the dune it is over —
                // so this fades out exactly as the hull lifts clear.
                float onSand = 1f - foil.RideHeight01;
                Quaternion slope = Quaternion.FromToRotation(Vector3.up, foil.GroundNormal);
                flat = Quaternion.Slerp(Quaternion.identity, slope, onSand) * flat;
            }

            transform.rotation = flat * lean;
        }

        /// <summary>Stop the craft dead. For spawning, teleports and tests.</summary>
        public void Halt()
        {
            velocity = Vector3.zero;
            yawRate = 0f;
            lastSpeed = 0f;
        }
    }
}
