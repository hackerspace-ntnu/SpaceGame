using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMovement : MonoBehaviour
    {
        private PlayerInputManager inputs; 
    
        [Header("Movement")]
        [Tooltip("Ordinary walking speed, and the one every other stance is measured against.")]
        [SerializeField] private float moveSpeed = 6f;

        [Tooltip("Speed while sprinting — double-tap forward and hold. See PlayerStance.")]
        [SerializeField] private float sprintSpeed = 9f;

        [Tooltip("Speed while crouched.")]
        [SerializeField] private float crouchSpeed = 2.6f;

        [Tooltip("Speed while aiming. Below crouch speed reads as sluggish; above walk speed " +
                 "makes aiming free.")]
        [SerializeField] private float aimSpeed = 3.5f;

        [SerializeField, Range(0f, 1f)] private float airControl = 0.3f;

        [Tooltip("Upward speed, in m/s, above which a carried fling still counts as in flight — " +
                 "see SteerWithoutBraking. Big enough to ignore the jitter of standing on a " +
                 "collider, small enough that any real launch clears it.")]
        [SerializeField] private float momentumRiseThreshold = 0.5f;

        [Tooltip("Sideways acceleration available while hanging from a rope, in m/s². Steering " +
                 "only — see SteerTether. It can turn a swing and pump it, never slow one.")]
        [SerializeField] private float tetherAcceleration = 22f;

        [Header("Jumping")]
        [SerializeField] private float jumpForce = 7f;
        [SerializeField] private float jumpCooldown = 0.6f;
        [SerializeField] private float groundCheckDistance = 0.2f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Animation matching")]
        [Tooltip("Ground speed the Move tree's run clip was authored to travel at. Above this " +
                 "the whole cycle is played proportionally faster, so a sprint puts down more " +
                 "steps instead of skating on the same ones.")]
        [SerializeField] private float runClipSpeed = 7.2f;

        [Tooltip("The same figure for the Crouch tree's walk clip.")]
        [SerializeField] private float crouchClipSpeed = 1.6f;

        [Header("Dash")]
        [SerializeField] private float dashSpeed = 10f;
        [SerializeField] private GameObject playerCamera;

        [SerializeField] private Rigidbody rb;
        [SerializeField] private Animator animator;
        [SerializeField] private CapsuleCollider playerCollider;
        private PlayerStance stance;
        private PlayerAimRig aimRig;
        private Vector2 moveInput;
        private float jumpCooldownTimer;
        private bool jumpOnCooldown;
        private bool groundSnapEnabled = true;
    
        [Header("Fall Damage")]
        [SerializeField] private float minFallSpeed = -5f;
        [SerializeField] private float maxFallSpeed = -30f;
        [SerializeField] private int maxFallDamage = 100;

        private float lastYVelocity;
        private bool wasGrounded;

        // Movement already computes the exact edges audio needs — the grounded transition, the
        // successful jump, the dash — and all of it from private state. Exposing them as events is
        // cheaper and far less brittle than a second component re-deriving grounded-ness with its
        // own raycast, which would drift from this one the moment either is tuned.
        /// <summary>Raised when a jump actually leaves the ground, not merely when the key is pressed.</summary>
        public event Action OnJumped;

        /// <summary>Raised on touchdown, carrying the vertical speed at impact (negative when falling).</summary>
        public event Action<float> OnLanded;

        /// <summary>Raised on a dash that was allowed to happen.</summary>
        public event Action OnDashed;

        /// <summary>Horizontal speed in m/s. Used to pace footsteps.</summary>
        public float HorizontalSpeed
        {
            get
            {
                if (rb == null) return 0f;
                Vector3 v = rb.linearVelocity;
                return new Vector3(v.x, 0f, v.z).magnitude;
            }
        }

        /// <summary>Whether the player was on the ground as of the last physics step.</summary>
        public bool IsOnGround => wasGrounded;

        /// <summary>
        /// The body, resolved on demand. <see cref="rb"/> is the authored reference; this is here so
        /// <see cref="EnsureMovableBody"/> can be called on a bare GameObject — a test, a
        /// script-built player — without the serialized field having been filled in.
        /// </summary>
        private Rigidbody Body => rb != null ? rb : rb = GetComponent<Rigidbody>();

        /// <summary>Logged once per player, not once per physics step. See EnsureMovableBody.</summary>
        private bool warnedAboutKinematicBody;

        /// <summary>
        /// Insists that a player who is meant to be walking has a body physics can move.
        ///
        /// A kinematic Rigidbody silently discards every <c>linearVelocity</c> write, so a player in
        /// that state stands still while everything upstream looks perfect: input arrives, this
        /// component runs to the end of FixedUpdate, the animator plays a walk. Only the body is
        /// missing from the conversation. That is not a state worth being tolerant of — there is no
        /// reading of it in which the player is having a good time — so it is corrected here rather
        /// than diagnosed later.
        ///
        /// Two things are allowed to hold the body and are left alone.
        ///
        ///   * A rider being carried. <c>MountModule</c> makes the body kinematic on purpose and
        ///     parents the player into the mount, and freeing it would drop them through their own
        ///     seat. Asked of <c>CarriedBody</c>, which every carrier registers with, plus the older
        ///     "has a parent" test for anything that carries by parenting without saying so.
        ///     <para>
        ///     The parent test alone was not enough, and the arrival is why. A player strapped into
        ///     a ship's seat for the crash landing is NOT parented — the player's NetworkTransform is
        ///     owner-authoritative and world-space, so a rider is carried by having their pose
        ///     written every frame — and is therefore kinematic with no parent, which is precisely
        ///     the shape this method exists to break. It duly broke it, every physics step, for the
        ///     whole descent.
        ///     </para>
        ///   * Somebody else's player. Netcode keeps a remote body kinematic deliberately, and this
        ///     component is disabled on those anyway — the ownership test is what makes that a rule
        ///     rather than a coincidence.
        ///
        /// It warns the first time it fires, because a body that reaches this state has come from a
        /// bug somewhere else and a silent repair would hide it. <c>RigidbodySaveable</c> was that
        /// bug once; the warning is what makes the next one findable.
        /// </summary>
        public void EnsureMovableBody()
        {
            if (Body == null || !Body.isKinematic) return;
            if (transform.parent != null) return;
            if (SpaceGame.Agents.CarriedBody.IsHeld(gameObject)) return;
            if (!Network.Owns(this)) return;

            Body.isKinematic = false;

            if (warnedAboutKinematicBody) return;
            warnedAboutKinematicBody = true;

            Debug.LogWarning(
                $"[PlayerMovement] {name} was driving a kinematic body, so nothing it was told to " +
                "do could move it. Released it. Something handed this player a body it does not " +
                "own — check whatever last touched isKinematic.", this);
        }

        /// <summary>
        /// How fast the player may travel right now.
        ///
        /// <para>
        /// The stance is asked rather than tracked, so there is exactly one component that decides
        /// whether the player is crouched — and it is the one that also shortened the capsule and
        /// dropped the camera. A player with no PlayerStance on them simply walks, which is what
        /// every caller wants from a body that has no stance to be in.
        /// </para>
        /// </summary>
        private float CurrentMoveSpeed
        {
            get
            {
                // Crouching outranks aiming: a crouched player is already slow, and testing it
                // first means the order of these branches stops being something anyone has to
                // think about.
                if (stance != null && stance.IsCrouching) return crouchSpeed;
                if (aimRig != null && aimRig.IsAiming) return Mathf.Min(aimSpeed, moveSpeed);
                if (stance == null) return moveSpeed;
                return stance.IsSprinting ? sprintSpeed : moveSpeed;
            }
        }

        private void Awake()
        {
            stance = GetComponent<PlayerStance>();
            aimRig = GetComponent<PlayerAimRig>();
        }

        private void Start()
        {
            inputs = GetComponent<PlayerController>().Input;
            inputs.OnJumpPressed += OnJump;
            inputs.OnDashPressed += OnDash;

            var health = GetComponent<HealthComponent>();
            if (health != null)
            {
                health.OnDamage += _ => TriggerAnimator("Hurt");
                health.OnDeath += () => TriggerAnimator("Die");
            }
        }

        private void FixedUpdate()
        {
            // Before anything is computed, because everything below is written into this body and a
            // kinematic one throws all of it away without complaining.
            EnsureMovableBody();

            moveInput = inputs.MoveInput;
            HandleJumpCooldown();

            if (!groundSnapEnabled)
            {
                return;
            }
        
            bool grounded = IsGrounded();

            // Deliberately skipped while on a rope. A swing on a 20 m tether passes the bottom of
            // its arc at around 19 m/s downward under this project's -18 gravity, which the fall
            // table prices at over half the player's health — so a grapple used to survive a drop
            // would bill them for the swing that saved them. The edge is still consumed: wasGrounded
            // is written below either way, so releasing over ground does not then fire a phantom
            // landing for a fall that already finished.
            if (!tethered) HandleFallDamage(grounded);

            Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
            move = Vector3.ClampMagnitude(move, 1f);
            Vector3 desiredHorizontal = move * CurrentMoveSpeed;

            Vector3 velocity = rb.linearVelocity;
            Vector3 currentHorizontal = new Vector3(velocity.x, 0f, velocity.z);

            Vector3 newHorizontal;
            if (tethered)
            {
                newHorizontal = SteerTether(currentHorizontal, move);
            }
            else
            {
                float control = grounded ? 1f : airControl;
                newHorizontal = Vector3.Lerp(currentHorizontal, desiredHorizontal, control);
            }
            newHorizontal = SteerWithoutBraking(currentHorizontal, newHorizontal, grounded);

            velocity.x = newHorizontal.x;
            velocity.z = newHorizontal.z;
            rb.linearVelocity = velocity;
        
            lastYVelocity = rb.linearVelocity.y;
            wasGrounded = grounded;

            UpdateAnimatorParameters(velocity, grounded);
        }
    
        /// <summary>
        /// Momentum this component did not produce and must not throw away.
        ///
        /// Set by <see cref="CarryMomentum"/> and cleared the moment the player
        /// lands or slows to a walk, so it is off for all of ordinary movement.
        /// "Lands" is stricter than the ground probe — see <see cref="SteerWithoutBraking"/>.
        /// </summary>
        private bool carryingMomentum;

        /// <summary>
        /// Keep whatever horizontal speed the body has until it lands.
        ///
        /// Called by anything that flings the player faster than they can run —
        /// today that is coming out of a portal. Without it the aim of
        /// "speedy thing goes in, speedy thing comes out" cannot survive
        /// <see cref="FixedUpdate"/>: the lerp below pulls horizontal velocity
        /// 30% of the way toward a 6 m/s walk fifty times a second, which turns
        /// a 40 m/s exit into a stroll in about a fifth of a second. The player
        /// sees the fling start and then be visibly confiscated.
        /// </summary>
        public void CarryMomentum() => carryingMomentum = true;

        /// <summary>
        /// True while something else is doing the moving on a rope — today the grappling hook.
        /// Unlike <see cref="carryingMomentum"/> this never expires on its own: the thing holding
        /// the rope is the only one that knows when it let go.
        /// </summary>
        private bool tethered;

        /// <summary>
        /// Hand the body over to a rope, or take it back.
        ///
        /// <para>
        /// This replaces what the grappling hook used to do, which was call
        /// <see cref="DisableGroundSnap"/> for 999 seconds. That name undersells it — a disabled
        /// ground snap makes <see cref="FixedUpdate"/> return before it does anything at all, so a
        /// grappling player had no steering, no animator updates and no grounded state for the whole
        /// swing. It also began at the press rather than at the hit, which is why firing the hook
        /// felt like being dragged before it had caught anything: control was gone the moment the
        /// trigger came down, while the rope was still in the air.
        /// </para>
        /// <para>
        /// A tether keeps every one of those running and changes only how the move input is applied.
        /// The caller MUST clear it — see the grappling hook's StopGrapple, which is reached from
        /// its release, its arrival, and its teardown alike.
        /// </para>
        /// </summary>
        public void SetTethered(bool value) => tethered = value;

        /// <summary>Whether a rope currently owns this body's horizontal motion.</summary>
        public bool IsTethered => tethered;

        /// <summary>
        /// True while the player is riding something sprung — today the jumping rod.
        ///
        /// <para>
        /// Deliberately much narrower than <see cref="tethered"/>, and the narrowness is the point.
        /// A tether takes over horizontal motion; this changes nothing about how the player moves.
        /// It suppresses <b>fall damage only</b>, because the whole business of a pogo stick is
        /// arriving hard and leaving harder: at this project's -18 gravity a three-metre hop lands
        /// at about -11 m/s, which the fall table prices at a fifth of the player's health — so a
        /// rod that bounced you well would kill you in five bounces.
        /// </para>
        /// <para>
        /// The rod is left to write <c>linearVelocity.y</c> directly rather than being given a
        /// method here, because <see cref="FixedUpdate"/> only ever writes x and z: the vertical
        /// axis is already free for anything that wants it, and a second jump API would be a second
        /// thing to keep in step with this one.
        /// </para>
        /// </summary>
        public void SetBouncing(bool value) => bouncing = value;

        /// <summary>Whether something sprung is absorbing this player's landings.</summary>
        public bool IsBouncing => bouncing;

        /// <summary>See <see cref="SetBouncing"/>. Owner-side only; nothing replicates it.</summary>
        private bool bouncing;

        /// <summary>
        /// Air steering for a player hanging on a rope.
        ///
        /// The ordinary air lerp cannot be used here, for the same reason
        /// <see cref="CarryMomentum"/> had to exist: it pulls horizontal velocity 30% of the way
        /// toward a 6 m/s walk fifty times a second, so a 25 m/s swing is confiscated in about a
        /// fifth of a second and the pendulum dies before it completes one pass.
        ///
        /// So this pushes instead of blending toward a target. The player can turn the arc and pump
        /// it, and nothing they press can brake it. The ceiling is whichever is greater of the speed
        /// they already had and a walk — steering can never itself become a source of speed, and a
        /// slow hang near the anchor is still nudgeable at walking pace.
        ///
        /// <para>
        /// Used whether or not the player is on the ground. Excluding the grounded case was the
        /// obvious-looking call — on your feet you should walk normally — and it was wrong. On the
        /// ground the ordinary branch runs at <c>control = 1</c>, which sets horizontal velocity
        /// straight to the input target, and with no input that target is ZERO. So a winch pulling
        /// toward anything near horizontal had its entire effect deleted fifty times a second while
        /// the player stood there; and because the distance to the anchor then never changed, the
        /// hook's own stall guard dropped the rope a moment later. Standing on the ground was a hard
        /// counter to the grappling hook.
        /// </para>
        /// <para>
        /// Nothing is given up by including it: with no move input this returns the current velocity
        /// unchanged, so ground friction, gravity and the rope all still do exactly what they did.
        /// </para>
        /// </summary>
        private Vector3 SteerTether(Vector3 current, Vector3 move)
        {
            Vector3 steered = current + move * (tetherAcceleration * Time.fixedDeltaTime);

            float ceiling = Mathf.Max(current.magnitude, CurrentMoveSpeed);
            return steered.magnitude > ceiling ? steered.normalized * ceiling : steered;
        }

        /// <summary>
        /// While momentum is being carried, air control may TURN the flight but
        /// never slow it.
        ///
        /// Preserving the magnitude rather than skipping the lerp is what keeps
        /// the player steerable in mid-air, which is the half of air control
        /// that was always wanted. It ends by itself: on touchdown, because the
        /// ground is where speed is supposed to be given back, and at walking
        /// pace, because below that there is no fling left to protect.
        ///
        /// <para>
        /// "On touchdown" cannot be read off <see cref="IsGrounded"/> alone, which is why
        /// <paramref name="grounded"/> is qualified by whether the body is still RISING. That probe
        /// sphere-casts a 0.45 m sphere from the capsule's centre over the full half-height plus
        /// the ground check distance, so with the authored capsule it keeps answering "grounded"
        /// for roughly the first 0.6 m of clearance. A fling leaves at up to ~10 m/s of vertical,
        /// which is 0.2 m of rise per physics step — so the launch is still "grounded" for the
        /// next several ticks, and the unqualified clause cleared the latch on the very first one.
        /// The horizontal half was then handed to the ordinary <c>control = 1</c> lerp, whose
        /// target with no input is ZERO: a standing victim popped straight up and landed on the
        /// spot, and the gauntlet's own recoil died the same way.
        /// </para>
        /// <para>
        /// Rising is not an escape hatch. Gravity spends the launch in well under a second, after
        /// which a grounded body clears the latch exactly as before; and the walking-pace clause
        /// is untouched, so it still ends a carry that has nothing left to protect.
        /// </para>
        /// </summary>
        private Vector3 SteerWithoutBraking(Vector3 current, Vector3 steered, bool grounded)
        {
            if (!carryingMomentum) return steered;

            float carried = current.magnitude;
            bool rising = rb != null && rb.linearVelocity.y > momentumRiseThreshold;
            if (ShouldEndCarry(grounded, rising, carried, CurrentMoveSpeed))
            {
                carryingMomentum = false;
                return steered;
            }

            return steered.sqrMagnitude > 1e-6f ? steered.normalized * carried : current;
        }

        /// <summary>
        /// Whether a carried fling is finished — the decision <see cref="SteerWithoutBraking"/>
        /// makes, pulled out as pure arithmetic so it can be pinned by a test without a physics
        /// scene, and so the reasoning above lives next to something checkable.
        ///
        /// <para>
        /// A body that is still rising has not landed, whatever the ground probe says; and a body
        /// down to walking pace has no fling left to protect, whether or not it is in the air.
        /// </para>
        /// </summary>
        public static bool ShouldEndCarry(bool grounded, bool rising, float carriedSpeed, float moveSpeed)
            => (grounded && !rising) || carriedSpeed <= moveSpeed;

        private void HandleFallDamage(bool grounded)
        {
            // Detect landing (was in air, now grounded)
            if (!wasGrounded && grounded)
            {
                // Fired for every landing, including harmless ones — audio wants the soft touchdowns
                // too, and the impact speed lets a listener pick between a step and a thud.
                OnLanded?.Invoke(lastYVelocity);

                // A sprung landing costs nothing. The event above still fires — the landing did
                // happen and audio still wants it — but the arrival was absorbed by something the
                // player is deliberately standing on rather than by their legs.
                if (bouncing) return;

                // Only apply if falling fast enough
                if (lastYVelocity < minFallSpeed)
                {
                    float t = Mathf.InverseLerp(minFallSpeed, maxFallSpeed, lastYVelocity);
                    int damage = Mathf.RoundToInt(t * maxFallDamage);

                    ApplyFallDamage(damage);
                }
            }
        }
    
        private void ApplyFallDamage(int damage)
        {
            var health = GetComponent<HealthComponent>();
            if (health)
            {
                // Only the owner measures its own fall, but the server owns the health that
                // results — otherwise a client's landing hurts nobody but their own screen.
                NetDamage.Apply(health.gameObject, damage);
            }
        }

        private void UpdateAnimatorParameters(Vector3 velocity, bool grounded)
        {
            if (!animator || animator.runtimeAnimatorController == null) return;

            Vector3 localVelocity = transform.worldToLocalMatrix.MultiplyVector(velocity);
            bool crouching = stance != null && stance.IsCrouching;

            // SpeedX/SpeedY feed two blend trees that were authored in different units. The
            // standing Move tree places its clips at the ground speed each one travels at — walk
            // at 4, run at 7.2 — so it wants metres per second. The Crouch tree places its four
            // clips on a unit square, so it wants a direction. Handing both the same number is
            // what pins the crouch blend to full stride the instant the player nudges the stick.
            float blendScale = crouching ? 1f / Mathf.Max(0.01f, crouchSpeed) : 1f;

            animator.SetFloat("SpeedX", localVelocity.x * blendScale, .1f, Time.deltaTime);
            animator.SetFloat("SpeedY", localVelocity.z * blendScale, .1f, Time.deltaTime);
            animator.SetFloat("FallSpeed", velocity.y, .1f, Time.deltaTime);
            animator.SetFloat("MoveAnimSpeed", StrideRate(localVelocity, crouching));
            animator.SetBool("IsGrounded", grounded);
            animator.SetBool("IsImmobalized", !groundSnapEnabled);
        }

        /// <summary>
        /// How fast to play the walk cycle so the feet keep up with the ground.
        ///
        /// <para>
        /// A blend tree picks WHICH clip plays, never how fast; the clip runs at the pace it was
        /// authored at whatever the body is doing. So the tree's fastest anchor is also the fastest
        /// the legs can honestly go, and a sprint past it is a run animation sliding along the
        /// floor. Above that anchor the state's whole playback rate is scaled by however far past
        /// it the player is, which is the only field that actually changes stride length.
        /// </para>
        /// <para>
        /// Below the anchor it returns exactly 1, so ordinary walking is untouched — and so is the
        /// idle at the centre of the tree, which a rate derived from speed would otherwise freeze
        /// solid the moment the player stood still.
        /// </para>
        /// </summary>
        private float StrideRate(Vector3 localVelocity, bool crouching)
        {
            float clipSpeed = crouching ? crouchClipSpeed : runClipSpeed;
            if (clipSpeed <= 0.01f) return 1f;

            float planar = new Vector2(localVelocity.x, localVelocity.z).magnitude;
            return planar <= clipSpeed ? 1f : planar / clipSpeed;
        }

        private void TriggerAnimator(string triggerName)
        {
            if (animator && animator.runtimeAnimatorController != null)
                animator.SetTrigger(triggerName);
        }

        public void ForceIdleAnimation()
        {
            if (!animator)
            {
                return;
            }

            animator.SetFloat("SpeedX", 0f);
            animator.SetFloat("SpeedY", 0f);
            animator.SetFloat("FallSpeed", 0f);
            animator.SetFloat("MoveAnimSpeed", 1f);
            animator.SetBool("IsGrounded", IsGrounded());
            animator.SetBool("IsImmobalized", true);
        }

        public void OnJump()
        {
            if (rb == null || !isActiveAndEnabled || rb.isKinematic)
            {
                return;
            }

            if (IsGrounded() && !jumpOnCooldown)
            {
                Vector3 v = rb.linearVelocity;
                if (v.y > 0f) v.y = 0f;
                rb.linearVelocity = v;
                rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
                jumpOnCooldown = true;

                OnJumped?.Invoke();
            }
        }

        public void OnDash()
        {
            if (rb == null || !isActiveAndEnabled || rb.isKinematic)
            {
                return;
            }

            Vector3 dashDirection = transform.forward;
            if (playerCamera)
            {
                dashDirection = playerCamera.transform.forward;
            }

            dashDirection.y = 0f;
            dashDirection.Normalize();

            Vector3 velocity = rb.linearVelocity;
            velocity = dashDirection * dashSpeed + Vector3.up * velocity.y;
            rb.linearVelocity = velocity;

            OnDashed?.Invoke();
        }

        public void DisableGroundSnap(float duration = 0.2f)
        {
            groundSnapEnabled = false;
            CancelInvoke(nameof(EnableGroundSnap));
            Invoke(nameof(EnableGroundSnap), duration);
        }

        private void EnableGroundSnap()
        {
            groundSnapEnabled = true;
        }

        private bool IsGrounded()
        {
            CapsuleCollider colliderToUse = playerCollider != null ? playerCollider : GetComponentInChildren<CapsuleCollider>();
            if (colliderToUse == null)
            {
                Vector3 rayOrigin = transform.position;
                return Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);
            }

            Bounds bounds = colliderToUse.bounds;
            float radius = Mathf.Max(0.05f, bounds.extents.x * 0.9f);
            Vector3 origin = bounds.center + Vector3.up * 0.05f;
            float distance = bounds.extents.y + groundCheckDistance;

            return Physics.SphereCast(origin, radius, Vector3.down, out _, distance, groundMask, QueryTriggerInteraction.Ignore);
        }

        private void HandleJumpCooldown()
        {
            if (!jumpOnCooldown) return;

            jumpCooldownTimer += Time.deltaTime;
            if (jumpCooldownTimer >= jumpCooldown)
            {
                jumpOnCooldown = false;
                jumpCooldownTimer = 0f;
            }
        }
    }
}
