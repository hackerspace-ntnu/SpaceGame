// Input reading + jump/leap decision logic for SteerModule.
// Jump = press + quick release (hold < leapHoldTime). Leap = press + hold >= leapHoldTime, then release.
// Vertical input (ascend/descend) is read from an optional Vector2 action and ignored by ground motors.
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceGame.Agents
{
    public partial class SteerModule
    {
        private void ResolveInputActions()
        {
            if (InputSystem.actions == null)
                return;

            moveAction = InputSystem.actions.FindAction(moveActionName);
            jumpAction = InputSystem.actions.FindAction(jumpActionName);
            verticalAction = string.IsNullOrEmpty(verticalActionName)
                ? null
                : InputSystem.actions.FindAction(verticalActionName);
            runAction = string.IsNullOrEmpty(runActionName)
                ? null
                : InputSystem.actions.FindAction(runActionName);
            turnAction = string.IsNullOrEmpty(turnActionName)
                ? null
                : InputSystem.actions.FindAction(turnActionName);
        }

        private void EnsureMountedInputActionsEnabled()
        {
            if (moveAction == null && jumpAction == null && verticalAction == null && runAction == null
                && turnAction == null)
                ResolveInputActions();

            EnsureActionEnabled(moveAction, ref forcedMoveActionEnabled);
            EnsureActionEnabled(jumpAction, ref forcedJumpActionEnabled);
            EnsureActionEnabled(verticalAction, ref forcedVerticalActionEnabled);
            EnsureActionEnabled(runAction, ref forcedRunActionEnabled);
            EnsureActionEnabled(turnAction, ref forcedTurnActionEnabled);
        }

        private void RestoreMountedInputActions()
        {
            RestoreActionEnabledState(moveAction, ref forcedMoveActionEnabled);
            RestoreActionEnabledState(jumpAction, ref forcedJumpActionEnabled);
            RestoreActionEnabledState(verticalAction, ref forcedVerticalActionEnabled);
            RestoreActionEnabledState(runAction, ref forcedRunActionEnabled);
            RestoreActionEnabledState(turnAction, ref forcedTurnActionEnabled);
        }

        private static void EnsureActionEnabled(InputAction action, ref bool forcedEnabled)
        {
            if (action == null || action.enabled)
                return;

            action.Enable();
            forcedEnabled = true;
        }

        private static void RestoreActionEnabledState(InputAction action, ref bool forcedEnabled)
        {
            if (!forcedEnabled)
                return;

            if (action != null)
                action.Disable();

            forcedEnabled = false;
        }

        private void ReadMountedInput()
        {
            Vector2 raw = ReadMountedMoveInput();
            currentMoveInput = new Vector2(
                Mathf.SmoothDamp(currentMoveInput.x, raw.x, ref moveInputVelocityX, turnSmoothTime),
                Mathf.SmoothDamp(currentMoveInput.y, raw.y, ref moveInputVelocityY, turnSmoothTime));

            float rawVertical = ReadMountedVerticalInput();
            currentVerticalInput = Mathf.SmoothDamp(currentVerticalInput, rawVertical, ref verticalInputVelocity, turnSmoothTime);

            float rawTurn = ReadMountedTurnInput();
            currentTurnInput = Mathf.SmoothDamp(currentTurnInput, rawTurn, ref turnInputVelocity, turnSmoothTime);

            // Turn counts toward claiming the frame. Without it a rider who only turns — the crab's
            // whole point, since it can pivot without translating — would fall through to the AI channel.
            float overrideMag = Mathf.Max(raw.sqrMagnitude, Mathf.Max(rawVertical * rawVertical, rawTurn * rawTurn));
            hasSteeringOverride = overrideMag >= steeringOverrideThreshold * steeringOverrideThreshold;
        }

        private float ReadMountedTurnInput()
        {
            if (turnAction == null)
                return 0f;

            // Accept either a float or a Vector2 action. For Vector2 we use the X axis, matching the
            // stick axis a turn would otherwise have come from.
            if (turnAction.expectedControlType == "Vector2")
                return Mathf.Clamp(turnAction.ReadValue<Vector2>().x, -1f, 1f);

            return Mathf.Clamp(turnAction.ReadValue<float>(), -1f, 1f);
        }

        private Vector2 ReadMountedMoveInput()
        {
            if (moveAction == null)
                return Vector2.zero;
            return Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        }

        private float ReadMountedVerticalInput()
        {
            if (verticalAction == null)
                return 0f;

            // Accept either a float or a Vector2 action. For Vector2 we use the Y axis.
            if (verticalAction.expectedControlType == "Vector2")
                return Mathf.Clamp(verticalAction.ReadValue<Vector2>().y, -1f, 1f);

            return Mathf.Clamp(verticalAction.ReadValue<float>(), -1f, 1f);
        }

        private void HandleJumpAndLeap(float deltaTime)
        {
            if (!jumpEnabled || jumpAction == null || jumpMotor == null)
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
                bool shouldLeap = leapEnabled && leapMotor != null && jumpHoldDuration >= leapHoldTime;
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
            if (jumpMotor == null)
                return;
            jumpMotor.RequestJump();
        }

        private void TriggerLeap()
        {
            if (leapMotor == null)
                return;

            Vector3 direction = transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f)
                direction = transform.forward;
            direction.Normalize();

            leapMotor.RequestLeap(direction, leapHorizontal, leapVertical, leapDuration);
        }

        private void ResetMountedInputState()
        {
            currentMoveInput = Vector2.zero;
            currentVerticalInput = 0f;
            currentTurnInput = 0f;
            moveInputVelocityX = 0f;
            moveInputVelocityY = 0f;
            verticalInputVelocity = 0f;
            turnInputVelocity = 0f;
            hasSteeringOverride = false;
            jumpHeld = false;
            jumpHoldDuration = 0f;
        }
    }
}
