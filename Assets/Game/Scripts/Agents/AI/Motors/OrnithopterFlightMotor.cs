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

        private OrnithopterFlightState state;
        private OrnithopterFlightInput pendingInput;
        private bool hasPendingInput;
        private int riderDriveFrame = -1;
        private float launchTime = -999f;
        private bool flying;

        /// <summary>Raised once when the craft touches down. The item listens and despawns it.</summary>
        public event Action Landed;

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

        private void CheckForLanding()
        {
            if (Time.time - launchTime < landingGraceSeconds)
                return;

            if (!Physics.Raycast(transform.position, Vector3.down, groundProbeDistance,
                                 groundMask, QueryTriggerInteraction.Ignore))
                return;

            EndFlight();
            Landed?.Invoke();
        }

        public void ForceStop() => EndFlight();

        private void OnValidate()
        {
            if (flight == null) flight = new OrnithopterFlightConfig();
            spreadDuration = Mathf.Max(0.05f, spreadDuration);
        }
    }
}
