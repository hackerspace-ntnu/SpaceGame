using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what an entity's motor was in the middle of doing.
    ///
    /// <b>One saver for all three motors, because a body has one.</b> NavMesh, hovercraft and legged
    /// driver are alternatives, not layers: an entity carries exactly one of them. Three keys and
    /// three policy clauses for three mutually exclusive components would be three things to keep in
    /// step for one question — "what was this thing's motor doing" — so it is asked once, and each
    /// block is written only when the motor it describes is present.
    ///
    /// <b>The leap is safety, not fidelity.</b> <c>NavMeshAgentMotor</c> runs a mounted leap by
    /// switching <c>agent.updatePosition</c> and <c>updateRotation</c> off and driving the transform
    /// by hand; only the frame the arc LANDS ever switches them back. A leap therefore has to come back as a leap and be allowed to finish,
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

        public struct HoverState
        {
            public bool present;
            public Destination destination;
            public bool headingValid;
            public float heading;
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
            public HoverState hover;
            public LeggedState legged;
        }

        private NavMeshAgentMotor nav;
        private HoverRigidbodyMotor hover;
        private LeggedDriver legged;
        private bool looked;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private void Look()
        {
            if (looked) return;
            looked = true;
            nav = GetComponent<NavMeshAgentMotor>();
            hover = GetComponent<HoverRigidbodyMotor>();
            legged = GetComponent<LeggedDriver>();
        }

        public object CaptureState()
        {
            Look();
            if (nav == null && hover == null && legged == null)
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

            if (hover != null)
            {
                HoverState h = restored.hover;
                hover.RestoreDestination(h.destination.has ? h.destination.position : (Vector3?)null,
                                         h.destination.stopDistance);
                if (h.present && h.headingValid) hover.RestoreHeading(h.heading);
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
            // No jump, no leap, no standing order.
            nav?.RestoreCooldowns(-1f, 0f, 0f);
            hover?.RestoreDestination(null, 0.5f);
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
