// The first-person view dipping with each footfall while the player walks.
//
// This component never touches the camera. The eye's local height belongs to PlayerStance (the
// crouch), and a second component writing that axis would delete the crouch on every frame it ran
// — or have the crouch delete the bob. So this only works out how far the eye should be dipped
// right now, and PlayerStance folds that into the one height it writes.
//
// Owner only, and presentation only: the dip exists for the camera that sees it. Nothing is on the
// wire and nothing is saved. On a remote copy PlayerLook is disabled from Awake, which is one of
// the things that holds the dip at zero here.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Characters
{
    [DisallowMultipleComponent]
    public class PlayerHeadBob : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerMovement movement;
        [SerializeField] private PlayerLook look;

        [Header("Walk")]
        [Tooltip("How far the eye dips at each footfall at walking speed, in metres. Slower than " +
                 "walking (a crouch, a push) dips proportionally less.")]
        [SerializeField] private float walkDip = 0.04f;

        [Tooltip("Footfalls per second at walking speed. Slower than walking steps proportionally " +
                 "slower.")]
        [SerializeField] private float walkStepRate = 2f;

        [Header("Sprint")]
        [Tooltip("How far the eye dips at each footfall at full sprint speed, in metres.")]
        [SerializeField] private float sprintDip = 0.07f;

        [Tooltip("Footfalls per second at full sprint speed. Between walk and sprint speed both " +
                 "values blend.")]
        [SerializeField] private float sprintStepRate = 3f;

        [Header("Fade")]
        [Tooltip("Seconds the dip takes to settle away when the player stops or leaves the ground, " +
                 "and to build up again when they start.")]
        [SerializeField] private float fadeTime = 0.2f;

        // Progress through the current step, 0..1, where 0 is the top of the stride.
        private float stepPhase;

        // Dip depth being applied now, eased toward what the speed asks for.
        private float depth;

        /// <summary>
        /// Metres to add to the eye's rest height this frame: zero or negative, never above the
        /// rest height, so the eye cannot rise through the top of the helmet.
        /// </summary>
        public float HeightOffset { get; private set; }

        private void Awake()
        {
            if (movement == null) movement = GetComponent<PlayerMovement>();
            if (look == null) look = GetComponent<PlayerLook>();
        }

        private void Update()
        {
            // Without control of the body there is no walk to show, and something else — a mount,
            // a cutscene, the ragdoll — may be driving this camera. Drop the dip at once rather
            // than fading it, so the stance's next write does not fight that driver for a fifth of
            // a second.
            if (!HasControl())
            {
                Settle();
                return;
            }

            float speed = IsStriding() ? movement.HorizontalSpeed : 0f;
            float walkSpeed = movement.WalkSpeed;
            float upToWalk = walkSpeed > 0f ? Mathf.Clamp01(speed / walkSpeed) : 0f;
            float walkToSprint = Mathf.InverseLerp(walkSpeed, movement.SprintSpeed, speed);

            float targetDepth = upToWalk * Mathf.Lerp(walkDip, sprintDip, walkToSprint)
                                * GameSettings.HeadBobIntensity;
            float stepRate = upToWalk * Mathf.Lerp(walkStepRate, sprintStepRate, walkToSprint);

            float fadeSpeed = fadeTime > 0f ? Mathf.Max(walkDip, sprintDip) / fadeTime : float.PositiveInfinity;
            depth = Mathf.MoveTowards(depth, targetDepth, fadeSpeed * Time.deltaTime);

            // Restart from the top of the stride once the dip has fully settled, so the next walk
            // begins with the eye at rest instead of halfway down a step.
            stepPhase = depth > 0f ? Mathf.Repeat(stepPhase + stepRate * Time.deltaTime, 1f) : 0f;

            // A raised cosine: smooth at the top and at the footfall, so neither end of the step
            // has a corner in it.
            HeightOffset = -depth * 0.5f * (1f - Mathf.Cos(stepPhase * 2f * Mathf.PI));
        }

        private bool HasControl() =>
            Network.Owns(this)
            && movement != null && movement.isActiveAndEnabled
            && look != null && look.isActiveAndEnabled;

        // Feet on the ground and walking on them: a ladder, a ledge climb and a glide all move
        // the body without a stride.
        private bool IsStriding() =>
            movement.IsOnGround && !movement.IsClimbing && !movement.IsGliding;

        private void OnDisable() => Settle();

        private void Settle()
        {
            stepPhase = 0f;
            depth = 0f;
            HeightOffset = 0f;
        }
    }
}
