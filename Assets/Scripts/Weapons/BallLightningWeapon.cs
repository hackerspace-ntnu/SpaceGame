using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class BallLightningWeapon : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Transform firePoint;
    [SerializeField] private BallLightningProjectile projectilePrefab;
    [Tooltip("Optional: Assign the gun barrel transform for projectile spawn position.")]
    [SerializeField] private Transform barrelTransform;

    [Header("Shot Settings")]
    [SerializeField] private float fireRate = 6f;
    [SerializeField] private float spawnOffset = 0.8f;
    [SerializeField] private float maxAimDistance = 300f;
    [SerializeField] private LayerMask aimMask = ~0;

    private InputAction attackAction;
    private float nextFireTime;
    private NetworkObject networkObject;

    private void Awake()
    {
        networkObject = GetComponentInParent<NetworkObject>();

        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }
    }

    private void OnEnable()
    {
        attackAction = InputSystem.actions.FindAction("Attack");
        attackAction?.Enable();
    }

    private void OnDisable()
    {
        attackAction?.Disable();
    }

    private void Update()
    {
        if (!HasLocalAuthority())
        {
            return;
        }

        if (projectilePrefab == null || attackAction == null)
        {
            return;
        }

        if (!attackAction.WasPressedThisFrame())
        {
            return;
        }

        if (Time.time < nextFireTime)
        {
            return;
        }

        Fire();
        nextFireTime = Time.time + (1f / Mathf.Max(0.01f, fireRate));
    }

    private bool HasLocalAuthority()
    {
        if (networkObject == null)
        {
            return true;
        }

        if (!networkObject.IsSpawned)
        {
            return false;
        }

        return networkObject.IsOwner;
    }

    private void Fire()
    {
        Transform origin = barrelTransform != null ? barrelTransform : (firePoint != null ? firePoint : (aimCamera != null ? aimCamera.transform : transform));

        Vector3 spawnPosition = origin.position + origin.forward * spawnOffset;
        Vector3 shootDirection = origin.forward;

        if (aimCamera != null)
        {
            Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 targetPoint = ray.origin + ray.direction * maxAimDistance;

            if (Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore))
            {
                targetPoint = hit.point;
            }

            shootDirection = (targetPoint - spawnPosition).normalized;
            if (shootDirection.sqrMagnitude < 0.0001f)
            {
                shootDirection = origin.forward;
            }
        }

        BallLightningProjectile projectile = Instantiate(projectilePrefab, spawnPosition, Quaternion.LookRotation(shootDirection, Vector3.up));
        projectile.Initialize(shootDirection, transform, spawnPosition);
    }
}
