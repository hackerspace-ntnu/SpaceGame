using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what an entity's motor was in the middle of doing.
    ///
    /// <b>One saver for all five motors, because a body has one.</b> NavMesh, rigidbody, hovercraft,
    /// flying craft and legged driver are alternatives, not layers: an entity carries exactly one of
    /// them. Five keys and five policy clauses for five mutually exclusive components would be five
    /// things to keep in step for one question — "what was this thing's motor doing" — so it is asked
    /// once, and each block is written only when the motor it describes is present.
    ///
    /// <b>Two of these are safety, not fidelity.</b>
    ///
    /// <c>RigidbodyMotor</c> runs a jump or a leap by forcing the body kinematic and remembering what
    /// it was BEFORE. That memory lives in one field and nowhere else, so a save taken mid-arc
    /// recorded a kinematic body and lost the only record of what it should stop being — leaving a
    /// vehicle that reloads weightless and unpushable for the rest of the session, with nothing in
    /// play able to fix it. The resting flag is therefore captured every time, arc or no arc, and
    /// re-asserted on restore.
    ///
    /// <c>NavMeshAgentMotor</c> runs a mounted leap by switching <c>agent.updatePosition</c> and
    /// <c>updateRotation</c> off and driving the transform by hand; only the frame the arc LANDS ever
    /// switches them back. A leap therefore has to come back as a leap and be allowed to finish,
    /// rather than being abandoned halfway with the agent's flags left where the takeoff put them.
    ///
    /// <b>What is deliberately left out.</b> Every rider channel — held sticks, tracked throttle,
    /// latched input — because a held stick is an INPUT and nobody is holding one on the frame a
    /// world loads; restoring one drives the vehicle off under its own power. <c>selfDriveSuspended</c>
    /// and the whole authority story, because that is a claim about THIS session's network topology
    /// and a restored one parks an agent with its NavMeshAgent switched off. Smoothing accumulators
    /// and the stuck timer, because they re-converge from live inputs inside a few frames and a stale
    /// stuck timer causes a spurious path reset on the first one. And the legged driver's NavMesh
    /// ROUTE, because <c>repathTimer</c> starts at zero so the route is rebuilt from the destination
    /// before the machine takes a step — and a stale corner list over terrain that has not streamed
    /// in yet would steer it somewhere nobody asked for.
    ///
    /// Not deferred: every field here is a number or a world position belonging to this object.
    /// </summary>
    public class MotorStateSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "motor";

        public string SaveKey => Key;

        /// <summary>
        /// A destination as a flag plus a value rather than a <c>Vector3?</c>. The shared serializer
        /// registers a converter for Vector3 and not for Nullable&lt;Vector3&gt;, and "no
        /// destination" is a meaningful state that has to survive the round trip intact.
        /// </summary>
        public struct Destination
        {
            public bool has;
            public Vector3 position;
            public float stopDistance;
        }

        public struct NavState
        {
            public bool present;
            public float jumpElapsed;
            public float jumpCooldown;
            public float leapCooldown;

            public bool leaping;
            public Vector3 leapStart;
            public Vector3 leapEnd;
            public float leapVertical;
            public float leapDuration;
            public float leapElapsed;
        }

        public struct BodyState
        {
            public bool present;
            public Destination destination;

            public bool arcing;
            public float arcElapsed;
            public float arcDuration;
            public float arcHeight;
            public Vector3 arcStart;
            public Vector3 arcEnd;
            public float arcCooldown;

            /// <summary>What <c>isKinematic</c> means for this body when nothing is arcing.</summary>
            public bool restingKinematic;
        }

        public struct HoverState
        {
            public bool present;
            public Destination destination;
            public bool headingValid;
            public float heading;
        }

        public struct FlyState
        {
            public bool present;
            public Destination destination;
            public bool riderYawValid;
            public float riderYaw;
        }

        public struct LeggedState
        {
            public bool present;
            public Destination destination;
            public float currentSpeed;
            public float currentStrafe;
            public Vector3 detourDirection;
            public float detourHold;
        }

        public struct State
        {
            public NavState nav;
            public BodyState body;
            public HoverState hover;
            public FlyState fly;
            public LeggedState legged;
        }

        private NavMeshAgentMotor nav;
        private RigidbodyMotor body;
        private HoverRigidbodyMotor hover;
        private FlyingRigidbodyMotor fly;
        private LeggedDriver legged;
        private bool looked;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private void Look()
        {
            if (looked) return;
            looked = true;
            nav = GetComponent<NavMeshAgentMotor>();
            body = GetComponent<RigidbodyMotor>();
            hover = GetComponent<HoverRigidbodyMotor>();
            fly = GetComponent<FlyingRigidbodyMotor>();
            legged = GetComponent<LeggedDriver>();
        }

        public object CaptureState()
        {
            Look();
            if (nav == null && body == null && hover == null && fly == null && legged == null)
                return null;

            var state = new State();

            if (nav != null)
            {
                state.nav = new NavState
                {
                    present = true,
                    jumpElapsed = nav.JumpElapsed,
                    jumpCooldown = nav.JumpCooldownTimer,
                    leapCooldown = nav.LeapCooldownTimer,
                    leaping = nav.IsLeaping,
                    leapStart = nav.LeapStart,
                    leapEnd = nav.LeapEnd,
                    leapVertical = nav.LeapVertical,
                    leapDuration = nav.LeapDuration,
                    leapElapsed = nav.LeapElapsed,
                };
            }

            if (body != null)
            {
                state.body = new BodyState
                {
                    present = true,
                    destination = Describe(body.CurrentDestination, body.StopDistance),
                    arcing = body.Arcing,
                    arcElapsed = body.ArcElapsed,
                    arcDuration = body.ArcDuration,
                    arcHeight = body.ArcHeight,
                    arcStart = body.ArcStart,
                    arcEnd = body.ArcEnd,
                    arcCooldown = body.ArcCooldownTimer,
                    restingKinematic = body.RestingKinematic,
                };
            }

            if (hover != null)
            {
                state.hover = new HoverState
                {
                    present = true,
                    destination = Describe(hover.CurrentDestination, hover.StopDistance),
                    headingValid = hover.HeadingValid,
                    heading = hover.Heading,
                };
            }

            if (fly != null)
            {
                state.fly = new FlyState
                {
                    present = true,
                    destination = Describe(fly.CurrentDestination, fly.StopDistance),
                    riderYawValid = fly.RiderYawValid,
                    riderYaw = fly.RiderYaw,
                };
            }

            if (legged != null)
            {
                state.legged = new LeggedState
                {
                    present = true,
                    destination = Describe(legged.Destination, legged.StopDistance),
                    currentSpeed = legged.CurrentSpeed,
                    currentStrafe = legged.CurrentStrafe,
                    detourDirection = legged.DetourDirection,
                    detourHold = legged.DetourHold,
                };
            }

            return state;
        }

        public void RestoreState(JObject state)
        {
            Look();

            if (state == null)
            {
                ResetToDefaults();
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            if (nav != null)
            {
                NavState n = restored.nav;
                nav.RestoreCooldowns(n.present ? n.jumpElapsed : -1f, n.jumpCooldown, n.leapCooldown);

                if (n.present && n.leaping)
                    nav.RestoreLeap(n.leapStart, n.leapEnd, n.leapVertical, n.leapDuration, n.leapElapsed);
            }

            if (body != null)
            {
                BodyState b = restored.body;
                body.RestoreDestination(b.destination.has ? b.destination.position : (Vector3?)null,
                                        b.destination.stopDistance);

                // Even when the record has no body block: `restingKinematic` then reads false, which
                // is the correct assertion for a motor that never arced — and the one case that
                // matters is a body that arrived kinematic from a mid-arc save whose block IS present.
                body.RestoreArc(b.present && b.arcing, b.arcElapsed, b.arcDuration, b.arcHeight,
                                b.arcStart, b.arcEnd,
                                b.present ? b.restingKinematic : body.RestingKinematic,
                                b.arcCooldown);
            }

            if (hover != null)
            {
                HoverState h = restored.hover;
                hover.RestoreDestination(h.destination.has ? h.destination.position : (Vector3?)null,
                                         h.destination.stopDistance);
                if (h.present && h.headingValid) hover.RestoreHeading(h.heading);
            }

            if (fly != null)
            {
                FlyState f = restored.fly;
                fly.RestoreDestination(f.destination.has ? f.destination.position : (Vector3?)null,
                                       f.destination.stopDistance);
                if (f.present && f.riderYawValid) fly.RestoreRiderYaw(f.riderYaw);
            }

            if (legged != null)
            {
                LeggedState l = restored.legged;
                legged.RestoreDrive(l.destination.has ? l.destination.position : (Vector3?)null,
                                    l.destination.stopDistance, l.currentSpeed, l.currentStrafe);
                legged.RestoreDetour(l.detourDirection, l.present ? l.detourHold : 0f);
            }
        }

        private void ResetToDefaults()
        {
            // No jump, no leap, no arc, no standing order. The arc reset is the load-bearing one: it
            // is what hands a body back its weight if it arrived kinematic from somewhere else.
            nav?.RestoreCooldowns(-1f, 0f, 0f);

            if (body != null)
            {
                body.RestoreDestination(null, 0.2f);
                body.RestoreArc(false, 0f, 0f, 0f, Vector3.zero, Vector3.zero,
                                body.RestingKinematic, 0f);
            }

            hover?.RestoreDestination(null, 0.5f);
            fly?.RestoreDestination(null, 0.5f);
            legged?.RestoreDrive(null, 0f, 0f, 0f);
            legged?.RestoreDetour(Vector3.zero, 0f);
        }

        private static Destination Describe(Vector3? destination, float stopDistance) => new()
        {
            has = destination.HasValue,
            position = destination ?? Vector3.zero,
            stopDistance = stopDistance,
        };
    }
}
