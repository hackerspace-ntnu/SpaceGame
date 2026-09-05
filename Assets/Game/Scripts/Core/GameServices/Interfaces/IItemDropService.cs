using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Core
{
    public interface IItemDropService
    {
        /// <summary>
        /// Put <paramref name="item"/> on the ground in front of <paramref name="origin"/>.
        ///
        /// <paramref name="charge"/> is how full it was (<see cref="SpaceGame.Items.SupplyCharge"/>),
        /// or <c>SupplyCharge.None</c> for anything that holds nothing. The world object is a fresh
        /// instantiate of the item prefab, so without it a dropped tank reverts to its authored
        /// starting charge the moment it leaves the hand.
        /// </summary>
        void DropItem(Transform origin, InventoryItem item, float charge = -1f);
    }
}
