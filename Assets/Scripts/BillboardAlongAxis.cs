using UnityEngine;

public class BillboardAlongAxis : MonoBehaviour
{
    [SerializeField] private Vector3 localAxis = Vector3.up;
    private Camera mainCamera;

    private void Start()
    {
        FindCamera();
    }

    private void FindCamera()
    {
        // Try to find player first
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            mainCamera = player.GetComponentInChildren<Camera>();
        }

        // Fallback to Camera.main
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }

    private void LateUpdate()
    {
        if (mainCamera == null)
        {
            FindCamera();
            if (mainCamera == null) return;
        }

        BillboardToCamera();
    }

    private void BillboardToCamera()
    {
        Transform parent = transform.parent;
        if (parent == null) return;

        // Get direction from this object to camera
        Vector3 dirToCamera = (mainCamera.transform.position - transform.position).normalized;

        // Project the direction onto the plane perpendicular to the parent's local axis
        Vector3 parentAxisWorld = parent.TransformDirection(localAxis);
        Vector3 projectedDir = Vector3.ProjectOnPlane(dirToCamera, parentAxisWorld).normalized;

        if (projectedDir.sqrMagnitude < 0.001f)
        {
            // If direction is parallel to axis, use a default perpendicular
            projectedDir = Vector3.Cross(parentAxisWorld, Vector3.up).normalized;
            if (projectedDir.sqrMagnitude < 0.001f)
            {
                projectedDir = Vector3.Cross(parentAxisWorld, Vector3.right).normalized;
            }
        }

        // Calculate the right direction (perpendicular to both axis and camera direction)
        Vector3 rightDir = Vector3.Cross(parentAxisWorld, projectedDir).normalized;

        // Create rotation that faces camera while keeping local axis aligned
        Vector3 finalForward = Vector3.Cross(rightDir, parentAxisWorld).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(-finalForward, parentAxisWorld);

        transform.rotation = targetRotation;
    }
}

