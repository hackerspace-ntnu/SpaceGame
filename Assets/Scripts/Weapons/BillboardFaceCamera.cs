using UnityEngine;

[DisallowMultipleComponent]
public class BillboardFaceCamera : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;

    private void LateUpdate()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            return;
        }

        transform.forward = cam.transform.forward;
    }
}
