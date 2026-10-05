// Keeps an escort on station beside its flagship, drifting about that station on its own.
//
// The station is a point in the flagship's own frame, so the escort turns with the fleet. About it the
// escort wanders on three slow sines of its own period and phase, so no two escorts move in step and
// none of them moves in step with the flagship — the independent bob and sway that sells a fleet of
// separate ships rather than one rigid model (GDC-L1-ANIM-0005, secondary motion).
//
// It steers through the motor like any module: it aims at the station point, led by the flagship's own
// velocity so it does not trail its station by speed ÷ gain, at a speed proportional to how far off it
// is, and asks the motor (through the intent's facing channel) to hold the flagship's heading rather
// than turn toward every wander point.
//
// Nothing to save: the wander is a pure function of time and the serialized phase, and the escort's
// pose is saved like any entity's.
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Vehicles
{
    public class FleetEscortModule : BehaviourModuleBase
    {
        [Header("Station")]
        [Tooltip("The hull this escort keeps station on.")]
        [SerializeField] private Transform flagship;

        [Tooltip("Where the escort keeps station, in the flagship's own frame (m).")]
        [SerializeField] private Vector3 station;

        [Header("Wander about the station")]
        [Tooltip("How far (m) the escort strays from its station along each of the flagship's axes.")]
        [SerializeField] private Vector3 wanderAmplitude = new(14f, 5f, 14f);

        [Tooltip("Seconds per full swing along each axis. Different periods keep the path from repeating.")]
        [SerializeField] private Vector3 wanderPeriod = new(61f, 43f, 73f);

        [Tooltip("Phase (radians) of each axis's swing. Different per escort, so no two move in step.")]
        [SerializeField] private Vector3 wanderPhase;

        [Header("Station keeping")]
        [Tooltip("Speed asked for per metre the escort is off its station point (1/s).")]
        [SerializeField] private float catchUpGain = 0.25f;

        [Tooltip("How far ahead (s) of the flagship's motion to aim, so the escort does not trail its station.")]
        [SerializeField] private float leadSeconds = 4f;

        [Tooltip("Inside this distance (m) of the station point the motor stops pushing.")]
        [SerializeField] private float stopDistance = 0.5f;

        private IMovementMotor ownMotor;
        private IMovementMotor flagshipMotor;
        private Transform flagshipMotorOwner;

        public Transform Flagship => flagship;
        public Vector3 Station => station;

        public override string ModuleDescription =>
            "Keeps station on a flagship at a point in its frame, wandering about it on three slow sines.\n\n" +
            "• station — flagship-local offset\n" +
            "• wanderAmplitude / wanderPeriod / wanderPhase — this escort's own drift\n" +
            "• Holds the flagship's heading through the intent's facing channel";

        protected override void OnValidate()
        {
            wanderPeriod = new Vector3(Mathf.Max(1f, wanderPeriod.x), Mathf.Max(1f, wanderPeriod.y),
                                       Mathf.Max(1f, wanderPeriod.z));
            catchUpGain = Mathf.Max(0.01f, catchUpGain);
            leadSeconds = Mathf.Max(0f, leadSeconds);
            stopDistance = Mathf.Max(0.01f, stopDistance);
        }

        /// <summary>
        /// The wander offset at <paramref name="time"/>: each axis a sine of its own amplitude, period
        /// and phase. Pure, so the drift's bounds are a unit test.
        /// </summary>
        public static Vector3 WanderOffset(Vector3 amplitude, Vector3 period, Vector3 phase, float time) => new(
            amplitude.x * Mathf.Sin(2f * Mathf.PI * time / period.x + phase.x),
            amplitude.y * Mathf.Sin(2f * Mathf.PI * time / period.y + phase.y),
            amplitude.z * Mathf.Sin(2f * Mathf.PI * time / period.z + phase.z));

        /// <summary>Where this escort should be at <paramref name="time"/>, before leading the flagship's motion.</summary>
        public Vector3 StationPoint(float time) =>
            flagship.TransformPoint(station + WanderOffset(wanderAmplitude, wanderPeriod, wanderPhase, time));

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (flagship == null)
                return null;

            Vector3 target = StationPoint(Time.time) + FlagshipVelocity() * leadSeconds;
            float distance = Vector3.Distance(context.Position, target);

            float topSpeed = OwnTopSpeed();
            float multiplier = topSpeed > 0f ? Mathf.Clamp(distance * catchUpGain / topSpeed, 0.01f, 1f) : 1f;

            return MoveIntent.MoveTo(target, stopDistance, multiplier)
                             .WithFacing(context.Position + flagship.forward);
        }

        private float OwnTopSpeed()
        {
            ownMotor ??= GetComponent<IMovementMotor>();
            return ownMotor?.TopSpeed ?? 0f;
        }

        private Vector3 FlagshipVelocity()
        {
            if (flagshipMotorOwner != flagship)
            {
                flagshipMotorOwner = flagship;
                flagshipMotor = flagship.GetComponent<IMovementMotor>();
            }

            return flagshipMotor?.Velocity ?? Vector3.zero;
        }
    }
}
