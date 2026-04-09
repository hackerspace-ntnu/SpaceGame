// Mount/dismount API and transition flow for rider attachment and restoration.
using UnityEngine;

public partial class MountController
{
    public bool CanMount(Interactor interactor) =>
        IsAvailableForMount && interactor != null && interactor.GetComponentInParent<PlayerMovement>() != null;

    public bool TryMount(Interactor interactor, Transform mountPointOverride)
    {
        if (!CanMount(interactor))
        {
            return false;
        }

        PlayerMovement playerMovement = interactor.GetComponentInParent<PlayerMovement>();
        if (!playerMovement)
        {
            return false;
        }

        CacheMountedPlayerReferences(playerMovement, mountPointOverride);
        EnsureMountedThirdPersonCamera();
        DisableRiderComponentsForMount();
        EnterMountedRigidbodyState();
        ParentRiderToMount();
        lastMountChangeTime = Time.time;
        Mounted?.Invoke(playerMovement);
        return true;
    }

    public void Dismount()
    {
        if (!IsMounted)
        {
            return;
        }

        Transform rider = mountedPlayer;
        rider.SetParent(null, true);

        Vector3 dismountPosition = dismountPoint
            ? dismountPoint.position
            : transform.position + transform.right * fallbackDismountDistance;
        rider.position = dismountPosition;

        ExitMountedRigidbodyState();
        RestoreRiderComponentsAfterDismount();
        Dismounted?.Invoke(mountedPlayerMovement);
        ReleaseMountedThirdPersonCamera();
        ClearMountedReferences();
        activeSeatPoint = seatPoint;
        lastMountChangeTime = Time.time;
    }

    private void EnsureMountedThirdPersonCamera()
    {
        if (mountedThirdPersonCamera != null || thirdPersonCameraPrefab == null)
        {
            return;
        }

        GameObject cameraObject = Instantiate(thirdPersonCameraPrefab.gameObject);
        cameraObject.name = $"{name}_MountedThirdPersonCamera";
        mountedThirdPersonCamera = cameraObject.GetComponent<Camera>();
        if (mountedThirdPersonCamera != null)
        {
            mountedThirdPersonCamera.enabled = false;
        }
    }

    private void ReleaseMountedThirdPersonCamera()
    {
        if (mountedThirdPersonCamera == null)
        {
            return;
        }

        Destroy(mountedThirdPersonCamera.gameObject);
        mountedThirdPersonCamera = null;
    }
}
