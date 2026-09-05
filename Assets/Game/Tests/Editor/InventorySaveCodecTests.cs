using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// The hotbar's save round trip, exercised through the real <see cref="PlayerInventory"/>.
    ///
    /// The fake below implements <see cref="IPlayerInventory"/> by delegating to that class, so the
    /// path under test is the production one — codec, interface, PlayerInventory, Inventory —
    /// without a PlayerController, an input manager or a spawned player. The component wrapper is
    /// deliberately not involved: MonoBehaviour Awake does not run outside play mode, so
    /// PlayerInventoryComponent hands out a null inventory in an EditMode test.
    /// </summary>
    public class InventorySaveCodecTests
    {
        /// <summary>Thin IPlayerInventory over the real PlayerInventory. No behaviour of its own.</summary>
        private class FakeHotbar : IPlayerInventory
        {
            private readonly PlayerInventory inner;

            public FakeHotbar(int size) => inner = new PlayerInventory(size);

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

            public bool TrySetSlot(int index, InventoryItem item)
            {
                inner.SetSlot(index, item);
                return true;
            }
            public void SelectSlot(int slotIndex) => inner.SelectSlot(slotIndex);
            public int GetInventorySize() => inner.GetInventorySize();
            public InventorySlot GetSlot(int index) => inner.GetSlot(index);
            public InventorySlot GetSelectedSlot() => inner.GetSelectedSlot();

            public InventoryItem GetSelectedItem()
            {
                InventorySlot slot = GetSelectedSlot();
                return slot == null || slot.IsEmpty ? null : slot.Item;
            }

            public void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot) =>
                inner.RestoreSlots(items, selectedSlot);
        }

        private readonly List<InventoryItem> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (InventoryItem item in created)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);

            created.Clear();
        }

        /// <summary>An item registered under a known ID, as RegistryLoader would at startup.</summary>
        private InventoryItem Item(string id)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.name = id;
            item.itemName = id;
            item.ID = id;
            created.Add(item);

            Registry<InventoryItem>.Register(item);
            return item;
        }

        private static JObject Payload(object state) =>
            JObject.FromObject(state, SaveSerializer.Serializer);

        [Test]
        public void RoundTrip_RestoresEverySlotInPlace()
        {
            InventoryItem a = Item("item-a"), b = Item("item-b");

            var source = new FakeHotbar(4);
            source.RestoreSlots(new List<InventoryItem> { a, null, b, null }, selectedSlot: 2);

            JObject payload = Payload(InventorySaveCodec.Capture(source));

            var target = new FakeHotbar(4);
            InventorySaveCodec.Restore(target, payload);

            Assert.AreEqual(a, target.GetSlot(0).Item);
            Assert.IsTrue(target.GetSlot(1).IsEmpty, "a hole was filled in");
            Assert.AreEqual(b, target.GetSlot(2).Item);
            Assert.IsTrue(target.GetSlot(3).IsEmpty);
            Assert.AreEqual(2, target.SelectedSlotIndex);
        }

        /// <summary>
        /// The failure that TryAddItem would cause: it fills the first free slot, so a saved layout
        /// with a gap comes back compacted and every item to the right of the gap has moved.
        /// </summary>
        [Test]
        public void RoundTrip_DoesNotCompactAroundEmptySlots()
        {
            InventoryItem last = Item("item-last");

            var source = new FakeHotbar(4);
            source.RestoreSlots(new List<InventoryItem> { null, null, null, last }, selectedSlot: -1);

            var target = new FakeHotbar(4);
            InventorySaveCodec.Restore(target, Payload(InventorySaveCodec.Capture(source)));

            Assert.IsTrue(target.GetSlot(0).IsEmpty);
            Assert.AreEqual(last, target.GetSlot(3).Item, "the item slid to the front of the hotbar");
        }

        [Test]
        public void Restore_OverwritesWhateverTheTargetAlreadyHeld()
        {
            InventoryItem saved = Item("saved"), unwanted = Item("unwanted");

            var source = new FakeHotbar(4);
            source.RestoreSlots(new List<InventoryItem> { saved, null, null, null }, selectedSlot: 0);

            var target = new FakeHotbar(4);
            target.RestoreSlots(new List<InventoryItem> { unwanted, unwanted, unwanted, unwanted }, 3);

            InventorySaveCodec.Restore(target, Payload(InventorySaveCodec.Capture(source)));

            Assert.AreEqual(saved, target.GetSlot(0).Item);
            Assert.IsTrue(target.GetSlot(1).IsEmpty, "starting items survived the load");
            Assert.AreEqual(0, target.SelectedSlotIndex);
        }

        /// <summary>A deleted item asset must cost its own slot, not the whole hotbar.</summary>
        [Test]
        public void Restore_LeavesUnknownItemIdsAsEmptySlotsAndKeepsPositions()
        {
            InventoryItem known = Item("known");

            var payload = Payload(new InventorySaveCodec.State
            {
                itemIds = new List<string> { "deleted-from-the-project", null, "known", null },
                selectedSlot = 2,
            });

            var target = new FakeHotbar(4);

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            InventorySaveCodec.Restore(target, payload);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsTrue(target.GetSlot(0).IsEmpty);
            Assert.AreEqual(known, target.GetSlot(2).Item, "the surviving item moved when its neighbour vanished");
            Assert.AreEqual(2, target.SelectedSlotIndex);
        }

        [Test]
        public void Restore_ClampsASelectedSlotThatNoLongerExists()
        {
            var payload = Payload(new InventorySaveCodec.State
            {
                itemIds = new List<string> { null, null },
                selectedSlot = 9,
            });

            var target = new FakeHotbar(2);
            InventorySaveCodec.Restore(target, payload);

            Assert.AreEqual(-1, target.SelectedSlotIndex);
        }

        /// <summary>A hotbar that grew between builds keeps what it had; the new slots are empty.</summary>
        [Test]
        public void Restore_HandlesASavedHotbarSmallerThanTheCurrentOne()
        {
            InventoryItem a = Item("item-a");

            var payload = Payload(new InventorySaveCodec.State
            {
                itemIds = new List<string> { "item-a" },
                selectedSlot = 0,
            });

            var target = new FakeHotbar(4);
            InventorySaveCodec.Restore(target, payload);

            Assert.AreEqual(a, target.GetSlot(0).Item);
            Assert.IsTrue(target.GetSlot(3).IsEmpty);
        }

        /// <summary>And one that shrank drops the overflow rather than throwing.</summary>
        [Test]
        public void Restore_HandlesASavedHotbarLargerThanTheCurrentOne()
        {
            InventoryItem a = Item("item-a");

            var payload = Payload(new InventorySaveCodec.State
            {
                itemIds = new List<string> { "item-a", "item-a", "item-a", "item-a", "item-a" },
                selectedSlot = 4,
            });

            var target = new FakeHotbar(2);

            Assert.DoesNotThrow(() => InventorySaveCodec.Restore(target, payload));
            Assert.AreEqual(a, target.GetSlot(0).Item);
            Assert.AreEqual(-1, target.SelectedSlotIndex, "a selection past the end must not stick");
        }

        [Test]
        public void Restore_IgnoresAPayloadWithNoItemList()
        {
            InventoryItem a = Item("item-a");

            var target = new FakeHotbar(4);
            target.RestoreSlots(new List<InventoryItem> { a, null, null, null }, 0);

            InventorySaveCodec.Restore(target, JObject.Parse(@"{""selectedSlot"":1}"));

            Assert.AreEqual(a, target.GetSlot(0).Item, "a malformed payload emptied the hotbar");
        }

        [Test]
        public void Capture_OfAnEmptyHotbarIsAllNulls()
        {
            var state = InventorySaveCodec.Capture(new FakeHotbar(3));

            Assert.AreEqual(3, state.itemIds.Count);
            Assert.IsTrue(state.itemIds.TrueForAll(id => id == null));
            Assert.AreEqual(-1, state.selectedSlot);
        }
    }
}
