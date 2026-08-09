// Drive for the robot ostrich. Turns input -- rider or AI -- into a commanded twist (forward speed
// plus yaw rate) and hands it to OstrichLocomotion.
//
// There is no keyboard path. SteerModule is the single input route for a mounted player, and a
// component polling the keyboard alongside it would fight the rider for the same frame.
//
// It deliberately does NOT write the body's transform. The legs decide how far the bird actually
// gets: OstrichLocomotion clamps the twist to what the stride can carry, so a throttle the legs
// cannot honour produces a slower bird rather than one skating ahead of its own feet. `Velocity`
// therefore reports the achieved motion, not the request.
//
// Implements IRiderControllable, the channel SteerModule forwards mounted-player input through
// (input.Move.x = yaw, input.Move.y = throttle), and IMovementMotor, which is both what lets
// SteerModule find the rider channel and what lets an AgentController drive the bird from a
// MoveIntent with no extra code.
//
// Rider input wins over the keyboard and over AI on any frame it arrives, mirroring the guard the
// other motors use so two channels cannot fight for the same frame.
//
// This is a near-twin of SpiderWalkerDriver. The shared steering logic wants extracting into a
// common base once both are known good; it is duplicated for now rather than refactoring a
// component that is already shipping on the walking station.
using UnityEngine;

[RequireComponent(typeof(OstrichLocomotion))]
public class OstrichDriver : MonoBehaviour, IRiderControllable, IMovementMotor
{
    [Header("Speeds")]
    [Tooltip("Top speed asked for at full throttle. Clamped to what the legs can carry, so " +
             "raising it past OstrichLocomotion.MaxSpeed does nothing.")]
    [SerializeField] private float moveSpeed = 9f;
    [SerializeField] private float turnSpeed = 90f;
    [Tooltip("How quickly the bird reaches commanded speed. Ostriches accelerate hard.")]
    [SerializeField] private float acceleration = 4f;
    [Tooltip("Throttle below which the bird walks rather than runs. Only affects how fast it " +
             "asks to go; the gait's own duty blend does the rest.")]
    [Range(0f, 1f)]
    [SerializeField] private float walkThrottle = 0.45f;

    [Header("Input")]
    [Tooltip("Walk forward on its own, for looking at the gait without a rider aboard. Debug only.")]
    [SerializeField] private bool autoWalk;

    [Header("AI steering")]
    [SerializeField] private float defaultStopDistance = 2f;
    [Tooltip("Heading error, in degrees, above which the bird turns on the spot instead of " +
             "running on.")]
    [SerializeField] private float turnInPlaceAngle = 60f;

    private float forward;
    private float turn;
    private float currentSpeed;
    private int riderFrame = -1;

    private OstrichLocomotion locomotion;
    private Vector3? destination;
    private float stopDistance;

    private void Awake()
    {
        locomotion = GetComponent<OstrichLocomotion>();
        stopDistance = defaultStopDistance;
    }

    /// True on any frame a mounted rider supplied input.
    public bool IsRiderDriven => riderFrame == Time.frameCount;

    /// Drive externally: both in -1..1.
    public void SetInput(float forwardInput, float turnInput)
    {
        forward = Mathf.Clamp(forwardInput, -1f, 1f);
        turn = Mathf.Clamp(turnInput, -1f, 1f);
    }

    // ─────────── IRiderControllable ───────────

    public void ApplyRiderInput(in RiderInput input, float deltaTime)
    {
        riderFrame = Time.frameCount;
        forward = Mathf.Clamp(input.Move.y, -1f, 1f);
        turn = Mathf.Clamp(input.Move.x, -1f, 1f);
    }

    // ─────────── IMovementMotor ───────────

    public Vector3 Velocity => locomotion != null ? locomotion.MeasuredVelocity : Vector3.zero;
    public bool IsImmobile => false;
    public Vector3? CurrentDestination => destination;

    public bool HasReachedDestination =>
        !destination.HasValue || FlatDistanceTo(destination.Value) <= stopDistance;

    public void Tick(in MoveIntent intent, float deltaTime)
    {
        // Rider owns the frame; skip the AI channel so the two cannot fight.
        if (IsRiderDriven) return;

        switch (intent.Type)
        {
            case AgentIntentType.MoveToPosition:
                destination = intent.TargetPosition;
                stopDistance = intent.StopDistance > 0f ? intent.StopDistance : defaultStopDistance;
                break;
            case AgentIntentType.StopAndFacePosition:
                destination = null;
                FaceTowards(intent.FacePosition);
                return;
            default:
                destination = null;
                forward = 0f;
                turn = 0f;
                return;
        }

        if (HasReachedDestination) { forward = 0f; turn = 0f; return; }
        SteerTowards(destination.Value, intent.SpeedMultiplier);
    }

    public void ForceStop()
    {
        destination = null;
        forward = 0f;
        turn = 0f;
        currentSpeed = 0f;
        // Cancel the standing order immediately rather than waiting for the next Update, so a stop
        // requested mid-frame cannot leak one more frame of travel into the legs.
        if (locomotion != null) locomotion.SetTwist(0f, 0f);
    }

    public void NudgeDestination(Vector3 offset)
    {
        if (destination.HasValue) destination = destination.Value + offset;
    }

    public void SuggestDestination(Vector3 position) => destination = position;

    private float FlatDistanceTo(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    private void FaceTowards(Vector3 worldPoint)
    {
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-4f) { turn = 0f; return; }
        turn = OstrichSteering.Turn(HeadingErrorTo(to));
        forward = 0f;
    }

    private void SteerTowards(Vector3 worldPoint, float speedMultiplier)
    {
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-4f) { forward = 0f; turn = 0f; return; }
        float error = HeadingErrorTo(to);
        turn = OstrichSteering.Turn(error);
        forward = OstrichSteering.Throttle(error, turnInPlaceAngle, speedMultiplier);
    }

    private float HeadingErrorTo(Vector3 flatDirection) =>
        Vector3.SignedAngle(transform.forward, flatDirection.normalized, Vector3.up);

    private void Update()
    {
        if (IsRiderDriven || destination.HasValue)
        {
            // rider or AI owns forward/turn this frame
        }
        else if (autoWalk)
        {
            forward = walkThrottle;
        }
        else
        {
            // Nobody is driving. There is deliberately no keyboard fallback: SteerModule is the
            // one input path, and a second one polling the keyboard directly would drive the bird
            // out from under a rider who is not touching it.
            forward = 0f;
            turn = 0f;
        }

        float dt = Time.deltaTime;
        currentSpeed = Mathf.Lerp(currentSpeed, forward * moveSpeed, 1f - Mathf.Exp(-acceleration * dt));

        // Hand over the request and let the legs answer it. Every write to the body's transform
        // lives in OstrichLocomotion so there is exactly one owner of the pose.
        locomotion.SetTwist(currentSpeed, turn * turnSpeed);
    }
}
