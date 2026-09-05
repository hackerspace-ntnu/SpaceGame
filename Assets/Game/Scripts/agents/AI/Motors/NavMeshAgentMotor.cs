// NavMesh-backed implementation of IMovementMotor used by NPCs and mounts.
// Applies MoveIntent navigation/facing commands to Unity's NavMeshAgent.
// Includes optional mounted-jump simulation via baseOffset animation.
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.World;
using SpaceGame.Teleporting;

namespace SpaceGame.Agents
{
    /// <summary>
    /// A movement motor that drives a standard Unity NavMeshAgent.
    /// This component translates high-level MoveIntents (from an AI brain or controller)
    /// into NavMeshAgent commands (SetDestination, isStopped, etc.).
    ///
    /// Key features:
    /// - Handles pathfinding movement to target positions.
    /// - Supports "Stop and Face" behavior for precise rotation.
    /// - Includes a "Stuck Recovery" mechanism to reset paths if the agent gets wedged.
    /// - Implements IMountJumpMotor to simulate jumping by animating the agent's baseOffset.
    /// </summary>
    // Run before default (0) so agent.enabled=false happens before NavMeshAgent's own Awake registers it.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(NavMeshAgent))]
    public class NavMeshAgentMotor : MonoBehaviour, IMovementMotor, IMountJumpMotor, IMountLeapMotor,
                                     IRiderControllable, ISelfDrivingMotor, ITeleportAware
    {
        [Header("Navigation")]
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private float navMeshSnapDistance = 6f;

        // How often a parked agent re-checks for a NavMesh underneath it. One NavMesh query per
        // parked agent per frame is pure waste while the world mesh is still streaming in.
        private const float ReattachInterval = 0.5f;

        [Header("Speeds")]
        [Tooltip("Fraction of the NavMeshAgent speed used when walking (not running). 1 = same as run speed.")]
        [SerializeField] [Range(0.01f, 1f)] private float walkSpeedMultiplier = 0.65f;

        [Header("Stuck Recovery")]
        [SerializeField] private float stuckVelocityThreshold = 0.05f;
        [SerializeField] private float stuckTime = 1.5f;

        [Header("Facing")]
        [SerializeField] private float faceRotateSpeed = 8f;

        [Header("Mounted Jump")]
        [SerializeField] private bool enableMountedJump = true;
        [SerializeField] private float mountedJumpHeight = 1.25f;
        [SerializeField] private float mountedJumpDuration = 0.55f;
        [SerializeField] private float mountedJumpCooldown = 0.45f;

        [Header("Mounted Leap")]
        [SerializeField] private bool enableMountedLeap = true;
        [SerializeField] private float mountedLeapCooldown = 0.6f;
        [SerializeField] private float mountedLeapSampleRadius = 6f;

        [Header("Rider Steering")]
        [Tooltip("Tank-steer yaw rate in degrees/sec while the rider is driving.")]
        [SerializeField] private float riderTurnSpeed = 120f;
        [Tooltip("How far ahead of self the NavMesh destination is placed while rider drives.")]
        [SerializeField] private float riderForwardTargetDistance = 2f;
        [SerializeField] private float riderStopDistance = 0.15f;
        [SerializeField] private float riderNavMeshSampleDistance = 4f;

        private float stuckTimer;
        private float reattachTimer;
        private bool selfDriveSuspended;
        private bool suspendedAgentWasEnabled;
        private bool defaultUpdateRotation;
        private bool defaultUpdatePosition;
        private float defaultStoppingDistance;
        private float defaultSpeed;
        private float defaultBaseOffset;
        private float jumpElapsed = -1f;
        private float jumpCooldownTimer;

        // baseOffset has more than one thing to say now, so nobody writes it directly any more.
        //
        // The jump arc was its only author until ground conforming arrived, and two components
        // assigning the same field do not add up -- whichever ran later in the frame silently
        // erased the other. So each contribution is kept separately and summed in one place.
        //
        // groundOffset is the NavMesh-to-real-ground correction, pushed in by AgentGroundConform.
        // jumpArc is the mounted jump. defaultBaseOffset is whatever the prefab was authored with.
        private float groundOffset;
        private float jumpArc;

        /// <summary>
        /// Vertical correction between the NavMesh polygon this agent stands on and the ground
        /// underneath it. Written by <c>AgentGroundConform</c>; 0 when nothing is conforming.
        /// </summary>
        public float GroundOffset
        {
            get => groundOffset;
            set
            {
                groundOffset = value;
                ApplyBaseOffset();
            }
        }

        /// <summary>
        /// World Y of the NavMesh polygon under this agent, with every offset stripped back off.
        ///
        /// <para>
        /// This is the number a ground conform has to correct against, and it cannot be recovered
        /// from <c>transform.position</c> alone: that already carries the prefab's own base offset
        /// and, mid-jump, the arc as well, so subtracting only the ground term would leave the
        /// conform fighting the jump. The fallback covers an agent that has been parked off the
        /// mesh, where the transform is the only position there is.
        /// </para>
        /// </summary>
        public float NavSurfaceY => Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh
            ? Agent.nextPosition.y - Agent.baseOffset
            : transform.position.y - groundOffset;

        private void ApplyBaseOffset()
        {
            if (Agent) Agent.baseOffset = defaultBaseOffset + groundOffset + jumpArc;
        }

        // Set to Time.frameCount inside ApplyRiderInput so the MoveIntent switch in Tick skips
        // that frame. Arc/cooldown updates still run.
        private int riderDriveFrame = -1;
        // True between a rider's last ApplyRiderInput and our restoration of agent defaults.
        // Without this, post-dismount the agent slides crab-wise (updateRotation stuck false) and
        // races toward the leftover rider-forward destination — looks like "speeds up abnormally".
        private bool riderStateDirty;

        private bool isLeaping;
        private float leapElapsed;
        private float leapDuration;
        private float leapVertical;
        private float leapCooldownTimer;
        private Vector3 leapStart;
        private Vector3 leapEnd;

        public Vector3 Velocity => IsAgentReady ? agent.velocity : Vector3.zero;

        /// <summary>See <see cref="IMovementMotor.TopSpeed"/>. The fallback is load-bearing:
        /// <c>defaultSpeed</c> is assigned in Awake, which does not run in EditMode.</summary>
        public float TopSpeed => defaultSpeed > 0.01f ? defaultSpeed
                               : agent != null ? agent.speed : 0f;

        public bool IsImmobile => !agent || !agent.isOnNavMesh || agent.isStopped;

        public Vector3? CurrentDestination
        {
            get
            {
                if (!IsAgentReady || agent.isStopped || !agent.hasPath)
                    return null;
                return agent.destination;
            }
        }

        public bool HasReachedDestination
        {
            get
            {
                if (!IsAgentReady || agent.pathPending)
                {
                    return false;
                }

                return agent.remainingDistance <= agent.stoppingDistance + 0.1f;
            }
        }

        private bool IsAgentReady => agent && agent.isActiveAndEnabled && agent.isOnNavMesh;

        /// <summary>
        /// The agent, resolved on first use rather than only in Awake.
        ///
        /// <para>
        /// The serialized reference is not assigned on every prefab — the Nomad's is
        /// <c>fileID: 0</c> — and Awake has always covered for that at runtime. The ground-conform
        /// members below are the first on this component reachable BEFORE Awake, from an EditMode
        /// test, and a null agent there does not throw: <c>ApplyBaseOffset</c> would simply do
        /// nothing while <c>GroundOffset</c> kept accumulating, so the correction runs away to its
        /// clamp with a clean console.
        /// </para>
        /// </summary>
        private NavMeshAgent Agent => agent ? agent : agent = GetComponent<NavMeshAgent>();

        private void Awake()
        {
            if (!agent)
            {
                agent = GetComponent<NavMeshAgent>();
            }

            defaultUpdateRotation = agent.updateRotation;
            defaultUpdatePosition = agent.updatePosition;
            defaultStoppingDistance = agent.stoppingDistance;
            defaultSpeed = agent.speed;
            defaultBaseOffset = agent.baseOffset;
            agent.autoBraking = false;

            // Only disable if the NavMesh isn't ready here yet — WorldStreamer will re-enable us
            // after the chunk is baked. If there's already a NavMesh covering our spawn position
            // (pre-baked scene, test scene), stay enabled so the agent works immediately.
            if (!NavMesh.SamplePosition(transform.position, out _, navMeshSnapDistance, NavMesh.AllAreas))
                agent.enabled = false;
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.Leap, OnLeapRequested);

            // A restore has already described a jump or a leap in flight. Consumed, so a later
            // genuine enable clears them as it always did.
            if (motorRestored)
            {
                motorRestored = false;
                return;
            }

            stuckTimer = 0f;
            reattachTimer = 0f;
            jumpCooldownTimer = 0f;
            jumpElapsed = -1f;
            leapCooldownTimer = 0f;
            isLeaping = false;
        }

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // A leap is the case that matters, and it matters because of what it turns OFF. RequestLeap
        // sets `agent.updatePosition = false` and `updateRotation = false` and drives the transform
        // by hand; UpdateMountedLeap is the only thing that ever puts them back, and it only does so
        // on the frame the arc completes. So a leap has to come back as a leap — resumed and allowed
        // to land — rather than being abandoned halfway with the agent's own flags left where the
        // takeoff put them.
        //
        // The cooldowns come too, for the same free-action reason as the weapon cadences: a mount
        // whose leap cooldown reloads at zero can leap on demand as often as the rider is willing to
        // reload.
        //
        // `stuckTimer` and `reattachTimer` are deliberately left out. Both are re-derived within a
        // fraction of a second of the first tick from things that are true right now — whether the
        // agent is moving, whether there is a NavMesh underneath — so storing them buys nothing and
        // a stale stuck timer would trigger a spurious path reset on the first frame after a load.
        // `selfDriveSuspended` is left out too: it is an authority state, asserted by
        // AgentController against THIS session's ownership, and restoring one from a session with a
        // different host is how an agent ends up parked with its NavMeshAgent switched off.
        private bool motorRestored;

