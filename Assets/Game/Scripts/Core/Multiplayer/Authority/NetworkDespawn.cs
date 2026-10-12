// Despawning a NetworkObject without leaving its local copies floating in the world.
//
// On the server, NetworkSpawnManager.OnDespawnObject lifts every child NetworkObject of the object
// being despawned to the scene root before it goes -- including ones that were never spawned, since
// their network parent reads as empty and so passes its "first generation child" check. An NPC's
// held weapon is exactly that: EquipItemSocket instantiates a local copy of the item prefab, which
// still carries the prefab's NetworkObject, under a hand bone. So every NPC despawn on the host left
// its weapon hanging in the air at hand height, kinematic and unsaved (seen 2026-10-05: dozens round
// the Strider city as its crew despawned). A client never deparents, so it never saw them.
//
// Destroying those copies first keeps them with their holder. A spawned child (a seated rider, a
// crew member on a house) is a world object of its own and is still lifted out exactly as before.
// Every despawn in the game goes through here; NetworkDespawnTests fails on one that does not.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    public static class NetworkDespawn
    {
        /// <summary>
        /// <see cref="NetworkObject.Despawn"/>, after destroying the object's never-spawned child
        /// NetworkObjects (see the file header). Server only, like the call it wraps.
        /// </summary>
        public static void Despawn(NetworkObject networkObject, bool destroy = true)
        {
            foreach (NetworkObject copy in LocalCopies(networkObject.gameObject))
                Object.Destroy(copy.gameObject);

            networkObject.Despawn(destroy);
        }

        /// <summary>
        /// The never-spawned NetworkObjects below <paramref name="root"/> (its own excluded): local
        /// copies such as a held or worn item, which Netcode would otherwise lift to the scene root.
        /// </summary>
        public static NetworkObject[] LocalCopies(GameObject root)
        {
            NetworkObject[] all = root.GetComponentsInChildren<NetworkObject>(true);
            int count = 0;
            foreach (NetworkObject candidate in all)
                if (candidate.gameObject != root && !candidate.IsSpawned) all[count++] = candidate;

            System.Array.Resize(ref all, count);
            return all;
        }
    }
}
