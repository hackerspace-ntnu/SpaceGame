using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// The Netcode-shaped edges of restoring world objects, kept in one place.
    ///
    /// The save system runs on the server in a game that is always hosted — even singleplayer goes
    /// through <c>StartHost</c> — so a restored object that carries a NetworkObject has to be
    /// network-spawned or it exists on the host alone and no client ever sees it. Equally, a
    /// restored object destroyed with plain <c>Destroy</c> leaves clients holding a ghost.
    /// </summary>
    public static class SaveNetworking
    {
        /// <summary>
        /// Spawns a restored object across the network when it is one Netcode owns.
        ///
        /// <para>
        /// <b>The registration check is ours, not Netcode's.</b> This used to wrap
        /// <c>Spawn()</c> in a try/catch on the assumption that an unregistered prefab throws here.
        /// It does not. A server-side dynamic spawn never consults the prefab table: it spawns
        /// locally and sends a <c>CreateObjectMessage</c> keyed by <c>GlobalObjectIdHash</c>, and it
        /// is the CLIENT that fails to find the hash — quietly, on somebody else's machine. So the
        /// host got a restored object it could see, re-saved it on every autosave forever, and no
        /// client ever had it, with nothing in the host's console to say so. Asking the prefab table
        /// here is the only place the answer is knowable on the machine that can report it.
        /// </para>
        /// </summary>
        public static void SpawnIfNetworked(GameObject instance)
        {
            if (instance == null || !Network.Server) return;

            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null || networkObject.IsSpawned) return;

            if (!IsRegisteredPrefab(networkObject))
            {
                Debug.LogError(
                    $"[Save] Restored object '{PrefabName(instance)}' " +
                    $"(prefab hash {networkObject.PrefabIdHash}) is NOT in " +
                    "NetworkManager.NetworkConfig.Prefabs, so no client can construct it. Refusing " +
                    "to spawn it: spawning would leave it visible to the host alone while every save " +
                    "from here on kept writing it back. Add its prefab to the NetworkManager prefab " +
                    "list (Assets/Game/Prefabs/Systems/NetworkManager.prefab), not to " +
                    "DefaultNetworkPrefabs.asset, which regenerates itself.", instance);
                return;
            }

            try
            {
                networkObject.Spawn();
            }
            catch (System.Exception e)
            {
                // Belt and braces for everything the check above cannot see — a NetworkManager that
                // is shutting down, a nested NetworkObject, an object already owned elsewhere. Worth
                // reporting loudly, not worth aborting the rest of the world's restore over.
                Debug.LogError($"[Save] Could not network-spawn restored object '{instance.name}': {e.Message}.", instance);
            }
        }

        /// <summary>
        /// Whether Netcode can rebuild this object on a client from its hash alone.
        ///
        /// Keyed by <see cref="NetworkObject.PrefabIdHash"/> rather than by the prefab asset,
        /// because all we hold here is an instance — and the hash is exactly the key the receiving
        /// client will look up. In-scene-placed objects are exempt: a client builds those from its
        /// own copy of the scene and they are never in the prefab list.
        /// </summary>
        private static bool IsRegisteredPrefab(NetworkObject networkObject)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.NetworkConfig?.Prefabs == null) return true;

            if (networkObject.IsSceneObject.GetValueOrDefault()) return true;

            return manager.NetworkConfig.Prefabs.NetworkPrefabOverrideLinks
                          .ContainsKey(networkObject.PrefabIdHash);
        }

        /// <summary>The prefab's name, with the "(Clone)" an Instantiate leaves behind taken off.</summary>
        private static string PrefabName(GameObject instance)
        {
            string name = instance.name;
            int clone = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
            return clone >= 0 ? name.Substring(0, clone) : name;
        }

        /// <summary>Removes an object on every peer, not just here.</summary>
        public static void DespawnAndDestroy(GameObject target)
        {
            if (target == null) return;

            var networkObject = target.GetComponent<NetworkObject>();

            if (Network.Server && networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn();
                return;
            }

            Destroy(target);
        }

        /// <summary>
        /// Destroys an object in whichever mode we are in.
        ///
        /// <see cref="Object.Destroy"/> is deferred to the end of the frame and does nothing at all
        /// outside play mode, so an editor tool or an EditMode test that drives the world store gets
        /// an object it was told was deleted — which is how the tombstone path first looked correct
        /// and was not. <see cref="Object.DestroyImmediate"/> is the edit-mode equivalent and is
        /// unsafe during play, hence the split rather than picking one.
        /// </summary>
        public static void Destroy(GameObject target)
        {
            if (target == null) return;

            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
