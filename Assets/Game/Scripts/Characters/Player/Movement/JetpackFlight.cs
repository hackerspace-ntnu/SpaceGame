// The jetpack's flight, on the player's own body.
//
// Nothing is spawned and nothing is mounted: the astronaut IS the aircraft, the same bargain the
// wingsuit makes. What that buys is that every other system keeps working — the player still owns
// their transform, their health, their inventory — and what it costs is that this component has to
// take the body off PlayerMovement for the duration and hand it back intact.
//
// Unlike the wingsuit it does NOT take the mouse. PlayerLook keeps the look and keeps yawing the
// body, because where the player is pointed is half of the steering.
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Gear.Jetpack;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Flies the player on a jetpack. Added to the player by <c>JetpackItem</c> when the pack is
    /// worn and destroyed when it comes off; it does nothing at all until <see cref="Begin"/>.
    ///
    /// <para>
    /// <b>Owner only.</b> The player's <c>NetworkTransform</c> is owner-authoritative, so the
    /// machine flying the body is the machine whose pose is the truth — a flight simulated
    /// anywhere else would be a second, divergent one fighting the replicated pose. Peers see the
    /// flight through the item's hold stream, which carries the throttle, the heat and the nozzle
    /// angle; nothing here is on the wire in its own right.
    /// </para>
    /// <para>
    /// Runs after <c>PlayerMovement</c> (execution order 150), the wingsuit's arrangement and for
    /// its reason: both read the same ground probe, and the step a flight ends on is the step
    /// movement must already have decided not to bill fall damage for.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(Rigidbody))]
    public class JetpackFlight : MonoBehaviour
    {
        [Header("Flight")]
        [Tooltip("Thrust, vectoring, drag and the heat budget. The whole machine.")]
        [SerializeField] private JetpackConfig flight = new JetpackConfig();

        [Tooltip("What arriving at a surface costs, on closing speed. This is the price of an " +
                 "overheat as well as of a bad landing — see JetpackLandingConfig.")]
        [SerializeField] private JetpackLandingConfig landing = new JetpackLandingConfig();

        [Header("View")]
        [Tooltip("Step the camera out behind the player while flying. On by default: the pack is " +
                 "on the wearer's BACK, so in first person every piece of feedback it produces — " +
                 "the pods vectoring, the flames, the tips going red — is behind the camera.")]
        [SerializeField] private bool thirdPersonWhileFlying = true;

        private Rigidbody body;
        private PlayerMovement movement;
        private PlayerLook look;
        private PlayerInputManager inputs;
        private PlayerController controller;
        private Animator animator;
        private JetpackThirdPerson view;

        private bool flying;
        private bool gravityBeforeFlight;

        private JetpackHeat heat = JetpackHeat.Cold;
        private JetNozzle nozzle = JetNozzle.Vertical;
        private JetThrottle throttle = JetThrottle.Cut;

        /// <summary>Whether the motors are lit and this body is being flown.</summary>
        public bool IsFlying => flying;

        /// <summary>The heat budget, live. Read by the item for its save bag and by the gauge.</summary>
        public JetpackHeat Heat => heat;

        /// <summary>What the motors did on the last step, after the overheat correction.</summary>
        public JetThrottle Throttle => throttle;

        /// <summary>Where the nozzles have actually got to — never where the input asked.</summary>
        public JetNozzle Nozzle => nozzle;

        /// <summary>Heat as 0..1, for the visor gauge and the glow. Cold when not worn.</summary>
        public float HeatFraction => heat.Fraction(flight);

        /// <summary>
        /// Throttle as 0..1, for the flames. Zero when not flying, and zero for a CUT — the dark
        /// nozzles are what tells a pilot that this fall is the overheat and not their own hand
        /// off the key, which is the only way the two states can be told apart from inside.
        /// </summary>
        public float ThrottleFraction => !flying ? 0f
            : throttle == JetThrottle.Thrust ? 1f
            : throttle == JetThrottle.Descend ? 0.35f
            : 0f;

        /// <summary>The tuning, so the item and the gauge read the same numbers this flies on.</summary>
        public JetpackConfig Config => flight;

        /// <summary>Raised where a flight ended: closing speed, and whether it was flown into
        /// something. For audio and feedback — nothing about the flight itself reads it.</summary>
        public event System.Action<float, bool> Landed;

        /// <summary>Raised on the step the motors cut from overheating, on the owner. The cue that
        /// has to arrive at the moment control is lost rather than a frame later.</summary>
        public event System.Action Overheated;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            movement = GetComponent<PlayerMovement>();
            look = GetComponentInChildren<PlayerLook>();
            inputs = GetComponent<PlayerInputManager>();
            controller = GetComponent<PlayerController>();
            animator = GetComponent<Animator>();

            if (thirdPersonWhileFlying)
            {
                view = GetComponent<JetpackThirdPerson>();
                if (view == null) view = gameObject.AddComponent<JetpackThirdPerson>();
                view.enabled = false;
            }
        }

        /// <summary>
        /// Light the motors and leave the ground.
        ///
        /// <para>
        /// Legal from standing, which is what separates this from the other two back items: the
        /// wing pack and the wingsuit both refuse on the ground because they need air to work in,
        /// and a vertical takeoff is the jetpack's whole point.
        /// </para>
        /// <para>
        /// The kick is a VELOCITY rather than an impulse added to what the player had, so a
        /// takeoff reads the same whether they were standing still, running or already falling. A
        /// launch whose height depends on what you were doing beforehand is a launch nobody can
        /// aim. The horizontal speed they had is kept, scaled by <c>SpeedCarry</c> — running at
        /// the lift-off should be worth something.
        /// </para>
        /// <para>
        /// Idempotent: a second call while already flying is ignored rather than re-kicking, which
        /// would otherwise be an unlimited ladder out of a double tap.
        /// </para>
        /// </summary>
        public void Begin()
        {
            if (flying || body == null) return;

            // An overheated pack does not start. Refused here as well as in the item's CanUse
            // because Begin is also reached from a restore, and a save taken mid-overheat must
            // not launder itself into a working pack.
            if (heat.Overheated) return;

            Vector3 carried = body.linearVelocity * flight.SpeedCarry;
            carried.y = flight.LaunchKick;

            Enter(carried);
        }

        /// <summary>
        /// Put a heat reading on a pack that is not flying — a restore, or a pack picked back up
        /// still hot.
        ///
        /// <para>
        /// Separate from <see cref="Resume"/> because <b>Resume launches</b>. A pack restored
        /// merely hot must not take off on its own, and the two calls were one call until that
        /// happened.
        /// </para>
        /// </summary>
        public void SetHeat(JetpackHeat value) => heat = value;

        /// <summary>
        /// Pick a flight back up where a save left it — motors already lit, at the velocity, heat
        /// and nozzle angle that were captured.
        ///
        /// <para>
        /// Not the same call as <see cref="Begin"/> and deliberately so. A launch kicks; a restore
        /// is already moving and must not be kicked again, or every quicksave in mid-air is worth
        /// a free seven metres a second. It also restores an overheat rather than clearing it,
        /// which is the difference between saving mid-fall and saving as a way to cool down.
        /// </para>
        /// </summary>
        public void Resume(Vector3 velocity, JetpackHeat savedHeat, JetNozzle savedNozzle)
        {
            heat = savedHeat;
            nozzle = savedNozzle;

            if (flying || body == null) return;

            Enter(velocity);
        }

        /// <summary>The half of a launch that is the same however the flight started.</summary>
        private void Enter(Vector3 velocity)
        {
            flying = true;
            body.linearVelocity = velocity;

            // One source of weight. JetpackStep integrates this world's own g, which is 18 — with
            // Unity's left on as well the pack would be lifting against twice its weight.
            gravityBeforeFlight = body.useGravity;
            body.useGravity = false;

            // Wider than a tether, narrower than DisableGroundSnap: the flight writes all three
            // axes and its own gravity, so PlayerMovement must write none of them — but the ground
            // probe and the animator have to keep running, because this component asks movement
            // where the ground is.
            if (movement != null) movement.SetGliding(true);
            if (view != null) view.enabled = true;
        }

        /// <summary>
        /// Cut the motors and hand the body back. Safe to call when not flying.
        ///
        /// <para>
        /// The body keeps the velocity the flight left it with, on purpose: stopping the jetpack
        /// at speed should feel like stopping flying, not like hitting a wall.
        /// <c>PlayerMovement.CarryMomentum</c> is what stops air control confiscating it over the
        /// next fifth of a second.
        /// </para>
        /// <para>
        /// Heat is deliberately NOT reset. It belongs to the pack rather than to the flight, so
        /// landing to cool off is a thing you have to actually wait through — and a player who
        /// could clear the gauge by tapping the deploy twice would never meet the heat rules at
        /// all.
        /// </para>
        /// </summary>
        public void End()
        {
            if (!flying) return;

            flying = false;
            throttle = JetThrottle.Cut;
            nozzle = JetNozzle.Vertical;

            if (body != null) body.useGravity = gravityBeforeFlight;

            if (movement != null)
            {
                movement.SetGliding(false);
                movement.CarryMomentum();
            }

            // Straight back to first person, and the component puts the lens where it found it.
            if (view != null) view.enabled = false;

            // Hand the animator back by simply stopping: PlayerMovement writes all six parameters
            // every FixedUpdate, so the frame after this it is telling the truth again.
        }

        private void OnDisable() => End();

        /// <summary>
        /// Heat keeps moving while the pack is worn but stowed, so the walk back from a hard
        /// flight is the cooldown. Without this the gauge would freeze the moment the player
        /// landed and a pack parked at 99 would still be at 99 an hour later.
        /// </summary>
        private void Update()
        {
            if (flying) return;

            heat = JetpackHeat.Step(heat, JetThrottle.Cut, flight, Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!flying) return;

            // A corpse does not fly. Death disables PlayerMovement and PlayerLook but knows
            // nothing about this component, so without the check a player killed in mid-air would
            // keep flying under a ragdoll that had already been handed their bones. IsDead is
            // asked rather than the enabled flags, for the reason PlayerController documents.
            if (controller != null && controller.IsDead)
            {
                End();
                return;
            }

            float dt = Time.fixedDeltaTime;
            bool wasOverheated = heat.Overheated;

            throttle = heat.Resolve(Wanted());
            heat = JetpackHeat.Step(heat, throttle, flight, dt);

            if (!wasOverheated && heat.Overheated) Overheated?.Invoke();

            Vector2 move = inputs != null ? inputs.MoveInput : Vector2.zero;
            float lookPitch = look != null ? look.Pitch : 0f;

            // An overheated pack's nozzles fall back to vertical rather than staying where the
            // player was steering. Dead motors have nothing to hold them over, and it puts the
            // pack in the right attitude for the relight the player is hoping for.
            JetNozzle command = throttle == JetThrottle.Cut
                ? JetNozzle.Vertical
                : JetpackVector.Command(move, lookPitch, flight);

            nozzle = JetpackVector.Advance(nozzle, command, flight, dt);

            body.linearVelocity = JetpackStep.Step(body.linearVelocity, throttle, nozzle,
                                                   transform.eulerAngles.y, flight, dt);

            PoseAnimator();
            CheckForLanding();
        }

        /// <summary>
        /// Stand the astronaut up while the pack is flying them.
        ///
        /// <para>
        /// <b>Without this the player flies in the JUMP animation, for the whole flight.</b>
        /// <c>SetGliding</c> is deliberately narrower than <c>DisableGroundSnap</c> and leaves
        /// <c>PlayerMovement.UpdateAnimatorParameters</c> running — which is right, because a body
        /// with no animator updates at all is the bug the tether was written to stop repeating —
        /// but it means the animator is told, truthfully, that the player is airborne and falling.
        /// A wingsuit answers that with a Glide clip on its own layer; the jetpack has no clip yet,
        /// so the honest thing is the idle: somebody hanging under a machine that is holding them
        /// up is standing, not falling.
        /// </para>
        /// <para>
        /// Written HERE rather than in <c>PlayerMovement</c> because of the execution order this
        /// component already declares: movement is order 0 and writes the six parameters, this is
        /// 150 and overwrites the three that would otherwise say "falling". Putting the special
        /// case in movement would have it carrying a flag for every future flying gadget.
        /// </para>
        /// </summary>
        private void PoseAnimator()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            // SpeedX/SpeedY go to zero as well, and that is not tidiness. PlayerMovement feeds
            // them the real velocity, and a jetpack cruising at 20 m/s pins the Move blend tree to
            // full sprint - so without this the pilot flies along running in mid-air, which is a
            // worse read than the jump pose it replaces.
            animator.SetFloat("SpeedX", 0f);
            animator.SetFloat("SpeedY", 0f);
            animator.SetFloat("FallSpeed", 0f);
            animator.SetFloat("MoveAnimSpeed", 1f);
            animator.SetBool("IsGrounded", true);
        }

        /// <summary>
        /// What the pilot is asking for, before the heat has its say. One key: Space up or Space
        /// down.
        ///
        /// <para>
        /// <b>There is no key for coming down, and that is the design.</b> Releasing Space is the
        /// descent — the motors idle to a steady sink with the nozzles still lit — so the whole
        /// machine is one button held and let go (<c>GDC-L1-UX-0005</c>: a new action costs an
        /// input, and this one replaces a binding rather than adding one). The crouch cut it
        /// replaced was a second way to say "down" that also happened to be the only way to cool,
        /// which made a hidden key mandatory for a long flight rather than optional.
        /// </para>
        /// <para>
        /// A pack with no input source at all descends rather than hangs, so a flight that loses
        /// its pilot comes down and lands instead of parking a body in the sky.
        /// </para>
        /// </summary>
        private JetThrottle Wanted()
        {
            if (inputs == null) return JetThrottle.Descend;

            return inputs.JumpHeld ? JetThrottle.Thrust : JetThrottle.Descend;
        }

        /// <summary>
        /// End the flight when the body settles onto the ground, and bill the arrival.
        ///
        /// <para>
        /// <b>Descending is half the test, and it is what makes a vertical takeoff possible.</b>
        /// The wingsuit can land on <c>IsOnGround</c> alone because it refuses to deploy on the
        /// ground in the first place; this one launches from standing, so the frame after
        /// <see cref="Begin"/> the probe still reports ground and a bare check would end the
        /// flight before it started. Rising is not landing. It also gets scraping along a dune
        /// under thrust right, for free, and needs no timer that could expire at the wrong moment.
        /// </para>
        /// <para>
        /// The ground truth is <c>PlayerMovement.IsOnGround</c> rather than a probe of this
        /// component's own: one probe means one answer, and the edges are exactly where a landing
        /// happens.
        /// </para>
        /// </summary>
        private void CheckForLanding()
        {
            if (movement == null || !movement.IsOnGround) return;
            if (body.linearVelocity.y > 0.01f) return;

            Land(movement.GroundNormal, wasImpact: false);
        }

        /// <summary>
        /// Flying into something. A cliff face is never underneath the pilot, so without this an
        /// overheated fall into rock would scrape down it looking for ground to settle on.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!flying || collision.contactCount == 0) return;

            Land(collision.GetContact(0).normal, wasImpact: true);
        }

        /// <summary>
        /// Both endings, one path — the ornithopter's rule, and for its reason: the two ways a
        /// flight can end must measure the same quantity or one of them is free.
        ///
        /// <para>
        /// Closing speed is read BEFORE the flight is ended and off the Rigidbody, which is the
        /// live velocity here because this flight writes velocity rather than integrating a
        /// separate state. On a collision step the solver has already begun eating that velocity,
        /// so this under-reports the very hardest hits — the same limitation the wingsuit avoids
        /// by keeping its own state, and the reason the safe threshold is generous.
        /// </para>
        /// </summary>
        private void Land(Vector3 surfaceNormal, bool wasImpact)
        {
            float closing = OrnithopterCrash.ClosingSpeed(body.linearVelocity, surfaceNormal);
            int damage = OrnithopterCrash.ImpactDamage(closing, landing);

            End();

            // Through NetDamage so the server owns the result, exactly as the player's own fall
            // damage does. Applied after the flight has ended so a fatal arrival leaves the body
            // where it hit rather than in mid-air.
            if (damage > 0) NetDamage.Apply(gameObject, damage, transform);

            Landed?.Invoke(closing, wasImpact);
        }
    }
}
