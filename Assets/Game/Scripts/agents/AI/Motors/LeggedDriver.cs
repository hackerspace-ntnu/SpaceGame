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
using SpaceGame.World;

namespace SpaceGame.Agents
{
    [DefaultExecutionOrder(50)]
    public abstract class LeggedDriver : MonoBehaviour, IRiderControllable, IMovementMotor, ITowable
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

        [Header("Steep ground")]
        [Tooltip("Seconds to commit to a way around ground the legs refused to climb.\n\nA machine " +
                 "that re-decides every frame at the edge of a slope oscillates against it instead " +
                 "of leaving; the detour is dropped early anyway, the moment the direct course is " +
                 "passable again.")]
        [SerializeField] private float detourHoldTime = 2f;

        [Header("Lateral travel")]
        [Tooltip("This machine may travel across its own nose.\n\nOFF is what every machine built " +
                 "before this did and what most of them still want: a bird and a walking station point " +
                 "at what they are walking toward, so the whole steering problem is one heading and a " +
                 "throttle.\n\nON turns the drive into a planar one. The AI translates straight at its " +
                 "destination whatever way the body is facing, and turns only to keep the target ABEAM " +
                 "— which for a crab is the direction it covers ground fastest in. A rider's stick " +
                 "becomes a two-axis translation: left and right STRAFE rather than turn, and the " +
                 "heading moves to SteerModule's separate turn action (Turn Action Name). Leave that " +
                 "action unbound and the machine strafes but cannot be turned by its rider.")]
        [SerializeField] private bool lateralSteering;

        /// Throttle and yaw for this frame, both -1..1. A subclass writes these from its own idle
        /// behaviour; everything else here is what fills them from a rider or an intent.
        protected float forward;
        protected float turn;

        /// Sideways throttle, -1..1, positive to the machine's right. Stays 0 on every machine that
        /// left `lateralSteering` off, and the twist is then handed over through the scalar overload —
        /// so a machine that cannot strafe takes literally the same path it always did.
        protected float strafe;

        private float speedScale = 1f;
        private float currentSpeed;
        private float currentStrafe;
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

        // The way around steep ground, once one has been found, and how long it is committed to.
        private Vector3 detourDirection;
        private float detourHold;

        /// Headings tried when the legs refuse the course, in the order they are tried: nearest the
        /// goal first, so the machine gives up as little ground as the hill allows. Both signs at
        /// each angle, since which side a hill opens on is not something the machine can know.
        private static readonly float[] DetourAngles = { 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f };

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

        /// True while this machine is being steered as a planar drive rather than a heading.
        public bool CanStrafe => lateralSteering;

        /// Drive externally: both in -1..1. For debug tools and cutscenes.
        public void SetInput(float forwardInput, float turnInput)
            => SetInput(forwardInput, turnInput, 0f);

        /// The same, with a sideways channel. Ignored by a machine that is not a lateral traveller,
        /// rather than quietly steering one that cannot honour it.
        public void SetInput(float forwardInput, float turnInput, float strafeInput)
        {
            commandFrame = Time.frameCount;
            forward = Mathf.Clamp(forwardInput, -1f, 1f);
            turn = Mathf.Clamp(turnInput, -1f, 1f);
            strafe = lateralSteering ? Mathf.Clamp(strafeInput, -1f, 1f) : 0f;
        }

        // ─────────── IRiderControllable ───────────
        // SteerModule calls this each frame the rider is steering. Stamp the frame so the AI channel
        // stands down, matching the rider-frame guard the other motors use.
        public void ApplyRiderInput(in RiderInput input, float deltaTime)
        {
            riderFrame = Time.frameCount;
            commandFrame = Time.frameCount;
            forward = Mathf.Clamp(input.Move.y, -1f, 1f);

            // On a lateral traveller the stick's X means STRAFE, so the heading comes off RiderInput's
            // dedicated Turn axis instead. That axis is 0 unless SteerModule has a turn action bound,
            // which is why a crab with nothing bound still strafes exactly as it did before.
            if (lateralSteering)
            {
                strafe = Mathf.Clamp(input.Move.x, -1f, 1f);
                turn = Mathf.Clamp(input.Turn, -1f, 1f);
            }
            else
            {
                turn = Mathf.Clamp(input.Move.x, -1f, 1f);
                strafe = 0f;
            }

            speedScale = 1f;
        }

