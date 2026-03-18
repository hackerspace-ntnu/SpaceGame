using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : NetworkBehaviour
{
    private InputControls controls;
    
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField, Range(0f, 1f)] private float airControl = 0.3f;

    [Header("Jumping")]
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private float jumpCooldown = 0.6f;
    [SerializeField] private float groundCheckDistance = 1.9f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private GameObject playerCamera;

    [SerializeField] private Rigidbody rb;
    [SerializeField] private Animator animator;
    private Vector2 input;
    private float jumpCooldownTimer;
    private bool jumpOnCooldown;
    private bool groundSnapEnabled = true;

    private void Awake()
    {
        controls  = new InputControls();

        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (animator == null)
            animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = new InputControls();

        controls.Player.Move.performed += ctx => OnMove(ctx.ReadValue<Vector2>());
        controls.Player.Move.canceled += ctx => OnMove(Vector2.zero);
        controls.Player.Jump.performed += ctx => OnJump();
        controls.Player.Dash.performed += ctx => OnDash();

        controls.Enable();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        HandleJumpCooldown();

        if (!groundSnapEnabled)
        {
            return;
        }

        Vector3 move = transform.right * input.x + transform.forward * input.y;
        move = Vector3.ClampMagnitude(move, 1f);
        Vector3 desiredHorizontal = move * moveSpeed;

        Vector3 velocity = rb.linearVelocity;
        Vector3 currentHorizontal = new Vector3(velocity.x, 0f, velocity.z);

        bool grounded = IsGrounded();
        float control = grounded ? 1f : airControl;
        Vector3 newHorizontal = Vector3.Lerp(currentHorizontal, desiredHorizontal, control);

        velocity.x = newHorizontal.x;
        velocity.z = newHorizontal.z;
        rb.linearVelocity = velocity;

        UpdateAnimatorParameters(velocity, grounded);
    }

    private void UpdateAnimatorParameters(Vector3 velocity, bool grounded)
    {
        if (!animator) return;

        UpdateAnimatorParametersServerRpc(velocity, grounded);
    }
    
    [ServerRpc]
    private void UpdateAnimatorParametersServerRpc(Vector3 velocity, bool grounded)
    {
        // Calculate local velocity for animation
        Vector3 localVelocity = transform.worldToLocalMatrix.MultiplyVector(velocity);
        
        animator.SetFloat("SpeedX", localVelocity.x, .1f, Time.deltaTime);
        animator.SetFloat("SpeedY", localVelocity.z, .1f, Time.deltaTime);
        animator.SetFloat("FallSpeed", velocity.y, .1f, Time.deltaTime);
        animator.SetBool("IsGrounded", grounded);
        animator.SetBool("IsImmobalized", !groundSnapEnabled);
    }

    public void OnMove(Vector2 inputVector)
    {
        if (!IsOwner) return;
        input = inputVector;
    }

    public void OnJump()
    {
        if (!IsOwner) return;

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
        if (!IsOwner) return;

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
        Vector3 origin = transform.position;
        return Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);
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
