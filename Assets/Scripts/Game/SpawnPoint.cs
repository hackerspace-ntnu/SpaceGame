using System.Collections.Generic;
using UnityEngine;

public class SpawnPoint : MonoBehaviour
{
    [SerializeField] private float spawnRadius = 10f;
    [SerializeField] private LayerMask blockingLayers;
    [Tooltip("Lift above the sampled ground point. The player capsule's bottom sits ~1m below " +
             "the prefab pivot, so spawning exactly on the surface buries half the collider and " +
             "PhysX sometimes resolves that penetration downwards, dropping the player through " +
             "the terrain.")]
    [SerializeField] private float groundClearance = 1.2f;
    public Vector3 GetSpawnPoint()
    {
        return GetValidSpawnPoint(transform.position, spawnRadius, blockingLayers);
    }
    private Vector3 GetValidSpawnPoint(Vector3 shipPosition, float radius, LayerMask layer)
    {
        for (int i = 0; i < 20; i++) 
        {
            Vector3 randomPoint = GetRandomPoint(shipPosition, radius);

            if (TryGetGroundPoint(randomPoint, out Vector3 groundPoint))
            {
                if (IsSpawnPointClear(groundPoint, 1.5f, layer))
                {
                    return groundPoint + Vector3.up * groundClearance;
                }
            }
        }
        return shipPosition;
    }
    
    private Vector3 GetRandomPoint(Vector3 center, float radius)
    {
        Vector2 randomCircle = Random.insideUnitCircle * radius;
        return new Vector3(center.x + randomCircle.x, center.y + 50f, center.z + randomCircle.y);
    }
    
    private bool TryGetGroundPoint(Vector3 origin, out Vector3 hitPoint)
    {
        RaycastHit hit;

        if (Physics.Raycast(origin, Vector3.down, out hit, 100f))
        {
            hitPoint = hit.point;
            return true;
        }

        hitPoint = Vector3.zero;
        return false;
    }
    
    private bool IsSpawnPointClear(Vector3 position, float radius, LayerMask blockingLayers)
    {
        return !Physics.CheckSphere(position, radius, blockingLayers);
    }
}
