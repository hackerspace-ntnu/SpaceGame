// Whether the player is crouched, sprinting, or just walking — and everything that has to change
// shape when they are.
//
// This is deliberately NOT part of PlayerMovement, for one reason: PlayerController.DisablePlayer
// switches PlayerMovement off on every remote copy, and a crouching player has to be crouched on
// everybody's screen, not only their own. Their capsule has to come down with them too, or a shot
// aimed at a crouched head passes through a collider that is still standing up.
//
// So stance runs on every machine. On the owner it is decided from input; everywhere else it is
// read back out of the Animator, where ClientNetworkAnimator has already replicated it from the
// owner. One value, one direction of travel, and no second network variable to keep in step with
// the animation.
using UnityEngine;
using SpaceGame.Core;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    [DisallowMultipleComponent]
    public class PlayerStance : MonoBehaviour
    {
        /// <summary>The bool the Crouch Tree is entered on, and the channel it replicates over.</summary>
        private const string CrouchParameter = "IsCrouching";

        /// <summary>Colliders hit by the stand-up test. Reused; see <see cref="HasHeadroom"/>.</summary>
        private static readonly RaycastHit[] HeadroomHits = new RaycastHit[8];

        [Header("References")]
        [Tooltip("The capsule that gets shorter. Leave empty to find one in the children.")]
        [SerializeField] private CapsuleCollider playerCollider;

        [Tooltip("The eye — the camera transform. Its local height is what drops when crouching.")]
        [SerializeField] private Transform eye;

        [SerializeField] private Animator animator;
        [SerializeField] private PlayerMovement movement;

        [Header("Crouch")]
        [Tooltip("Crouched capsule height as a fraction of the standing one. Cannot go below " +
                 "twice the radius — a capsule shorter than its own caps is not a shape.")]
        [SerializeField, Range(0.35f, 0.95f)] private float crouchHeightFraction = 0.6f;

        [Tooltip("How far the eye drops, in metres. Should roughly match how far the top of the " +
                 "capsule comes down, or the view and the body disagree about how low you are.")]
        [SerializeField] private float crouchEyeDrop = 0.6f;

        [Tooltip("Seconds to go all the way down or all the way up. 0 snaps.")]
        [SerializeField] private float stanceBlendTime = 0.12f;

        [Tooltip("What counts as a ceiling when deciding whether standing up is allowed.")]
        [SerializeField] private LayerMask headroomMask = ~0;

        [Header("Sprint")]
        [Tooltip("Seconds allowed between the two forward taps. Longer is more forgiving and " +
                 "more likely to sprint when nobody asked.")]
        [SerializeField] private float doubleTapWindow = 0.3f;

        [Tooltip("How far forward the stick has to be pushed to count as a tap. Keyboard is " +
                 "always 1; this is for analogue sticks.")]
        [SerializeField, Range(0.1f, 1f)] private float forwardTapThreshold = 0.5f;

        [Tooltip("Seconds of sprinting a full tank is worth.")]
        [SerializeField] private float sprintDuration = 10f;

        [Tooltip("Seconds to refill an empty tank. A partly spent one comes back proportionally " +
                 "sooner — this is the worst case, not a fixed wait.")]
        [SerializeField] private float sprintRecharge = 5f;

        private PlayerInputManager inputs;
        private PlayerController controller;

        private bool isCrouching;
        private bool isSprinting;

        // Sprint is a double tap, so the edge matters, not the level.
        private bool forwardWasHeld;
        private float lastForwardPressTime = float.NegativeInfinity;

        // What is left in the tank, 0 to 1, and whether it ran dry. Kept as a fraction rather than
        // seconds so retuning either duration in the inspector does not invalidate a tank that is
        // already half spent.
        private float sprintCharge = 1f;
        private bool winded;

        // 0 standing, 1 fully crouched, and what was last written out of it. Nothing is written
        // while those two agree, so a standing player's camera is left alone — mounting and
        // cutscenes drive that same transform, and a stance that re-asserted an eye height every
        // frame would quietly fight them.
        private float stanceT;
        private float appliedT = float.NaN;

        private float standingHeight;
        private float standingCenterY;
        private float standingEyeY;

        /// <summary>Crouched right now, on any machine — the owner's decision or their replica of it.</summary>
        public bool IsCrouching => isCrouching;

        /// <summary>Sprinting right now. Only ever true on the machine that owns this player.</summary>
        public bool IsSprinting => isSprinting;

        /// <summary>
        /// How much sprint is left, 1 full and 0 empty. Here for a HUD to draw — nothing reads it
        /// yet, and a sprint that stops with no warning is the obvious thing to fix next.
        /// </summary>
        public float SprintCharge => sprintCharge;

        /// <summary>
        /// Out of breath: the tank ran dry, and sprinting is refused until it is full again.
        /// </summary>
        public bool Winded => winded;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();

            if (movement == null) movement = GetComponent<PlayerMovement>();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (playerCollider == null) playerCollider = GetComponentInChildren<CapsuleCollider>(true);

            // Included-inactive is the point: on a remote copy PlayerController.Awake has already
            // switched the camera GameObject off, and its transform is still the eye that
            // PlayerViewNetwork hangs the aim pivot on.
            if (eye == null && controller != null) eye = controller.PlayerCameraTransform;

            if (playerCollider != null)
            {
                standingHeight = playerCollider.height;
                standingCenterY = playerCollider.center.y;
            }

            if (eye != null) standingEyeY = eye.localPosition.y;
        }

        private void Update()
        {
            if (Network.Owns(this))
            {
                DecideStance();
            }
            else
            {
                isCrouching = animator != null
                              && animator.runtimeAnimatorController != null
                              && animator.GetBool(CrouchParameter);
            }

            ApplyStance(Time.deltaTime);
        }

        /// <summary>
        /// Owner side: what does the player want to be doing, and are they allowed to?
        /// </summary>
        private void DecideStance()
        {
            if (inputs == null && controller != null) inputs = controller.Input;

            // Death, a mount, a cutscene and a menu all express themselves the same way — the
            // components that drive the body are switched off. A player who is not walking on
            // their own legs has no stance to hold, and standing them up here is what hands the
            // camera back to whatever took over.
            bool driving = inputs != null && inputs.enabled && movement != null && movement.enabled;

            // Crouching is a ground stance. The Crouch Tree is only allowed to hand back to the
            // Move Tree once IsGrounded is true, so a player who left the ground crouched would
            // stay folded up until they landed. Standing them up at take-off keeps the animator
            // and the collider telling the same story.
            bool wantsCrouch = driving && inputs.CrouchHeld && movement.IsOnGround;

            // A ceiling outranks letting go of the key. Checked only when already down, because
            // that is the only direction the test can refuse.
            if (!wantsCrouch && isCrouching && driving && !HasHeadroom()) wantsCrouch = true;

            isCrouching = wantsCrouch;

            UpdateSprint(driving);

            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetBool(CrouchParameter, isCrouching);
        }

        /// <summary>
        /// Sprint is a double tap of forward, held: tap, then tap and keep holding, and you run
        /// until you let go.
        ///
        /// <para>
        /// Read off <see cref="PlayerInputManager.MoveInput"/> rather than given a binding of its
        /// own, because "forward" is a composite of four keys on the keyboard and a stick
        /// elsewhere, and the gesture is about that resolved axis — not about W. It also means the
        /// same two taps work on a gamepad with nothing extra to bind.
        /// </para>
        /// </summary>
        private void UpdateSprint(bool driving)
        {
            if (!driving)
            {
                isSprinting = false;
                forwardWasHeld = false;

                // Still recovering. A player who is dead, mounted or reading a menu is not
                // sprinting, and holding their breath for them would only mean arriving back on
                // their feet with an empty tank they never spent.
                Recover(Time.deltaTime);
                return;
            }

            bool forwardHeld = inputs.MoveInput.y >= forwardTapThreshold;

            if (forwardHeld && !forwardWasHeld)
            {
                if (Time.time - lastForwardPressTime <= doubleTapWindow) isSprinting = true;
                lastForwardPressTime = Time.time;
            }

            forwardWasHeld = forwardHeld;

            // It ends by itself. Letting go of forward is the obvious way out, crouching is the
            // second — a sprint that survived going low would outrun both the crouch speed and the
            // crouch animation — and being out of breath is the third.
            if (!forwardHeld || isCrouching || winded) isSprinting = false;

            if (isSprinting) Spend(Time.deltaTime);
            else Recover(Time.deltaTime);
        }

        /// <summary>
        /// Burn a frame's worth of sprint, and cut it off when there is none left.
        /// </summary>
        private void Spend(float deltaTime)
        {
            sprintCharge = sprintDuration <= 0f
                ? 0f
                : sprintCharge - deltaTime / sprintDuration;

            if (sprintCharge > 0f) return;

            sprintCharge = 0f;
            isSprinting = false;
            winded = true;
        }

        /// <summary>
        /// Refill, and decide when the player has their breath back.
        ///
        /// <para>
        /// Being winded is cleared only once the tank is FULL, not the instant a sliver returns.
        /// Otherwise an exhausted player who keeps forward held sprints again on the very next
        /// frame, spends that sliver, and stalls — a stutter that costs them their speed several
        /// times a second and reads as the sprint being broken rather than spent.
        /// </para>
        /// <para>
        /// Stopping voluntarily has no such penalty: end a sprint with half a tank left and the
        /// next one is available immediately, with half a tank in it.
        /// </para>
        /// </summary>
        private void Recover(float deltaTime)
        {
            if (sprintCharge >= 1f)
            {
                winded = false;
                return;
            }

            sprintCharge = sprintRecharge <= 0f
                ? 1f
                : Mathf.Min(1f, sprintCharge + deltaTime / sprintRecharge);

            if (sprintCharge >= 1f) winded = false;
        }

        /// <summary>
        /// Ease the body and the view towards the stance, and write neither once they agree.
        /// </summary>
        private void ApplyStance(float deltaTime)
        {
            float target = isCrouching ? 1f : 0f;

            stanceT = stanceBlendTime <= 0f
                ? target
                : Mathf.MoveTowards(stanceT, target, deltaTime / stanceBlendTime);

            if (stanceT == appliedT) return;
            appliedT = stanceT;

            if (playerCollider != null)
            {
                float height = Mathf.Lerp(standingHeight, CrouchedHeight, stanceT);

                // The feet stay put; only the top comes down. Moving the centre by half of what
                // the height lost is what keeps the bottom cap on the floor — without it a crouch
                // sinks the player into the ground and standing up shoves them out of it.
                Vector3 center = playerCollider.center;
                center.y = standingCenterY - (standingHeight - height) * 0.5f;

                playerCollider.height = height;
                playerCollider.center = center;
            }

            if (eye != null)
            {
                Vector3 local = eye.localPosition;
                local.y = standingEyeY - crouchEyeDrop * stanceT;
                eye.localPosition = local;
            }
        }

        /// <summary>
        /// The shortest the capsule is allowed to be. A CapsuleCollider silently clamps its own
        /// height to twice its radius, so asking for less would leave the collider at one height
        /// and this component's arithmetic at another — and the feet would drift.
        /// </summary>
        private float CrouchedHeight =>
            playerCollider == null
                ? standingHeight
                : Mathf.Max(standingHeight * crouchHeightFraction, playerCollider.radius * 2f);

        /// <summary>
        /// Is there room overhead to stand back up?
        ///
        /// <para>
        /// Swept from the top of the crouched capsule to where the top of the standing one would
        /// be, so it asks about exactly the space standing up would need and nothing else. The
        /// player's own colliders are skipped rather than masked out: they sit on the same layer
        /// as plenty of things the mask is for, and a mask that excluded them would also excuse a
        /// real ceiling.
        /// </para>
        /// </summary>
        private bool HasHeadroom()
        {
            if (playerCollider == null) return true;

            Transform space = playerCollider.transform;
            Vector3 scale = space.lossyScale;

            float crouchedHeight = CrouchedHeight;
            float gap = (standingHeight - crouchedHeight) * Mathf.Abs(scale.y);
            if (gap <= 0.001f) return true;

            // Centre of the crouched capsule's upper cap, in the collider's own space.
            float crouchedCenterY = standingCenterY - (standingHeight - crouchedHeight) * 0.5f;
            float capCenterY = crouchedCenterY + crouchedHeight * 0.5f - playerCollider.radius;

            Vector3 origin = space.TransformPoint(
                new Vector3(playerCollider.center.x, capCenterY, playerCollider.center.z));

            // Slightly under the real radius, so brushing a wall is not read as a ceiling.
            float radius = playerCollider.radius
                           * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) * 0.95f;

            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.up, HeadroomHits, gap,
                                                   headroomMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = HeadroomHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Stand up on the way out.
        ///
        /// Without it a player disabled mid-crouch — despawned, pooled, handed over to a mount —
        /// leaves a shortened capsule and a lowered eye on a body that is no longer crouching, and
        /// nothing still running to put either back.
        /// </summary>
        private void OnDisable()
        {
            isCrouching = false;
            isSprinting = false;
            forwardWasHeld = false;
            sprintCharge = 1f;
            winded = false;
            stanceT = 0f;
            appliedT = float.NaN;
            ApplyStance(0f);
        }
    }
}
