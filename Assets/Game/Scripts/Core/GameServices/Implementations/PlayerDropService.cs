using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.Core
{
    /// <summary>
    /// Puts an item into the world as a physical object — a player emptying a hotbar slot, or a
    /// dead agent shedding its loot.
    ///
    /// This used to carry a <c>[Rpc(SendTo.Server)]</c> method, which did nothing at all: Netcode's
    /// code generator only rewrites RPC methods on a <see cref="Unity.Netcode.NetworkBehaviour"/>,
    /// and this is a plain service object. The attribute compiled, read as networked, and left an
    /// ordinary method that ran wherever it was called — so a client "sending to the server" simply
    /// ran the drop locally and hit <c>NetworkObject.Spawn()</c> on a machine that is not allowed to
    /// spawn. Every AI death did it, because HealthComponent.OnDeath fires on clients too when the
    /// replicated health crosses zero.
    ///
    /// There is no RPC here now, and there should not be one. Dropping is a change to the shared
    /// world, so it belongs to the server by the same rule as every other such change, and the two
    /// callers are already server-side decisions. A client that reaches here is a bug in the caller,
    /// and <see cref="IWorldService.Spawn"/> says so rather than quietly creating a local ghost.
    /// </summary>
    public class PlayerDropService : IItemDropService
    {
        public void DropItem(Transform origin, InventoryItem item)
        {
            if (origin == null || item == null || item.itemPrefab == null) return;

            GameObject obj = GameServices.World.Spawn(item.itemPrefab, origin.position, Quaternion.identity);
            if (obj == null) return;

            // Stamped with the ITEM's registry id rather than the prefab's own, because that is the
            // key SaveablePrefabRegistry derives from the item table — so a dropped item persists
            // without its prefab having been touched in the editor at all.
            SaveableEntity.EnsureRuntime(obj, item.ID);

            ApplyForce(origin.forward, obj);
        }

        private void ApplyForce(Vector3 direction, GameObject droppedItem)
        {
            Rigidbody rb = droppedItem.GetComponent<Rigidbody>();

            if (rb == null)
                return;

            rb.isKinematic = false;

            Vector3 force =
                direction * 1.5f +
                Vector3.up * 1.0f;

            rb.AddForce(force, ForceMode.Impulse);
        }
    }
}
