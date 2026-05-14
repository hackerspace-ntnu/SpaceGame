using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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

        velocity.x = newHorizontal.x;
        velocity.z = newHorizontal.z;
        rb.linearVelocity = velocity;
        
        lastYVelocity = rb.linearVelocity.y;
        wasGrounded = grounded;

        UpdateAnimatorParameters(velocity, grounded);
    }
    
    private void HandleFallDamage(bool grounded)
    {
        // Detect landing (was in air, now grounded)
        if (!wasGrounded && grounded)
        {
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
            health.Damage(damage);
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
