using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Core
{
    public class WorldService : IWorldService
    {
        public void Despawn(GameObject gameObject)
        {
            // Before the object is gone, because the save system needs to read what it was. Only
            // authored world objects leave a mark; everything else falls through. Without this, a
            // picked-up crate that was placed in a chunk scene by hand is back the next time that
            // chunk streams in.
            SaveManager.NotifyDestroyed(gameObject);

            var networkObject = gameObject.GetComponent<NetworkObject>();
            if (Network.IsNetworked && networkObject && networkObject.IsSpawned)
            {
                networkObject.Despawn(false);
            }
        
            Object.Destroy(gameObject);
        }
    }
}
