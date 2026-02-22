// Bridges motor output into animator parameters for agent characters.
// Converts world velocity into local animation-space values each frame.
// Keeps animation updates centralized and independent from brain logic.
using UnityEngine;

public class AgentAnimatorDriver : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private float animationSpeedMultiplier = 1.5f;

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

    public void Tick(Vector3 worldVelocity, bool isImmobile)
    {
        if (!animator)
        {
            return;
        }

        // Convert velocity in the animator rig's local space (important when rig is on a child transform).
        Vector3 localVelocity = animator.transform.worldToLocalMatrix.MultiplyVector(worldVelocity) * animationSpeedMultiplier;

        animator.SetFloat("SpeedX", localVelocity.x, 0.1f, Time.deltaTime);
        animator.SetFloat("SpeedY", localVelocity.z, 0.1f, Time.deltaTime);
        animator.SetFloat("FallSpeed", worldVelocity.y, 0.1f, Time.deltaTime);
        animator.SetBool("IsGrounded", true);
        animator.SetBool("IsImmobalized", isImmobile);
    }

    private void OnValidate()
    {
        animationSpeedMultiplier = Mathf.Max(0.1f, animationSpeedMultiplier);
    }
}