        // ─────────── IMovementMotor ───────────
        // Being the machine's motor is what lets SteerModule find the rider channel: it resolves
        // riderMotor as (AgentController.Motor as IRiderControllable) and returns early, so a driver
        // that is not the motor would never be reached.
        public Vector3 Velocity => locomotion != null ? locomotion.MeasuredVelocity : Vector3.zero;

        /// <summary>See <see cref="IMovementMotor.TopSpeed"/>. The locomotion owns the figure —
        /// the driver's own speed knob is clamped by it.</summary>
        public float TopSpeed => locomotion != null ? locomotion.MaxSpeed : 0f;
        public bool IsImmobile => false;
        public Vector3? CurrentDestination => destination;

        // ─────────── ITowable ───────────
        //
        // A legged machine has to be ASKED, exactly as a mounted rider does, and for the same
        // reason at one remove: LeggedLocomotion is the single author of the body transform
        // (Invariant I4), so the rope's MovePosition on the Rigidbody is overwritten from pathPos
        // on the next LateUpdate. A towed ostrich read as completely static — no resistance, no
        // drift, nothing in the console.
        //
        // This lives on the driver rather than on the locomotion because ITowable is in the default
        // assembly and no asmdef may reference it — the same constraint that put the driver out
        // here in the first place.

        /// <summary>The body itself: where a rope tied to a walking animal pulls from.</summary>
        public Vector3 TowAttachPoint => locomotion != null && locomotion.IsReady
            ? locomotion.BodyPosition
            : transform.position;

        /// <summary>
        /// A rope wants this machine at <paramref name="anchor"/> this step.
        ///
        /// <para>
        /// Capped at the machine's own top speed, so a rope can drag an animal about as fast as it
        /// could walk and no faster. Without the cap the rope becomes a winch: the correction is a
        /// position, and a position written every physics step is a teleport with extra steps.
        /// </para>
        /// </summary>
        public bool RequestTow(Vector3 anchor)
        {
            // Nothing to drag yet, or somebody else's copy to drag: a peer towing a machine whose
            // pose arrives over the wire would be a second authority on it.
            if (locomotion == null || !locomotion.IsReady || locomotion.ExternallyPosed) return false;

            Vector3 delta = anchor - TowAttachPoint;

            float ceiling = Mathf.Max(0f, TopSpeed) * Time.fixedDeltaTime;
            if (ceiling <= 0f) return false;

            if (delta.magnitude > ceiling) delta = delta.normalized * ceiling;

            locomotion.Drag(delta);
            return true;
        }

        public bool HasReachedDestination =>
            !destination.HasValue || FlatDistanceTo(destination.Value) <= EffectiveStopDistance;

