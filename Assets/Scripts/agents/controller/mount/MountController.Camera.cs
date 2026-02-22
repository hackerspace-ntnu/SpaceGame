// Camera control for mounted first/third person perspectives.
using UnityEngine;

public partial class MountController
{
    private void LateUpdate()
    {
        if (!IsMounted || activePerspective != CameraPerspective.ThirdPerson || thirdPersonCamera == null)
        {
            return;
        }

        Transform pivot = thirdPersonPivot ? thirdPersonPivot : (activeSeatPoint ? activeSeatPoint : transform);
        Quaternion cameraRot = Quaternion.Euler(mountedPitch, cameraYaw, 0f);
        Vector3 targetPosition = pivot.position + cameraRot * thirdPersonOffset;
        thirdPersonCamera.transform.position = Vector3.Lerp(
            thirdPersonCamera.transform.position,
            targetPosition,
            Mathf.Clamp01(thirdPersonFollowLerp * Time.deltaTime));

        Vector3 toPivot = pivot.position - thirdPersonCamera.transform.position;
        float horizontalYaw = Mathf.Atan2(toPivot.x, toPivot.z) * Mathf.Rad2Deg;
        thirdPersonCamera.transform.rotation = Quaternion.Euler(mountedPitch, horizontalYaw, 0f);
    }

    private void TogglePerspective()
    {
        if (!IsMounted)
        {
            return;
        }

        CameraPerspective next = activePerspective == CameraPerspective.FirstPerson
            ? CameraPerspective.ThirdPerson
            : CameraPerspective.FirstPerson;
        ApplyPerspective(next);
    }

    private void ApplyPerspective(CameraPerspective perspective)
    {
        activePerspective = perspective;
        if (activePerspective == CameraPerspective.FirstPerson)
        {
            SetThirdPersonCameraEnabled(false);
            SetFirstPersonCameraEnabled(true);
            mountedPlayerLook?.SetHeadVisible(false);
        }
        else
        {
            SetFirstPersonCameraEnabled(false);
            SetThirdPersonCameraEnabled(true);
            mountedPlayerLook?.SetHeadVisible(true);
        }

        currentSteeringForward = GetSteeringForward();
    }

    private void EnsureThirdPersonCamera()
    {
        if (thirdPersonCamera)
        {
            return;
        }

        GameObject cameraObject = new GameObject($"{name}_MountThirdPersonCamera");
        thirdPersonCamera = cameraObject.AddComponent<Camera>();
        if (!cameraObject.GetComponent<AudioListener>())
        {
            cameraObject.AddComponent<AudioListener>();
        }
    }

    private void SetFirstPersonCameraEnabled(bool enabled)
    {
        if (mountedFirstPersonCamera != null)
        {
            mountedFirstPersonCamera.enabled = enabled;
            AudioListener listener = mountedFirstPersonCamera.GetComponent<AudioListener>();
            if (listener)
            {
                listener.enabled = enabled;
            }
        }
    }

    private void SetThirdPersonCameraEnabled(bool enabled)
    {
        EnsureThirdPersonCamera();

        if (thirdPersonCamera != null)
        {
            thirdPersonCamera.enabled = enabled;
            AudioListener listener = thirdPersonCamera.GetComponent<AudioListener>();
            if (listener)
            {
                listener.enabled = enabled;
            }
        }
    }
}
