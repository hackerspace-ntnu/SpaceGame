// Main runtime coordinator for entity agents.
// Each frame: ticks all side-effect modules (ClaimsMovement==false) unconditionally, then
// evaluates movement modules (ClaimsMovement==true) highest-priority first — first non-null wins.
// Also supports the legacy IAgentBrain interface so old prefabs don't break immediately.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    // IPersistentEntity: anything with an AgentController can end the session somewhere other than
    // where it started, so it must be saved. This is the clause that covers every agent, every
    // creature and every AI-capable vehicle in one place — see IPersistentEntity for why the save
    // policy's component sniffing missed all of them.
    public class AgentController : MonoBehaviour, IPersistentEntity
    {
        [Header("Dependencies")]
        [SerializeField] private MonoBehaviour MotorComponent;
        [SerializeField] private AgentAnimatorDriver animatorDriver;

        [Header("Nearby Agents (Flocking)")]
        [Tooltip("Radius within which nearby agents are gathered for FlockingModule. 0 = disabled.")]
        [SerializeField] private float nearbyAgentScanRadius = 0f;
        [SerializeField] private LayerMask nearbyAgentLayer;

        [Header("Speed Variation")]
        [Tooltip("How much the agent's speed can drift above and below its base. 0.1 = ±10%.")]
        [SerializeField] private float speedVariationAmount = 0.1f;
        [Tooltip("How many seconds one full drift cycle takes.")]
        [SerializeField] private float speedVariationPeriod = 6f;

        public IMovementMotor Motor { get; private set; }
        private IBehaviourModule[] movementModules;   // ClaimsMovement == true, sorted by priority
        private IBehaviourModule[] sideEffectModules; // ClaimsMovement == false, ticked every frame
        private IBehaviourModule[] presentationModules; // IPresentationModule — ticked on every machine
        private IFacingModule[] facingModules;        // separate facing channel, priority-sorted
        private IAgentBrain legacyBrain;
        private HerdModule herdModule;
        private AgentTargeting targeting;
        private AgentGoal goal;
        private float speedVariationPhase;

        // Reused buffers for neighbour scan — instance-level to avoid cross-agent corruption.
        private readonly Collider[] neighbourBuffer = new Collider[32];
        private readonly Vector3[] nearbyPositionBuffer = new Vector3[32];
        private readonly Vector3[] nearbyVelocityBuffer = new Vector3[32];

        private AgentAuthority authority;

        // What the last frame concluded, so the switch between deciding and watching is an EVENT
        // and not a per-frame reassertion. Starts true because that is what an agent has always
        // been — offline, in a test, in a scene opened from the editor — and because the first
        // Update on a machine that is only watching then sees a change and parks the motor.
        private bool simulating = true;

        /// <summary>
        /// Is this machine the one deciding what this agent does? See <see cref="AgentAuthority"/>.
        ///
        /// True offline and in single-player, which runs as a host — so nothing about the solo game
        /// changes. Read by modules that are reachable from somewhere other than this controller's
        /// tick, and by tests.
        /// </summary>
        public bool SimulatesHere => authority == null || authority.SimulatedHere;

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // The phase is why a crowd does not march in step. It is randomised per agent in Awake, so a
        // load re-rolls it for everybody at once — and a re-roll is not the same as a fresh roll:
        // every agent's sine is sampled against the same Time.time, so the visible artefact is a
        // group that was nicely staggered briefly moving as one before drifting apart again.
        //
        // `simulating` is deliberately NOT saved. It is a one-frame cache of an answer
        // RefreshAuthority re-derives every Update, and the question it answers — "does THIS machine
        // own this agent" — is about the session's network topology, which a load does not carry
        // over. Restoring a previous session's answer would be restoring a stale reading of a
        // different world; the field's `true` default is already the correct starting assumption,
        // and the first Update reconciles it.
        public float SpeedVariationPhase => speedVariationPhase;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreSpeedVariationPhase(float phase) => speedVariationPhase = phase;

        private void Awake()
        {
            authority = new AgentAuthority(this);
            ResolveMotor();
            ResolveModules();
            speedVariationPhase = Random.Range(0f, Mathf.PI * 2f);
        }

        // Reparenting is the one thing that can move an agent under a different NetworkObject — a
        // creature carried on a walker's deck, a rider seated on a mount — and it is the only case
        // the cached lookup cannot see for itself.
        private void OnTransformParentChanged() => authority?.Invalidate();

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            // Before anything decides or moves. Every module below this line writes shared state —
            // a target, a path, a bite — and running them on a machine that does not own the entity
            // is not a smaller version of the same behaviour, it is a second one: two brains
            // pathing the same body against a server-authoritative NetworkTransform, and every
            // client's copy of a swing routed to the server as its own damage request. Host plus
            // two clients used to be three bites per bite.
            if (!RefreshAuthority())
            {
                TickPresentation(deltaTime);
                return;
            }

            if (Motor == null)
                return;

            AgentContext context = BuildContext();
            MoveIntent intent = EvaluateModules(in context, deltaTime);
            ApplyFacingOverride(in context, ref intent);

            if (speedVariationAmount > 0f && intent.Type == AgentIntentType.MoveToPosition)
            {
                float drift = 1f + Mathf.Sin(Time.time * (Mathf.PI * 2f / speedVariationPeriod) + speedVariationPhase) * speedVariationAmount;
                intent.SpeedMultiplier *= drift;
            }

            Motor.Tick(in intent, deltaTime);

            if (animatorDriver)
                animatorDriver.Tick(Motor.Velocity, Motor.IsImmobile, intent.IsRunning);
        }

        // ──────────────────────────────────────────────
        // Authority
        // ──────────────────────────────────────────────

        /// <summary>
        /// Answer whether this machine simulates the agent, and act on the moment that changes.
        ///
        /// Ownership moves mid-life — every mount and dismount hands a vehicle between machines —
        /// so this cannot be decided once at spawn. It is cheap to ask every frame (see
        /// <see cref="AgentAuthority"/>) and expensive to get wrong in either direction, so it is
        /// asked every frame and only the transitions cost anything.
        /// </summary>
        private bool RefreshAuthority()
        {
            bool simulatesNow = SimulatesHere;
            if (simulatesNow == simulating)
                return simulating;

            simulating = simulatesNow;

            if (simulating) ResumeSimulation();
            else SuspendSimulation();

            return simulating;
        }

        // Handing the body over to whoever does own it. Stopping the motor is not the same as
        // ceasing to tick it: a NavMeshAgent keeps walking its last path forever, and would spend
        // the rest of the session fighting the replicated transform. See ISelfDrivingMotor.
        private void SuspendSimulation()
        {
            Motor?.ForceStop();

            if (Motor is ISelfDrivingMotor selfDriving)
                selfDriving.SuspendSelfDrive();
        }

        private void ResumeSimulation()
        {
            if (Motor is ISelfDrivingMotor selfDriving)
                selfDriving.ResumeSelfDrive();
        }

        /// <summary>
        /// The only thing a watching machine still runs: modules that produce local output and
        /// nothing else. See <see cref="IPresentationModule"/>.
        ///
        /// <para>
        /// Locomotion animation is deliberately NOT driven from here. It is driven by
        /// <see cref="AgentAnimatorDriver"/> off the replicated transform, because this controller
        /// is not reliably running at all on a watching machine — NetAuthority disables it outright
        /// on the prefabs that carry one — and an animation that only plays when the brain happens
        /// to be enabled is the "creatures slide instead of walking" bug wearing a different hat.
        /// </para>
        /// </summary>
        private void TickPresentation(float deltaTime)
        {
            if (presentationModules == null || presentationModules.Length == 0)
                return;

            AgentContext context = BuildPresentationContext();

            foreach (IBehaviourModule module in presentationModules)
            {
                if (module.IsActive)
                    module.Tick(in context, deltaTime);
            }
        }

        // ──────────────────────────────────────────────
        // Context
        // ──────────────────────────────────────────────

        private AgentContext BuildContext()
        {
            AgentContext ctx = new AgentContext
            {
                Self = transform,
                Position = transform.position,
                Velocity = Motor.Velocity,
                HasReachedDestination = Motor.HasReachedDestination,
                IsImmobile = Motor.IsImmobile,
                Targeting = targeting,
                Goal = goal,
            };

            if (nearbyAgentScanRadius > 0f)
            {
                int count = Physics.OverlapSphereNonAlloc(transform.position, nearbyAgentScanRadius, neighbourBuffer, nearbyAgentLayer);
                int written = 0;
                for (int i = 0; i < count && written < nearbyPositionBuffer.Length; i++)
                {
                    Transform t = neighbourBuffer[i].transform;
                    if (t == transform)
                        continue;
                    nearbyPositionBuffer[written] = t.position;
                    // Populate velocity from NavMeshAgentMotor if available.
                    IMovementMotor neighbourMotor = t.GetComponent<IMovementMotor>();
                    nearbyVelocityBuffer[written] = neighbourMotor != null ? neighbourMotor.Velocity : Vector3.zero;
                    written++;
                }
                ctx.NearbyAgentPositions = nearbyPositionBuffer;
                ctx.NearbyAgentVelocities = nearbyVelocityBuffer;
                ctx.NearbyAgentCount = written;
            }

            return ctx;
        }

        /// <summary>
        /// The context a watching machine can honestly fill in.
        ///
        /// A separate method rather than a flag on <see cref="BuildContext"/>, because the two are
        /// not the same query with an option: this one may not touch the motor (it has been parked,
        /// and its Velocity would be a stale zero dressed up as a measurement) and must not run the
        /// neighbour OverlapSphere, which is the single most expensive thing an agent does and the
        /// whole reason a client should not be paying for agents it does not own.
        /// </summary>
        private AgentContext BuildPresentationContext() => new AgentContext
        {
            Self = transform,
            Position = transform.position,
            Targeting = targeting,
            Goal = goal,
        };

        // ──────────────────────────────────────────────
        // Module evaluation
        // ──────────────────────────────────────────────

        private MoveIntent EvaluateModules(in AgentContext context, float deltaTime)
        {
            // Always tick side-effect modules (attacks, audio, etc.) — they never produce a MoveIntent.
            if (sideEffectModules != null)
            {
                foreach (IBehaviourModule module in sideEffectModules)
                {
                    if (module.IsActive)
                        module.Tick(in context, deltaTime);
                }
            }

            // First movement module to return non-null wins this frame.
            if (movementModules != null)
            {
                foreach (IBehaviourModule module in movementModules)
                {
                    if (!module.IsActive)
                        continue;

                    MoveIntent? result = module.Tick(in context, deltaTime);
                    if (result.HasValue)
                    {
                        // Don't broadcast Idle — it would lock the whole herd in place.
                        if (result.Value.Type != AgentIntentType.Idle)
                            herdModule?.Publish(module.Priority, result.Value);
                        return result.Value;
                    }
                }
            }

            // Fall back to legacy brain if present (old NpcBrain / EnemyBrain on same prefab).
            if (legacyBrain != null)
                return legacyBrain.Tick(in context, deltaTime);

            return MoveIntent.Idle();
        }

        // Second arbitration pass, over the facing channel only. Runs after a locomotion winner is
        // picked and does not disturb it: the highest-priority facing module that wants the body
        // pointed somewhere gets it, whether or not it also won the movement frame.
        private void ApplyFacingOverride(in AgentContext context, ref MoveIntent intent)
        {
            if (facingModules == null)
                return;

            foreach (IFacingModule module in facingModules)
            {
                if (!module.IsActive)
                    continue;

                if (module.TryGetFacing(in context, out Vector3 facePosition))
                {
                    intent.FacePosition = facePosition;
                    intent.OverrideFacing = true;
                    return;
                }
            }
        }

        // ──────────────────────────────────────────────
        // Setup
        // ──────────────────────────────────────────────

        private void ResolveModules()
        {
            List<(IBehaviourModule Module, int Discovery)> movement = new List<(IBehaviourModule, int)>();
            List<IBehaviourModule> sideEffects = new List<IBehaviourModule>();
            List<IBehaviourModule> presentation = new List<IBehaviourModule>();
            List<(IFacingModule Module, int Discovery)> facing = new List<(IFacingModule, int)>();

            int discovered = 0;
            foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is IBehaviourModule module)
                {
                    if (module.ClaimsMovement)
                        movement.Add((module, discovered++));
                    else
                        sideEffects.Add(module);

                    // Also, not instead: on the machine that simulates the agent a presentation
                    // module ticks exactly where it always did, in priority order among its peers.
                    // This second list is only consulted on machines that are watching, so nothing
                    // is ever ticked twice in one frame.
                    if (mb is IPresentationModule)
                        presentation.Add(module);
                }

                if (mb is IFacingModule facingModule)
                    facing.Add((facingModule, discovered++));
            }

            // Highest priority first, ties broken by component order on the GameObject.
            // List<T>.Sort is introsort and therefore unstable: without the discovery-index
            // tiebreak, two modules sharing a priority (ChaseModule and AlertReceiverModule
            // both sit at Reactive on several prefabs) arbitrate in an arbitrary order that
            // can differ between agents, runs and builds.
            movement.Sort((a, b) =>
            {
                int byPriority = b.Module.Priority.CompareTo(a.Module.Priority);
                return byPriority != 0 ? byPriority : a.Discovery.CompareTo(b.Discovery);
            });

            movementModules = new IBehaviourModule[movement.Count];
            for (int i = 0; i < movement.Count; i++)
                movementModules[i] = movement[i].Module;

            facing.Sort((a, b) =>
            {
                int byPriority = b.Module.FacingPriority.CompareTo(a.Module.FacingPriority);
                return byPriority != 0 ? byPriority : a.Discovery.CompareTo(b.Discovery);
            });

            facingModules = new IFacingModule[facing.Count];
            for (int i = 0; i < facing.Count; i++)
                facingModules[i] = facing[i].Module;

            sideEffectModules = sideEffects.ToArray();
            presentationModules = presentation.ToArray();
            herdModule = GetComponentInChildren<HerdModule>(true);
            // Auto-added rather than required, so prefabs that predate the component still get one
            // shared target decision instead of every combat module resolving its own.
            targeting = AgentTargeting.GetOrAdd(gameObject);

            // Same reasoning for travel: one destination per agent, written by whoever decides and
            // read by whoever moves. Auto-added so a prefab needs no extra step to be sendable.
            goal = AgentGoal.GetOrAdd(gameObject);

            // Legacy fallback: pick up any old IAgentBrain that isn't also IBehaviourModule.
            foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is IAgentBrain brain && mb is not IBehaviourModule)
                {
                    legacyBrain = brain;
                    break;
                }
            }

            if (movementModules.Length == 0 && legacyBrain == null)
                Debug.LogWarning($"{name}: AgentController found no movement IBehaviourModule or IAgentBrain. Add at least one module.", this);
        }

        private void ResolveMotor()
        {
            if (MotorComponent != null && MotorComponent is not IMovementMotor)
            {
                Debug.LogWarning($"{name}: MotorComponent does not implement IMovementMotor. Auto-resolving.", this);
                MotorComponent = null;
            }

            if (MotorComponent == null)
            {
                foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb is IMovementMotor)
                    {
                        MotorComponent = mb;
                        break;
                    }
                }
            }

            if (animatorDriver == null)
                animatorDriver = GetComponentInChildren<AgentAnimatorDriver>(true);

            Motor = MotorComponent as IMovementMotor;

            if (Motor == null)
                Debug.LogError($"{name}: AgentController could not find an IMovementMotor. Add NavMeshAgentMotor (pathfinding) or RigidbodyMotor (physics vehicle).", this);
        }

        // Allow modules or external systems to force a live refresh (e.g. after adding components at runtime).
        public void RefreshModules() => ResolveModules();
        public void RefreshMotor() => ResolveMotor();
    }
}
