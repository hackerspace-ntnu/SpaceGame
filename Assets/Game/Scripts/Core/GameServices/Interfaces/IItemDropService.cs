using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Core
{
    public interface IItemDropService
    {
        /// <summary>
        /// Put <paramref name="item"/> on the ground in front of <paramref name="origin"/>.
        ///
        /// <para>
        /// <paramref name="state"/> is the per-instance bag the item was carrying — how full it
        /// was, how many charges it had spent, what it had folded inside it. The world object is a
        /// fresh instantiate of the item prefab, so without it every dropped item reverts to its
        /// authored defaults the moment it leaves the hand: a drained tank hits the sand full, and
        /// a full canister hits it empty.
        /// </para>
        /// <para>
        /// It is the whole <see cref="ItemState"/> rather than the one float that used to travel
        /// here. A charge was hand-threaded through this signature, the inventory event and both
        /// hotbar implementations while everything else in the bag was silently thrown away — so
        /// the seam already existed and was simply too narrow. One mechanism carries all of it, and
        /// the next item with state costs the drop path nothing.
        /// </para>
        /// <para>
        /// Null for a caller with nothing to hand over — loot shed by a dying agent, a rope cut off
        /// a hogtied body — which lands the item at its authored defaults, exactly as before.
        /// </para>
        /// </summary>
        void DropItem(Transform origin, InventoryItem item, ItemState state = null);
    }
}
