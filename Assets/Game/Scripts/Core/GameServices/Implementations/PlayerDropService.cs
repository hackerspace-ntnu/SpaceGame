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
        /// <summary>How fast the item leaves the hand, in metres per second.</summary>
        private const float TossForward = 1.5f;
        private const float TossUp = 1f;

        public void DropItem(Transform origin, InventoryItem item, float charge = SupplyCharge.None)
        {
            if (origin == null || item == null || item.itemPrefab == null) return;

            GameObject obj = GameServices.World.Spawn(item.itemPrefab, SpawnPoint(origin, item),
                                                     Quaternion.identity);
            if (obj == null) return;

            // A dropped reservoir keeps what it held. The spawn is a fresh instantiate of the item
            // prefab, so without this a tank emptied to 3% hits the sand at its authored starting
            // charge -- an infinite supply of air for anyone who noticed.
            if (charge >= 0f && obj.TryGetComponent(out DockableSupply supply)) supply.SetCharge(charge);

            // Stamped with the ITEM's registry id rather than the prefab's own, because that is the
            // key SaveablePrefabRegistry derives from the item table — so a dropped item persists
            // without its prefab having been touched in the editor at all.
            SaveableEntity.EnsureRuntime(obj, item.ID);

            Toss(origin.forward, obj);
        }

        /// <summary>
        /// Where the item is born: ahead of the hand by its own reach, so it does not start the
        /// frame inside the person dropping it.
        ///
        /// <para>
        /// It used to be the hand socket exactly, which was survivable while a dropped item came out
        /// at whatever scale its prefab happened to carry — usually something small. Now that
        /// <see cref="ItemWorldScale"/> sizes one to the metre it is drawn at everywhere else, a
        /// rifle born at the palm is a rifle born half inside the dropper's chest, and the physics
        /// step that untangles it is a shove, not a drop.
        /// </para>
        /// </summary>
        private static Vector3 SpawnPoint(Transform origin, InventoryItem item)
        {
            float reach = 0.5f * ItemWorldScale.SizeOf(item.itemPrefab);

            return origin.position + origin.forward * reach;
        }

        /// <summary>
        /// Give the item the speed of having been tossed, rather than a force of having been pushed.
        ///
        /// <para>
        /// <see cref="ForceMode.VelocityChange"/>, not <see cref="ForceMode.Impulse"/>. An impulse
        /// is divided by the body's mass, and until <see cref="WorldItem"/> started deriving one
        /// every item weighed the Rigidbody default of 1 kg, so the two read the same. They do not
        /// any more: the same impulse that tossed a scanner would drop a hull module straight down
        /// its own side. How far a dropped thing is lobbed is a decision about the drop, not about
        /// how heavy the thing is.
        /// </para>
        /// </summary>
        private static void Toss(Vector3 forward, GameObject droppedItem)
        {
            if (!droppedItem.TryGetComponent(out Rigidbody body)) return;

            body.AddForce(forward * TossForward + Vector3.up * TossUp, ForceMode.VelocityChange);
        }
    }
}
