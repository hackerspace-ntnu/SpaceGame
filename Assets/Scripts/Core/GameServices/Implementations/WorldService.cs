
using Unity.Netcode;
using UnityEngine;

public class WorldService : IWorldService
{
    public void Despawn(GameObject gameObject)
    {
        var networkObject = gameObject.GetComponent<NetworkObject>();
        if (Network.IsNetworked && networkObject && networkObject.IsSpawned)
        {
            networkObject.Despawn(false);
        }
        
        Object.Destroy(gameObject);
    }
}

