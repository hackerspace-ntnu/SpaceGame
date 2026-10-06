using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Test doubles shared between the editor test fixtures.
    ///
    /// <para>
    /// Nested on purpose. A top-level MonoBehaviour in an editor folder has a MonoScript Unity
    /// classes as an editor script, and AddComponent refuses it (returns null, with a console
    /// error). A nested type has no MonoScript of its own and is added like any runtime component.
    /// </para>
    /// </summary>
    public static class TestDoubles
    {
        /// <summary>
        /// A hotbar that is a COMPONENT.
        ///
        /// A plain C# adapter is enough when a container is handed the interface directly. It is not
        /// enough for a request: the server resolves the hotbar off the body named in the message,
        /// with <c>GetComponentInChildren</c>, so a hotbar that is not a component is invisible to
        /// exactly the code under test. Shared by WallInventoryTests and BrokenTransmitterTests.
        /// </summary>
        public sealed class HotbarBehaviour : MonoBehaviour, IPlayerInventory
        {
            private readonly PlayerInventory inner = new(4);

            public int SelectedSlotIndex => inner.SelectedSlotIndex;

            public event Action<InventorySlot> OnSlotSelected
            {
                add => inner.OnSlotSelected += value;
                remove => inner.OnSlotSelected -= value;
            }

            public event Action<int, InventorySlot> OnSlotChanged
            {
                add => inner.OnSlotChanged += value;
                remove => inner.OnSlotChanged -= value;
            }

            public event Action<InventoryItem, ItemState> OnItemDropped
            {
                add => inner.OnItemDropped += value;
                remove => inner.OnItemDropped -= value;
            }

            public bool TryAddItem(InventoryItem item) => inner.TryAddItem(item);
            public bool TryRemoveItem(int index) => inner.TryRemoveItem(index);
            public void SelectSlot(int slotIndex) => inner.SelectSlot(slotIndex);

            public bool TrySetSlot(int index, InventoryItem item)
            {
                inner.SetSlot(index, item);
                return true;
            }

            public void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot) =>
                inner.RestoreSlots(items, selectedSlot);

            public int GetInventorySize() => inner.GetInventorySize();
            public InventorySlot GetSlot(int index) => inner.GetSlot(index);
            public InventorySlot GetSelectedSlot() => inner.GetSelectedSlot();

            public InventoryItem GetSelectedItem()
            {
                InventorySlot slot = GetSelectedSlot();
                return slot == null || slot.IsEmpty ? null : slot.Item;
            }
        }
    }
}
