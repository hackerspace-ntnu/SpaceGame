// Input reading + jump/leap decision logic for SteerModule.
// Jump = press + quick release (hold < leapHoldTime). Leap = press + hold >= leapHoldTime, then release.
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SteerModule
{
    private void ResolveInputActions()
    {
        if (InputSystem.actions == null)
            return;

        moveAction = InputSystem.actions.FindAction(moveActionName);
        lookAction = InputSystem.actions.FindAction(lookActionName);
        jumpAction = InputSystem.actions.FindAction(jumpActionName);
        togglePerspectiveAction = InputSystem.actions.FindAction(togglePerspectiveActionName);
    }

    private void ReadMountedInput()
    {
        Vector2 raw = ReadMountedMoveInput();
        currentMoveInput = new Vector2(
            Mathf.SmoothDamp(currentMoveInput.x, raw.x, ref moveInputVelocityX, turnSmoothTime),
            Mathf.SmoothDamp(currentMoveInput.y, raw.y, ref moveInputVelocityY, turnSmoothTime));
        hasSteeringOverride = raw.sqrMagnitude >= steeringOverrideThreshold * steeringOverrideThreshold;
    }

    private Vector2 ReadMountedMoveInput()
    {
        if (moveAction == null)
            return Vector2.zero;
        return Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
    }

    private void EnsureMountedLookActionEnabled()
    {
        if (lookAction == null || lookAction.enabled)
            return;
        lookAction.Enable();
        forcedMountedLookActionEnabled = true;
    }

    private void HandleJumpAndLeap(float deltaTime)
    {
        if (!jumpEnabled || jumpAction == null)
        {
            jumpHeld = false;
            jumpHoldDuration = 0f;
            return;
        }

        if (jumpAction.WasPressedThisFrame())
        {
            jumpHeld = true;
            jumpHoldDuration = 0f;
        }

        if (jumpHeld)
            jumpHoldDuration += deltaTime;

        if (jumpAction.WasReleasedThisFrame() && jumpHeld)
        {
            bool shouldLeap = leapEnabled && jumpHoldDuration >= leapHoldTime;
            jumpHeld = false;
            jumpHoldDuration = 0f;

            if (shouldLeap)
                TriggerLeap();
            else
                TriggerJump();
        }
    }

    private void TriggerJump()
    {
        if (jumpMotor != null)
        {
            jumpMotor.RequestJump();
            return;
        }

        // Self-drive fallback (vertical hop). Only runs on non-agent hosts — the arc is advanced
        // by SelfDriveTick, which doesn't tick when an AgentController is present.
        if (!hasAgentController && !selfDriveArcing)
            BeginSelfDriveArc(Vector3.zero, 0f, selfDriveJumpHeight, selfDriveJumpDuration);
    }

    private void TriggerLeap()
    {
        Vector3 direction = GetSteeringForward();
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-4f)
            direction = transform.forward;
        direction.Normalize();

        if (leapMotor != null)
        {
            leapMotor.RequestLeap(direction, leapHorizontal, leapVertical, leapDuration);
            return;
        }

        // Self-drive fallback — same guard: SelfDriveTick only runs on non-agent hosts.
        if (!hasAgentController && !selfDriveArcing)
            BeginSelfDriveArc(direction, leapHorizontal, leapVertical, leapDuration);
    }

    private void HandleTogglePerspective()
    {
        if (togglePerspectiveAction != null && togglePerspectiveAction.WasPressedThisFrame())
            TogglePerspective();
    }

    private void ResetMountedInputState()
    {
        currentMoveInput = Vector2.zero;
        moveInputVelocityX = 0f;
        moveInputVelocityY = 0f;
        hasSteeringOverride = false;
        jumpHeld = false;
        jumpHoldDuration = 0f;
    }
}
