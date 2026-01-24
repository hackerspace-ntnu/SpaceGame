using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField, Range(0f, 1f)] private float airControl = 0.3f;

    [Header("Jumping")]
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private float jumpCooldown = 0.6f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckDistance = 1.9f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private GameObject camera;

    private Rigidbody rb;
    private Vector2 input;
    private float jumpCooldownTimer;
    private bool jumpOnCooldown;
    private bool groundSnapEnabled = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
    }

    private void Start()
    {
        if (!groundCheck)
        {
            groundCheck = transform;
        }
    }

    private void FixedUpdate()
    {
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
    }

    public void OnMove(InputValue value)
    {
        input = value.Get<Vector2>();
    }

    public void OnJump()
    {
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
        Vector3 dashDirection = transform.forward;
        if (camera)
        {
            dashDirection = camera.transform.forward;
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
        Vector3 origin = groundCheck ? groundCheck.position : transform.position;
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
