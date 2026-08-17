using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Taking an item out of the pack when the hotbar is already full.
    ///
    /// The hotbar under test is the REAL <see cref="PlayerInventory"/> behind a thin adapter, not a
    /// stand-in. That matters: the one thing this feature can plausibly get wrong is that
    /// PlayerInventory.TryRemoveItem quietly clears SelectedSlotIndex when it removes the selected
    /// slot, and a hand-written fake would have to reproduce that bug on purpose to catch it.
    /// </summary>
    public class BackpackSwapTests
    {
        private readonly List<InventoryItem> created = new();
        private readonly List<GameObject> spawned = new();

        private InventoryItem Item(string name)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = name;
            created.Add(item);
            return item;
        }

        private BackpackObject Pack()
        {
            var go = new GameObject("TestPack");
            spawned.Add(go);
            return go.AddComponent<BackpackObject>();   // Awake builds Container
        }

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            foreach (InventoryItem item in created)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);

            spawned.Clear();
            created.Clear();
        }

        /// The real PlayerInventory, exposed through the interface the pack talks to.
        private sealed class Hotbar : IPlayerInventory
        {
            private readonly PlayerInventory inner;

            public Hotbar(int size) => inner = new PlayerInventory(size);

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

            public event Action<InventoryItem> OnItemDropped
            {
                add => inner.OnItemDropped += value;
                remove => inner.OnItemDropped -= value;
            }

            public bool TryAddItem(InventoryItem item) => inner.TryAddItem(item);
            public bool TryRemoveItem(int index) => inner.TryRemoveItem(index);
            public void SelectSlot(int slotIndex) => inner.SelectSlot(slotIndex);

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

        private Hotbar FullHotbar(int size, out InventoryItem[] items)
        {
            var hotbar = new Hotbar(size);
            items = new InventoryItem[size];

            for (int i = 0; i < size; i++)
            {
                items[i] = Item("hotbar" + i);
                Assert.IsTrue(hotbar.TryAddItem(items[i]), "hotbar should have accepted item " + i);
            }

            return hotbar;
        }

        // ---------------------------------------------------------------- PlaceAt

        [Test]
        public void PlaceAt_FillsTheNamedSlot()
        {
            var pack = new BackpackContainer();
            InventoryItem rock = Item("rock");

            Assert.IsTrue(pack.PlaceAt(BackpackCompartment.Main, 7, rock));
            Assert.AreSame(rock, pack.GetSlot(BackpackCompartment.Main, 7).Item);

            // The point of PlaceAt: TryAdd would have put it in slot 0.
            Assert.IsTrue(pack.GetSlot(BackpackCompartment.Main, 0).IsEmpty);
        }

        [Test]
        public void PlaceAt_RefusesAnOccupiedSlot()
        {
            var pack = new BackpackContainer();
            InventoryItem first = Item("first");
            InventoryItem second = Item("second");

            pack.PlaceAt(BackpackCompartment.Main, 3, first);

            Assert.IsFalse(pack.PlaceAt(BackpackCompartment.Main, 3, second));
            Assert.AreSame(first, pack.GetSlot(BackpackCompartment.Main, 3).Item,
                           "a refused place must not overwrite what was there");
        }

        [Test]
        public void PlaceAt_RefusesNullAndOutOfRange()
        {
            var pack = new BackpackContainer();

            Assert.IsFalse(pack.PlaceAt(BackpackCompartment.Main, 0, null));
            Assert.IsFalse(pack.PlaceAt(BackpackCompartment.Main, -1, Item("x")));
            Assert.IsFalse(pack.PlaceAt(BackpackCompartment.Main, BackpackContainer.MainSlots, Item("y")));
        }

        [Test]
        public void PlaceAt_RaisesExactlyOneChange()
        {
            var pack = new BackpackContainer();
            int changes = 0;
            int seenIndex = -1;

            pack.OnSlotChanged += (compartment, index, slot) => { changes++; seenIndex = index; };
            pack.PlaceAt(BackpackCompartment.Main, 5, Item("lamp"));

            Assert.AreEqual(1, changes, "the display rebuilds per event, so a double-fire is a double rebuild");
            Assert.AreEqual(5, seenIndex);
        }

        // ---------------------------------------------------------------- the swap

        [Test]
        public void FullHotbar_SwapsWithTheSelectedSlot()
        {
            BackpackObject pack = Pack();
            Hotbar hotbar = FullHotbar(4, out InventoryItem[] held);
            hotbar.SelectSlot(2);

            InventoryItem stowed = Item("stowed");
            pack.Container.PlaceAt(BackpackCompartment.Main, 6, stowed);

            Assert.IsTrue(pack.TryTakeToHotbar(BackpackCompartment.Main, 6, hotbar));

            Assert.AreSame(stowed, hotbar.GetSlot(2).Item, "the pack item belongs in the selected slot");
            Assert.AreSame(held[2], pack.Container.GetSlot(BackpackCompartment.Main, 6).Item,
                           "the held item belongs in the pocket that was aimed at, not the first free one");
        }

        [Test]
        public void FullHotbar_KeepsTheSlotSelected()
        {
            BackpackObject pack = Pack();
            Hotbar hotbar = FullHotbar(4, out _);
            hotbar.SelectSlot(1);

            pack.Container.PlaceAt(BackpackCompartment.Main, 0, Item("stowed"));
            pack.TryTakeToHotbar(BackpackCompartment.Main, 0, hotbar);

            // PlayerInventory.TryRemoveItem sets SelectedSlotIndex to -1 behind the swap's back.
            // Without the closing SelectSlot the player finishes holding nothing.
            Assert.AreEqual(1, hotbar.SelectedSlotIndex);
            Assert.IsNotNull(hotbar.GetSelectedItem());
        }

        [Test]
        public void FullHotbar_LosesNothing()
        {
            BackpackObject pack = Pack();
            Hotbar hotbar = FullHotbar(4, out InventoryItem[] held);
            hotbar.SelectSlot(3);

            InventoryItem stowed = Item("stowed");
            pack.Container.PlaceAt(BackpackCompartment.Main, 11, stowed);

            pack.TryTakeToHotbar(BackpackCompartment.Main, 11, hotbar);

            var everything = new List<InventoryItem>();
            for (int i = 0; i < hotbar.GetInventorySize(); i++)
                if (!hotbar.GetSlot(i).IsEmpty) everything.Add(hotbar.GetSlot(i).Item);

            foreach ((_, _, InventoryItem item) in pack.Container.Contents()) everything.Add(item);

            Assert.AreEqual(5, everything.Count, "four hotbar items plus the stowed one, none dropped or duplicated");
            CollectionAssert.Contains(everything, stowed);
            foreach (InventoryItem item in held) CollectionAssert.Contains(everything, item);
        }

        [Test]
        public void FullHotbar_WithNothingSelected_SwapsWithSlotZero()
        {
            BackpackObject pack = Pack();
            Hotbar hotbar = FullHotbar(4, out InventoryItem[] held);

            Assert.AreEqual(-1, hotbar.SelectedSlotIndex, "precondition: nothing selected");

            InventoryItem stowed = Item("stowed");
            pack.Container.PlaceAt(BackpackCompartment.Main, 2, stowed);

            Assert.IsTrue(pack.TryTakeToHotbar(BackpackCompartment.Main, 2, hotbar));
            Assert.AreSame(stowed, hotbar.GetSlot(0).Item);
            Assert.AreSame(held[0], pack.Container.GetSlot(BackpackCompartment.Main, 2).Item);
        }

        [Test]
        public void RoomInTheHotbar_StillTakesWithoutSwapping()
        {
            BackpackObject pack = Pack();
            var hotbar = new Hotbar(4);
            hotbar.TryAddItem(Item("only"));

            InventoryItem stowed = Item("stowed");
            pack.Container.PlaceAt(BackpackCompartment.Main, 4, stowed);

            Assert.IsTrue(pack.TryTakeToHotbar(BackpackCompartment.Main, 4, hotbar));
            Assert.IsTrue(pack.Container.GetSlot(BackpackCompartment.Main, 4).IsEmpty,
                          "with room to spare the pocket must simply empty, not receive anything back");
            Assert.AreSame(stowed, hotbar.GetSlot(1).Item);
        }

        [Test]
        public void EmptyPocket_TakesNothingAndSwapsNothing()
        {
            BackpackObject pack = Pack();
            Hotbar hotbar = FullHotbar(4, out InventoryItem[] held);
            hotbar.SelectSlot(0);

            Assert.IsFalse(pack.TryTakeToHotbar(BackpackCompartment.Main, 5, hotbar));
            Assert.AreSame(held[0], hotbar.GetSlot(0).Item, "a miss must not disturb the hotbar");
        }
    }
}