        public float JumpElapsed => jumpElapsed;
        public float JumpCooldownTimer => jumpCooldownTimer;
        public float LeapCooldownTimer => leapCooldownTimer;
        public float LeapElapsed => leapElapsed;
        public float LeapDuration => leapDuration;
        public float LeapVertical => leapVertical;
        public Vector3 LeapStart => leapStart;
        public Vector3 LeapEnd => leapEnd;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreCooldowns(float jumpElapsedSeconds, float jumpCooldown, float leapCooldown)
        {
            motorRestored = true;
            jumpElapsed = jumpElapsedSeconds;
            jumpCooldownTimer = jumpCooldown;
            leapCooldownTimer = leapCooldown;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Re-asserts the agent flags the takeoff turned off, not just the arithmetic: a leap
        /// restored as numbers alone would move the transform while an enabled NavMeshAgent wrote
        /// over it every frame.
        /// </summary>
        public void RestoreLeap(Vector3 start, Vector3 end, float vertical, float duration, float elapsed)
        {
            motorRestored = true;

            leapStart = start;
            leapEnd = end;
            leapVertical = Mathf.Max(0f, vertical);
            leapDuration = Mathf.Max(0.05f, duration);
            leapElapsed = Mathf.Max(0f, elapsed);
            isLeaping = true;

            if (!agent) return;

            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }

            agent.updatePosition = false;
            agent.updateRotation = false;
        }

