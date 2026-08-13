using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// The steerable foil. The wheel at the stern turns this, and turning this is what turns the
    /// craft.
    ///
    /// There is one thing on this vessel in contact with the sand — a fourteen-metre strut with a
    /// blade on the bottom of it — so pointing that strut IS the steering. The wheel does not
    /// apply an abstract turning force somewhere in the hull; it swings the blade, you can watch
    /// it swing, and the hull follows it round.
    ///
    /// Owns the strut transform and nothing else. It reports the turn rate the geometry asks for;
    /// <see cref="DuneFoilLocomotion"/> decides what the hull does about it.
    /// </summary>
    public class FoilRudder : MonoBehaviour
    {
        [Header("Geometry")]
        [Tooltip("The strut. Turned about the craft's vertical axis through its own pivot, which " +
                 "is where the strut meets the hull.")]
        [SerializeField] private Transform strut;

        [Tooltip("How far the blade swings at full lock, degrees. Enough to see from the deck.")]
        [SerializeField, Range(5f, 60f)] private float maxSteerAngle = 27f;

        [Tooltip("Effective distance between the steered blade and the hull's own centre of " +
                 "lateral resistance, metres. This sets the turning circle: radius is roughly " +
                 "this divided by the tangent of the steering angle, so a longer wheelbase is a " +
                 "wider, more ship-like turn. NOT the blade's lever arm — the hull resists " +
                 "sliding sideways along its whole length, and this is what that adds up to.")]
        [SerializeField, Min(1f)] private float wheelbase = 17f;

        [Header("Grip")]
        [Tooltip("What the blade can hold sideways before it washes out, m/s². This is what " +
                 "makes the craft understeer as it gets quick: the same handful of wheel that " +
                 "carves a tight circle at walking pace opens into a long arc at speed.")]
        [SerializeField, Min(0.5f)] private float maxLateralAcceleration = 8.5f;

        [Tooltip("How much steering is left when the craft is flying at the top of its strut, " +
                 "with most of the blade out of the sand. Floored well above zero: a foiler that " +
                 "becomes unsteerable exactly when it is going fastest is a way to lose the craft.")]
        [SerializeField, Range(0.2f, 1f)] private float flyingAuthority = 0.7f;

        [Header("Standstill")]
        [Tooltip("Turn rate the craft keeps with no way on at all, deg/s. Physically this is " +
                 "zero — a blade needs flow over it to bite — but a craft that cannot be pointed " +
                 "off the dune it just stopped on is stranded, so it keeps a slow shuffle.")]
        [SerializeField, Range(0f, 8f)] private float parkingYawRate = 3.5f;

        [Tooltip("Speed by which the parking shuffle has faded out entirely, m/s.")]
        [SerializeField, Min(0.5f)] private float parkingFadeSpeed = 3f;

        [Header("Feel")]
        [Tooltip("How quickly the blade swings to where the wheel is asking, deg/s. A rudder " +
                 "this size is worked by gearing, not flicked.")]
        [SerializeField, Min(5f)] private float slewRate = 55f;

        private float command;
        private float angle;

        /// <summary>Where the wheel is asking the blade to go, -1 hard a-port to +1 hard a-starboard.</summary>
        public float Command => command;

        /// <summary>Where the blade actually is, degrees. Positive is turned to starboard.</summary>
        public float SteerAngle => angle;

        /// <summary>The blade angle as a fraction of full lock, -1..1.</summary>
        public float SteerAngle01 => maxSteerAngle <= 0f ? 0f : angle / maxSteerAngle;

        /// <summary>Full lock, degrees.</summary>
        public float MaxSteerAngle => maxSteerAngle;

        /// <summary>Effective wheelbase, metres.</summary>
        public float Wheelbase { get => wheelbase; set => wheelbase = Mathf.Max(1f, value); }

        /// <summary>
        /// Point the blade. -1 is hard a-port, +1 hard a-starboard. Written by the helm every
        /// frame somebody is at the wheel.
        /// </summary>
        public void SetCommand(float value) => command = Mathf.Clamp(value, -1f, 1f);

        /// <summary>
        /// Swing the blade toward where the wheel is asking and report the turn rate the craft
        /// will settle at, degrees per second, positive to starboard.
        /// </summary>
        /// <param name="speed">Forward speed, m/s. Signed: sternway reverses the helm, as it does
        /// on anything steered from behind its centre of resistance.</param>
        /// <param name="rideHeight01">0 with the hull down, 1 flying.</param>
        /// <param name="maxYawRate">The hull's own ceiling on turn rate, deg/s.</param>
        /// <param name="deltaTime">Frame time.</param>
        public float Tick(float speed, float rideHeight01, float maxYawRate, float deltaTime)
        {
            Capture();
            angle = Mathf.MoveTowards(angle, command * maxSteerAngle, slewRate * deltaTime);
            ApplyPose();

            float authority = FoilPhysics.SteeringAuthority(rideHeight01, flyingAuthority);

            float sailing = FoilPhysics.SteadyYawRate(speed, angle, wheelbase,
                                                      maxLateralAcceleration * authority,
                                                      maxYawRate);

            // The shuffle a stopped craft keeps, faded out as soon as it has any way on so it
            // never adds to a real turn.
            float parked = parkingYawRate * command * authority *
                           (1f - Mathf.Clamp01(Mathf.Abs(speed) / parkingFadeSpeed));

            return Mathf.Clamp(sailing + parked, -maxYawRate, maxYawRate);
        }

        // Captured, not assumed: the strut's authored orientation is where "amidships" is, and
        // every turn is composed onto it. Same reason the helm captures the wheel's rest pose.
        private Quaternion restRotation = Quaternion.identity;
        private Vector3 axisInParent = Vector3.up;
        private bool posed;

        private void Awake() => Capture();

        private void Capture()
        {
            if (strut == null || posed) return;

            restRotation = strut.localRotation;

            // Turn about the CRAFT's up, expressed in the strut pivot's parent space. Anything
            // under an imported FBX keeps Blender-native local axes, so "local Y" is not the
            // vertical down here and hardcoding one is how the ship's wheel first shipped
            // spinning like a turntable. Asking the transform cannot be wrong, and because the
            // parent is rigidly attached to the hull the answer never changes.
            Transform parent = strut.parent != null ? strut.parent : strut;
            Vector3 up = transform.up;
            Vector3 axis = parent.InverseTransformDirection(up);
            axisInParent = axis.sqrMagnitude < 1e-8f ? Vector3.up : axis.normalized;

            posed = true;
        }

        private void ApplyPose()
        {
            if (strut == null) return;
            if (!posed) Capture();
            strut.localRotation = Quaternion.AngleAxis(angle, axisInParent) * restRotation;
        }

        /// <summary>Centre the blade and forget any command. For spawning, teleports and tests.</summary>
        public void Centre()
        {
            command = 0f;
            angle = 0f;
            ApplyPose();
        }

        /// <summary>Wire it up. Used by the prefab builder.</summary>
        public void Bind(Transform strutPivot, float effectiveWheelbase)
        {
            strut = strutPivot;
            wheelbase = Mathf.Max(1f, effectiveWheelbase);
            posed = false;
            Capture();
        }

        private void OnDrawGizmosSelected()
        {
            if (strut == null) return;

            Gizmos.color = new Color(0.95f, 0.35f, 0.30f, 0.9f);
            Vector3 pivot = strut.position;
            Vector3 blade = Quaternion.AngleAxis(angle, transform.up) * transform.forward;
            Gizmos.DrawLine(pivot, pivot - blade * 6f);
            Gizmos.DrawWireSphere(pivot - blade * 6f, 0.6f);
        }
    }
}
