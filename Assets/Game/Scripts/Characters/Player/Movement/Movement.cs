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
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.3f;

        [Header("Jumping")]
        [SerializeField] private float jumpForce = 7f;
        [SerializeField] private float jumpCooldown = 0.6f;
        [SerializeField] private float groundCheckDistance = 0.2f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Dash")]
        [SerializeField] private float dashSpeed = 10f;
        [SerializeField] private GameObject playerCamera;

        [SerializeField] private Rigidbody rb;
        [SerializeField] private Animator animator;
        [SerializeField] private CapsuleCollider playerCollider;
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
        ///     seat. Phrased as "has a parent" rather than a MountModule lookup, the same way
        ///     <c>UnderTerrainGuard.Evaluate</c> decides the same question, so any future carrier is
        ///     covered without being named.
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
            if (!Network.Owns(this)) return;

            Body.isKinematic = false;

            if (warnedAboutKinematicBody) return;
            warnedAboutKinematicBody = true;

            Debug.LogWarning(
                $"[PlayerMovement] {name} was driving a kinematic body, so nothing it was told to " +
                "do could move it. Released it. Something handed this player a body it does not " +
                "own — check whatever last touched isKinematic.", this);
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

            HandleFallDamage(grounded);

            Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
            move = Vector3.ClampMagnitude(move, 1f);
            Vector3 desiredHorizontal = move * moveSpeed;

            Vector3 velocity = rb.linearVelocity;
            Vector3 currentHorizontal = new Vector3(velocity.x, 0f, velocity.z);
        
            float control = grounded ? 1f : airControl;
            Vector3 newHorizontal = Vector3.Lerp(currentHorizontal, desiredHorizontal, control);
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
        /// While momentum is being carried, air control may TURN the flight but
        /// never slow it.
        ///
        /// Preserving the magnitude rather than skipping the lerp is what keeps
        /// the player steerable in mid-air, which is the half of air control
        /// that was always wanted. It ends by itself: on touchdown, because the
        /// ground is where speed is supposed to be given back, and at walking
        /// pace, because below that there is no fling left to protect.
        /// </summary>
        private Vector3 SteerWithoutBraking(Vector3 current, Vector3 steered, bool grounded)
        {
            if (!carryingMomentum) return steered;

            float carried = current.magnitude;
            if (grounded || carried <= moveSpeed)
            {
                carryingMomentum = false;
                return steered;
            }

            return steered.sqrMagnitude > 1e-6f ? steered.normalized * carried : current;
        }

        private void HandleFallDamage(bool grounded)
        {
            // Detect landing (was in air, now grounded)
            if (!wasGrounded && grounded)
            {
                // Fired for every landing, including harmless ones — audio wants the soft touchdowns
                // too, and the impact speed lets a listener pick between a step and a thud.
                OnLanded?.Invoke(lastYVelocity);

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

            animator.SetFloat("SpeedX", localVelocity.x, .1f, Time.deltaTime);
            animator.SetFloat("SpeedY", localVelocity.z, .1f, Time.deltaTime);
            animator.SetFloat("FallSpeed", velocity.y, .1f, Time.deltaTime);
            animator.SetBool("IsGrounded", grounded);
            animator.SetBool("IsImmobalized", !groundSnapEnabled);
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
