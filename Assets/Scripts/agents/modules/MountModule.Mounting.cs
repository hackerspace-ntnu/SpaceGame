// Mount/dismount flow, rider state caching, rigidbody handoff, and third-person camera spawn/cleanup.
// Split off MountModule.cs purely for readability.
using UnityEngine;

public partial class MountModule
{
    public bool CanMount(Interactor interactor) =>
        IsAvailableForMount && interactor != null && interactor.GetComponentInParent<PlayerMovement>() != null;

    public bool TryMount(Interactor interactor, Transform mountPointOverride)
    {
        if (!CanMount(interactor))
            return false;

        PlayerMovement playerMovement = interactor.GetComponentInParent<PlayerMovement>();
        if (!playerMovement)
            return false;

        CacheMountedPlayerReferences(playerMovement, mountPointOverride);
        DisableRiderComponentsForMount();
        EnterMountedRigidbodyState();
        ParentRiderToMount();
        ApplyModuleSuppression();
        StopOwnMotorOnMount();
        FreezeOwnRigidbodyRotationOnMount();
        IgnoreRiderMountCollisions();
        SuppressRootMotionOnMount();
        InitializeMountedViewState();
        ApplyPerspective(defaultPerspective);
        lastMountChangeTime = Time.time;
        Mounted?.Invoke(playerMovement);
        return true;
    }

    // Clear any pending nav destination / velocity so the mount doesn't keep
    // cruising on its own when the rider takes over with no input.
    private void StopOwnMotorOnMount()
    {
        if (allowAISelfMovementWhenMounted)
            return;
        IMovementMotor motor = GetComponent<IMovementMotor>();
        motor?.ForceStop();
    }

    // Rider is a kinematic Rigidbody parented inside the mount's collider — contacts from that
    // overlap spin the mount in place. The rider owns rotation via SteerModule writing to
    // transform.rotation directly, so physics rotation isn't wanted while mounted anyway.
    // Lock it, remember the original constraints, restore on dismount.
    private void FreezeOwnRigidbodyRotationOnMount()
    {
        if (!ownRigidbody)
            ownRigidbody = GetComponent<Rigidbody>();
        if (!ownRigidbody)
            return;
        ownRigidbodyConstraints = ownRigidbody.constraints;
        ownRigidbodyConstraintsCaptured = true;
        ownRigidbody.constraints = ownRigidbodyConstraints | RigidbodyConstraints.FreezeRotation;
        ownRigidbody.angularVelocity = Vector3.zero;
    }

    private void RestoreOwnRigidbodyRotationAfterDismount()
    {
        if (!ownRigidbody || !ownRigidbodyConstraintsCaptured)
            return;
        ownRigidbody.constraints = ownRigidbodyConstraints;
        ownRigidbody.angularVelocity = Vector3.zero;
        ownRigidbodyConstraintsCaptured = false;
    }

    // Disable collision between every rider collider and every mount collider so the rider's
    // kinematic body can't shove the mount via contacts at the seat point.
    private void IgnoreRiderMountCollisions()
    {
        if (mountedPlayer == null)
            return;

        Collider[] riderColliders = mountedPlayer.GetComponentsInChildren<Collider>(true);
        Collider[] mountColliders = GetComponentsInChildren<Collider>(true);
        if (riderColliders.Length == 0 || mountColliders.Length == 0)
            return;

        var pairs = new System.Collections.Generic.List<(Collider, Collider)>(
            riderColliders.Length * mountColliders.Length);

        foreach (Collider r in riderColliders)
        {
            if (!r) continue;
            foreach (Collider m in mountColliders)
            {
                if (!m || r == m) continue;
                Physics.IgnoreCollision(r, m, true);
                pairs.Add((r, m));
            }
        }

        ignoredCollisionPairs = pairs.ToArray();
    }

    private void RestoreRiderMountCollisions()
    {
        if (ignoredCollisionPairs == null)
            return;

        foreach (var (a, b) in ignoredCollisionPairs)
        {
            if (a && b)
                Physics.IgnoreCollision(a, b, false);
        }

        ignoredCollisionPairs = null;
    }

    // Animator root motion can translate/rotate the mount transform even when every module
    // is suppressed and every intent is Idle — a classic "mount walks in circles" source.
    // Turn it off on the mount's animators for the mounted duration.
    private void SuppressRootMotionOnMount()
    {
        if (allowAISelfMovementWhenMounted)
            return;
        Animator[] animators = GetComponentsInChildren<Animator>(true);
        suppressibleAnimators = animators;
        suppressibleAnimatorRootMotion = new bool[animators.Length];
        for (int i = 0; i < animators.Length; i++)
        {
            suppressibleAnimatorRootMotion[i] = animators[i].applyRootMotion;
            animators[i].applyRootMotion = false;
        }
    }

    private void RestoreRootMotionAfterDismount()
    {
        if (suppressibleAnimators == null)
            return;
        for (int i = 0; i < suppressibleAnimators.Length; i++)
        {
            if (suppressibleAnimators[i])
                suppressibleAnimators[i].applyRootMotion = suppressibleAnimatorRootMotion[i];
        }
        suppressibleAnimators = null;
        suppressibleAnimatorRootMotion = null;
    }

