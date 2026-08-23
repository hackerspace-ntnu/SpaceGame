using UnityEngine;

namespace SpaceGame.Items
{
    public class InventorySlot
    {
        private InventoryItem item;

        public int Index { get; private set; }

        public InventoryItem Item
        {
            get => item;
            set
            {
                // A different item means the old item's per-instance state describes nothing. Cleared
                // here rather than at each call site, because every path that empties or replaces a
                // slot goes through this setter and any one of them forgetting would leave a fresh
                // pistol holding the last one's ammo count.
                if (item != value) State = null;
                item = value;
            }
        }

        /// <summary>
        /// What the item in this slot has become — ammo spent, charges used, a hook still attached.
        ///
        /// <para>
        /// Null means "at its authored defaults", which is the ordinary case and is what a slot
        /// starts at. The bag is written when the item is unequipped and applied when it is equipped
        /// again (see <c>EquipmentController</c>), because the held object is a fresh instance every
        /// time and cannot carry anything across itself.
        /// </para>
        /// </summary>
        public ItemState State { get; set; }

        public bool IsEmpty => Item == null;

        public InventorySlot(int index)
        {
            Index = index;
            Item = null;
        }

        public override string ToString()
        {
            return "Slot " + (!IsEmpty ? Item.itemName : "Empty");
        }
    }
}
