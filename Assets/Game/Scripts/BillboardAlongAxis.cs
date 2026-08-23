using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Presentation;

public class BillboardAlongAxis : MonoBehaviour
{
    [SerializeField] private Vector3 localAxis = Vector3.up;
    private Camera mainCamera;

    private void Start()
    {
        FindCamera();
    }

    /// <summary>
    /// Binds to the camera this peer is actually looking through.
    /// <para>
    /// This was <c>FindWithTag("Player")</c> followed by a <c>GetComponentInChildren&lt;Camera&gt;</c>.
    /// Every player object in a session carries the "Player" tag, so the tag search returned an
    /// arbitrary body — and a billboard that turns to face somebody else's viewpoint is edge-on to
    /// yours. <see cref="GameplayMenuScope.FindLocalPlayer()"/> is the project's one answer to which
    /// player this machine is driving; the parameterless overload, because this component sits on a
    /// world object and has no owning player to read off its parents.
    /// </para>
    /// <para>
    /// The camera is only taken when it is active and enabled. A remote player's camera object is
    /// deactivated by <c>PlayerController.DisablePlayer</c>, and the local player's is switched off
    /// while they are mounted and a third-person rig renders instead — in both cases the thing
    /// actually drawing the frame is whatever <c>Camera.main</c> resolves to, which is why that
    /// stays the fallback rather than a last resort.
    /// </para>
    /// </summary>
    private void FindCamera()
    {
        PlayerController player = GameplayMenuScope.FindLocalPlayer();
        Camera own = player != null ? player.PlayerCamera : null;

        if (own != null && own.isActiveAndEnabled)
        {
            mainCamera = own;
            return;
        }

        mainCamera = Camera.main;
    }

    private void LateUpdate()
    {
        // Re-resolved when the bound camera stops rendering, not only when it is destroyed:
        // mounting switches the player's own camera off and leaves the object alive, so a billboard
        // that only checked for null would keep facing a camera nobody is looking through.
        if (mainCamera == null || !mainCamera.isActiveAndEnabled)
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

