// Drive for a legged machine. Turns a movement request into a commanded twist -- forward speed plus
// yaw rate -- and hands it to the locomotion.
//
// There are exactly two things that may move one of these machines, and no third:
//
//   1. A mounted rider, through SteerModule -> IRiderControllable.ApplyRiderInput.
//   2. An AI module on the same AgentController, through IMovementMotor.Tick(MoveIntent), which is
//      followed over the NavMesh rather than as the crow flies.
//
// Nothing else can. The driver reads no input device of its own, and on any frame neither channel
// spoke the twist falls to zero -- so an unmounted machine with no brain stands still instead of
// coasting on whatever the last owner asked for. The rider wins on any frame it arrives, mirroring
// the guard the other motors use so the two cannot fight.
//
// It deliberately does NOT write the machine's transform. The legs decide how far it actually gets:
// LeggedLocomotion clamps the twist to what the stride can carry, so a throttle the legs cannot
// honour produces a slower machine rather than one skating ahead of its own feet. `Velocity`
// therefore reports the achieved motion, not the request.
//
// Pathfinding is done with NavMesh.CalculatePath rather than a NavMeshAgent component. An agent
// moves the transform it sits on, and LeggedLocomotion is the single owner of the pose (invariant
// I4) -- the two would fight every frame. Here the NavMesh is asked for a route and nothing else;
// the legs still carry the machine along it.
//
// This has to live in Assembly-CSharp: IRiderControllable and IMovementMotor are declared here, and
// no asmdef may reference the default assembly. That is the whole reason the locomotion is a
// separate, testable assembly and the driver is a shell around it.
//
// Runs at 50: after AgentController (0), so a MoveIntent or a rider's input lands before the twist
// for that frame is computed, and before LeggedLocomotion (100), which consumes it.
using SpaceGame.Locomotion;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(50)]
public abstract class LeggedDriver : MonoBehaviour, IRiderControllable, IMovementMotor
{
    [Header("Speeds")]
    [Tooltip("Top speed asked for at full throttle. Clamped to what the legs can carry, so raising " +
             "it past the locomotion's MaxSpeed does nothing.")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float turnSpeed = 25f;
    [Tooltip("How quickly the machine reaches commanded speed. Low values feel heavy.")]
    [SerializeField] private float acceleration = 2.5f;

    [Header("AI steering")]
    [Tooltip("How close to a MoveIntent destination counts as arrived, when no stop distance is given.")]
    [SerializeField] private float defaultStopDistance = 6f;
    [Tooltip("Heading error, in degrees, above which the machine turns on the spot instead of driving on.")]
    [SerializeField] private float turnInPlaceAngle = 50f;

    [Header("AI pathfinding")]
    [Tooltip("Seconds between route recalculations while following a NavMesh path.")]
    [SerializeField] private float repathInterval = 0.5f;
    [Tooltip("How far a destination must move before the route is rebuilt early.")]
    [SerializeField] private float repathTolerance = 2f;
    [Tooltip("How close to a path corner counts as rounded. Size this to the machine: too small " +
             "and one that cannot turn sharply grinds against the corner it is standing on.")]
    [SerializeField] private float cornerArriveRadius = 6f;
    [Tooltip("How far from the machine and from the destination to search for the NavMesh. A deck " +
             "that rides metres above the ground needs this to clear the ride height.")]
    [SerializeField] private float navMeshSampleDistance = 20f;

    /// Throttle and yaw for this frame, both -1..1. A subclass writes these from its own idle
    /// behaviour; everything else here is what fills them from a rider or an intent.
    protected float forward;
    protected float turn;

    private float speedScale = 1f;
    private float currentSpeed;
    private int riderFrame = -1;
    private int commandFrame = -1;

    private LeggedLocomotion locomotion;
    private Vector3? destination;
    private float stopDistance;

    // Pathfinding state. The buffer is reused across recalculations; `path` reports how much of it
    // is real, so a stale tail from a longer route is never steered at.
    //
    // navPath is built in Awake rather than in a field initializer: NavMeshPath's constructor calls
    // into native code, which Unity forbids during deserialisation.
    private NavMeshPath navPath;
    private readonly Vector3[] cornerBuffer = new Vector3[64];
    private readonly WalkerPath path = new WalkerPath();
    private Vector3? pathTarget;
    private float repathTimer;
    private bool hasPath;

    protected LeggedLocomotion Locomotion => locomotion;

    protected virtual void Awake()
    {
        locomotion = GetComponent<LeggedLocomotion>();
        stopDistance = defaultStopDistance;
        navPath = new NavMeshPath();
    }

    /// True on any frame a mounted rider supplied input.
    public bool IsRiderDriven => riderFrame == Time.frameCount;

    /// True while an AI route is being followed rather than a straight line to the destination.
    public bool IsFollowingPath => hasPath && path.HasPath;

    /// Drive externally: both in -1..1. For debug tools and cutscenes.
    public void SetInput(float forwardInput, float turnInput)
    {
        commandFrame = Time.frameCount;
        forward = Mathf.Clamp(forwardInput, -1f, 1f);
        turn = Mathf.Clamp(turnInput, -1f, 1f);
    }

    // ─────────── IRiderControllable ───────────
    // SteerModule calls this each frame the rider is steering. Stamp the frame so the AI channel
    // stands down, matching the rider-frame guard the other motors use.
    public void ApplyRiderInput(in RiderInput input, float deltaTime)
    {
        riderFrame = Time.frameCount;
        commandFrame = Time.frameCount;
        forward = Mathf.Clamp(input.Move.y, -1f, 1f);
        turn = Mathf.Clamp(input.Move.x, -1f, 1f);
        speedScale = 1f;
    }

    // ─────────── IMovementMotor ───────────
    // Being the machine's motor is what lets SteerModule find the rider channel: it resolves
    // riderMotor as (AgentController.Motor as IRiderControllable) and returns early, so a driver
    // that is not the motor would never be reached.
    public Vector3 Velocity => locomotion != null ? locomotion.MeasuredVelocity : Vector3.zero;
    public bool IsImmobile => false;
    public Vector3? CurrentDestination => destination;

    public bool HasReachedDestination =>
        !destination.HasValue || FlatDistanceTo(destination.Value) <= stopDistance;

    public void Tick(in MoveIntent intent, float deltaTime)
    {
        // Rider owns the frame; skip the AI channel so the two cannot fight.
        if (IsRiderDriven) return;

        commandFrame = Time.frameCount;

        switch (intent.Type)
        {
            case AgentIntentType.MoveToPosition:
                destination = intent.TargetPosition;
                stopDistance = intent.StopDistance > 0f ? intent.StopDistance : defaultStopDistance;
                // A legged machine has one heading and cannot strafe, so intent.OverrideFacing is
                // not honoured while moving -- the body faces where it is going.
                speedScale = Mathf.Clamp(
                    intent.SpeedMultiplier <= 0f ? 1f : intent.SpeedMultiplier, 0f, 1f);
                break;
            case AgentIntentType.StopAndFacePosition:
                ClearPath();
                destination = null;
                FaceTowards(intent.FacePosition);
                return;
            default:
                ClearPath();
                destination = null;
                forward = 0f;
                turn = 0f;
                return;
        }

        if (HasReachedDestination)
        {
            ClearPath();
            forward = 0f;
            turn = 0f;
            return;
        }

        SteerAlongPath(destination.Value, deltaTime);
    }

    public void ForceStop()
    {
        ClearPath();
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

    // ─────────── AI pathfinding ───────────

    /// Follow the NavMesh route to `target`, rebuilding it when it goes stale. Falls back to
    /// steering straight at the destination whenever no route can be had -- an unbaked test scene, a
    /// chunk the streamer has not finished, a destination off the mesh -- so the machine still moves
    /// rather than standing there waiting for a path that is not coming.
    private void SteerAlongPath(Vector3 target, float deltaTime)
    {
        repathTimer -= deltaTime;
        bool targetMoved = !pathTarget.HasValue ||
                           Vector3.Distance(pathTarget.Value, target) > repathTolerance;

        if (targetMoved || repathTimer <= 0f)
        {
            repathTimer = repathInterval;
            pathTarget = target;
            hasPath = TryBuildPath(target);
        }

        // Once the corners are spent the machine is within the last leg of the route; steer at the
        // destination itself, which is where the stop distance is measured from anyway.
        Vector3 steerAt = target;
        if (hasPath && path.TryGetSteerTarget(transform.position, cornerArriveRadius, out Vector3 corner))
            steerAt = corner;

        SteerTowards(steerAt);
    }

    private bool TryBuildPath(Vector3 target)
    {
        if (!TrySampleNavMesh(transform.position, out Vector3 from)) return false;
        if (!TrySampleNavMesh(target, out Vector3 to)) return false;

        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, navPath)) return false;
        // A partial path is kept: it is the best route toward a destination the mesh cannot fully
        // reach, and the repath timer will pick up the rest once the streamer bakes it.
        if (navPath.status == NavMeshPathStatus.PathInvalid) return false;

        int corners = navPath.GetCornersNonAlloc(cornerBuffer);
        if (corners < 2) return false;

        path.Set(cornerBuffer, corners);
        return true;
    }

