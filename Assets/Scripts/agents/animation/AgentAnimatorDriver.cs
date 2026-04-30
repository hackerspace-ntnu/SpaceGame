// Bridges motor output into animator parameters for agent characters.
// Converts world velocity into local animation-space values each frame.
// Keeps animation updates centralized and independent from brain logic.
using UnityEngine;

public class AgentAnimatorDriver : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private float animationSpeedMultiplier = 1.5f;
    [Tooltip("Extra velocity scale applied when walking (not running), to compensate for the reduced walk speed so animations don't look sluggish.")]
    [SerializeField] private float walkAnimBoost = 2f;

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
        }
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
    }
}
