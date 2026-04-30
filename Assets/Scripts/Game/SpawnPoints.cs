using System.Collections.Generic;
using UnityEngine;

public class SpawnPoints : MonoBehaviour
{
    
    [SerializeField] private List<Transform> spawnPoints;
    
    public Transform GetSpawnPoint(ulong clientId)
    {
        int index = (int)(clientId % (ulong)spawnPoints.Count);
        return spawnPoints[index].transform;
    }
}
