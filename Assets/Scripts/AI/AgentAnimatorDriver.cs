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
    }

    public void Tick(Vector3 worldVelocity, bool isImmobile)
    {
        if (!animator)
        {
            return;
        }

        Vector3 localVelocity = transform.worldToLocalMatrix.MultiplyVector(worldVelocity) * animationSpeedMultiplier;

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
