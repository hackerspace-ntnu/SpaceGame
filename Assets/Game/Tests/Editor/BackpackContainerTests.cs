using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class BackpackContainerTests
    {
        private readonly List<InventoryItem> created = new();

        /// InventoryItem is a ScriptableObject, so it cannot be `new`ed. These are throwaway
        /// instances with no asset behind them; TearDown destroys them so a run of the whole
        /// suite does not leave a pile of orphaned objects in the editor.
        private InventoryItem Item(string name)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = name;
            created.Add(item);
            return item;
        }

        [TearDown]
        public void DestroyCreatedItems()
        {
            foreach (InventoryItem item in created)
                if (item != null) Object.DestroyImmediate(item);

            created.Clear();
        }

        /// Records every event the container raises. The double-fire this guards against is
        /// invisible from the container's state alone -- two identical events leave exactly the
        /// same slots as one -- so the tests have to look at the sequence.
        private sealed class EventLog
        {
            public readonly List<(BackpackCompartment compartment, int index, InventoryItem item)> Entries = new();

            public EventLog(BackpackContainer pack)
            {
                pack.OnSlotChanged += (compartment, index, slot) => Entries.Add((compartment, index, slot?.Item));
            }

            public int Count => Entries.Count;
        }

        private static void AssertEntry(
            (BackpackCompartment compartment, int index, InventoryItem item) entry,
            BackpackCompartment compartment, int index, InventoryItem item, string because = null)
        {
            Assert.AreEqual(compartment, entry.compartment, because);
            Assert.AreEqual(index, entry.index, because);
            Assert.AreSame(item, entry.item, because);
        }

        private static void Fill(BackpackContainer pack, BackpackCompartment compartment, System.Func<int, InventoryItem> item)
        {
            for (int i = 0; i < pack.SlotCount(compartment); i++)
                Assert.IsTrue(pack.TryAdd(compartment, item(i), out _), $"slot {i} should still have been free");
        }

        // ─────────── shape ───────────

        [Test]
        public void TheTwoCompartments_AreSeparateStoresWithTheirOwnIndexSpaces()
        {
            var pack = new BackpackContainer();

            Assert.AreEqual(10, BackpackContainer.StrapSlots);
            Assert.AreEqual(12, BackpackContainer.MainSlots);
            Assert.AreEqual(BackpackContainer.StrapSlots, pack.SlotCount(BackpackCompartment.Strap));
            Assert.AreEqual(BackpackContainer.MainSlots, pack.SlotCount(BackpackCompartment.Main));

            Assert.AreSame(pack.Straps, pack.Get(BackpackCompartment.Strap));
            Assert.AreSame(pack.Main, pack.Get(BackpackCompartment.Main));
            Assert.AreNotSame(pack.Straps, pack.Main);

            // Index 0 exists in both and means a different slot in each -- the display walks these
            // as (compartment, index) pairs, so a shared index space would collide the sockets.
            Assert.IsTrue(pack.TryAdd(BackpackCompartment.Strap, Item("Bedroll"), out int strapIndex));
            Assert.IsTrue(pack.TryAdd(BackpackCompartment.Main, Item("Rock"), out int mainIndex));
            Assert.AreEqual(0, strapIndex);
            Assert.AreEqual(0, mainIndex);
            Assert.AreEqual("Bedroll", pack.GetSlot(BackpackCompartment.Strap, 0).Item.itemName);
            Assert.AreEqual("Rock", pack.GetSlot(BackpackCompartment.Main, 0).Item.itemName);
        }

        [Test]
        public void IsFull_IsAskedPerCompartment_NotOfThePackAsAWhole()
        {
            var pack = new BackpackContainer();

            Fill(pack, BackpackCompartment.Main, i => Item($"Rock{i}"));

            Assert.IsTrue(pack.IsFull(BackpackCompartment.Main));
            Assert.IsFalse(pack.IsFull(BackpackCompartment.Strap), "a full interior says nothing about the straps");
        }

        // ─────────── adding ───────────

        [Test]
        public void AddingToAFullCompartment_ReturnsFalse_SetsMinusOne_AndRaisesNothing()
        {
            var pack = new BackpackContainer();
            var log = new EventLog(pack);

            Fill(pack, BackpackCompartment.Strap, i => Item($"Clip{i}"));
            Assert.AreEqual(BackpackContainer.StrapSlots, log.Count, "one event per slot filled");

            InventoryItem refused = Item("Shovel");
            Assert.IsFalse(pack.TryAdd(BackpackCompartment.Strap, refused, out int index));
            Assert.AreEqual(-1, index);
            Assert.AreEqual(BackpackContainer.StrapSlots, log.Count, "a refused add must not raise a change");

            for (int i = 0; i < BackpackContainer.StrapSlots; i++)
                Assert.AreNotSame(refused, pack.GetSlot(BackpackCompartment.Strap, i).Item,
                    "and must not have displaced anything");
        }

        [Test]
        public void AddingNull_IsRefused_RatherThanBurningASlotOnNothing()
        {
            // Inventory.TryAddItem writes whatever it is handed into the first free slot and raises
            // a change for it, so a null passed straight through would occupy a pocket, fire an
            // event, and leave the display building a visual for an item that does not exist.
            var pack = new BackpackContainer();
            var log = new EventLog(pack);

            Assert.IsFalse(pack.TryAdd(BackpackCompartment.Main, null, out int index));
            Assert.AreEqual(-1, index);
            Assert.IsFalse(pack.TryAddToMain(null, out int mainIndex));
            Assert.AreEqual(-1, mainIndex);

            Assert.AreEqual(0, log.Count);
            Assert.IsTrue(pack.GetSlot(BackpackCompartment.Main, 0).IsEmpty);
        }

        [Test]
        public void TryAddToMain_NeverWritesIntoAStrapSlot_EvenWhileTheStrapsAreEmpty()
        {
            // The overflow path for world pickups. Straps mean "the player clipped this here on
            // purpose", so a rock picked up off the ground must never be able to take one.
            var pack = new BackpackContainer();

            for (int i = 0; i < BackpackContainer.MainSlots; i++)
            {
                Assert.IsTrue(pack.TryAddToMain(Item($"Rock{i}"), out int index));
                Assert.AreEqual(i, index, "the interior fills front to back");
            }

            for (int i = 0; i < BackpackContainer.StrapSlots; i++)
                Assert.IsTrue(pack.GetSlot(BackpackCompartment.Strap, i).IsEmpty, $"strap {i} was written to");

            // Interior full and straps still empty: the overflow refuses rather than spilling over.
            Assert.IsFalse(pack.TryAddToMain(Item("OneTooMany"), out int overflow));
            Assert.AreEqual(-1, overflow);
            Assert.IsFalse(pack.IsFull(BackpackCompartment.Strap));
        }

        // ─────────── taking ───────────

        [Test]
        public void TakeOut_OnAnEmptyOrOutOfRangeIndex_ReturnsNull_AndRaisesNothing()
        {
            var pack = new BackpackContainer();
            var log = new EventLog(pack);

            Assert.IsNull(pack.TakeOut(BackpackCompartment.Main, 0), "nothing in it yet");
            Assert.IsNull(pack.TakeOut(BackpackCompartment.Main, -1), "a negative index would index the array raw");
            Assert.IsNull(pack.TakeOut(BackpackCompartment.Main, BackpackContainer.MainSlots));
            Assert.IsNull(pack.TakeOut(BackpackCompartment.Strap, 999));
            Assert.AreEqual(0, log.Count);

            // GetSlot has to survive the same abuse -- the display asks for a slot by index when an
            // event names one, and a throw there would take the whole refresh down.
            Assert.IsNull(pack.GetSlot(BackpackCompartment.Main, -1));
            Assert.IsNull(pack.GetSlot(BackpackCompartment.Strap, BackpackContainer.StrapSlots));
        }

        [Test]
        public void TakeOut_ReturnsTheItemItRemoved_AndLeavesTheSlotEmpty()
        {
            var pack = new BackpackContainer();
            InventoryItem lamp = Item("Lamp");
            pack.TryAdd(BackpackCompartment.Main, lamp, out int index);

            Assert.AreSame(lamp, pack.TakeOut(BackpackCompartment.Main, index));
            Assert.IsTrue(pack.GetSlot(BackpackCompartment.Main, index).IsEmpty);
            Assert.IsNull(pack.TakeOut(BackpackCompartment.Main, index), "and cannot be taken twice");
        }

        [Test]
        public void TakeOutThenAdd_ReusesTheFreedIndex()
        {
            // The pocket sockets are a fixed grid, so a hole in the middle has to be refilled rather
            // than skipped -- otherwise the pack looks half empty while reporting itself full.
            var pack = new BackpackContainer();
            pack.TryAdd(BackpackCompartment.Main, Item("A"), out _);
            pack.TryAdd(BackpackCompartment.Main, Item("B"), out int middle);
            pack.TryAdd(BackpackCompartment.Main, Item("C"), out _);

            Assert.AreEqual(1, middle);
            Assert.AreEqual("B", pack.TakeOut(BackpackCompartment.Main, middle).itemName);

            Assert.IsTrue(pack.TryAdd(BackpackCompartment.Main, Item("D"), out int reused));
            Assert.AreEqual(middle, reused);
            Assert.AreEqual("D", pack.GetSlot(BackpackCompartment.Main, middle).Item.itemName);
        }

        // ─────────── enumeration ───────────

        [Test]
        public void Contents_YieldsStrapsBeforeMain_Ascending_SkippingEmpties()
        {
            var pack = new BackpackContainer();
            Assert.AreEqual(0, new List<(BackpackCompartment, int, InventoryItem)>(pack.Contents()).Count,
                "an empty pack yields nothing");

            InventoryItem bedroll = Item("Bedroll");
            InventoryItem canteen = Item("Canteen");
            InventoryItem rope = Item("Rope");
            InventoryItem core = Item("Core");
            InventoryItem shard = Item("Shard");
            InventoryItem valve = Item("Valve");

            pack.TryAdd(BackpackCompartment.Strap, bedroll, out _);   // 0
            pack.TryAdd(BackpackCompartment.Strap, canteen, out _);   // 1
            pack.TryAdd(BackpackCompartment.Strap, rope, out _);      // 2
            pack.TakeOut(BackpackCompartment.Strap, 1);               // punch a hole in the middle

            pack.TryAdd(BackpackCompartment.Main, core, out _);       // 0
            pack.TryAdd(BackpackCompartment.Main, shard, out _);      // 1
            pack.TryAdd(BackpackCompartment.Main, valve, out _);      // 2
            pack.TakeOut(BackpackCompartment.Main, 0);                // and one at the front

            var contents = new List<(BackpackCompartment compartment, int index, InventoryItem item)>(pack.Contents());

            Assert.AreEqual(4, contents.Count);
            AssertEntry(contents[0], BackpackCompartment.Strap, 0, bedroll);
            AssertEntry(contents[1], BackpackCompartment.Strap, 2, rope, "index 1 was emptied and must be skipped");
            AssertEntry(contents[2], BackpackCompartment.Main, 1, shard, "straps come first, whatever the indices");
            AssertEntry(contents[3], BackpackCompartment.Main, 2, valve);
        }

        // ─────────── the event ───────────

        [Test]
        public void OnSlotChanged_FiresExactlyOncePerRealChange()
        {
            // Inventory raises its own OnSlotChanged from TryAddItem and TryRemoveItem. The
            // container forwards those rather than raising alongside them; raising as well would
            // fire twice and make the pack tear down and rebuild every socket's visual twice.
            var pack = new BackpackContainer();
            var log = new EventLog(pack);
            InventoryItem rock = Item("Rock");

            Assert.IsTrue(pack.TryAdd(BackpackCompartment.Main, rock, out int index));
            Assert.AreEqual(1, log.Count, "one add, one event");
            AssertEntry(log.Entries[0], BackpackCompartment.Main, index, rock);

            Assert.IsFalse(pack.TryAdd(BackpackCompartment.Main, null, out _));
            Assert.AreEqual(1, log.Count, "a refused add is not a change");

            Assert.AreSame(rock, pack.TakeOut(BackpackCompartment.Main, index));
            Assert.AreEqual(2, log.Count, "one take, one event");
            AssertEntry(log.Entries[1], BackpackCompartment.Main, index, null, "the slot is reported as it now is: empty");

            Assert.IsNull(pack.TakeOut(BackpackCompartment.Main, index));
            Assert.AreEqual(2, log.Count, "taking from an already-empty slot is not a change");
        }

        [Test]
        public void OnSlotChanged_CarriesTheCompartmentTheChangeCameFrom()
        {
            // Both inner Inventories number their slots from zero, so the index alone cannot say
            // which socket to rebuild.
            var pack = new BackpackContainer();
            var log = new EventLog(pack);
            InventoryItem clipped = Item("Bedroll");
            InventoryItem stowed = Item("Rock");

            pack.TryAdd(BackpackCompartment.Strap, clipped, out _);
            pack.TryAdd(BackpackCompartment.Main, stowed, out _);

            Assert.AreEqual(2, log.Count);
            AssertEntry(log.Entries[0], BackpackCompartment.Strap, 0, clipped);
            AssertEntry(log.Entries[1], BackpackCompartment.Main, 0, stowed);
        }

        [Test]
        public void FillingACompartment_RaisesOneEventPerSlot_InOrder()
        {
            var pack = new BackpackContainer();
            var log = new EventLog(pack);

            Fill(pack, BackpackCompartment.Main, i => Item($"Rock{i}"));

            Assert.AreEqual(BackpackContainer.MainSlots, log.Count);
            for (int i = 0; i < BackpackContainer.MainSlots; i++)
                AssertEntry(log.Entries[i], BackpackCompartment.Main, i, pack.GetSlot(BackpackCompartment.Main, i).Item);
        }
    }
}
