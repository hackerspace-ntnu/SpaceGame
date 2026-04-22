// Self-drive execution path for SteerModule when no AgentController is present.
// Drives a Rigidbody when one is available, else falls back to manipulating transform.position
// directly — so the same SteerModule works on vehicles, crates, props, or even empty GameObjects.
// Also owns a unified arc animation used by the jump and leap fallbacks when the host has no
// IMountJumpMotor / IMountLeapMotor.
using UnityEngine;

public partial class SteerModule
{
    private void SelfDriveTick(float deltaTime)
    {
        if (selfDriveArcing)
        {
            UpdateSelfDriveArc(deltaTime);
            return;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.forward;
        forward.Normalize();

        bool hasInput = hasSteeringOverride && Mathf.Abs(currentMoveInput.y) >= 0.001f;
        currentSpeedMultiplier = Mathf.MoveTowards(
            currentSpeedMultiplier,
            hasInput ? 1f : 0f,
            acceleration * deltaTime);

        if (selfDriveRigidbody)
        {
            Vector3 velocity = selfDriveRigidbody.linearVelocity;
            if (hasInput)
            {
                Vector3 desired = forward * currentMoveInput.y * moveSpeed * currentSpeedMultiplier;
                velocity.x = desired.x;
                velocity.z = desired.z;
            }
            else
            {
                velocity.x = Mathf.MoveTowards(velocity.x, 0f, acceleration * deltaTime);
                velocity.z = Mathf.MoveTowards(velocity.z, 0f, acceleration * deltaTime);
            }
            selfDriveRigidbody.linearVelocity = velocity;
            return;
        }

        // Transform-only fallback — no physics. Manually step the position along forward.
        if (hasInput)
        {
            Vector3 step = forward * currentMoveInput.y * moveSpeed * currentSpeedMultiplier * deltaTime;
            transform.position += step;
        }
    }

    // Unified arc animation for self-drive jump and leap.
    private void BeginSelfDriveArc(Vector3 direction, float horizontalDistance, float height, float duration)
    {
        selfDriveArcing = true;
        selfDriveArcElapsed = 0f;
        selfDriveArcDuration = Mathf.Max(0.05f, duration);
        selfDriveArcHeight = Mathf.Max(0f, height);
        selfDriveArcStart = transform.position;

        Vector3 horizontal = direction;
        horizontal.y = 0f;
        if (horizontal.sqrMagnitude < 1e-4f || horizontalDistance <= 0f)
        {
            selfDriveArcEnd = selfDriveArcStart;
        }
        else
        {
            horizontal.Normalize();
            selfDriveArcEnd = selfDriveArcStart + horizontal * horizontalDistance;
        }

        selfDriveArcRestoredKinematic = false;
        if (selfDriveRigidbody)
        {
            selfDriveArcKinematicBefore = selfDriveRigidbody.isKinematic;
            selfDriveRigidbody.linearVelocity = Vector3.zero;
            selfDriveRigidbody.angularVelocity = Vector3.zero;
            selfDriveRigidbody.isKinematic = true;
            selfDriveArcRestoredKinematic = true;
        }
    }

    private void UpdateSelfDriveArc(float deltaTime)
    {
        selfDriveArcElapsed += deltaTime;
        float t = Mathf.Clamp01(selfDriveArcElapsed / selfDriveArcDuration);
        float arc = Mathf.Sin(t * Mathf.PI);

        Vector3 flat = Vector3.Lerp(selfDriveArcStart, selfDriveArcEnd, t);
        transform.position = new Vector3(flat.x, flat.y + arc * selfDriveArcHeight, flat.z);

        if (t >= 1f)
        {
            selfDriveArcing = false;
            if (selfDriveRigidbody && selfDriveArcRestoredKinematic)
                selfDriveRigidbody.isKinematic = selfDriveArcKinematicBefore;
        }
    }
}
