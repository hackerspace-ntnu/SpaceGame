using UnityEngine;

public class AimProvider : MonoBehaviour
{

    [SerializeField] private Camera playerCamera;

    public RaycastHit? getRayCast(float maxDistance = 100f)
    {
        if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, maxDistance))
        {
            return hit;
        }
        Debug.LogWarning("Raycast did not hit anything.");
        return null;
    }
}