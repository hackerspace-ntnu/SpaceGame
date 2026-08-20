using System;
using System.Collections.Generic;

namespace SpaceGame.Items
{
    public enum BackpackCompartment
    {
        // Exterior hang-off points. Their contents are visible whether the pack is worn or not, so
        // a loaded pack carries gear openly on the astronaut's back.
        Strap,
        // The interior. One compartment, not a grid of pockets — the anchors are spread across the
        // two doors purely so the contents lay out legibly once they swing open.
        Main
    }

    // The pack's storage: two independent Inventories behind one event, each with its own
    // index space. Free of UnityEngine references so the EditMode tests can drive it directly.
    //
    // The compartments are kept apart rather than folded into one 12-slot Inventory because a
    // strap slot means "the player deliberately clipped this here" — a rock picked up off the
    // ground must never be able to displace a bedroll.
    public class BackpackContainer
    {
        // Ten outside, twelve inside. Both are the count of authored anchors on the model — raising
        // either without adding sockets in field_backpack.blend just logs a missing-socket warning.
        // The exterior is deliberately the bigger half of the two: gear lashed under the nets is
        // the thing you actually see on a worn pack.
        public const int StrapSlots = 10;
        public const int MainSlots = 12;

        public Inventory Straps { get; }
        public Inventory Main { get; }

        /// Raised for every content change, from either compartment.
        public event Action<BackpackCompartment, int, InventorySlot> OnSlotChanged;

        public BackpackContainer()
        {
            Straps = new Inventory(StrapSlots);
            Main = new Inventory(MainSlots);

            // Inventory already raises OnSlotChanged from TryAddItem and TryRemoveItem, so these
            // forwards are the ONLY place this event is raised. Raising it again alongside each
            // call would double-fire and make the display rebuild every socket twice.
            Straps.OnSlotChanged += (index, slot) => OnSlotChanged?.Invoke(BackpackCompartment.Strap, index, slot);
            Main.OnSlotChanged += (index, slot) => OnSlotChanged?.Invoke(BackpackCompartment.Main, index, slot);
        }

        public Inventory Get(BackpackCompartment compartment) =>
            compartment == BackpackCompartment.Strap ? Straps : Main;

        public InventorySlot GetSlot(BackpackCompartment compartment, int index) =>
            Get(compartment).GetSlot(index);

        public int SlotCount(BackpackCompartment compartment) => Get(compartment).GetSize();

        public bool IsFull(BackpackCompartment compartment) => Get(compartment).FindEmptySlot() == -1;

        /// Place into a specific compartment's first free slot.
        public bool TryAdd(BackpackCompartment compartment, InventoryItem item, out int index)
        {
            index = -1;

            // Inventory.TryAddItem writes a null straight into the first free slot and raises a
            // change for it, so the null has to be refused here or the pack fires phantom events
            // and burns a pocket on nothing.
            if (item == null) return false;

            // TryAddItem sets index to -1 itself when the compartment is full, and raises nothing.
            return Get(compartment).TryAddItem(item, out index);
        }

        /// Overflow target for world pickups: main compartment only. Straps are for deliberate
        /// placement, so a picked-up rock must never displace a bedroll.
        public bool TryAddToMain(InventoryItem item, out int index) =>
            TryAdd(BackpackCompartment.Main, item, out index);

        /// Put an item into one SPECIFIC slot. False if the slot is occupied, out of range, or the
        /// item is null.
        ///
        /// This is what a swap needs and TryAdd cannot give it: TryAdd fills the first free slot,
        /// so the item handed back by a swap would land somewhere other than the socket the player
        /// is aiming at — which reads as the pack shuffling its own contents.
        public bool PlaceAt(BackpackCompartment compartment, int index, InventoryItem item) =>
            Get(compartment).TryPlaceAt(index, item);

        /// Removes and returns the item, or null if the slot was empty.
        public InventoryItem TakeOut(BackpackCompartment compartment, int index)
        {
            Inventory inventory = Get(compartment);

            // Inventory.TryRemoveItem only range-checks the top end, so handing it a negative
            // index would index the array and throw. GetSlot returns null for both ends.
            InventorySlot slot = inventory.GetSlot(index);
            if (slot == null || slot.IsEmpty) return null;

            InventoryItem item = slot.Item;
            inventory.TryRemoveItem(index);   // raises the change, which the constructor forwards
            return item;
        }

        /// Every occupied slot, for rebuilding the visual display.
        public IEnumerable<(BackpackCompartment compartment, int index, InventoryItem item)> Contents()
        {
            for (int i = 0; i < Straps.GetSize(); i++)
            {
                InventorySlot slot = Straps.GetSlot(i);
                if (!slot.IsEmpty) yield return (BackpackCompartment.Strap, i, slot.Item);
            }

            for (int i = 0; i < Main.GetSize(); i++)
            {
                InventorySlot slot = Main.GetSlot(i);
                if (!slot.IsEmpty) yield return (BackpackCompartment.Main, i, slot.Item);
            }
        }
    }
}
