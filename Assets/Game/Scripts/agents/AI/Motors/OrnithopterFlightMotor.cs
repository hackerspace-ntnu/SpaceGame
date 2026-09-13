// Rigidbody-backed motor for the dune ornithopter. Unlike FlyingRigidbodyMotor — which is a
// throttle-and-yaw model for blimps and drones that are always level — this one flies an energy
// model: airspeed is bought with altitude or with flapping, the wing stalls if it is asked for too
// much, and the craft banks to turn.
//
// The physics is NOT here. It lives in SpaceGame.Vehicles.Ornithopter.OrnithopterFlightModel as
// pure functions, so it can be tested without a scene. This class is the shell that owns the
// Rigidbody, translates rider input, and publishes state for the wing animator.
using System;
using SpaceGame.Locomotion;
using SpaceGame.Vehicles.Ornithopter;
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Agents
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    public partial class OrnithopterFlightMotor : MonoBehaviour, IMovementMotor, IRiderControllable,
                                                  IOrnithopterFlightState, IExternallyPosed,
                                                  ITeleportAware, ITowable
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

        [Tooltip("Steepest climb the craft will accept at launch, degrees. A pilot slung upward off " +
                 "a grapple swing enters the flight already going up, and trades that height back " +
                 "for speed on the way over the top.")]
        [SerializeField, Range(0f, 80f)] private float maxLaunchClimb = 45f;

        [Tooltip("Steepest dive the craft will accept at launch, degrees. Kept far tighter than the " +
                 "climb on purpose: the ordinary launch is a step off a ledge, and the pilot is " +
                 "already falling when the wings open. Taking that plunge literally would deploy " +
                 "the craft pointing at the ground it just left.")]
        [SerializeField, Range(0f, 80f)] private float maxLaunchDive = 12f;

        [Header("Tow")]
        [Tooltip("How close to the anchor the tow lets go, metres. The craft is 10 m across and " +
                 "arriving at a rock face at flying speed is a crash, so this is deliberately far " +
                 "wider than the hook's own arrival distance on foot.")]
        [SerializeField, Min(1f)] private float towReleaseDistance = 12f;

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

        // Latched by RequestTow, consumed by one FixedUpdate and then cleared. Consuming it is what
        // makes the tow stop on its own when the rope stops asking, and it is why nothing has to
        // tell this motor that a hook was dropped, an item unequipped or a pilot killed.
        private Vector3 towAcceleration;
        private bool towActive;
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

        /// <summary>See <see cref="IMovementMotor.TopSpeed"/>. Scales tow strength only; it does
        /// not affect flight.</summary>
        public float TopSpeed => flight != null ? flight.FullAuthoritySpeed : 0f;
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
        ///
        /// <para>
        /// <paramref name="climbDegrees"/> is the flight path the pilot arrives on, measured off
        /// the speed they were already carrying. It is clamped HERE rather than where it is
        /// measured, and asymmetrically: the pack reports what the pilot was doing, the airframe
        /// decides what it will accept. A slingshot off a grapple swing enters climbing; a plunge
        /// off a cliff is levelled out to something the wings can fly out of.
        /// </para>
        /// </summary>
        public void Launch(Vector3 headingForward, float initialSpeed, float climbDegrees = 0f)
        {
            Vector3 flat = headingForward;
            flat.y = 0f;
            float heading = flat.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(flat.normalized).eulerAngles.y
                : transform.eulerAngles.y;

            float climb = Mathf.Clamp(climbDegrees, -maxLaunchDive, maxLaunchClimb);

            state = OrnithopterFlightState.Launch(Mathf.Max(initialSpeed, launchAirspeed), heading, climb);
            pendingInput = OrnithopterFlightInput.Neutral;
            hasPendingInput = false;
            launchTime = Time.time;
            flying = true;

            // Snap the pose rather than going through MoveRotation: Launch runs on the render loop,
            // where MoveRotation would defer the attitude to the next physics step and render one
            // frame of the craft still pointing wherever it was instantiated.
            //
            // ApplyPose is a no-op while externally posed, which is the right answer on a machine
            // that is only watching: the launch attitude it would write is a guess, and the real
            // one is already on its way over the wire.
            ApplyPose(snap: true);
        }

        /// <summary>Stop flying and stop writing the body. Used on dismount and on landing.</summary>
        public void EndFlight()
        {
            flying = false;
            hasPendingInput = false;
            towActive = false;

            // Kinematic is the ordinary state here, not an exceptional one: NetAuthority freezes
            // the body on every machine that does not own the craft, and PhysX refuses velocity
            // writes to a frozen body with an error per call. The craft is not going anywhere on
            // those machines anyway — the wire is moving it.
            if (body && !body.isKinematic)
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
            if (!flying || body == null || ExternallyPosed)
                return;

            float dt = Time.fixedDeltaTime;

            // Deployment is the animator's cue as much as the physics': folded wings make almost no
            // lift, so the spread ramp IS the launch.
            state.Deployment = Mathf.MoveTowards(state.Deployment, 1f, dt / spreadDuration);

            OrnithopterFlightInput input = hasPendingInput ? pendingInput : OrnithopterFlightInput.Neutral;

            // One step per request. The rope has to ask again for the next one, which is what makes
            // a tow that is no longer being driven end by itself instead of hauling the craft
            // across the desert after whatever was holding it has gone.
            Vector3 tow = towActive ? towAcceleration : Vector3.zero;
            towActive = false;

            state = OrnithopterFlightModel.Step(state, input, flight, dt, tow);

            ApplyPose();
            CheckForLanding();
        }

        /// <summary>
        /// Turn the flight with the craft.
        ///
        /// The flight STATE is the truth here, not the Rigidbody: <see cref="ApplyPose"/> writes
        /// both the velocity and the attitude out of <c>state.Heading</c> every physics step. So a
        /// teleport that turns the craft — which is every portal traversal — is undone on the very
        /// next step, and the wing arrives at the far aperture flying the heading it had in the room
        /// it left. It does not look like a rotation bug; it looks like the craft ignoring the
        /// portal and continuing on its old course from a new place.
        ///
        /// Only the yaw is taken. Pitch and roll are the aircraft's own attitude about its heading
        /// and mean the same thing in any room; a portal on a ceiling would otherwise compose a
        /// half-roll into the flight model and invert the controls.
        /// </summary>
        public void OnTeleported(in TeleportMove move)
        {
            Vector3 heading = move.Direction(
                Quaternion.Euler(0f, state.Heading, 0f) * Vector3.forward);

            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-6f) return;

            state.Heading = Mathf.Repeat(
                Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg, 360f);

            // Straight back onto the body, so the frame the craft arrives in is already flying the
            // new heading rather than spending one step on the old one.
            ApplyPose(snap: true);
        }

        /// <summary>
        /// Write the body from the flight state. Velocity is set outright rather than accumulated with
        /// forces — the model has already done the integration, and letting the solver redo it with
        /// damping and gravity would produce a different craft than the one that was tuned.
        /// </summary>
        private void ApplyPose(bool snap = false)
        {
            if (body == null || ExternallyPosed) return;

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
            // Externally posed means another machine is flying this craft and is the one that gets
            // to say how the flight ended. A second opinion from here would report a touchdown the
            // pilot never had, from a copy that is one interpolation step behind them.
            if (!flying || ExternallyPosed || !PastLaunchGrace || collision.contactCount == 0)
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

            var touchdown = new OrnithopterTouchdown(point, normal, closingSpeed, ground, wasImpact);

            EndFlight();

            // The wire first, then the listeners. Only the machine simulating the flight can see it
            // end, and only the server may decide what that cost and tear the craft down — so the
            // report goes out before anything local starts unwinding the flight it describes.
            PublishTouchdown(touchdown);
            Landed?.Invoke(touchdown);
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

        // ─────────── ITowable ───────────

        /// <summary>
        /// The cradle, which is where the prefab's origin sits and where the pilot is slung. Close
        /// enough to the rope's real eye that no one can tell, and it moves with the craft for free.
        /// </summary>
        public Vector3 TowAttachPoint => transform.position;

        /// <summary>
        /// A rope wants to pull the craft towards <paramref name="anchor"/> this step.
        ///
        /// <para>
        /// Answered here rather than by the hook because every reason a tow ends is the craft's
        /// business, not the rope's: how close is too close depends on a 10 m wingspan, and how
        /// long the pull can last depends on a stamina reserve the hook has never heard of. The
        /// hook supplies one thing nothing else knows — where the far end is tied.
        /// </para>
        /// <para>
        /// The pull itself is only latched. It is resolved against the flight path inside the model
        /// on the next step, so a rope adds speed, climb and turn in the same proportions lift and
        /// weight already do.
        /// </para>
        /// </summary>
        public bool RequestTow(Vector3 anchor)
        {
            towActive = false;

            // Nothing to tow: on the ground, wrecked, or being flown by somebody else's machine.
            // Refusing on ExternallyPosed matters — a peer that towed its own copy would be a
            // second authority on a craft whose pose arrives over the wire.
            if (!flying || ExternallyPosed)
                return false;

            Vector3 toAnchor = anchor - TowAttachPoint;
            float distance = toAnchor.magnitude;

            // Arrived. On foot the hook reels you into the anchor and pops you over the lip; a
            // craft this size doing 25 m/s does not arrive at a rock face, it hits it.
            if (distance <= towReleaseDistance)
                return false;

            // Spent. The rope drains the same reserve the wings do, so a tow that has run the
            // pilot dry has to let go rather than fade to a pull too weak to feel.
            if (state.Stamina <= 0f)
                return false;

            towAcceleration = toAnchor / distance * flight.TowAcceleration;
            towActive = true;
            return true;
        }

        /// <summary>
        /// The AI channel's "stop what you are doing". For a ground motor that means dropping the
        /// nav destination; for this one it must mean nothing at all while the craft is airborne.
        ///
        /// MountModule calls it on every mount, so a rider climbing aboard a craft that is already
        /// flying — the wing pack's own launch order, a save restored mid-flight, a peer replaying
        /// the mount a moment late — would otherwise switch the flight model off underneath them
        /// and turn the aircraft into a falling prop.
        /// </summary>
        public void ForceStop()
        {
            if (flying) return;

            EndFlight();
        }

        private void OnValidate()
        {
            if (flight == null) flight = new OrnithopterFlightConfig();
            if (crash == null) crash = new OrnithopterCrashConfig();
            spreadDuration = Mathf.Max(0.05f, spreadDuration);
            towReleaseDistance = Mathf.Max(1f, towReleaseDistance);
        }
    }
}