        /// <summary>
        /// The stop distance to actually steer by, never zero.
        ///
        /// <c>stopDistance</c> has no field initialiser and is first assigned in <c>Awake</c>, so
        /// anything that reads it before then sees 0 — and a machine that considers itself arrived
        /// only at range zero never arrives. The guarantee belongs here, at the point of use, rather
        /// than in <see cref="RestoreDrive"/> where it used to live: a restore that rewrites the
        /// value it is handed cannot round-trip, because the next capture reads back something the
        /// record never said. That asymmetry is what made the Ostrich fail its save/load test.
        /// </summary>
        private float EffectiveStopDistance => stopDistance > 0f ? stopDistance : defaultStopDistance;

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
                    // A machine with one heading cannot strafe, so intent.OverrideFacing is not honoured
                    // while moving -- the body faces where it is going. A lateral traveller keeps its
                    // heading independently of its course, which is what SteerTowards does below.
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
                    strafe = 0f;
                    return;
            }

            if (HasReachedDestination)
            {
                ClearPath();
                forward = 0f;
                turn = 0f;
                strafe = 0f;
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
            strafe = 0f;
            currentSpeed = 0f;
            currentStrafe = 0f;
            // Cancel the standing order immediately rather than waiting for the next Update, so a stop
            // requested mid-frame cannot leak one more frame of travel into the legs.
            if (locomotion != null) locomotion.SetTwist(0f, 0f);
        }

        public void NudgeDestination(Vector3 offset)
        {
            if (destination.HasValue) destination = destination.Value + offset;
        }

        public void SuggestDestination(Vector3 position) => destination = position;

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // What is worth a record here is the standing ORDER and the machine's momentum, and nothing
        // else on the route.
        //
        // The route itself — `navPath`, `path`, `pathTarget`, `hasPath`, `repathTimer` — is
        // deliberately left out. It is a derived thing: `repathTimer` starts at zero, so the first
        // Tick after a load rebuilds the whole route from the destination before the machine takes a
        // step, and a NavMeshPath cannot be serialized anyway. Worse, a stale corner list is
        // actively harmful: the terrain it was calculated over may not have streamed in, and a
        // machine steering at a corner from a route it can no longer walk goes somewhere nobody
        // asked it to.
        //
        // The DETOUR (`detourDirection`, `detourHold`) goes too, and for a reason worth stating: it
        // is a two-second commitment to going around a hill the legs refused, and it is dropped the
        // moment the direct course is passable again. Restoring it costs two floats and saves a
        // machine that saved mid-detour from walking straight back into the slope it had already
        // decided to go around.
        //
        // `currentSpeed` and `currentStrafe` are the smoothed throttle, and they are here rather
        // than in the "re-converges, skip it" pile because on these machines the smoothing feeds the
        // GAIT: the twist sets the pace, the pace sets the stride, and a machine restored at zero
        // throttle beside a restored mid-stride gait has its feet and its body disagreeing for the
        // half second it takes to spin back up. Two floats to keep a walk looking like a walk.
        public Vector3? Destination => destination;
        public float StopDistance => stopDistance;
        public float CurrentSpeed => currentSpeed;
        public float CurrentStrafe => currentStrafe;
        public Vector3 DetourDirection => detourDirection;
        public float DetourHold => detourHold;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Assigns verbatim. A restore that "helpfully" substitutes a default for a value it does
        /// not like breaks the one property the save system needs from every saver — that capturing
        /// a restored object gives back what was stored — and it breaks it silently, because both
        /// halves look individually reasonable. <see cref="EffectiveStopDistance"/> is where the
        /// never-zero guarantee lives now.
        /// </summary>
        public void RestoreDrive(Vector3? restoredDestination, float stop, float speed, float sideways)
        {
            destination = restoredDestination;
            stopDistance = stop;
            currentSpeed = speed;

            // Still filtered by the rig's own capability: a machine that cannot strafe must not be
            // handed a sideways throttle by a record written for one that can. Capture reads the
            // same filtered field back, so this stays symmetric.
            currentStrafe = lateralSteering ? sideways : 0f;
        }

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreDetour(Vector3 direction, float hold)
        {
            detourDirection = direction;
            detourHold = Mathf.Max(0f, hold);
        }

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

            SteerTowards(ApplyClimbDetour(steerAt, deltaTime));
        }

        /// Go another way when the legs will not climb what is in front of them.
        ///
        /// The locomotion refuses the climb on its own and refuses it for everybody -- a rider gets
        /// nothing but a machine that declines, which is the honest answer, since a rider already
        /// holds full turn authority and anything cleverer would be fighting them for the stick.
        /// This is the AI's half: having been refused, pick a heading that is not refused.
        ///
        /// Two steps, in this order, because they answer different questions. A repath asks whether
        /// the ROUTE was wrong; usually it was not, and the machine was merely cutting the corner
        /// between two waypoints across a spur the NavMesh went around. Only once a fresh route is
        /// on its way is it worth asking the second question, which is which way the hill opens.
        private Vector3 ApplyClimbDetour(Vector3 steerAt, float deltaTime)
        {
            if (locomotion == null || !locomotion.IsReady) return steerAt;

            Vector3 direct = steerAt - transform.position;
            direct.y = 0f;
            if (direct.sqrMagnitude < 1e-4f) { detourHold = 0f; return steerAt; }
            direct.Normalize();

            if (detourHold > 0f)
            {
                detourHold -= deltaTime;
                // Dropped the moment the goal is reachable again, rather than run to its timeout.
                // The hold is there to stop the machine dithering on the boundary, not to make it
                // walk a fixed distance away from a hill it has already cleared.
                if (detourHold > 0f && !locomotion.CanTravel(direct))
                    return transform.position + detourDirection * cornerArriveRadius;

                detourHold = 0f;
            }

            if (!locomotion.ClimbBlocked) return steerAt;

            // The route may well be fine. Ask for a fresh one before committing to going around.
            repathTimer = 0f;

            foreach (float angle in DetourAngles)
            {
                Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * direct;
                if (!locomotion.CanTravel(candidate)) continue;

                detourDirection = candidate;
                detourHold = detourHoldTime;
                return transform.position + candidate * cornerArriveRadius;
            }

            // Hemmed in on every heading tried. Keep steering at the goal rather than inventing a
            // direction: the machine stands, which is what it should do in a hole, and the next
            // repath is already scheduled.
            return steerAt;
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
            // A detour belongs to the route it was taken from. Carrying one across a new order is
            // how a machine sets off at right angles to a destination it was never blocked from.
            detourHold = 0f;
        }

        private void FaceTowards(Vector3 worldPoint)
        {
            Vector3 to = worldPoint - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) { turn = 0f; return; }
            turn = WalkerSteering.Turn(HeadingErrorTo(to));
            forward = 0f;
            strafe = 0f;
        }

        private void SteerTowards(Vector3 worldPoint)
        {
            Vector3 to = worldPoint - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) { forward = 0f; turn = 0f; strafe = 0f; return; }

            if (lateralSteering) { SteerLaterally(to.normalized); return; }

            float error = HeadingErrorTo(to);
            turn = WalkerSteering.Turn(error);
            forward = WalkerSteering.Throttle(error, turnInPlaceAngle, speedScale);
        }

        /// Steering for a machine that travels across its own nose.
        ///
        /// The course is resolved straight into the body frame, so the machine sets off toward its
        /// destination on frame one whatever way it happens to be pointing -- there is no turn-in-place
        /// phase, because there is nothing to turn in place FOR.
        ///
        /// The heading is then a separate, slower question, and the answer is to keep the destination
        /// ABEAM. A crab's legs sweep their yaw arcs along its X, so that is the axis it covers ground
        /// fastest on; putting the course on it is the difference between scuttling and shuffling.
        /// Either beam will do -- the nearest one wins, so the machine never spins 180 degrees to
        /// present the other side.
        private void SteerLaterally(Vector3 course)
        {
            Vector3 local = transform.InverseTransformDirection(course);
            forward = Mathf.Clamp(local.z, -1f, 1f) * speedScale;
            strafe = Mathf.Clamp(local.x, -1f, 1f) * speedScale;

            float abeam = Vector3.SignedAngle(transform.right, course, Vector3.up);
            if (abeam > 90f) abeam -= 180f;
            else if (abeam < -90f) abeam += 180f;
            turn = WalkerSteering.Turn(abeam);
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
            strafe = 0f;
        }

        private void Update()
        {
            // Nobody claimed the machine this frame -- no rider steering it, no AgentController ticking
            // a MoveIntent into it.
            if (commandFrame != Time.frameCount) Idle();

            float dt = Time.deltaTime;
            float k = 1f - Mathf.Exp(-acceleration * dt);
            currentSpeed = Mathf.Lerp(currentSpeed, forward * moveSpeed, k);
            currentStrafe = Mathf.Lerp(currentStrafe, strafe * moveSpeed, k);

            // Hand over the request and let the legs answer it. Every write to the machine's transform
            // lives in the locomotion so there is exactly one owner of the pose.
            //
            // The scalar overload for a machine that does not strafe, deliberately: it is the call the
            // shipping machines have always made, so nothing about their path changes here. (It forwards
            // to the planar one with a zero X, so the two agree — this is belt and braces on a file
            // three machines now share.)
            if (locomotion == null) return;
            if (lateralSteering) locomotion.SetTwist(new Vector2(currentStrafe, currentSpeed), turn * turnSpeed);
            else locomotion.SetTwist(currentSpeed, turn * turnSpeed);
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
            detourHoldTime = Mathf.Max(0f, detourHoldTime);
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            // Drawn before the path test, and in its own colour: the detour is most worth seeing on
            // exactly the frames there is no route to draw, when the machine has been refused a
            // climb and is picking its way around by itself.
            if (detourHold > 0f)
            {
                Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.9f);
                Gizmos.DrawLine(transform.position,
                                transform.position + detourDirection * cornerArriveRadius);
            }

            if (!IsFollowingPath) return;

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.DrawLine(transform.position, path.CurrentCorner);
            Gizmos.DrawWireSphere(path.CurrentCorner, cornerArriveRadius);

            if (destination.HasValue)
            {
                Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
                Gizmos.DrawWireSphere(destination.Value, EffectiveStopDistance);
            }
        }
    }
}
