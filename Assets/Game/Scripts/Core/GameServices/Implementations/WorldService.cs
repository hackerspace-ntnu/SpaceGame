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

        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                                ulong ownerClientId = NetworkSpawn.NoOwner)
        {
            if (prefab == null)
                return null;

            GameObject instance = Object.Instantiate(prefab, position, rotation);

            // Opt the new object in to saving, by the same rule a scene load applies to everything it
            // hydrates. Without this a runtime-spawned world object persisted only as well as whoever
            // authored its prefab happened to remember — the ornithopter the wing pack deploys saved
            // its pose and velocity but not that somebody was flying it, because MountSaveable is
            // added by the policy and the policy had never been run over a spawn.
            //
            // NeedsSaving is what keeps this from being reckless: projectiles are on its transient
            // blacklist and effects match none of its clauses, so they fall straight through.
            SaveablePolicy.EnsureSpawned(instance);

            if (!Network.IsNetworked)
                return instance;

            // Offline-shaped call arriving on a client. Spawning here would throw, and returning the
            // local instance would resurrect the very bug this method exists to prevent, so say so.
            if (!Network.Server)
            {
                Debug.LogError($"[WorldService] Spawn('{prefab.name}') called on a client. Route it " +
                               "through a ServerRpc — the object would otherwise exist only here.");
                Object.Destroy(instance);
                return null;
            }

            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                // A prefab with no NetworkObject can never replicate. It still works locally, which is
                // why this goes unnoticed until a second player joins.
                Debug.LogError($"[WorldService] Prefab '{prefab.name}' has no NetworkObject, so it will " +
                               "only ever exist on the server. Add one and register it in DefaultNetworkPrefabs.",
                               instance);
                return instance;
            }

            if (ownerClientId == NetworkSpawn.NoOwner)
                networkObject.Spawn(true);
            else
                networkObject.SpawnWithOwnership(ownerClientId, true);

            return instance;
        }
    }
}
