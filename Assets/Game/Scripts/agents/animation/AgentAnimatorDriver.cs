// Bridges motor output into animator parameters for agent characters.
// Converts world velocity into local animation-space values each frame.
// Keeps animation updates centralized and independent from brain logic.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class AgentAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float animationSpeedMultiplier = 1.5f;
        [Tooltip("Extra velocity scale applied when walking (not running), to compensate for the reduced walk speed so animations don't look sluggish.")]
        [SerializeField] private float walkAnimBoost = 2f;

        [Tooltip("Playback rate for the whole Animator, applied once at Awake. 1 = leave alone.\n\n" +
                 "This is the fix for feet that skate. The two fields above only choose WHICH clip " +
                 "the blend tree lands on; neither changes how fast that clip plays, so a character " +
                 "whose motor speed does not match the clip's authored stride slides no matter how " +
                 "they are tuned — forwards if it moves slower than the stride, backwards if faster. " +
                 "Set this to groundSpeed / strideSpeed.\n\n" +
                 "Per-Animator, not per-controller, so a shared controller can drive a slow amble on " +
                 "one character and a brisk walk on another.")]
        [SerializeField] private float animatorSpeedScale = 1f;

        private void Awake()
        {
            if (!animator)
            {
                animator = GetComponent<Animator>();
            }

            if (!animator)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (!animator)
            {
                Debug.LogWarning($"{name}: AgentAnimatorDriver could not find an Animator on this object or children.", this);
                return;
            }

            // Applied once rather than every frame: nothing else on the agent writes Animator.speed,
            // and re-asserting it per tick would stamp on a hit-stop or slow-motion effect that did.
            if (!Mathf.Approximately(animatorSpeedScale, 1f))
                animator.speed = animatorSpeedScale;
        }

        public void Tick(Vector3 worldVelocity, bool isImmobile, bool isRunning = false)
        {
            if (!animator)
            {
                return;
            }

            if (animator.runtimeAnimatorController == null)
            {
                return;
            }

            float speedScale = animationSpeedMultiplier * (isRunning ? 1f : walkAnimBoost);
            // Convert velocity in the animator rig's local space (important when rig is on a child transform).
            Vector3 localVelocity = animator.transform.worldToLocalMatrix.MultiplyVector(worldVelocity) * speedScale;

            animator.SetFloat("SpeedX", localVelocity.x, 0.1f, Time.deltaTime);
            animator.SetFloat("SpeedY", localVelocity.z, 0.1f, Time.deltaTime);
            animator.SetFloat("FallSpeed", worldVelocity.y, 0.1f, Time.deltaTime);
            animator.SetBool("IsGrounded", true);
            animator.SetBool("IsImmobalized", isImmobile);
        }

        public void TriggerHurt() => SetTriggerSafe("Hurt");
        public void TriggerDie() => SetTriggerSafe("Die");
        public void TriggerShootRifle() => SetTriggerSafe("ShootRifle");
        public void TriggerSpearAttack() => SetTriggerSafe("SpearAttack");
        public void TriggerByName(string triggerName) => SetTriggerSafe(triggerName);
        public void SetIsAiming(bool aiming) => animator?.SetBool("IsAiming", aiming);

        private void SetTriggerSafe(string triggerName)
        {
            if (animator && animator.runtimeAnimatorController != null)
                animator.SetTrigger(triggerName);
        }

        private void OnValidate()
        {
            animationSpeedMultiplier = Mathf.Max(0.1f, animationSpeedMultiplier);
            animatorSpeedScale = Mathf.Clamp(animatorSpeedScale, 0.05f, 4f);
        }
    }
}