    private bool TrySampleNavMesh(Vector3 around, out Vector3 onMesh)
    {
        if (NavMesh.SamplePosition(around, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
        {
            onMesh = hit.position;
            return true;
        }

        onMesh = around;
        return false;
    }

    private void ClearPath()
    {
        path.Clear();
        hasPath = false;
        pathTarget = null;
        repathTimer = 0f;
    }

    private void FaceTowards(Vector3 worldPoint)
    {
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-4f) { turn = 0f; return; }
        turn = WalkerSteering.Turn(HeadingErrorTo(to));
        forward = 0f;
    }

    private void SteerTowards(Vector3 worldPoint)
    {
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-4f) { forward = 0f; turn = 0f; return; }

        float error = HeadingErrorTo(to);
        turn = WalkerSteering.Turn(error);
        forward = WalkerSteering.Throttle(error, turnInPlaceAngle, speedScale);
    }

    private float HeadingErrorTo(Vector3 flatDirection) =>
        Vector3.SignedAngle(transform.forward, flatDirection.normalized, Vector3.up);

    // ─────────── the frame ───────────

    /// What to do on a frame nobody claimed the machine. Stopping is the default and the right
    /// answer: repeating the last order forever is how an unmounted machine wanders off.
    protected virtual void Idle()
    {
        forward = 0f;
        turn = 0f;
    }

