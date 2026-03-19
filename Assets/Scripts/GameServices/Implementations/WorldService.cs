
using Unity.Netcode;
using UnityEngine;

public class WorldService : IWorldService
{
    public void Despawn(GameObject gameObject)
    {
        var networkObject = gameObject.GetComponent<NetworkObject>();
        if (networkObject)
        {
            networkObject.Despawn();
            return;
        }
        
        Object.Destroy(gameObject);
    }
}

