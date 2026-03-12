
using UnityEngine;

public class WorldService : IWorldService
{
    public void Despawn(GameObject gameObject)
    {
       Object.Destroy(gameObject);
    }
}