    private void Update()
    {
        // Nobody claimed the machine this frame -- no rider steering it, no AgentController ticking
        // a MoveIntent into it.
        if (commandFrame != Time.frameCount) Idle();

        float dt = Time.deltaTime;
        currentSpeed = Mathf.Lerp(currentSpeed, forward * moveSpeed, 1f - Mathf.Exp(-acceleration * dt));

        // Hand over the request and let the legs answer it. Every write to the machine's transform
        // lives in the locomotion so there is exactly one owner of the pose.
        if (locomotion != null) locomotion.SetTwist(currentSpeed, turn * turnSpeed);
    }

    protected virtual void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        acceleration = Mathf.Max(0.01f, acceleration);
        defaultStopDistance = Mathf.Max(0.1f, defaultStopDistance);
        turnInPlaceAngle = Mathf.Clamp(turnInPlaceAngle, 1f, 179f);
        repathInterval = Mathf.Max(0.05f, repathInterval);
        repathTolerance = Mathf.Max(0.1f, repathTolerance);
        cornerArriveRadius = Mathf.Max(0.1f, cornerArriveRadius);
        navMeshSampleDistance = Mathf.Max(0.5f, navMeshSampleDistance);
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !IsFollowingPath) return;

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Gizmos.DrawLine(transform.position, path.CurrentCorner);
        Gizmos.DrawWireSphere(path.CurrentCorner, cornerArriveRadius);

        if (destination.HasValue)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(destination.Value, stopDistance);
        }
    }
}
