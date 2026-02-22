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
        DisableRiderComponentsForMount();
        EnterMountedRigidbodyState();
        ParentRiderToMount();
        InitializeMountedViewState();

        ResetMountedInputState();
        currentSteeringForward = GetSteeringForward();
        lastMountChangeTime = Time.time;
        ResetSteeringState();
        ApplyPerspective(defaultPerspective);
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

        SetThirdPersonCameraEnabled(false);
        SetFirstPersonCameraEnabled(true);
        ExitMountedRigidbodyState();
        RestoreRiderComponentsAfterDismount();
        ClearMountedReferences();
        activeSeatPoint = seatPoint;
        ResetMountedInputState();
        currentSteeringForward = GetSteeringForward();
        ResetSteeringState();
        SetVisualLean(0f);
        lastMountChangeTime = Time.time;
    }
}