        public void Tick(in MoveIntent intent, float deltaTime)
        {
            UpdateMountedJump(deltaTime);
            UpdateMountedLeap(deltaTime);

            if (!agent)
            {
                return;
            }

            // Awake parks this agent when it wakes before a NavMesh exists beneath it, on the
            // promise that something will switch it back on. WorldStreamer.SnapAgentsToNavMesh
            // keeps that promise only for agents that live in a *chunk* scene — it walks the roots
            // of the chunk it just loaded and nothing else. An agent placed in the persistent
            // scene (or an interior, or a test scene) wakes before the world mesh is up, gets
            // parked, is never visited, and stays motionless for the rest of the session with
            // nothing logged. Recover here so the promise no longer depends on which scene the
            // agent happens to live in.
            //
            // Safe against corpses: HealthReactionModule disables the AgentController on death,
            // so a dead agent stops calling Tick long before this runs.
            if (!agent.isActiveAndEnabled)
            {
                TryReattachToNavMesh(deltaTime);
                return;
            }

            // During a leap we drive the transform directly; ignore new movement intents.
            if (isLeaping)
            {
                return;
            }

            if (!agent.isOnNavMesh)
            {
                TrySnapToNavMesh(deltaTime);
                return;
            }

            NoteNavMeshFound();

            // Rider is driving this frame via ApplyRiderInput — don't re-interpret a MoveIntent.
            if (riderDriveFrame == Time.frameCount)
                return;

            // Rider just released. Restore agent defaults the rider's ApplyRiderInput mutated
            // (updateRotation, stoppingDistance, speed) and clear any leftover rider destination
            // so the AI starts from a clean slate.
            if (riderStateDirty)
            {
                riderStateDirty = false;
                agent.updateRotation = defaultUpdateRotation;
                agent.stoppingDistance = defaultStoppingDistance;
                agent.speed = defaultSpeed;
                if (agent.hasPath)
                    agent.ResetPath();
                agent.velocity = Vector3.zero;
            }

            switch (intent.Type)
            {
                case AgentIntentType.MoveToPosition:
                    ApplyMoveIntent(intent, deltaTime);
                    HandleStuckRecovery(deltaTime);
                    break;

                case AgentIntentType.StopAndFacePosition:
                    StopAgentPath();
                    agent.updateRotation = false;
                    FacePosition(intent.FacePosition, deltaTime);
                    break;

                default:
                    StopAgentPath();
                    if (intent.OverrideFacing)
                    {
                        agent.updateRotation = false;
                        FacePosition(intent.FacePosition, deltaTime);
                    }
                    break;
            }
        }

