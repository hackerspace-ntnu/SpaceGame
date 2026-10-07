// Main runtime coordinator for entity agents.
// Each frame: ticks all side-effect modules (ClaimsMovement==false) unconditionally, then
// evaluates movement modules (ClaimsMovement==true) highest-priority first — first non-null wins —
// then lets a facing module turn the body only if it outranks that winner.
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using SpaceGame.Diagnostics;
using SpaceGame.Gameplay.Status;
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

        [Tooltip("Off for a body that never picks a target of its own — the NPC craft, whose pilot does the " +
                 "fighting. No AgentTargeting is added, so its modules see no target and it needs no EntityFaction.")]
        [SerializeField] private bool acquiresTargets = true;


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
        private AgentTargeting targeting;
        private AgentGoal goal;
        private float speedVariationPhase;


        private AgentAuthority authority;

        /// <summary>
        /// The conditions this agent is under, so a frozen or foamed one stops acting.
        ///
        /// <para>
        /// Ensured rather than authored, and on EVERY machine: a status arrives as a message on
        /// this body's own relay, and a body with nothing subscribed drops it without a word. An
        /// agent that has to be freezable is every agent, so the receiver belongs on the component
        /// every agent already has rather than on the prefabs somebody remembered to tick.
        /// </para>
        /// </summary>
        private StatusReceiver status;

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

        /// <summary>
        /// Is this agent riding on something else as cargo? Set by <see cref="NpcPassenger"/> when
        /// it seats a body and cleared when it puts one down; nothing else may write it.
        ///
        /// <para>
        /// Deliberately a flag on the controller rather than the controller being switched off,
        /// which is what seating used to do. "Cannot walk" and "cannot act" are different states —
        /// the same distinction <see cref="StatusReceiver.Suppressed"/> draws one line above — and
        /// collapsing them is what made every mounted NPC in the game harmless.
        /// </para>
        /// <para>
        /// Not saved, and it must not be: it is a reading of where this body currently is, and
        /// <see cref="NpcPassenger"/> re-derives it the moment it seats anybody. See the note on
        /// <c>simulating</c> above for the same argument.
        /// </para>
        /// <para>
        /// While it is set, seating owns the motor: <see cref="Dormant"/> and <see cref="Offstage"/>
        /// still starve the modules but leave the motor alone (NpcSeating recorded what it switched
        /// off, and a resume under a moving hull would put the agent off the mesh). Clearing it hands
        /// the motor back in the state the park reasons now ask for (SimulationDistance.md).
        /// </para>
        /// </summary>
        public bool RidesAsPassenger
        {
            get => ridesAsPassenger;
            set
            {
                if (ridesAsPassenger == value)
                    return;

                ridesAsPassenger = value;
                if (!ridesAsPassenger) ReconcileMotorWithPark();
            }
        }

        private bool ridesAsPassenger;

        /// <summary>
        /// Present but not acting: indoors, asleep, a cutscene extra. No module ticks — the same
        /// starvation as <see cref="StatusReceiver.Suppressed"/> — and the motor is stopped and
        /// suspended, so a NavMeshAgent stops writing the transform and the body can be placed.
        /// <see cref="AgentTargeting"/> and <see cref="PerceptionModule"/> skip their own Update too.
        ///
        /// <para>
        /// A flag rather than <c>enabled = false</c>: <c>enabled</c> belongs to
        /// <see cref="HealthReactionModule"/> (death) and to the save system that captures it, and
        /// an offstage agent switched off is one that reloads dead-still for ever. Server-side and
        /// not saved — whoever set it re-derives it.
        /// </para>
        /// </summary>
        public bool Offstage
        {
            get => offstage;
            set
            {
                if (offstage == value)
                    return;

                bool wasParked = IsParked;
                offstage = value;
                RefreshPark(wasParked);
            }
        }

        private bool offstage;

        /// <summary>
        /// Parked because no player is near: the second reason beside <see cref="Offstage"/>, written
        /// only by <c>SimulationRange</c> (SimulationDistance.md). Same starvation, same parked
        /// motor; kept apart from Offstage so the residents' routine and the range can never release
        /// each other's park. Server-side, not saved, not replicated — the range re-derives it every tick.
        /// </summary>
        public bool Dormant
        {
            get => dormant;
            set
            {
                if (dormant == value)
                    return;

                bool wasParked = IsParked;
                dormant = value;
                RefreshPark(wasParked);
            }
        }

        private bool dormant;

        /// <summary>Not acting for any reason: <see cref="Offstage"/> or <see cref="Dormant"/>.</summary>
        public bool IsParked => offstage || dormant;

        // What the motor was last told, so a passenger set down can be handed the motor the park
        // reasons now ask for: they may have changed while seating held it.
        private bool motorParked;

        // The motor parks on the first reason and unparks only when the last one clears. A watcher's
        // motor is already parked; authority returning resumes it unless still parked. A passenger's
        // motor belongs to its seating until it is put down.
        private void RefreshPark(bool wasParked)
        {
            if (IsParked == wasParked || !simulating || ridesAsPassenger)
                return;

            if (IsParked) ParkMotor();
            else UnparkMotor();
        }

        private void ReconcileMotorWithPark()
        {
            if (!simulating || IsParked == motorParked)
                return;

            if (IsParked) ParkMotor();
            else UnparkMotor();
        }

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

            // On THIS object rather than through StatusReceiver.Ensure, which resolves to the
            // networked root: a rider is parented into a saddle, and an agent that borrowed its
            // mount's receiver would be frozen by whatever was sprayed at the animal under it.
            // Every Awake on a GameObject runs before any OnEnable, so a StatusReactionModule
            // beside this one finds this receiver rather than making a second.
            status = GetComponent<StatusReceiver>();
            if (status == null) status = gameObject.AddComponent<StatusReceiver>();

            ResolveMotor();
            ResolveModules();
            speedVariationPhase = Random.Range(0f, Mathf.PI * 2f);
        }

        // Reparenting is the one thing that can move an agent under a different NetworkObject — a
        // creature carried on a walker's deck, a rider seated on a mount — and it is the only case
        // the cached lookup cannot see for itself.
        private void OnTransformParentChanged() => authority?.Invalidate();

        // Profiler markers (Diagnostics.md → Profiling). Compiled out of non-development builds.
        private const string UpdateMarkerName = "SpaceGame.Agent.Update";
        private static readonly ProfilerMarker UpdateMarker = new(UpdateMarkerName);
        private const string ModulesMarkerName = "SpaceGame.Agent.Modules";
        private static readonly ProfilerMarker ModulesMarker = new(ModulesMarkerName);
        private const string FacingMarkerName = "SpaceGame.Agent.Facing";
        private static readonly ProfilerMarker FacingMarker = new(FacingMarkerName);

        private void Update()
        {
            using (UpdateMarker.Auto())
                Simulate(Time.deltaTime);
        }

        private void Simulate(float deltaTime)
        {
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

            // Not in the scene's action at all (offstage or dormant). The motor was parked when the flag went up, so
            // there is nothing to tick but the animator, which settles to idle.
            if (IsParked)
            {
                if (animatorDriver)
                    animatorDriver.Tick(Vector3.zero, true, false);

                return;
            }

            // Cargo on something else. Its feet are not its own — NpcPassenger has parented it into
            // a saddle and switched its motor off — so no movement module and no motor may run, and
            // the presentation context is the honest one: asking a parked motor for its velocity
            // reads a stale zero as a measurement.
            //
            // The side-effect channel still runs, and that is the whole point of the distinction: a
            // module that claims no movement claims nothing the carrier owns, so a rider can still
            // see, acquire and shoot from the saddle. Before this, seating an NPC switched its whole
            // brain off and a mounted gunman was scenery.
            if (RidesAsPassenger)
            {
                if (status == null || !status.Suppressed)
                {
                    using ProfilerMarker.AutoScope sample = ModulesMarker.Auto();
                    TickSideEffectModules(BuildPresentationContext(), deltaTime);
                }

                return;
            }

            if (Motor == null)
                return;

            // Helplessness is DERIVED here, every frame, and written nowhere. A frozen creature is
            // not a creature with its brain switched off — that is what a world save captures, and
            // it reloads suppressed for ever with a clean console. It is a creature whose modules
            // are simply not asked this frame, so the frame the condition ends it moves again with
            // nothing to restore.
            //
            // Above the modules rather than inside one of them, because "cannot act" has to hold
            // for every agent in the game and not only for the ones somebody remembered to put a
            // StatusReactionModule on. Side-effect modules are starved with the rest: a frozen
            // creature that could still bite is not frozen.
            if (status != null && status.Suppressed)
            {
                MoveIntent idle = MoveIntent.Idle();
                Motor.Tick(in idle, deltaTime);

                if (animatorDriver)
                    animatorDriver.Tick(Motor.Velocity, Motor.IsImmobile, false);

                return;
            }

            AgentContext context = BuildContext();
            MoveIntent intent = EvaluateModules(in context, deltaTime, out IBehaviourModule winner);

            // A module can seat this body in the pass above (ResidentSeating): its agent is off on purpose, and ticking the
            // motor now would re-attach it to the NavMesh under the seat and drag it off.
            if (RidesAsPassenger)
                return;

            ApplyFacingOverride(in context, winner, ref intent);

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

        // Handing the body over to whoever does own it. An offstage or dormant body's motor is parked already.
        private void SuspendSimulation()
        {
            if (!IsParked) ParkMotor();
        }

        private void ResumeSimulation()
        {
            if (!IsParked) UnparkMotor();
        }

        // Stopping the motor is not the same as ceasing to tick it: a NavMeshAgent keeps walking
        // its last path forever, and would spend the rest of the session fighting the replicated
        // transform. See ISelfDrivingMotor.
        //
        // A motor NetAuthority has already switched off moves nothing and is not stopped: the
        // rigidbody motors stop by writing velocity, which a remote copy's kinematic body answers
        // with a console warning — on every client, for every such agent, now that this controller
        // stays enabled there.
        private void ParkMotor()
        {
            motorParked = true;

            if (MotorComponent is Behaviour { isActiveAndEnabled: true })
                Motor?.ForceStop();

            if (Motor is ISelfDrivingMotor selfDriving)
                selfDriving.SuspendSelfDrive();
        }

        private void UnparkMotor()
        {
            motorParked = false;

            if (Motor is ISelfDrivingMotor selfDriving)
                selfDriving.ResumeSelfDrive();
        }

        /// <summary>
        /// The only thing a watching machine still runs: modules that produce local output and
        /// nothing else. See <see cref="IPresentationModule"/>.
        ///
        /// <para>
        /// NetAuthority leaves this controller enabled on watching machines for exactly this call
        /// (SimulationDrivers does not list it); before it did, presentation modules — ambient
        /// chatter — never ticked on a client at all.
        /// </para>
        /// <para>
        /// Locomotion animation is deliberately NOT driven from here. It is driven by
        /// <see cref="AgentAnimatorDriver"/> off the replicated transform, because this controller
        /// can be switched off on a watching machine — by death, a ragdoll, a teleport — and an
        /// animation that only plays when the brain happens to be enabled is the "creatures slide
        /// instead of walking" bug wearing a different hat.
        /// </para>
        /// </summary>
        private void TickPresentation(float deltaTime)
        {
            if (presentationModules == null || presentationModules.Length == 0)
                return;

            AgentContext context = BuildPresentationContext();

            using ProfilerMarker.AutoScope sample = ModulesMarker.Auto();
            foreach (IBehaviourModule module in presentationModules)
            {
                if (module.IsActive)
                    RunModule(module, in context, deltaTime);
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

            return ctx;
        }

        /// <summary>
        /// The context a machine that is not driving this body can honestly fill in — a watcher, or
        /// the authority for a body that is riding as cargo (<see cref="RidesAsPassenger"/>).
        ///
        /// A separate method rather than a flag on <see cref="BuildContext"/>, because the two are
        /// not the same query with an option: this one may not touch the motor, which has been
        /// parked, and whose Velocity would be a stale zero dressed up as a measurement.
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

        /// <summary>
        /// Ticks one module behind the fault barrier, and reports what it claimed.
        ///
        /// <para>
        /// A module that throws returns null — the same answer as "I pass" — so the frame falls
        /// through to the next module rather than being lost. That is the whole degradation
        /// contract here: one broken behaviour costs the creature that behaviour, not its ability
        /// to move at all. Before this, the throw escaped the loop and every module below the
        /// broken one starved, which is why a bug in chasing also removed fleeing and wandering.
        /// </para>
        /// <para>
        /// Public and static so the barrier can be tested without a motor, a NavMesh or an Awake.
        /// Not <c>internal</c>: the tests live in <c>Assembly-CSharp-Editor</c>, which has no
        /// <c>InternalsVisibleTo</c> into <c>Assembly-CSharp</c> and would not see it.
        /// </para>
        /// <para>
        /// <see cref="Fault.TryEnter"/> and a local try/catch rather than <see cref="Fault.Run"/>:
        /// this runs once per module per creature per frame, and a closure here was a heap
        /// allocation on every one of those calls.
        /// </para>
        /// </summary>
        public static MoveIntent? RunModule(IBehaviourModule module, in AgentContext context, float deltaTime)
        {
            if (module is not Component owner || !Fault.TryEnter(owner, ModuleSite)) return null;

            try
            {
                return module.Tick(in context, deltaTime);
            }
            catch (System.Exception e)
            {
                Fault.Report(owner, ModuleSite, e);
                return null;
            }
        }

        /// <summary>One site name for every module, so a creature's quarantines are per component.</summary>
        private const string ModuleSite = "AgentModule.Tick";

        /// <summary>The facing pass's site — separate, so a broken aim does not quarantine the walk.</summary>
        private const string FacingSite = "AgentModule.Facing";

        /// <summary>
        /// Attacks, audio, the gun in the NPC's hand: everything that claims no movement. Ticked
        /// unconditionally, and separately from <see cref="EvaluateModules"/>, because a body that
        /// may not walk may still act — see <see cref="RidesAsPassenger"/>.
        /// </summary>
        private void TickSideEffectModules(in AgentContext context, float deltaTime)
        {
            if (sideEffectModules == null)
                return;

            foreach (IBehaviourModule module in sideEffectModules)
            {
                if (module.IsActive)
                    RunModule(module, in context, deltaTime);
            }
        }

        // winner: the movement module whose intent this is, or null when nothing claimed the frame.
        private MoveIntent EvaluateModules(in AgentContext context, float deltaTime, out IBehaviourModule winner)
        {
            using ProfilerMarker.AutoScope sample = ModulesMarker.Auto();
            TickSideEffectModules(in context, deltaTime);
            winner = null;

            // First movement module to return non-null wins this frame.
            if (movementModules != null)
            {
                foreach (IBehaviourModule module in movementModules)
                {
                    if (!module.IsActive)
                        continue;

                    MoveIntent? result = RunModule(module, in context, deltaTime);
                    if (result.HasValue)
                    {
                        winner = module;
                        return result.Value;
                    }
                }
            }

            return MoveIntent.Idle();
        }

        /// <summary>
        /// May a facing module with <paramref name="facingPriority"/> turn a body whose movement
        /// frame was won at <paramref name="winnerPriority"/> (null: nothing claimed it)? Only from
        /// strictly above — or when the facing module IS the winner, aiming its own move.
        ///
        /// <para>
        /// Before this rule any facing module turned the body, so a telegraph (22) or a ranged aim
        /// could turn an NPC away from the player <see cref="InteractionFocusModule"/> (100) had it
        /// facing mid-conversation. Public and static so the rule is testable without a scene.
        /// </para>
        /// </summary>
        public static bool FacingApplies(int facingPriority, int? winnerPriority, bool isWinner) =>
            isWinner || !winnerPriority.HasValue || facingPriority > winnerPriority.Value;

        // Second arbitration pass, over the facing channel only. Runs after a locomotion winner is
        // picked and does not disturb it: the highest-priority facing module that wants the body
        // pointed somewhere gets it — if it outranks the movement winner (FacingApplies).
        private void ApplyFacingOverride(in AgentContext context, IBehaviourModule winner, ref MoveIntent intent)
        {
            if (facingModules == null)
                return;

            using ProfilerMarker.AutoScope sample = FacingMarker.Auto();
            int? winnerPriority = winner?.Priority;

            foreach (IFacingModule module in facingModules)
            {
                if (!module.IsActive)
                    continue;

                // Not break: the winner itself may sit further down the list, aiming its own move.
                if (!FacingApplies(module.FacingPriority, winnerPriority, ReferenceEquals(module, winner)))
                    continue;

                if (!RunFacing(module, in context, out Vector3 facePosition)) continue;

                intent.FacePosition = facePosition;
                intent.OverrideFacing = true;
                return;
            }
        }

        /// <summary>
        /// Asks one facing module behind the fault barrier. A throw reads as "wants nothing", the
        /// facing twin of <see cref="RunModule"/>'s null. Public and static for the same reason.
        /// </summary>
        public static bool RunFacing(IFacingModule module, in AgentContext context, out Vector3 facePosition)
        {
            facePosition = Vector3.zero;
            if (module is not Component owner || !Fault.TryEnter(owner, FacingSite)) return false;

            // Same allocation-free barrier as RunModule.
            try
            {
                return module.TryGetFacing(in context, out facePosition);
            }
            catch (System.Exception e)
            {
                Fault.Report(owner, FacingSite, e);
                facePosition = Vector3.zero;
                return false;
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
            // Auto-added rather than required, so prefabs that predate the component still get one
            // shared target decision instead of every combat module resolving its own.
            targeting = acquiresTargets ? AgentTargeting.GetOrAdd(gameObject) : null;

            // Same reasoning for travel: one destination per agent, written by whoever decides and
            // read by whoever moves. Auto-added so a prefab needs no extra step to be sendable.
            goal = AgentGoal.GetOrAdd(gameObject);

            if (movementModules.Length == 0)
                Debug.LogWarning($"{name}: AgentController found no movement IBehaviourModule. Add at least one module.", this);
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
                Debug.LogError($"{name}: AgentController could not find an IMovementMotor. Add NavMeshAgentMotor (pathfinding), HoverRigidbodyMotor or LeggedDriver.", this);
        }
    }
}
