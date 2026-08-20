using UnityEngine;

namespace SpaceGame.Core
{
    public interface IWorldService
    {
        public void Despawn(GameObject gameObject);

        /// <summary>
        /// Instantiate a prefab so that every peer sees it. Offline this is a plain Instantiate;
        /// networked it also spawns the NetworkObject, optionally handing ownership to a client.
        ///
        /// Server-only. Callers on a client must route through an RPC first — a client-side
        /// NetworkObject.Spawn throws, and silently Instantiating instead is exactly the
        /// "works in solo, invisible in multiplayer" bug this replaces.
        /// </summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                                ulong ownerClientId = NetworkSpawn.NoOwner);
    }

    /// <summary>Sentinel for "leave this object owned by the server".</summary>
    public static class NetworkSpawn
    {
        public const ulong NoOwner = ulong.MaxValue;
    }
}
