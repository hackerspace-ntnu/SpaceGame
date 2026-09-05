// The wingsuit's flight, on the player's own body.
//
// There is no craft here and nothing is mounted: the astronaut IS the glider. What that buys is
// that every other system keeps working — the player is still the player, still owns their own
// transform, still carries their own health — and what it costs is that this component has to take
// the body off PlayerMovement and PlayerLook for the duration and hand it back intact.
//
// The physics is the ornithopter's, unchanged, with thrust set to zero. See WingsuitFlightConfig.
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gear.Wingsuit;
using SpaceGame.Teleporting;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Flies the player under a wingsuit. Added to the player by <c>WingsuitItem</c> when the suit
    /// is worn and destroyed when it comes off; it does nothing at all until <see cref="Begin"/>.
    ///
    /// <para>
    /// Owner only. The player's NetworkTransform is owner-authoritative, so the machine flying the
    /// body is the machine whose pose is the truth — a glide simulated anywhere else would be a
    /// second, divergent flight fighting the replicated one. Peers see it through the replicated
    /// <c>IsGliding</c> animator bool and the pose it drives; nothing about the flight is on the
    /// wire in its own right.
    /// </para>
    /// <para>
    /// Runs after <c>PlayerMovement</c> deliberately (execution order). Both look at the same
    /// ground probe, and the frame the glide ends is the frame movement must already have decided
    /// not to bill fall damage for it — see <see cref="CheckForLanding"/>.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(Rigidbody))]
    public class WingsuitFlight : MonoBehaviour, ITeleportAware
    {
        [Header("Flight")]
        [Tooltip("The whole flight model, tuned for a human under four square metres of membrane. " +
                 "Thrust is zero and must stay zero — that is what makes it a wingsuit.")]
        [SerializeField] private WingsuitFlightConfig flight = new WingsuitFlightConfig();

        [Tooltip("What arriving at a surface costs, on closing speed.")]
        [SerializeField] private WingsuitLandingConfig landing = new WingsuitLandingConfig();

        [Header("Deploy")]
        [Tooltip("Seconds for the membrane to go from folded to full span. The wing makes almost " +
                 "no lift until it is open, so this is also how long the drop lasts before the " +
                 "suit catches.")]
        [SerializeField, Min(0.01f)] private float spreadDuration = 0.35f;

        [Tooltip("Airspeed a deploy starts at even when the pilot had less, m/s. Below the stall " +
                 "the wing makes nothing, and a suit that read as broken on the frame it opened " +
                 "would be blamed for the fall that followed.")]
        [SerializeField, Min(0f)] private float minAirspeed = 14f;

        [Tooltip("Fraction of the pilot's speed carried into the glide. 1 is all of it.")]
        [SerializeField, Range(0f, 1f)] private float speedCarry = 1f;

        [Header("Stick")]
        [Tooltip("How fast the mouse moves the nose, as a MULTIPLE of the player's ordinary look " +
                 "sensitivity. 1 means aiming the wing feels exactly like looking around, which " +
                 "is the whole promise of 'fly where you look' — less than that reads as lag.")]
        [SerializeField, Min(0.05f)] private float lookSensitivityShare = 1f;

        [Tooltip("How hard the mouse's horizontal movement rolls the wing, per unit of look " +
                 "sensitivity. This is the main way to turn.")]
        [SerializeField, Min(0f)] private float mouseBank = 0.05f;

        [Tooltip("How much of the mouse's swing reaches the BANK. Whatever is left over goes to " +
                 "the flat rudder, which is what keeps the wing answering below flying speed.")]
        [SerializeField, Range(0f, 1f)] private float bankShare = 0.85f;

        [Tooltip("How fast the mouse's swing falls back to centre when the mouse stops, per " +
                 "second. High rolls level quickly out of a turn; low holds the bank in.")]
        [SerializeField, Min(0f)] private float swingCentring = 2.2f;

        [Tooltip("How far the nose has to be off its commanded angle for the stick to be hard " +
                 "over, degrees. Small is direct, large is soft.")]
        [SerializeField, Min(0.1f)] private float noseSaturation = 2.5f;

        [Header("View")]
        [Tooltip("How much of the bank the view leans with, 0..1. Zero for players who would " +
                 "rather the horizon stayed put.")]
        [SerializeField, Range(0f, 1f)] private float viewRollFraction = 0.5f;

        private Rigidbody body;
        private PlayerMovement movement;
        private PlayerLook look;
        private PlayerInputManager inputs;
        private Animator animator;
        private PlayerController controller;
        private AimProvider aim;

        private OrnithopterFlightState state;
        private bool gliding;
        private bool gravityBeforeGlide;

        private float commandedPitch;
        private float swing;

        /// <summary>Animator bool every machine reads to know the wings are out. Replicated by
        /// <c>ClientNetworkAnimator</c> like every other parameter, which is why the pose and the
        /// membranes need nothing of their own on the wire.</summary>
        public const string GlidingParameter = "IsGliding";

        private static readonly int GlidingId = Animator.StringToHash(GlidingParameter);

        /// <summary>Whether the wings are out and this body is being flown.</summary>
        public bool IsGliding => gliding;

        /// <summary>Airspeed of the glide, m/s. Zero when not gliding.</summary>
        public float Airspeed => gliding ? state.Airspeed : 0f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            movement = GetComponent<PlayerMovement>();
            look = GetComponentInChildren<PlayerLook>();
            inputs = GetComponent<PlayerInputManager>();
            animator = GetComponent<Animator>();
            controller = GetComponent<PlayerController>();
            aim = GetComponent<AimProvider>();
        }

        /// <summary>
        /// Spread the wings and start flying, from whatever motion the player already had.
        ///
        /// <para>
        /// Idempotent: a second call while already gliding is ignored rather than restarting the
        /// flight at the current speed, which would be a way to launder a dive into a fresh glide.
        /// </para>
        /// </summary>
        public void Begin()
        {
            if (gliding || body == null) return;

            Enter(WingsuitControl.Deploy(body.linearVelocity, LookDirection(),
                                         speedCarry, minAirspeed));
        }

        /// <summary>
        /// Where the player is looking, in world space — the direction the glide opens along.
        ///
        /// Taken from <c>AimProvider</c>, the one camera-derived ray anything in this project
        /// should ask for, and honest only on the owner's machine: a peer's copy of a player has
        /// an AimProvider with no live camera behind it. That is fine here, because a deploy is
        /// only ever resolved by the owner.
        /// </summary>
        private Vector3 LookDirection()
        {
            if (aim != null)
            {
                Transform lens = aim.AimTransform;
                if (lens != null) return lens.forward;
            }

            // No camera to ask — a headless test, or a body whose view is switched off. The body's
            // own facing is the only intent left, and it is flat.
            return transform.forward;
        }

        /// <summary>
        /// Pick a glide back up where a save left it — wings already open, at the speed, flight
        /// path and nose angle that were captured.
        ///
        /// <para>
        /// Not the same call as <see cref="Begin"/> and deliberately so. A deploy starts folded and
        /// carries the pilot's own motion in; a restore is already flying and must not spend the
        /// spread ramp again, because on the frame a save lands the body has no velocity to be
        /// caught by and the suit would read as having dropped the player.
        /// </para>
        /// </summary>
        public void Resume(float airspeed, float gammaDegrees, float headingDegrees,
                           float pitchDegrees)
        {
            if (gliding || body == null) return;

            OrnithopterFlightState resumed =
                OrnithopterFlightState.Launch(airspeed, headingDegrees, gammaDegrees);

            resumed.Pitch = pitchDegrees;
            resumed.Deployment = 1f;
            resumed.WingSpread = 1f;

            Enter(resumed);
        }

        /// <summary>The half of a deploy that is the same however the glide started.</summary>
        private void Enter(OrnithopterFlightState entry)
        {
            state = entry;
            commandedPitch = state.Pitch;
            swing = 0f;
            gliding = true;

            // One source of weight. The model integrates its own g, and this world's is -18 —
            // leaving Unity's on would make the suit read as a brick with wings.
            gravityBeforeGlide = body.useGravity;
            body.useGravity = false;

            if (movement != null) movement.SetGliding(true);
            if (look != null) look.SetFlying(true);
            if (animator != null) animator.SetBool(GlidingId, true);
        }

        /// <summary>The live flight, for anything that has to write it into a save file.</summary>
        public OrnithopterFlightState State => state;

        /// <summary>
        /// Fold the wings and hand the body back. Safe to call when not gliding.
        ///
        /// <para>
        /// The body keeps the velocity the glide left it with, on purpose: folding at speed should
        /// feel like stopping flying, not like being stopped. <c>PlayerMovement.CarryMomentum</c>
        /// is what stops air control confiscating it over the next fifth of a second.
        /// </para>
        /// </summary>
        public void End()
        {
            if (!gliding) return;

            gliding = false;
            if (body != null) body.useGravity = gravityBeforeGlide;

            if (movement != null)
            {
                movement.SetGliding(false);
                movement.CarryMomentum();
            }

            if (look != null) look.SetFlying(false);
            if (animator != null) animator.SetBool(GlidingId, false);
        }

        private void OnDisable() => End();

        /// <summary>
        /// The stick, sampled on the render loop where the mouse actually moves.
        ///
        /// Reading mouse delta in FixedUpdate loses movement on any frame that carries no physics
        /// step and doubles it on one that carries two, which reads as a nose that stutters when
        /// the frame rate does.
        /// </summary>
        private void Update()
        {
            if (!gliding || inputs == null) return;

            // The SAME degrees-per-mouse-unit the ordinary look uses, taken from PlayerLook rather
            // than tuned separately here. That is the entire fix for "steering is not responsive":
            // this used to carry its own smaller number, so aiming the wing moved at a fraction of
            // the speed of looking around, and every player read the difference as lag. A share
            // rather than a copy, so somebody can still make the wing heavier on purpose.
            float perUnit = (look != null ? look.LookDegreesPerUnit : 20f * GameSettings.MouseSensitivity)
                            * lookSensitivityShare * Time.deltaTime;

            float mouseY = GameSettings.InvertLookY ? -inputs.LookInput.y : inputs.LookInput.y;

            commandedPitch = WingsuitControl.AimNose(
                commandedPitch, mouseY * perUnit, flight.MaxPitch);

            // Horizontal mouse rolls the wing. mouseBank converts "degrees I would have turned" into
            // stick units, so the roll scales with the player's own sensitivity setting like
            // everything else.
            swing = WingsuitControl.Swing(
                swing, inputs.LookInput.x * perUnit * mouseBank, swingCentring, Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!gliding) return;

            // A corpse does not fly. Death disables PlayerMovement and PlayerLook but knows
            // nothing about this component, so without the check a player killed mid-glide would
            // keep sailing along under a ragdoll that had already been handed their bones.
            // IsDead is asked rather than the enabled flags, for the reason PlayerController
            // documents: everything that restores captured flags would otherwise revive them.
            if (controller != null && controller.IsDead)
            {
                End();
                return;
            }

            float dt = Time.fixedDeltaTime;

            // The membrane opening is the caller's ramp, not the model's — the same shape the
            // ornithopter's deployment has, and what makes a deploy a moment rather than a switch.
            state.Deployment = Mathf.MoveTowards(state.Deployment, 1f, dt / spreadDuration);

            float strafe = inputs != null ? inputs.MoveInput.x : 0f;

            OrnithopterFlightInput stick = WingsuitControl.Stick(
                WingsuitControl.NoseStick(commandedPitch, state.Pitch, noseSaturation),
                WingsuitControl.Bank(swing, strafe, bankShare),

                // Whatever share of the swing the bank did not take becomes flat yaw. It is what
                // keeps the wing answering at low speed, where a bank has too little lift to turn.
                swing * (1f - bankShare),
                inputs != null && inputs.CrouchHeld);

            state = OrnithopterFlightModel.Step(state, stick, flight, dt);

            ApplyPose();
            CheckForLanding();
        }

        /// <summary>
        /// Put the state on the body: velocity outright, heading on the Rigidbody, attitude on the
        /// view.
        ///
        /// <para>
        /// Only the HEADING goes on the body. The capsule is three metres of upright collider and
        /// everything that probes it — the ground check, the crouch, the head look — assumes it
        /// stands up; pitching and rolling it would break all of that to move a collider nobody
        /// can see. The pitch and the bank are expressed where they can actually be seen instead:
        /// on the view here, and on the astronaut's own model by <see cref="WingsuitPose"/>.
        /// </para>
        /// </summary>
        private void ApplyPose()
        {
            body.linearVelocity = OrnithopterFlightModel.VelocityOf(state);
            body.MoveRotation(Quaternion.Euler(0f, state.Heading, 0f));

            // Unity's positive camera pitch looks down; the model's positive pitch is nose up.
            if (look != null)
                look.SetFlightAttitude(-state.Pitch,
                                       WingsuitControl.ViewRoll(state.Roll, viewRollFraction));
        }

        /// <summary>
        /// End the glide when the body reaches the ground, and bill the arrival.
        ///
        /// <para>
        /// The ground truth is <c>PlayerMovement.IsOnGround</c> rather than a probe of this
        /// component's own. One probe means one answer: a second one would disagree with the
        /// first at the edges, and the edges are exactly where a landing happens. It also means
        /// this inherits a probe already written to ignore the player's own colliders — the trap
        /// the ornithopter documents.
        /// </para>
        /// <para>
        /// The ordering with <c>PlayerMovement</c> is load-bearing and is why this component
        /// declares an execution order. Movement runs first, sees <c>gliding</c> still true and so
        /// skips fall damage, and writes <c>wasGrounded = true</c>. Then this runs, ends the glide
        /// and bills the closing speed. By the next step the landing edge has already been
        /// consumed, so the fall table cannot charge for the same arrival a second time.
        /// </para>
        /// </summary>
        private void CheckForLanding()
        {
            if (movement == null || !movement.IsOnGround) return;

            Land(movement.GroundNormal, wasImpact: false);
        }

        /// <summary>
        /// Flying into something. A cliff face is never underneath the pilot, so without this the
        /// glide would scrape along the rock until it found ground to settle on.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!gliding || collision.contactCount == 0) return;

            Land(collision.GetContact(0).normal, wasImpact: true);
        }

        /// <summary>
        /// Both endings, one path — the ornithopter's rule, and for its reason: the two ways a
        /// flight can end must measure the same quantity or one of them is free.
        ///
        /// <para>
        /// Closing speed is read from the FLIGHT STATE, before the glide is ended. By the time a
        /// collision callback runs the solver has already eaten the Rigidbody's velocity, so
        /// asking the body under-reports exactly the hardest hits.
        /// </para>
        /// </summary>
        private void Land(Vector3 surfaceNormal, bool wasImpact)
        {
            float closing = OrnithopterCrash.ClosingSpeed(
                OrnithopterFlightModel.VelocityOf(state), surfaceNormal);

            int damage = OrnithopterCrash.ImpactDamage(closing, landing);

            End();

            // Through NetDamage so the server owns the result, exactly as the player's own fall
            // damage does. Applied after the glide has ended so a fatal arrival leaves the body
            // where it hit rather than mid-flight.
            if (damage > 0) NetDamage.Apply(gameObject, damage, transform);

            Landed?.Invoke(closing, wasImpact);
        }

        /// <summary>Raised where the glide ended: closing speed, and whether it was flown into
        /// something. For audio and feedback — nothing about the flight itself reads it.</summary>
        public event System.Action<float, bool> Landed;

        /// <summary>
        /// Bring the heading through a portal. The flight's heading is world-space state kept
        /// outside the transform, and <see cref="ApplyPose"/> writes it back to the body every
        /// step — so a rotation the portal applied is undone on the next frame unless it is
        /// rebased here. Yaw only: composing the pitch would invert the controls under a ceiling
        /// portal, which is the same call the ornithopter made.
        /// </summary>
        public void OnTeleported(in TeleportMove move)
        {
            if (!gliding) return;

            Vector3 heading = move.Direction(Quaternion.Euler(0f, state.Heading, 0f) * Vector3.forward);
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-6f) return;

            state.Heading = Mathf.Repeat(FlightLaunch.HeadingOf(heading), 360f);
        }
    }
}