        public void ForceStop()
        {
            StopAgentPath();
            if (IsAgentReady)
            {
                // Zero residual internal velocity so the agent doesn't drift
                // (NavMeshAgent otherwise decelerates from its current velocity, which with
                // autoBraking=false can take a while and can look like slow circling).
                agent.velocity = Vector3.zero;
            }
        }

        // ─────────── ISelfDrivingMotor ───────────
        //
        // ForceStop is not enough here, and the difference is the whole reason this interface
        // exists. A stopped NavMeshAgent is still an ENABLED NavMeshAgent, and an enabled one
        // writes transform.position from its own internal position every frame: the replicated
        // pose lands, the agent overwrites it on the next frame with where it thinks it is, and
        // the remote copy stops following the server entirely. Switching the component off is what
        // hands the transform back to the NetworkTransform.

        public void SuspendSelfDrive()
        {
            if (selfDriveSuspended || agent == null)
                return;

            selfDriveSuspended = true;

            // Recorded rather than assumed, because "enabled" is not this agent's resting state:
            // Awake parks it when it wakes before a NavMesh exists beneath it, and resuming would
            // otherwise switch on an agent that was never on and drop it through the world.
            suspendedAgentWasEnabled = agent.enabled;

            if (!agent.enabled)
                return;

            // Clear the path first. An agent re-enabled later would otherwise resume walking to a
            // destination chosen an ownership change ago, on a machine that has since been told
            // where the body actually is.
            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                agent.ResetPath();

            agent.enabled = false;
        }

        public void ResumeSelfDrive()
        {
            if (!selfDriveSuspended)
                return;

            selfDriveSuspended = false;

            if (agent != null && suspendedAgentWasEnabled)
                agent.enabled = true;
        }

        public void NudgeDestination(Vector3 offset)
        {
            if (!IsAgentReady || agent.isStopped || !agent.hasPath)
                return;

            Vector3 nudged = agent.destination + offset;
            if (NavMesh.SamplePosition(nudged, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
        }

        public void SuggestDestination(Vector3 position)
        {
            if (!IsAgentReady)
                return;

            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                agent.isStopped = false;
                agent.SetDestination(hit.position);
            }
        }

        public void ApplyRiderInput(in RiderInput input, float deltaTime)
        {
            riderDriveFrame = Time.frameCount;
            riderStateDirty = true;
            if (!IsAgentReady || isLeaping)
                return;

            // Tank steer: rotate transform directly. Disable agent's own rotation so it doesn't
            // fight us on the next path update.
            float yaw = input.Move.x * riderTurnSpeed * deltaTime;
            if (Mathf.Abs(yaw) > 1e-4f)
                transform.Rotate(0f, yaw, 0f, Space.World);
            agent.updateRotation = false;

            float throttle = input.Move.y;
            agent.stoppingDistance = Mathf.Max(0.01f, riderStopDistance);
            float baseMultiplier = input.IsRunning ? 1f : walkSpeedMultiplier;
            agent.speed = defaultSpeed * baseMultiplier * Mathf.Max(0.01f, Mathf.Abs(throttle));

            if (Mathf.Abs(throttle) <= 0.01f)
            {
                StopAgentPath();
                return;
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 desired = transform.position + forward * (riderForwardTargetDistance * Mathf.Sign(throttle));
            Vector3 target = desired;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, riderNavMeshSampleDistance, NavMesh.AllAreas))
                target = hit.position;

            agent.isStopped = false;
            agent.SetDestination(target);
        }

