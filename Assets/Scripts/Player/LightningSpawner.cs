using UnityEngine;
using UnityEngine.InputSystem;

public class LightningSpawner : MonoBehaviour
{
    [Header("Input")]
    public InputActionReference actionInput;

    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GameObject lightningVFXPrefab;

    [Header("Settings")]
    [SerializeField] private float raycastDistance = 500f;
    [SerializeField] private float spawnHeightOffset = 10f;
    [SerializeField] private LayerMask raycastMask = ~0;
    [SerializeField] private Vector3 spawnRotation = new Vector3(90f, 0f, 0f);

    private void OnEnable()
    {
        actionInput.action.Enable();
        actionInput.action.performed += OnAction;
    }

    private void OnDisable()
    {
        actionInput.action.performed -= OnAction;
        actionInput.action.Disable();
    }

    private void OnAction(InputAction.CallbackContext context)
    {
        if (lightningVFXPrefab == null)
        {
            Debug.LogWarning("LightningSpawner: No Lightning VFX prefab assigned.");
            return;
        }

        if (playerCamera == null)
        {
            Debug.LogWarning("LightningSpawner: No camera assigned.");
            return;
        }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        Vector3 spawnPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, raycastDistance, raycastMask))
        {
            spawnPoint = hit.point + Vector3.up * spawnHeightOffset;
        }
        else
        {
            // Fallback: use a point along the ray at max distance
            spawnPoint = ray.GetPoint(raycastDistance) + Vector3.up * spawnHeightOffset;
        }

        Instantiate(lightningVFXPrefab, spawnPoint, Quaternion.Euler(spawnRotation));
    }
}
