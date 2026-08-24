// How an NPC comes into the world: visible on every machine, and saved by nobody.
//
// Deliberately not GameServices.World.Spawn. That opts its instance into the save system, and an
// NPC that saves itself is an NPC that comes back twice — everything that spawns NPCs here (an
// NpcGroup record folding back out into a caravan, a mount putting its rider in the saddle) is
// itself persisted and rebuilds its own members on load, so a second independent record would
// restore a duplicate standing beside the real one.
using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    public static class NpcSpawn
    {
        /// <summary>
        /// Instantiate <paramref name="prefab"/> and, in a session, spawn it for every peer.
        ///
        /// <para>
        /// Authority only. Callers reach this behind their own <see cref="Network.Simulates"/>
        /// check, which also answers true offline; a client that called it anyway would get an
        /// instance nobody else can see.
        /// </para>
        /// </summary>
        /// <param name="context">Logged as the object to select when a spawn fails.</param>
        public static GameObject Create(GameObject prefab, Vector3 position, Quaternion rotation,
                                        UnityEngine.Object context = null)
        {
            if (prefab == null) return null;

            GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation);

            if (!Network.IsNetworked || !Network.Server) return instance;
            if (!instance.TryGetComponent(out NetworkObject netObj) || netObj.IsSpawned) return instance;

            try
            {
                netObj.Spawn();
            }
            catch (Exception e)
            {
                // An unregistered prefab fails ONLY on clients — the server instantiates its own
                // copy and never consults the list — so this is deliberately not silent.
                Debug.LogError($"[NpcSpawn] '{prefab.name}' has a NetworkObject but could not be " +
                               $"spawned — is it in the NetworkManager's prefab list? {e.Message}",
                               context != null ? context : instance);
            }

            return instance;
        }
    }
}
