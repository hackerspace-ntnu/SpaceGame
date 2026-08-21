// A backpack is a container two people can reach into at once, which makes it the same problem as a
// trader's stock: exactly one machine may be allowed to decide which of them got the last water
// cell. It shipped as the opposite of that — a local Instantiate, a local container, and a slot view
// that moved the item into whoever's inventory it could find — so every machine ran its own copy of
// a pack and they diverged the first time anybody took anything.
//
// These tests run with no NetworkManager at all, which is what an EditMode test, a scene opened
// straight from the editor and a torn-down session all look like. On that machine every send falls
// through to a local dispatch and Network.Simulates answers true, so the SERVER's path is the one
// that runs — which is exactly the path worth pinning, and the reason the degradation contract is
// worth having.
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class BackpackNetworkingTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> spawned = new();
        private readonly List<ScriptableObject> assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            foreach (ScriptableObject asset in assets)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);

            spawned.Clear();
            assets.Clear();
        }

        // ─────────── Fixtures ───────────

        private GameObject NewObject(string name, params Type[] components)
        {
            var go = new GameObject(name, components);
            spawned.Add(go);
            return go;
        }

        private InventoryItem Item(string itemName)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = itemName;
            assets.Add(item);
            return item;
        }

        private static void Invoke(Component component, string method) =>
            component.GetType().GetMethod(method, Hidden)?.Invoke(component, null);

        private static void SetAutoProperty(object target, string property, object value) =>
            target.GetType()
                  .GetField($"<{property}>k__BackingField", Hidden)
                  .SetValue(target, value);

        /// <summary>
        /// A pack and the wearer it answers to, wired by hand.
        ///
        /// BackpackController.Awake instantiates the pack from a serialized prefab and resolves a
        /// spine bone, neither of which exists here — and Awake does not run for an AddComponent
        /// outside play mode anyway. OnEnable is called explicitly because that is where the
        /// message handlers are registered, and it is the whole subject of these tests.
        /// </summary>
        private (BackpackController controller, BackpackObject pack) DeployedPack()
        {
            GameObject packGo = NewObject("pack");
            var pack = packGo.AddComponent<BackpackObject>();
            Invoke(pack, "Awake");

            GameObject wearerGo = NewObject("wearer");
            var controller = wearerGo.AddComponent<BackpackController>();

            pack.Bind(controller);
            SetAutoProperty(controller, "Pack", pack);
            SetAutoProperty(controller, "CurrentState", BackpackController.State.Open);

            Invoke(controller, "OnEnable");

            return (controller, pack);
        }

        /// <summary>A second player, walking up to somebody else's open pack.</summary>
        private (Interactor interactor, FakeHotbar hotbar) Taker(int hotbarSlots = 2)
        {
            GameObject body = NewObject("taker");
            var hotbar = body.AddComponent<FakeHotbar>();
            hotbar.Resize(hotbarSlots);

            return (body.AddComponent<Interactor>(), hotbar);
        }

        // ─────────── The server settles the race ───────────

        [Test]
        public void OneItemGoesToOneTakerNoMatterHowManyAskForIt()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            InventoryItem cell = Item("Water Cell");
            Assert.IsTrue(pack.Container.TryAdd(BackpackCompartment.Main, cell, out int slot));

            controller.RequestTake(BackpackCompartment.Main, slot, interactor);
            controller.RequestTake(BackpackCompartment.Main, slot, interactor);

            Assert.AreEqual(1, hotbar.Count(cell),
                "The second request is what the loser of a race between two players looks like " +
                "from the server's side. It has to find the slot already empty and answer no, or " +
                "the last water cell in a pack is handed to both of them.");
            Assert.IsTrue(pack.Container.GetSlot(BackpackCompartment.Main, slot).IsEmpty);
        }

        [Test]
        public void AShoulderedPackCannotBeReachedInto()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            SetAutoProperty(controller, "CurrentState", BackpackController.State.Shouldered);

            InventoryItem cell = Item("Water Cell");
            pack.Container.TryAdd(BackpackCompartment.Main, cell, out int slot);

            controller.RequestTake(BackpackCompartment.Main, slot, interactor);

            Assert.AreEqual(0, hotbar.Count(cell),
                "The state is re-checked on the server, not trusted from the machine that asked — " +
                "the pack can have been re-shouldered while that request was in flight.");
        }

        [Test]
        public void TheSlotViewAsksTheOwnerRatherThanMovingTheItemItself()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            InventoryItem cell = Item("Water Cell");
            pack.Container.TryAdd(BackpackCompartment.Main, cell, out int slot);

            // Open enough to be interacted with, but the wearer has already asked for it back — so
            // the server will refuse. The old slot view could not know that: it reached into
            // whatever IPlayerInventory it could find on the interactor and moved the item.
            SetAutoProperty(pack, "IsOpen", true);
            SetAutoProperty(controller, "CurrentState", BackpackController.State.Stowing);

            var view = NewObject("stowed item").AddComponent<BackpackSlotView>();
            view.Bind(pack, BackpackCompartment.Main, slot);

            view.Interact(interactor);

            Assert.AreEqual(0, hotbar.Count(cell),
                "A take has to travel to the machine that owns the pack. Moving the item here " +
                "instead is how one pack became four packs that disagreed.");
        }

        // ─────────── The hotbar half is already replicated; do not double it up ───────────

        [Test]
        public void SwappingIntoAFullHotbarLeavesTheTakerStillHoldingSomething()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker(hotbarSlots: 1);

            InventoryItem held = Item("Rock");
            InventoryItem cell = Item("Water Cell");

            hotbar.TryAddItem(held);
            hotbar.Select(0);

            pack.Container.TryAdd(BackpackCompartment.Main, cell, out int slot);

            controller.RequestTake(BackpackCompartment.Main, slot, interactor);

            Assert.AreEqual(1, hotbar.Count(cell), "The swap must hand over the pack's item.");
            Assert.AreEqual(0, hotbar.SelectedSlotIndex,
                "PlayerInventoryNetwork.SelectSlot is a TOGGLE — re-selecting a slot that is " +
                "already selected DESELECTS it. Re-selecting unconditionally after a swap therefore " +
                "left the player holding nothing on exactly the implementation that ships.");
            Assert.AreSame(held, pack.Container.GetSlot(BackpackCompartment.Main, slot).Item,
                "…and the displaced item goes into the socket the player was aiming at.");
        }

        // ─────────── Contents on the wire ───────────

        [Test]
        public void EverySlotOfBothCompartmentsHasItsOwnPlaceOnTheWire()
        {
            MethodInfo toWire = typeof(BackpackNetwork).GetMethod("WireIndex", HiddenStatic);
            MethodInfo fromWire = typeof(BackpackNetwork).GetMethod("FromWireIndex", HiddenStatic);

            Assert.IsNotNull(toWire, "BackpackNetwork.WireIndex was renamed; rename it here too.");
            Assert.IsNotNull(fromWire, "BackpackNetwork.FromWireIndex was renamed; rename it here too.");

            var seen = new HashSet<int>();

            foreach (BackpackCompartment compartment in
                     new[] { BackpackCompartment.Strap, BackpackCompartment.Main })
            {
                int count = compartment == BackpackCompartment.Strap
                    ? BackpackContainer.StrapSlots
                    : BackpackContainer.MainSlots;

                for (int slot = 0; slot < count; slot++)
                {
                    var wire = (int)toWire.Invoke(null, new object[] { compartment, slot });

                    Assert.IsTrue(seen.Add(wire),
                        $"{compartment} slot {slot} shares wire index {wire} with another slot. " +
                        "Two compartments packed end to end into one list is only safe while the " +
                        "mapping is a bijection — a collision silently scrambles which anchor an " +
                        "item comes back on.");

                    object[] round = { wire, null, null };
                    Assert.IsTrue((bool)fromWire.Invoke(null, round));
                    Assert.AreEqual(compartment, round[1]);
                    Assert.AreEqual(slot, round[2]);
                }
            }

            Assert.AreEqual(BackpackContainer.StrapSlots + BackpackContainer.MainSlots, seen.Count);
        }

        [Test]
        public void ContentsCannotRideTheMessageChannel()
        {
            // Not a style preference. NetArg carries four numbers, a point and a rotation, and an
            // item is identified by InventoryItem.ID — its asset GUID. If a field ever appears that
            // could carry one, BackpackNetwork's NetworkList is worth revisiting; until then this
            // is the reason it exists, written down where it will be read.
            foreach (FieldInfo field in typeof(SpaceGame.Core.NetArg).GetFields())
            {
                Assert.AreNotEqual(typeof(string), field.FieldType,
                    $"NetArg.{field.Name} is a string now. The backpack's contents were put in a " +
                    "NetworkList because they could not be expressed as a message.");
            }
        }

        // ─────────── Wiring another agent has to apply ───────────

        [Test]
        public void ThePlayerPrefabCarriesTheComponentThatReplicatesItsPack()
        {
            const string path = "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(player, $"No prefab at {path}");

            Assert.IsNotNull(player.GetComponent<BackpackController>(),
                "The player has no BackpackController, so nobody has a pack at all.");

            Assert.IsNotNull(player.GetComponent<BackpackNetwork>(),
                "The player has no BackpackNetwork, so the pack's CONTENTS never cross: every " +
                "machine keeps its own copy, a late joiner sees items that were taken out an hour " +
                "ago, and reaching for one is refused with nothing on screen to say why. Add it " +
                "beside BackpackController on PlayerCharacterNetworked.prefab.");
        }

        // ─────────── A stand-in for the networked hotbar ───────────

        /// <summary>
        /// Enough of <see cref="IPlayerInventory"/> to take an item, and — the part that matters —
        /// <see cref="SelectSlot"/> toggling exactly the way PlayerInventoryNetwork's does.
        /// </summary>
        private class FakeHotbar : MonoBehaviour, IPlayerInventory
        {
            private Inventory bag = new(4);

            public int SelectedSlotIndex { get; private set; } = -1;

            public event Action<InventorySlot> OnSlotSelected;
            public event Action<int, InventorySlot> OnSlotChanged;
            public event Action<InventoryItem> OnItemDropped;

            public void Resize(int slots) => bag = new Inventory(slots);

            /// <summary>Test setup: select without the toggle, the way a hotbar key would not.</summary>
            public void Select(int index) => SelectedSlotIndex = index;

            public int Count(InventoryItem item)
            {
                int found = 0;
                for (int i = 0; i < bag.GetSize(); i++)
                {
                    InventorySlot slot = bag.GetSlot(i);
                    if (slot != null && !slot.IsEmpty && slot.Item == item) found++;
                }

                return found;
            }

            public bool TryAddItem(InventoryItem item)
            {
                bool added = bag.TryAddItem(item);
                if (added) OnSlotChanged?.Invoke(0, bag.GetSlot(0));
                return added;
            }

            public bool TryRemoveItem(int index) => bag.TryRemoveItem(index);

            public void SelectSlot(int slotIndex)
            {
                SelectedSlotIndex = SelectedSlotIndex == slotIndex ? -1 : slotIndex;
                OnSlotSelected?.Invoke(GetSelectedSlot());
            }

            public void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot)
            {
                for (int i = 0; i < bag.GetSize(); i++)
                    bag.RestoreSlot(i, items != null && i < items.Count ? items[i] : null);

                SelectedSlotIndex = selectedSlot;
                OnItemDropped?.Invoke(null);
            }

            public int GetInventorySize() => bag.GetSize();
            public InventorySlot GetSlot(int index) => bag.GetSlot(index);
            public InventorySlot GetSelectedSlot() => bag.GetSlot(SelectedSlotIndex);

            public InventoryItem GetSelectedItem()
            {
                InventorySlot slot = GetSelectedSlot();
                return slot == null || slot.IsEmpty ? null : slot.Item;
            }
        }
    }
}
