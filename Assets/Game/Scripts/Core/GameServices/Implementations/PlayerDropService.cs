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

        /// <summary>
        /// How far above the drop origin the ground probe starts, in metres. Must stay inside the
        /// dropper: below a 3 m player's head when the origin is the hand, and above the feet when
        /// the origin is an NPC's root. See <see cref="ClearOfGround"/>.
        /// </summary>
        private const float GroundProbeLift = 0.5f;

        public GameObject DropItem(Transform origin, InventoryItem item, ItemState state = null)
        {
            if (origin == null || item == null || item.itemPrefab == null) return null;

            GameObject obj = GameServices.World.Spawn(item.itemPrefab, SpawnPoint(origin, item),
                                                     Quaternion.identity);
            if (obj == null) return null;

            // Everything the slot remembered about this instance rides the object it becomes, and
            // goes back into whichever slot picks it up. Uninterpreted while it lies there — see
            // PickupableItem, which is the custodian and explains why applying a bag to an item
            // nobody is holding would be wrong.
            if (obj.TryGetComponent(out PickupableItem pickup)) pickup.Remember(state);

            // The one key that is also RENDERED on the ground. A dropped reservoir paints a gauge
            // the player reads off the sand, so the instance has to be told its fill or a tank
            // emptied to 3% lies there reading full -- an infinite supply of air for anyone who
            // noticed. Not a second channel for the charge: the value still comes out of the one
            // bag above, and this is a component being handed the one key it draws.
            float charge = SupplyCharge.Read(state);
            SupplyReservoir supply = charge >= 0f ? SupplyReservoir.On(obj) : null;
            if (supply != null) supply.SetCharge(charge);

            // Stamped with the ITEM's registry id rather than the prefab's own, because that is the
            // key SaveablePrefabRegistry derives from the item table — so a dropped item persists
            // without its prefab having been touched in the editor at all.
            SaveableEntity.EnsureRuntime(obj, item.ID);

            Toss(origin.forward, obj);
            return obj;
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

            return ClearOfGround(origin.position, origin.position + origin.forward * reach, reach);
        }

        /// <summary>
        /// Lifts <paramref name="point"/> until it is at least <paramref name="reach"/> above the
        /// ground under <paramref name="origin"/>.
        ///
        /// <para>
        /// An item born with its pivot at or below a TerrainCollider or MeshCollider surface is not
        /// pushed back out: it falls straight through and keeps falling (measured: a rifle born with
        /// its pivot 0.15 m up lands, one born at the surface is 80 m down two seconds later). A hand
        /// never put it there, but a body does — <c>EntityLootTable</c> drops from the dead NPC's
        /// root, which is its feet if it dies standing and its pelvis, turned with the body, if it
        /// dies knocked down; face down, "ahead" is into the sand. Most battlefield loot fell out of
        /// the world, and the next load landed every record of it back on the surface at its X/Z
        /// (<c>WorldSaveStore.LandAwaitingGround</c>) — weapons appearing from nowhere.
        /// </para>
        ///
        /// <para>
        /// The probe starts <see cref="GroundProbeLift"/> above the origin, inside the dropper's own
        /// body, so it never meets a ceiling over the dropper's head. Ground further below the point
        /// than the item's reach is left alone: a drop off a ledge still falls.
        /// </para>
        /// </summary>
        private static Vector3 ClearOfGround(Vector3 origin, Vector3 point, float reach)
        {
            Vector3 probe = new Vector3(point.x, origin.y + GroundProbeLift, point.z);
            float depth = probe.y - (point.y - reach);
            if (depth <= 0f) return point;

            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit ground, depth,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return point;

            point.y = Mathf.Max(point.y, ground.point.y + reach);
            return point;
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
