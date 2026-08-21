using System;
using System.Collections.Generic;

namespace SpaceGame.Items
{
    /// <summary>
    /// Inventory holds an array of InventorySlots, which can hold InventoryItems.
    /// It provides methods to get a slot, swap items between slots, and find an empty slot.
    /// </summary>
    public class Inventory
    {
        private InventorySlot[] slots;
    
        public event Action<int, InventorySlot> OnSlotChanged;
    
        public Inventory(int size)
        {
            slots = new InventorySlot[size];

            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new InventorySlot(i);
            }
        }
    
        public List<string> GetItemIDs()
        {
            List<string> ids = new();

            for (int i = 0; i < GetSize(); i++)
            {
                var slot = GetSlot(i);
                ids.Add(slot.IsEmpty ? null : slot.Item.ID);
            }

            return ids;
        }
    
        public void SetItem(int index, InventoryItem item)
        {
            var slot = GetSlot(index);
            slot.Item = item;
        }

        /// <summary>
        /// Puts an item into a named slot, replacing whatever is there, and tells listeners.
        ///
        /// Neither neighbour will do for a load. SetItem is deliberately silent — its callers raise
        /// OnSlotChanged themselves once a multi-slot operation has finished — so a UI restored
        /// through it never redraws. TryPlaceAt refuses an occupied slot and a null item, both of
        /// which are ordinary contents of a saved hotbar.
        /// </summary>
        public void RestoreSlot(int index, InventoryItem item)
        {
            var slot = GetSlot(index);
            if (slot == null) return;

            slot.Item = item;
            OnSlotChanged?.Invoke(index, slot);
        }
    
        public bool TryAddItem(InventoryItem item)
        {
            return TryAddItem(item, out _);
        }
    
        public bool TryAddItem(InventoryItem item, out int index)
        {
            index = FindEmptySlot();
            if (index == -1) return false;
        
            SetItem(index, item);
            OnSlotChanged?.Invoke(index, GetSlot(index));
            return true;
        }
    
        public bool TryRemoveItem(int index)
        {
            if (index >= slots.Length) return false;
            if (!slots[index].Item) return false;
        
            SetItem(index, null);
            OnSlotChanged?.Invoke(index, GetSlot(index));
            return true;
        }
    
        /// <summary>
        /// Put an item into one NAMED slot, rather than into whichever slot happens to be free
        /// first. Refuses a null item, an out-of-range index and an occupied slot, so it can never
        /// silently overwrite what is already there.
        ///
        /// TryAddItem cannot serve this: it fills FindEmptySlot(), which for a swap would drop the
        /// outgoing item into a different socket than the one the player is looking at.
        /// </summary>
        public bool TryPlaceAt(int index, InventoryItem item)
        {
            if (!item) return false;

            InventorySlot slot = GetSlot(index);
            if (slot == null || !slot.IsEmpty) return false;

            SetItem(index, item);
            OnSlotChanged?.Invoke(index, GetSlot(index));
            return true;
        }

        public bool TryMoveItem(int to, int from)
        {
            if (from == to) {return true;}
            if (to >= slots.Length || from >= slots.Length) {return false;}

            bool successfulMove;
        
            InventorySlot slotTo = GetSlot(to);
            InventorySlot slotFrom = GetSlot(from);

            if (slotTo.Item == null) {
                // The per-instance state travels with the item. Read before the assignment, because
                // assigning Item clears the state of whichever slot it lands in.
                ItemState carried = slotFrom.State;
                slotTo.Item = slotFrom.Item;
                slotFrom.Item = null;
                slotTo.State = carried;
                successfulMove = true;
            }
            else
            {
                successfulMove = SwapItems(to,from);;
            }

            if (successfulMove)
            {
                OnSlotChanged?.Invoke(from, GetSlot(from));
                OnSlotChanged?.Invoke(to, GetSlot(to));
            }
        
            return successfulMove;
        }
    
        public int GetSize()
        {
            return slots.Length;
        }

        public InventorySlot GetSlot(int index)
        {
            if(index < 0) return null;
        
            if (index < slots.Length)
            {
                return slots[index];
            } 
        
            return null;
        }

        public bool SwapItems(int indexA, int indexB)
        {
            // Both bags are read out first: assigning Item clears the destination slot's state, so a
            // swap that moved only the items would leave two half-used items claiming each other's
            // ammo and charges.
            ItemState stateA = slots[indexA].State;
            ItemState stateB = slots[indexB].State;

            (slots[indexA].Item, slots[indexB].Item) = (slots[indexB].Item, slots[indexA].Item);

            slots[indexA].State = stateB;
            slots[indexB].State = stateA;
            return true;
        }

        public int FindEmptySlot()
        {
            for (int i = 0; i < slots.Length; i++)
            {
               if (!slots[i].Item)
               { 
                   return i;
               } 
            }
            return -1;
        }
    }
}
