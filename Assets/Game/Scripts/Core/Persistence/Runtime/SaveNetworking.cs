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
        /// <summary>Spawns a restored object across the network when it is one Netcode owns.</summary>
        public static void SpawnIfNetworked(GameObject instance)
        {
            if (instance == null || !Network.Server) return;

            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null || networkObject.IsSpawned) return;

            try
            {
                networkObject.Spawn();
            }
            catch (System.Exception e)
            {
                // A prefab missing from NetworkManager's prefab list throws here. That is a wiring
                // error worth reporting loudly, but not one worth aborting the rest of the world's
                // restore over — the object stays host-only rather than taking the load down.
                Debug.LogError($"[Save] Could not network-spawn restored object '{instance.name}': {e.Message}. " +
                               "Is its prefab registered with NetworkManager?", instance);
            }
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
