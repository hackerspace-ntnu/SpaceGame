// MountController helper partial containing rider state setup/teardown routines.
using UnityEngine;

public partial class MountController
{
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
        {
            mountedPlayerLook.enabled = false;
        }

        if (disablePlayerInteractor && mountedInteractor)
        {
            mountedInteractor.enabled = false;
        }
    }

    private void RestoreRiderComponentsAfterDismount()
    {
        if (disablePlayerMovement && mountedPlayerMovement)
        {
            mountedPlayerMovement.enabled = true;
        }

        if (disablePlayerLook && mountedPlayerLook)
        {
            mountedPlayerLook.SetHeadVisible(false);
            mountedPlayerLook.enabled = true;
        }

        if (disablePlayerInteractor && mountedInteractor)
        {
            mountedInteractor.enabled = true;
        }
    }

    private void EnterMountedRigidbodyState()
    {
        if (!mountedPlayerRigidbody)
        {
            return;
        }

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
        {
            return;
        }

        mountedPlayerRigidbody.isKinematic = playerRigidbodyWasKinematic;
        mountedPlayerRigidbody.useGravity = playerRigidbodyHadGravity;
        mountedPlayerRigidbody.linearVelocity = Vector3.zero;
        mountedPlayerRigidbody.angularVelocity = Vector3.zero;
    }

    private void ParentRiderToMount()
    {
        Transform rideParent = activeSeatPoint ? activeSeatPoint : (seatPoint ? seatPoint : transform);
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
}
