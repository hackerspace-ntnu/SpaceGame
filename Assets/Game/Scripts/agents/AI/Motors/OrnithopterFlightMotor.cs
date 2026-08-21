// Rigidbody-backed motor for the dune ornithopter. Unlike FlyingRigidbodyMotor — which is a
// throttle-and-yaw model for blimps and drones that are always level — this one flies an energy
// model: airspeed is bought with altitude or with flapping, the wing stalls if it is asked for too
// much, and the craft banks to turn.
//
// The physics is NOT here. It lives in SpaceGame.Vehicles.Ornithopter.OrnithopterFlightModel as
// pure functions, so it can be tested without a scene. This class is the shell that owns the
// Rigidbody, translates rider input, and publishes state for the wing animator.
using System;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Agents
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    public class OrnithopterFlightMotor : MonoBehaviour, IMovementMotor, IRiderControllable,
                                          IOrnithopterFlightState
    {
        [Header("References")]
        [SerializeField] private Rigidbody body;

        [Header("Flight")]
        [SerializeField] private OrnithopterFlightConfig flight = new OrnithopterFlightConfig();

        [Header("Deployment")]
        [Tooltip("Seconds for the wings to go from folded to fully spread at launch.")]
        [SerializeField, Min(0.05f)] private float spreadDuration = 0.6f;

        [Tooltip("Floor on the airspeed the craft is launched with, m/s. A running jump carries its " +
                 "own speed in and keeps it if it is faster.")]
        [SerializeField, Min(0f)] private float launchAirspeed = 12f;

        [Header("Ground")]
        [Tooltip("How far below the craft to look for ground. Landing ends the flight.")]
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 1.4f;

        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Seconds after launch during which ground contact is ignored, so launching from a " +
                 "ledge does not land the craft on the frame it spawned.")]
        [SerializeField, Min(0f)] private float landingGraceSeconds = 0.35f;

        [Header("Crash")]
        [SerializeField] private OrnithopterCrashConfig crash = new OrnithopterCrashConfig();

        // How far above the spot being examined the ground search starts. A contact that is already
        // resting ON the ground has nothing below it to find, so the ray has to begin above it.
        private const float GroundSearchLift = 1.5f;

        // Clearance left between the ground and the pilot's feet. The player's transform origin is
        // at their feet, so this only has to be enough to keep them out of the surface.
        private const float GroundStandOffset = 0.05f;

        private OrnithopterFlightState state;
        private OrnithopterFlightInput pendingInput;
        private bool hasPendingInput;
        private int riderDriveFrame = -1;
        private float launchTime = -999f;
        private bool flying;

        // Reused by every ground probe. Sized for the handful of colliders a downward ray through a
        // dune ever crosses; overflowing it costs the deepest hits, never the nearest one, which is
        // the one the search wants.
        private readonly RaycastHit[] groundHits = new RaycastHit[16];

        /// <summary>
        /// Raised once when the flight ends against the world — settling onto ground or flying into
        /// something. Carries how hard the arrival was and somewhere solid to put the pilot; the wing
        /// pack listens, hurts them if it was bad enough, stands them there and despawns the craft.
        /// </summary>
        public event Action<OrnithopterTouchdown> Landed;

        // ─────────── IOrnithopterFlightState ───────────
        public float Airspeed => state.Airspeed;
        public float FlapPhase => state.FlapPhase;
        public float FlapEffort => state.FlapEffort;
        public float WingSpread => state.WingSpread;
        public float BankAngle => state.Roll;
        public float PitchInput => pendingInput.Pitch;
        public float TurnInput => pendingInput.Roll;
        public bool IsStalled => state.Stalled;

        public bool IsFlying => flying;
        public float Stamina => state.Stamina;
        public OrnithopterFlightConfig Config => flight;

        /// <summary>Crash tuning. Read by whoever applies the consequences of a touchdown.</summary>
        public OrnithopterCrashConfig Crash => crash;

        /// <summary>Stall speed for the current configuration — handy in the inspector and in tests.</summary>
        public float StallSpeed => OrnithopterFlightModel.StallSpeed(flight, 1f);

        // ─────────── IMovementMotor ───────────
        public Vector3 Velocity => body ? body.linearVelocity : Vector3.zero;
        public bool IsImmobile => !flying;
        public bool HasReachedDestination => true;
        public Vector3? CurrentDestination => null;
        public void NudgeDestination(Vector3 offset) { }
        public void SuggestDestination(Vector3 position) { }

        private void Awake()
        {
            if (!body) body = GetComponent<Rigidbody>();
            if (body)
            {
                // The flight model integrates weight itself, as one equation with lift and drag.
                // Leaving Unity's gravity on as well means two systems pulling the craft down, and a
                // stall that reads as a brick rather than as a wing running out of air.
                body.useGravity = false;
                body.linearDamping = 0f;
                body.angularDamping = 0f;
            }
        }

        /// <summary>
        /// Put the craft into the air. Called by the wing pack the moment the player uses it.
        /// The wings start folded and spread over <see cref="spreadDuration"/>.
        /// </summary>
        public void Launch(Vector3 headingForward, float initialSpeed)
        {
            Vector3 flat = headingForward;
            flat.y = 0f;
            float heading = flat.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(flat.normalized).eulerAngles.y
                : transform.eulerAngles.y;

            state = OrnithopterFlightState.Launch(Mathf.Max(initialSpeed, launchAirspeed), heading);
            pendingInput = OrnithopterFlightInput.Neutral;
            hasPendingInput = false;
            launchTime = Time.time;
            flying = true;

            // Snap the pose rather than going through MoveRotation: Launch runs on the render loop,
            // where MoveRotation would defer the attitude to the next physics step and render one
            // frame of the craft still pointing wherever it was instantiated.
            ApplyPose(snap: true);
        }

        /// <summary>Stop flying and stop writing the body. Used on dismount and on landing.</summary>
        public void EndFlight()
        {
            flying = false;
            hasPendingInput = false;
            if (body)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        // Latched on the render loop, consumed on the physics loop — the same split
        // FlyingRigidbodyMotor uses, and for the reason recorded there: writing a Rigidbody from
        // Update drives it with the wrong clock, so the per-step advance is uneven and a follow camera
        // turns that unevenness straight into shake.
        public void ApplyRiderInput(in RiderInput input, float deltaTime)
        {
            riderDriveFrame = Time.frameCount;

            // Move.y is pitch and Move.x is roll — this is a flying machine, not a tank. Vertical is
            // the flap axis: positive beats, negative tucks and dives. Mapping it here rather than in
            // SteerModule is what lets the ornithopter fly on the existing input actions without
            // touching the shared steering module or the input asset.
            pendingInput = new OrnithopterFlightInput(
                pitch: input.Move.y,
                roll: input.Move.x,
                flap: input.Vertical,
                turn: Mathf.Abs(input.Turn) > 0.01f ? input.Turn : input.Move.x);

            hasPendingInput = true;
        }

        public void Tick(in MoveIntent intent, float deltaTime)
        {
            // The rider owns this motor. There is no AI channel for it: no behaviour module is tuned
            // to fly a 3D energy model, and one that steered it like a ground vehicle would fly it
            // into terrain. IMovementMotor is implemented so AgentController has something to hold.
            if (riderDriveFrame == Time.frameCount)
                return;

            if (!flying)
                return;

            // Rider let go mid-air — keep flying on neutral controls rather than freezing.
            hasPendingInput = true;
            pendingInput = OrnithopterFlightInput.Neutral;
        }

        private void FixedUpdate()
        {
            if (!flying || body == null)
                return;

            float dt = Time.fixedDeltaTime;

            // Deployment is the animator's cue as much as the physics': folded wings make almost no
            // lift, so the spread ramp IS the launch.
            state.Deployment = Mathf.MoveTowards(state.Deployment, 1f, dt / spreadDuration);

            OrnithopterFlightInput input = hasPendingInput ? pendingInput : OrnithopterFlightInput.Neutral;
            state = OrnithopterFlightModel.Step(state, input, flight, dt);

            ApplyPose();
            CheckForLanding();
        }

        /// <summary>
        /// Write the body from the flight state. Velocity is set outright rather than accumulated with
        /// forces — the model has already done the integration, and letting the solver redo it with
        /// damping and gravity would produce a different craft than the one that was tuned.
        /// </summary>
        private void ApplyPose(bool snap = false)
        {
            if (body == null) return;

            body.linearVelocity = OrnithopterFlightModel.VelocityOf(state);

            // Attitude straight from the flight state. Quaternion.Euler applies Z, then X, then Y —
            // roll, pitch, yaw — which is the aircraft order, so this composes correctly as written.
            // The negations are Unity's sign conventions: +X pitches the nose DOWN for a +Z-forward
            // craft, and +Z rolls the RIGHT wing UP.
            Quaternion attitude = Quaternion.Euler(-state.Pitch, state.Heading, -state.Roll);

            if (snap)
                body.transform.rotation = attitude;
            else
                // MoveRotation rather than the transform: rotating a non-kinematic Rigidbody's
                // transform teleports it and discards the pose interpolation blends from, which the
                // follow camera reads as judder.
                body.MoveRotation(attitude);
        }

        /// <summary>
        /// The gentle way a flight ends: ground has appeared underneath the craft. Deliberately a
        /// probe rather than a wait for contact, so a wing flown onto the sand touches down as a
        /// landing instead of arriving as a collision a step later.
        /// </summary>
        private void CheckForLanding()
        {
            if (!PastLaunchGrace)
                return;

            if (!GroundRaycast(transform.position, groundProbeDistance, out RaycastHit hit))
                return;

            ReportTouchdown(hit.point, hit.normal, wasImpact: false);
        }

        /// <summary>
        /// The other way a flight ends: the craft flew into something. A cliff face, a rock, a
        /// building, another creature — none of it is under the craft, so the downward probe never
        /// sees any of it, and without this the machine simply grinds against the obstacle with its
        /// velocity rewritten from the flight model every step.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!flying || !PastLaunchGrace || collision.contactCount == 0)
                return;

            ContactPoint contact = collision.GetContact(0);

            // ContactPoint.normal points out of the thing that was hit and towards this craft, which
            // is the same convention the ground probe's hit normal uses. Both feed ClosingSpeed
            // unchanged.
            ReportTouchdown(contact.point, contact.normal, wasImpact: true);
        }

        // Launching from a ledge clips the ledge. Ignoring the world for a moment after launch is
        // what stops the wings opening and the craft immediately reporting that it has crashed into
        // the ground it just stepped off.
        private bool PastLaunchGrace => Time.time - launchTime >= landingGraceSeconds;

        /// <summary>
        /// Both endings funnel through here so a dive into the sand and a dive into a cliff cost the
        /// same, and so there is exactly one place that decides where the pilot is left standing.
        /// </summary>
        private void ReportTouchdown(Vector3 point, Vector3 normal, bool wasImpact)
        {
            if (!flying)
                return;

            // Read the speed from the flight state, and read it BEFORE EndFlight zeroes the body.
            // The state is the truth here: the Rigidbody's velocity has already been mangled by the
            // solver's contact response by the time a collision callback runs, so asking it how fast
            // the craft was going would under-report exactly the hardest hits.
            Vector3 velocity = OrnithopterFlightModel.VelocityOf(state);
            float closingSpeed = OrnithopterCrash.ClosingSpeed(velocity, normal);
            Vector3 ground = ResolveGroundPosition(point, normal);

            EndFlight();
            Landed?.Invoke(new OrnithopterTouchdown(point, normal, closingSpeed, ground, wasImpact));
        }

        /// <summary>
        /// Somewhere solid near the impact to stand the pilot. Falls back to the contact point
        /// stepped out of the surface, which for a cliff face a long way up means the pilot is
        /// released in mid-air and falls — the right answer, and one PlayerMovement's own fall
        /// damage already prices.
        /// </summary>
        private Vector3 ResolveGroundPosition(Vector3 contactPoint, Vector3 normal)
        {
            Vector3 clear = normal.sqrMagnitude > 1e-6f
                ? contactPoint + normal.normalized * crash.SurfaceClearance
                : contactPoint;

            Vector3 origin = clear + Vector3.up * GroundSearchLift;

            if (GroundRaycast(origin, GroundSearchLift + crash.GroundSearchDistance, out RaycastHit hit))
                return hit.point + Vector3.up * GroundStandOffset;

            return clear;
        }

        /// <summary>
        /// A downward raycast that ignores the craft and everything parented under it.
        ///
        /// The exclusion is not optional. The craft's own hull colliders sit around its origin and
        /// the pilot is parented into the cradle while mounted, so an unfiltered probe finds the
        /// machine itself the moment it looks down — reporting ground at the crash site whether or
        /// not any exists.
        /// </summary>
        private bool GroundRaycast(Vector3 origin, float distance, out RaycastHit best)
        {
            best = default;

            int count = Physics.RaycastNonAlloc(new Ray(origin, Vector3.down), groundHits, distance,
                                                groundMask, QueryTriggerInteraction.Ignore);
            bool found = false;

            // RaycastNonAlloc does not sort, so the nearest surviving hit has to be picked out here
            // rather than taken from index zero.
            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = groundHits[i].collider;
                if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
                    continue;

                if (!found || groundHits[i].distance < best.distance)
                {
                    best = groundHits[i];
                    found = true;
                }
            }

            return found;
        }

        public void ForceStop() => EndFlight();

        private void OnValidate()
        {
            if (flight == null) flight = new OrnithopterFlightConfig();
            if (crash == null) crash = new OrnithopterCrashConfig();
            spreadDuration = Mathf.Max(0.05f, spreadDuration);
        }
    }
}
