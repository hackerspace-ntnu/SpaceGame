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
        EnsureMountedThirdPersonCamera();
        DisableRiderComponentsForMount();
        EnterMountedRigidbodyState();
        ParentRiderToMount();
        ApplyModuleSuppression();
        lastMountChangeTime = Time.time;
        Mounted?.Invoke(playerMovement);
        return true;
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

        ExitMountedRigidbodyState();
        RestoreRiderComponentsAfterDismount();
        RestoreModuleSuppression();
        PlayerMovement dismountedMovement = mountedPlayerMovement;
        Dismounted?.Invoke(dismountedMovement);
        ReleaseMountedThirdPersonCamera();
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
        mountedPlayerRigidbody.linearVelocity = Vector3.zero;
        mountedPlayerRigidbody.angularVelocity = Vector3.zero;
        mountedPlayerRigidbody.isKinematic = true;
        mountedPlayerRigidbody.useGravity = false;
    }

    private void ExitMountedRigidbodyState()
    {
        if (!mountedPlayerRigidbody)
            return;

        mountedPlayerRigidbody.isKinematic = playerRigidbodyWasKinematic;
        mountedPlayerRigidbody.useGravity = playerRigidbodyHadGravity;
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

    // ─────────── Third-person camera spawn ───────────
    private void EnsureMountedThirdPersonCamera()
    {
        if (mountedThirdPersonCamera != null || thirdPersonCameraPrefab == null)
            return;

        GameObject cameraObject = Instantiate(thirdPersonCameraPrefab.gameObject);
        cameraObject.name = $"{name}_MountedThirdPersonCamera";
        mountedThirdPersonCamera = cameraObject.GetComponent<Camera>();
        if (mountedThirdPersonCamera != null)
            mountedThirdPersonCamera.enabled = false;
    }

    private void ReleaseMountedThirdPersonCamera()
    {
        if (mountedThirdPersonCamera == null)
            return;

        Destroy(mountedThirdPersonCamera.gameObject);
        mountedThirdPersonCamera = null;
    }
}