        public void RequestJump()
        {
            if (!enableMountedJump || !agent)
            {
                return;
            }

            if (jumpElapsed >= 0f || jumpCooldownTimer > 0f)
            {
                return;
            }

            jumpElapsed = 0f;
            jumpCooldownTimer = mountedJumpCooldown;
        }

        public bool IsLeapAvailable => enableMountedLeap && !isLeaping && leapCooldownTimer <= 0f;
        public bool IsLeaping => isLeaping;

        public void RequestLeap(Vector3 direction, float horizontalDistance, float verticalHeight, float duration)
        {
            if (!IsLeapAvailable || !agent)
            {
                return;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f)
            {
                direction = transform.forward;
                direction.y = 0f;
            }
            direction.Normalize();

            Vector3 desired = transform.position + direction * Mathf.Max(0.1f, horizontalDistance);
            Vector3 endPoint = desired;
            if (agent.isOnNavMesh && NavMesh.SamplePosition(desired, out NavMeshHit hit, mountedLeapSampleRadius, NavMesh.AllAreas))
            {
                endPoint = hit.position;
            }

            leapStart = transform.position;
            leapEnd = endPoint;
            leapVertical = Mathf.Max(0f, verticalHeight);
            leapDuration = Mathf.Max(0.05f, duration);
            leapElapsed = 0f;
            isLeaping = true;
            leapCooldownTimer = leapDuration + mountedLeapCooldown;

            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }
            agent.updatePosition = false;
            agent.updateRotation = false;
        }

        /// <summary>
        /// Bring a leap in flight through a teleport.
        ///
        /// A leap runs with <c>agent.updatePosition</c> switched off and the body driven along a
        /// lerp between two WORLD points, so it is the agent's own navigation that is suspended and
        /// this arc that is authoritative. Leave the endpoints in the room the creature left and the
        /// next frame drags it straight back at them, through the wall, at whatever speed the two
        /// apertures are apart divided by what remains of the leap.
        ///
        /// The agent's own position is not touched here — SaveTeleport has already warped it, and
        /// re-warping mid-leap is what <c>updatePosition = false</c> exists to prevent.
        /// </summary>
        public void OnTeleported(in TeleportMove move)
        {
            leapStart = move.Point(leapStart);
            leapEnd = move.Point(leapEnd);
        }

        private void ApplyMoveIntent(in MoveIntent intent, float deltaTime)
        {
            if (intent.OverrideFacingDirection)
            {
                // The brain is supplying an explicit facing direction — suppress NavMesh
                // auto-rotation so an external system (e.g. SteerModule) can own it.
                agent.updateRotation = false;
            }
            else if (intent.OverrideFacing)
            {
                // Move-and-aim: travel along the path but keep the body turned toward the facing
                // target. NavMesh auto-rotation would fight this every frame, so it stays off.
                agent.updateRotation = false;
                FacePosition(intent.FacePosition, deltaTime);
            }
            else
            {
                agent.updateRotation = defaultUpdateRotation;
            }

            agent.stoppingDistance = Mathf.Max(0.01f, intent.StopDistance);
            float baseMultiplier = intent.IsRunning ? 1f : walkSpeedMultiplier;
            agent.speed = defaultSpeed * baseMultiplier * Mathf.Max(0.01f, intent.SpeedMultiplier);

            agent.isStopped = false;

            if (!agent.hasPath || Vector3.Distance(agent.destination, intent.TargetPosition) > 0.2f)
            {
                agent.SetDestination(intent.TargetPosition);
            }

            if (HasReachedDestination)
            {
                StopAgentPath();
            }
        }

        private void StopAgentPath()
        {
            if (!agent.isOnNavMesh) return;

            agent.stoppingDistance = defaultStoppingDistance;
            agent.speed = defaultSpeed;
            agent.isStopped = true;

            if (agent.hasPath)
            {
                agent.ResetPath();
            }

            stuckTimer = 0f;
        }

        private void FacePosition(Vector3 worldPosition, float deltaTime)
        {
            Vector3 direction = worldPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, faceRotateSpeed * deltaTime);
        }

        private void HandleStuckRecovery(float deltaTime)
        {
            // Path still resolving — too early to call anything stuck.
            if (agent.pathPending)
            {
                stuckTimer = 0f;
                return;
            }

            if (HasReachedDestination)
            {
                stuckTimer = 0f;
                return;
            }

            if (agent.velocity.sqrMagnitude > stuckVelocityThreshold * stuckVelocityThreshold)
            {
                stuckTimer = 0f;
                return;
            }

            // Partial or invalid paths also count as stuck — the agent is not moving and can't reach
            // the target as-is. Periodically re-request the path in case the nav graph has changed
            // or a dynamic obstacle has moved out of the way.
            stuckTimer += deltaTime;
            if (stuckTimer < stuckTime)
            {
                return;
            }

            Vector3 destination = agent.destination;
            agent.ResetPath();
            agent.SetDestination(destination);
            stuckTimer = 0f;
        }

        // Un-park an agent this component disabled in Awake, once a NavMesh has appeared under it.
        // Mirrors WorldStreamer.SnapAgentsToNavMesh, including the ordering: Warp only takes effect
        // on an enabled agent, so position first, then enable, then Warp.
        private void TryReattachToNavMesh(float deltaTime)
        {
            // Only our own parking is recoverable here. An agent whose GameObject is inactive never
            // reaches this method, so a disabled component is the parked case by elimination —
            // except for the one other thing that parks it, which is a machine that does not
            // simulate this entity. Re-attaching there would switch the agent back on and hand it
            // the transform the server is replicating into.
            if (agent.enabled || selfDriveSuspended)
            {
                return;
            }

            reattachTimer -= deltaTime;
            if (reattachTimer > 0f)
            {
                return;
            }
            reattachTimer = ReattachInterval;

            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit,
                                        navMeshSnapDistance, NavMesh.AllAreas))
            {
                NoteNavMeshMissing(ReattachInterval);
                return;
            }

            transform.position = hit.position;
            agent.enabled = true;
            agent.Warp(hit.position);
            stuckTimer = 0f;
            NoteNavMeshFound();
        }

        private void TrySnapToNavMesh(float deltaTime)
        {
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshSnapDistance, NavMesh.AllAreas))
            {
                NoteNavMeshMissing(deltaTime);
                return;
            }

            agent.Warp(hit.position);
            stuckTimer = 0f;
            NoteNavMeshFound();
        }

        // Both searches above give up silently when nothing is found, and an agent with no NavMesh
        // under it then stands still for the rest of the session with nothing logged. That is
        // indistinguishable from a broken prefab, a dead animator or a brain returning no intent,
        // and it is the failure people actually hit — dropping a creature into a scene that was
        // never baked, or onto terrain the mesh floats several metres above.
        //
        // The delay matters: in a streamed world an agent legitimately wakes before its chunk's
        // mesh exists, so warning immediately would fire on every spawn. Warn once per agent, name
        // the position, and stay quiet forever after — a per-frame warning is its own bug.
        private const float NoNavMeshWarnDelay = 3f;
        private float noNavMeshTimer;
        private bool loggedNoNavMesh;

        private void NoteNavMeshMissing(float deltaTime)
        {
            if (loggedNoNavMesh)
            {
                return;
            }

            noNavMeshTimer += deltaTime;
            if (noNavMeshTimer < NoNavMeshWarnDelay)
            {
                return;
            }

            loggedNoNavMesh = true;
            Debug.LogWarning(
                $"{name}: no NavMesh within {navMeshSnapDistance} m of {transform.position} after " +
                $"{NoNavMeshWarnDelay:0.#}s — this agent cannot move and will stand still. Bake a " +
                "NavMesh for this scene, or place the agent on one.", this);
        }

        private void NoteNavMeshFound()
        {
            noNavMeshTimer = 0f;
            loggedNoNavMesh = false;
        }

        private void UpdateMountedLeap(float deltaTime)
        {
            leapCooldownTimer = Mathf.Max(0f, leapCooldownTimer - deltaTime);
            if (!isLeaping || !agent)
            {
                return;
            }

            leapElapsed += deltaTime;
            float t = Mathf.Clamp01(leapElapsed / leapDuration);
            float arc = Mathf.Sin(t * Mathf.PI);

            Vector3 flat = Vector3.Lerp(leapStart, leapEnd, t);
            Vector3 pos = new Vector3(flat.x, flat.y + arc * leapVertical, flat.z);
            transform.position = pos;

            if (t >= 1f)
            {
                isLeaping = false;
                agent.updatePosition = defaultUpdatePosition;
                agent.updateRotation = defaultUpdateRotation;
                if (NavMesh.SamplePosition(leapEnd, out NavMeshHit hit, mountedLeapSampleRadius, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                }
                else
                {
                    agent.Warp(leapEnd);
                }
                agent.isStopped = false;
            }
        }

        private void UpdateMountedJump(float deltaTime)
        {
            if (!agent)
            {
                return;
            }

            jumpCooldownTimer = Mathf.Max(0f, jumpCooldownTimer - deltaTime);
            if (jumpElapsed < 0f)
            {
                return;
            }

            jumpElapsed += deltaTime;
            float t = Mathf.Clamp01(jumpElapsed / Mathf.Max(0.01f, mountedJumpDuration));
            jumpArc = Mathf.Sin(t * Mathf.PI) * Mathf.Max(0.01f, mountedJumpHeight);

            if (t >= 1f)
            {
                jumpElapsed = -1f;
                jumpArc = 0f;
            }

            ApplyBaseOffset();
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.Leap, OnLeapRequested);

            if (agent)
            {
                groundOffset = 0f;
                jumpArc = 0f;
                agent.baseOffset = defaultBaseOffset;
                agent.updateRotation = defaultUpdateRotation;
            }
        }

        /// <summary>
        /// A blast has thrown this animal. Run the leap here only if this machine owns it.
        ///
        /// <para>
        /// Broadcast on the mount's relay, so every machine receives this and exactly one acts —
        /// the server for a loose creature, the RIDER's machine for a mount somebody is on. That
        /// distinction is the whole reason the message exists: a ridden mount's transform is
        /// owner-authoritative, so the leap the server used to run on its own copy was overwritten
        /// within a tick and the rider saw nothing at all.
        /// </para>
        /// <para>
        /// The same shape as <c>FlungBody</c>, which is this for a player. See
        /// <see cref="NetMsg.Leap"/> for the payload.
        /// </para>
        /// </summary>
        private void OnLeapRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Owns(this)) return;

            float distance = arg.P.magnitude;
            if (distance < 1e-3f) return;

            // Checked here rather than by the sender: availability is a property of THIS motor, and
            // the machine that composed the message was looking at a different copy of it.
            if (!IsLeapAvailable) return;

            RequestLeap(arg.P / distance, distance, arg.A * 0.01f, arg.B * 0.001f);
        }

        private void OnValidate()
        {
            navMeshSnapDistance = Mathf.Max(0.5f, navMeshSnapDistance);
            stuckVelocityThreshold = Mathf.Max(0.001f, stuckVelocityThreshold);
            stuckTime = Mathf.Max(0.1f, stuckTime);
            faceRotateSpeed = Mathf.Max(0.1f, faceRotateSpeed);
            mountedJumpHeight = Mathf.Max(0.05f, mountedJumpHeight);
            mountedJumpDuration = Mathf.Max(0.05f, mountedJumpDuration);
            mountedJumpCooldown = Mathf.Max(0f, mountedJumpCooldown);
            mountedLeapCooldown = Mathf.Max(0f, mountedLeapCooldown);
            mountedLeapSampleRadius = Mathf.Max(0.5f, mountedLeapSampleRadius);
            riderTurnSpeed = Mathf.Max(1f, riderTurnSpeed);
            riderForwardTargetDistance = Mathf.Max(0.1f, riderForwardTargetDistance);
            riderStopDistance = Mathf.Max(0.01f, riderStopDistance);
            riderNavMeshSampleDistance = Mathf.Max(0.1f, riderNavMeshSampleDistance);
        }
    }
}