    public void Dismount()
    {
        if (!IsMounted)
            return;

        Transform rider = mountedPlayer;
        rider.SetParent(null, true);

        Vector3 dismountPosition = dismountPoint
            ? dismountPoint.position
            : transform.position + transform.right * fallbackDismountDistance;
        rider.position = dismountPosition;

        // Strip any tilt the rider inherited from a tilted mount — keep only yaw so the
        // player stands upright after dismount.
        rider.rotation = Quaternion.Euler(0f, rider.eulerAngles.y, 0f);

        ExitMountedRigidbodyState();
        RestoreRiderComponentsAfterDismount();
        RestoreOwnRigidbodyRotationAfterDismount();
        RestoreRiderMountCollisions();
        RestoreRootMotionAfterDismount();
        RestoreModuleSuppression();
        SetThirdPersonCameraEnabled(false);
        SetFirstPersonCameraEnabled(true);
        SetMountedVisorEnabled(true);
        PlayerMovement dismountedMovement = mountedPlayerMovement;
        Dismounted?.Invoke(dismountedMovement);
        ReleaseRuntimeThirdPersonCamera();
        ClearMountedReferences();
        activeSeatPoint = seatPoint;
        lastMountChangeTime = Time.time;
    }

    // ─────────── Rider state cache/restore ───────────
    private void CacheMountedPlayerReferences(PlayerMovement playerMovement, Transform mountPointOverride)
    {
        mountedPlayer = playerMovement.transform;
        mountedPlayerMovement = playerMovement;
        mountedPlayerLook = mountedPlayer.GetComponent<PlayerLook>();
        mountedInteractor = mountedPlayer.GetComponentInChildren<Interactor>(true);
        mountedPlayerRigidbody = mountedPlayer.GetComponent<Rigidbody>();
        mountedFirstPersonCamera = mountedPlayer.GetComponentInChildren<Camera>(true);
        mountedFirstPersonCameraRoot = mountedPlayerLook != null ? mountedPlayerLook.cameraRoot : null;
        activeSeatPoint = mountPointOverride ? mountPointOverride : seatPoint;
    }

    private void DisableRiderComponentsForMount()
    {
        if (disablePlayerMovement && mountedPlayerMovement)
        {
            mountedPlayerMovement.enabled = false;
            mountedPlayerMovement.ForceIdleAnimation();
        }

        if (disablePlayerLook && mountedPlayerLook)
            mountedPlayerLook.enabled = false;

        if (disablePlayerInteractor && mountedInteractor)
            mountedInteractor.enabled = false;
    }

    private void RestoreRiderComponentsAfterDismount()
    {
        if (disablePlayerMovement && mountedPlayerMovement)
            mountedPlayerMovement.enabled = true;

        if (disablePlayerLook && mountedPlayerLook)
        {
            mountedPlayerLook.SetHeadVisible(false);
            mountedPlayerLook.enabled = true;
        }

        if (disablePlayerInteractor && mountedInteractor)
            mountedInteractor.enabled = true;
    }

    private void EnterMountedRigidbodyState()
    {
        if (!mountedPlayerRigidbody)
            return;

        playerRigidbodyWasKinematic = mountedPlayerRigidbody.isKinematic;
        playerRigidbodyHadGravity = mountedPlayerRigidbody.useGravity;
        playerRigidbodyInterpolation = mountedPlayerRigidbody.interpolation;
        mountedPlayerRigidbody.linearVelocity = Vector3.zero;
        mountedPlayerRigidbody.angularVelocity = Vector3.zero;
        mountedPlayerRigidbody.isKinematic = true;
        mountedPlayerRigidbody.useGravity = false;
        // Rider is parented to the mount and follows it via the transform hierarchy. Letting
        // the rider's Rigidbody also interpolate makes it sample stale world positions one
        // physics step behind the parent, which renders the rider drifting off the seat.
        mountedPlayerRigidbody.interpolation = RigidbodyInterpolation.None;
    }

    private void ExitMountedRigidbodyState()
    {
        if (!mountedPlayerRigidbody)
            return;

        mountedPlayerRigidbody.isKinematic = playerRigidbodyWasKinematic;
        mountedPlayerRigidbody.useGravity = playerRigidbodyHadGravity;
        mountedPlayerRigidbody.interpolation = playerRigidbodyInterpolation;
        mountedPlayerRigidbody.linearVelocity = Vector3.zero;
        mountedPlayerRigidbody.angularVelocity = Vector3.zero;
    }

    private void ParentRiderToMount()
    {
        Transform rideParent = seatPoint ? seatPoint : transform;
        mountedPlayer.SetParent(rideParent, true);
        mountedPlayer.localPosition = Vector3.zero;
        mountedPlayer.localRotation = Quaternion.identity;
    }

    private void ClearMountedReferences()
    {
        mountedPlayer = null;
        mountedPlayerMovement = null;
        mountedPlayerLook = null;
        mountedInteractor = null;
        mountedPlayerRigidbody = null;
        mountedFirstPersonCamera = null;
        mountedFirstPersonCameraRoot = null;
    }

    private void ReleaseRuntimeThirdPersonCamera()
    {
        if (runtimeThirdPersonCamera == null)
            return;
        Destroy(runtimeThirdPersonCamera.gameObject);
        runtimeThirdPersonCamera = null;
    }
}
