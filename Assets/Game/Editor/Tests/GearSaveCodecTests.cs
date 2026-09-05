// The gear save format, shared by the hotbar and the body, read back from real JSON text.
//
// Two properties are pinned. Positions survive: an empty slot between two full ones stays empty
// on the way back, and an unknown id leaves ITS slot empty without moving its neighbours. And the
// hotbar's fourth entry — every save written when the bar was four wide — reaches the overflow
// callback instead of vanishing, which is what RestoreSlots used to do with it.
//
// Items are registered by hand: RegistryLoader runs at boot, not in an EditMode test.
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class GearSaveCodecTests
    {
        private readonly List<InventoryItem> made = new();

        private InventoryItem Item(string id, EquipKind kind = EquipKind.Hand)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.ID = id;
            item.itemName = id;
            item.equipKind = kind;
            Registry<InventoryItem>.Register(item);
            made.Add(item);
            return item;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (InventoryItem item in made) Object.DestroyImmediate(item);
            made.Clear();
        }

        /// <summary>A hotbar with no networking: the pure model behind the component.</summary>
        private sealed class FakeHotbar : IPlayerInventory
        {
            private readonly PlayerInventory inventory;

            public FakeHotbar(int size) => inventory = new PlayerInventory(size);

            public int SelectedSlotIndex => inventory.SelectedSlotIndex;
            public event System.Action<InventorySlot> OnSlotSelected { add { } remove { } }
            public event System.Action<int, InventorySlot> OnSlotChanged { add { } remove { } }
            public event System.Action<InventoryItem, float> OnItemDropped { add { } remove { } }

            public bool TryAddItem(InventoryItem item) => inventory.TryAddItem(item);
            public bool TryRemoveItem(int index) => inventory.TryRemoveItem(index);
            public void SelectSlot(int slotIndex) => inventory.SelectSlot(slotIndex);
            public void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot) => inventory.RestoreSlots(items, selectedSlot);
            public bool TrySetSlot(int index, InventoryItem item) { inventory.SetSlot(index, item); return true; }
            public int GetInventorySize() => inventory.GetInventorySize();
            public InventorySlot GetSlot(int index) => inventory.GetSlot(index);
            public InventorySlot GetSelectedSlot() => inventory.GetSelectedSlot();
            public InventoryItem GetSelectedItem() => inventory.GetSelectedSlot()?.Item;
        }

        private static JObject ThroughJson(object state) =>
            JObject.Parse(JsonConvert.SerializeObject(state));

        [Test]
        public void SlotsAndStatesRoundTripPositionally()
        {
            InventoryItem a = Item("gear-a");
            InventoryItem c = Item("gear-c");

            var source = new FakeHotbar(3);
            source.TrySetSlot(0, a);
            source.TrySetSlot(2, c);
            source.GetSlot(2).State = new ItemState(new Dictionary<string, string> { { "uses", "2" } });
            source.SelectSlot(2);

            JObject json = ThroughJson(InventorySaveCodec.Capture(source));

            var target = new FakeHotbar(3);
            InventorySaveCodec.Restore(target, json);

            Assert.AreSame(a, target.GetSlot(0).Item);
            Assert.IsTrue(target.GetSlot(1).IsEmpty, "the gap must stay a gap");
            Assert.AreSame(c, target.GetSlot(2).Item);
            Assert.AreEqual(2, target.SelectedSlotIndex);
            Assert.AreEqual(2, target.GetSlot(2).State.GetInt("uses"), "the bag rides with its slot");
            Assert.IsNull(target.GetSlot(0).State, "an item at its defaults has no bag");
        }

        [Test]
        public void AnUnknownIdLeavesOnlyItsOwnSlotEmpty()
        {
            InventoryItem b = Item("gear-b");

            var json = JObject.Parse("{\"itemIds\":[\"never-registered\",\"gear-b\",null],\"selectedSlot\":-1}");

            var target = new FakeHotbar(3);
            LogAssert_Expect();
            InventorySaveCodec.Restore(target, json);

            Assert.IsTrue(target.GetSlot(0).IsEmpty);
            Assert.AreSame(b, target.GetSlot(1).Item, "the neighbour must not shift left");
        }

        private static void LogAssert_Expect() =>
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("not in the registry"));

        [Test]
        public void AFourthHotbarEntryReachesTheOverflowCallback()
        {
            InventoryItem a = Item("gear-a");
            InventoryItem grapple = Item("gear-grapple", EquipKind.Gauntlet);

            var json = JObject.Parse("{\"itemIds\":[\"gear-a\",null,null,\"gear-grapple\"],\"selectedSlot\":0}");

            var overflow = new List<InventoryItem>();
            var target = new FakeHotbar(3);
            InventorySaveCodec.Restore(target, json, overflow.Add);

            Assert.AreSame(a, target.GetSlot(0).Item);
            CollectionAssert.AreEqual(new[] { grapple }, overflow,
                "the item past the bar's width must be handed on, not dropped");
        }

        [Test]
        public void BodyStatesRoundTrip()
        {
            InventoryItem wings = Item("gear-wings", EquipKind.Back);

            var slots = new List<InventorySlot> { new(0), new(1), new(2) };
            slots[0].Item = wings;
            slots[0].State = new ItemState(new Dictionary<string, string> { { "craft", "abc" } });

            var state = new BodyEquipmentSaveable.State
            {
                itemIds = GearSaveCodec.CaptureIds(slots),
                itemStates = GearSaveCodec.CaptureStates(slots),
            };

            JObject json = ThroughJson(state);

            List<InventoryItem> items = GearSaveCodec.ReadItems(json["itemIds"] as JArray);
            var back = new List<InventorySlot> { new(0), new(1), new(2) };
            for (int i = 0; i < back.Count; i++) back[i].Item = items[i];
            GearSaveCodec.RestoreStates(back, json["itemStates"] as JArray);

            Assert.AreSame(wings, back[0].Item);
            Assert.AreEqual("abc", back[0].State.GetString("craft"));
            Assert.IsTrue(back[1].IsEmpty);
            Assert.IsNull(back[1].State);
        }

        [Test]
        public void NoStatesMeansNoStatesField()
        {
            var slots = new List<InventorySlot> { new(0), new(1) };
            slots[0].Item = Item("gear-plain");

            Assert.IsNull(GearSaveCodec.CaptureStates(slots),
                "a list of nulls says nothing and costs a line per slot in every save");
        }
    }
}
