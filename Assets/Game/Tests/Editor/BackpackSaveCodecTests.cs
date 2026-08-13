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
    /// The backpack's save round trip, against a real <see cref="BackpackContainer"/>.
    ///
    /// Twenty-two slots across two compartments that behave independently, which is exactly the
    /// shape a restore gets subtly wrong — the usual failure is one compartment overwriting the
    /// other's indices, and it is invisible until a player opens the pack.
    /// </summary>
    public class BackpackSaveCodecTests
    {
        private readonly List<InventoryItem> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (InventoryItem item in created)
                if (item != null) Object.DestroyImmediate(item);

            created.Clear();
        }

        private InventoryItem Item(string id)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.name = id;
            item.ID = id;
            created.Add(item);

            Registry<InventoryItem>.Register(item);
            return item;
        }

        private static JObject Payload(object state) =>
            JObject.FromObject(state, SaveSerializer.Serializer);

        [Test]
        public void RoundTrip_RestoresBothCompartmentsInPlace()
        {
            InventoryItem strap = Item("strap-item"), main = Item("main-item");

            var source = new BackpackContainer();
            source.Get(BackpackCompartment.Strap).RestoreSlot(3, strap);
            source.Get(BackpackCompartment.Main).RestoreSlot(7, main);

            JObject payload = Payload(BackpackSaveCodec.Capture(source));

            var target = new BackpackContainer();
            BackpackSaveCodec.Restore(target, payload);

            Assert.AreEqual(strap, target.GetSlot(BackpackCompartment.Strap, 3).Item);
            Assert.AreEqual(main, target.GetSlot(BackpackCompartment.Main, 7).Item);
        }

        /// <summary>
        /// The compartments share slot indices but nothing else. A restore that fed one
        /// compartment's list to both would put strap gear inside the pack and pass every
        /// single-compartment test.
        /// </summary>
        [Test]
        public void RoundTrip_KeepsTheCompartmentsApart()
        {
            InventoryItem strap = Item("strap-item"), main = Item("main-item");

            var source = new BackpackContainer();
            source.Get(BackpackCompartment.Strap).RestoreSlot(0, strap);
            source.Get(BackpackCompartment.Main).RestoreSlot(0, main);

            var target = new BackpackContainer();
            BackpackSaveCodec.Restore(target, Payload(BackpackSaveCodec.Capture(source)));

            Assert.AreEqual(strap, target.GetSlot(BackpackCompartment.Strap, 0).Item);
            Assert.AreEqual(main, target.GetSlot(BackpackCompartment.Main, 0).Item,
                            "one compartment's contents were written into the other");
        }

        [Test]
        public void RoundTrip_PreservesSlotPositionsAcrossAFullCompartment()
        {
            var source = new BackpackContainer();
            Inventory main = source.Get(BackpackCompartment.Main);

            for (int i = 0; i < BackpackContainer.MainSlots; i += 2)
                main.RestoreSlot(i, Item("item-" + i));

            var target = new BackpackContainer();
            BackpackSaveCodec.Restore(target, Payload(BackpackSaveCodec.Capture(source)));

            for (int i = 0; i < BackpackContainer.MainSlots; i++)
            {
                bool shouldBeFilled = i % 2 == 0;
                Assert.AreEqual(shouldBeFilled, !target.GetSlot(BackpackCompartment.Main, i).IsEmpty,
                                $"slot {i} came back wrong");
            }
        }

        [Test]
        public void Restore_ClearsSlotsTheSaveLeftEmpty()
        {
            InventoryItem stale = Item("stale");

            var target = new BackpackContainer();
            target.Get(BackpackCompartment.Main).RestoreSlot(1, stale);

            BackpackSaveCodec.Restore(target, Payload(BackpackSaveCodec.Capture(new BackpackContainer())));

            Assert.IsTrue(target.GetSlot(BackpackCompartment.Main, 1).IsEmpty,
                          "starting contents survived a load of an empty pack");
        }

        [Test]
        public void Restore_LeavesUnknownItemIdsAsEmptySlots()
        {
            InventoryItem known = Item("known");

            var payload = Payload(new BackpackSaveCodec.State
            {
                strapItemIds = new List<string> { "deleted-from-the-project", "known" },
                mainItemIds = new List<string>(),
            });

            var target = new BackpackContainer();

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            BackpackSaveCodec.Restore(target, payload);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsTrue(target.GetSlot(BackpackCompartment.Strap, 0).IsEmpty);
            Assert.AreEqual(known, target.GetSlot(BackpackCompartment.Strap, 1).Item);
        }

        /// <summary>
        /// A save written before a compartment existed must leave that compartment alone rather than
        /// empty it — the difference between "nothing was stored" and "it was stored empty".
        /// </summary>
        [Test]
        public void Restore_LeavesACompartmentAbsentFromThePayloadUntouched()
        {
            InventoryItem existing = Item("existing");

            var target = new BackpackContainer();
            target.Get(BackpackCompartment.Main).RestoreSlot(0, existing);

            BackpackSaveCodec.Restore(target, JObject.Parse(@"{""strapItemIds"":[]}"));

            Assert.AreEqual(existing, target.GetSlot(BackpackCompartment.Main, 0).Item);
        }

        [Test]
        public void Restore_HandlesAPayloadLongerThanTheCompartment()
        {
            var ids = new List<string>();
            for (int i = 0; i < BackpackContainer.StrapSlots + 5; i++) ids.Add(null);

            var payload = Payload(new BackpackSaveCodec.State { strapItemIds = ids, mainItemIds = new List<string>() });
            var target = new BackpackContainer();

            Assert.DoesNotThrow(() => BackpackSaveCodec.Restore(target, payload));
        }

        [Test]
        public void Capture_OfAnEmptyPackIsAllNulls()
        {
            var state = BackpackSaveCodec.Capture(new BackpackContainer());

            Assert.AreEqual(BackpackContainer.StrapSlots, state.strapItemIds.Count);
            Assert.AreEqual(BackpackContainer.MainSlots, state.mainItemIds.Count);
            Assert.IsTrue(state.strapItemIds.TrueForAll(id => id == null));
            Assert.IsTrue(state.mainItemIds.TrueForAll(id => id == null));
        }
    }
}
