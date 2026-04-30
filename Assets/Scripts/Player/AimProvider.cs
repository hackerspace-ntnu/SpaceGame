using UnityEngine;

public class AimProvider : MonoBehaviour
{

    [SerializeField] private Camera playerCamera;

    public Ray GetAimRay()
    {
        return new Ray(playerCamera.transform.position, playerCamera.transform.forward);
    }

    public RaycastHit? GetRayCast(float maxDistance = 100f)
    {
        Ray ray = GetAimRay();
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
        {
            return hit;
        }
        Debug.LogWarning("Raycast did not hit anything.");
        return null;
    }
}
