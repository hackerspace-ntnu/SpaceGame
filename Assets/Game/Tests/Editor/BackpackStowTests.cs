using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The way IN: a hotbar slot put onto the pack.
    ///
    /// <para>
    /// This half did not exist. <c>TryStow</c> was reachable from a world pickup overflowing, from
    /// the pack's own starting items and from the full-hotbar swap, and from nowhere a player could
    /// press — so an item that reached the hotbar could only ever leave it by being dropped on the
    /// ground. What is tested here is the transfer itself, not the keystroke: the input path is in
    /// <c>PlayerInputManager</c> and the request in <c>BackpackController</c>, and both of those
    /// need a live NetworkManager to say anything about.
    /// </para>
    /// <para>
    /// The hotbar is the REAL <see cref="PlayerInventory"/> behind a thin adapter, for the reason
    /// <c>BackpackSwapTests</c> gives: its TryRemoveItem clears SelectedSlotIndex behind the
    /// caller's back, and a hand-written fake would have to reproduce that on purpose.
    /// </para>
    /// </summary>
    public class BackpackStowTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<InventoryItem> created = new();
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void ClearMeasurementCache() => ItemFootprint.ClearCache();

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

        /// <summary>
        /// An item with an id, because the layout is keyed by id. With no prefab it measures the
        /// 0.1 m square <see cref="ItemFootprint"/> gives anything it cannot measure, which is all
        /// these tests need.
        /// </summary>
        private InventoryItem Item(string name)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = name;
            item.ID = name;
            created.Add(item);
            return item;
        }

        /// <summary>A pack with one face on it, sized by the caller. See BackpackSwapTests.Pack.</summary>
        private BackpackObject Pack(Vector2 size)
        {
            var go = new GameObject("TestPack");
            spawned.Add(go);

            var surfaceGo = new GameObject("SURF_Leaf");
            surfaceGo.transform.SetParent(go.transform, false);

            var surface = surfaceGo.AddComponent<PackSurface>();
            typeof(PackSurface).GetField("id", Hidden).SetValue(surface, PackSurfaceId.Leaf);
            typeof(PackSurface).GetField("size", Hidden).SetValue(surface, size);

            var pack = go.AddComponent<BackpackObject>();

            // Deployed, not worn. A pack defaults to being on somebody's back, and Reaches answers
            // a worn pack with its exterior face alone — every face of a folded rig but that one is
            // inside the fold. Everything below is a player rummaging in a pack on the sand.
            pack.SetWorn(false);

            return pack;
        }

        private BackpackObject Pack() => Pack(new Vector2(0.86f, 0.72f));

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

        private static bool IsInSlot(IPlayerInventory hotbar, int index, InventoryItem item)
        {
            InventorySlot slot = hotbar.GetSlot(index);
            return slot != null && !slot.IsEmpty && slot.Item == item;
        }

        private static bool TryPlacementOf(BackpackObject pack, InventoryItem item, out PackPlacement found)
        {
            foreach (PackPlacement placement in pack.Layout.Placements)
            {
                if (placement.ItemId != item.ID) continue;

                found = placement;
                return true;
            }

            found = default;
            return false;
        }

        // ---------------------------------------------------------------- the stow

        [Test]
        public void Stow_TakesItOutOfTheHotbarAndPutsItWhereTheCursorWas()
        {
            BackpackObject pack = Pack();
            var hotbar = new Hotbar(4);

            InventoryItem carried = Item("carried");
            Assert.IsTrue(hotbar.TryAddItem(carried));

            var spot = new Vector2(0.4f, 0.3f);

            Assert.IsTrue(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 0f));

            Assert.IsFalse(IsInSlot(hotbar, 0, carried), "the hotbar slot must be empty afterwards");

            Assert.IsTrue(TryPlacementOf(pack, carried, out PackPlacement placed),
                          "and the item must be on the pack, not nowhere");
            Assert.AreEqual(spot, placed.Uv,
                            "at the spot that was aimed at — the whole point of pointing at one");
        }

        /// <summary>
        /// The failure that matters. Every other refusal in this system leaves the world as it
        /// was; a stow gets it wrong by emptying the hotbar slot first and then finding the pack
        /// full, which deletes the item out of the game with no error anywhere.
        /// </summary>
        [Test]
        public void Stow_ThatFitsNowhere_LeavesBothTheHotbarAndThePackAlone()
        {
            // Smaller than the 0.1 m square an unmeasurable item gets, so nothing fits on it at
            // any angle and the first-fit fallback has nowhere to go either.
            BackpackObject pack = Pack(new Vector2(0.05f, 0.05f));
            var hotbar = new Hotbar(4);

            InventoryItem carried = Item("carried");
            Assert.IsTrue(hotbar.TryAddItem(carried));

            Assert.IsFalse(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf,
                                                  new Vector2(0.02f, 0.02f), 0f));

            Assert.IsTrue(IsInSlot(hotbar, 0, carried), "the item must still be in the hotbar");
            Assert.AreEqual(0, pack.Layout.Placements.Count, "and nothing may have landed on the pack");
        }

        [Test]
        public void Stow_ThenTake_PutsItBackInTheHotbar()
        {
            BackpackObject pack = Pack();
            var hotbar = new Hotbar(4);

            InventoryItem carried = Item("carried");
            Assert.IsTrue(hotbar.TryAddItem(carried));

            var spot = new Vector2(0.4f, 0.3f);
            Assert.IsTrue(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 0f));

            Assert.IsTrue(pack.TryTakeToHotbar(PackSurfaceId.Leaf, spot, hotbar));

            Assert.IsTrue(IsInSlot(hotbar, 0, carried), "the round trip must end where it started");
            Assert.AreEqual(0, pack.Layout.Placements.Count, "and leave nothing behind on the pack");
        }

        /// <summary>
        /// The whole point of removing the magnet: a stow goes where it was pointed, at the turn
        /// it was shown at, and nowhere else.
        /// </summary>
        [Test]
        public void Stow_PlacesAtTheYawItWasGiven()
        {
            BackpackObject pack = Pack();
            var hotbar = new Hotbar(4);

            InventoryItem carried = Item("carried");
            Assert.IsTrue(hotbar.TryAddItem(carried));

            var spot = new Vector2(0.4f, 0.3f);

            Assert.IsTrue(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 90f));

            Assert.IsTrue(TryPlacementOf(pack, carried, out PackPlacement placed));
            Assert.AreEqual(90f, placed.Yaw, "the turn the player lined up is the turn it lands at");
        }

        /// <summary>
        /// The other half of "no auto placement". A spot that is taken is a REFUSAL — the item
        /// stays in the hotbar. It used to fall through to a first-fit search and land somewhere
        /// the player never pointed at.
        /// </summary>
        [Test]
        public void Stow_OntoATakenSpot_RefusesRatherThanFindingRoomElsewhere()
        {
            BackpackObject pack = Pack();
            var hotbar = new Hotbar(4);

            InventoryItem sitting = Item("sitting");
            var spot = new Vector2(0.4f, 0.3f);
            Assert.IsTrue(pack.TryPlace(sitting, PackSurfaceId.Leaf, spot, 0f));

            InventoryItem carried = Item("carried");
            Assert.IsTrue(hotbar.TryAddItem(carried));

            Assert.IsFalse(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 0f),
                           "the spot is taken, so the stow is refused");

            Assert.IsTrue(IsInSlot(hotbar, 0, carried), "the item must still be in the hotbar");
            Assert.AreEqual(1, pack.Layout.Placements.Count,
                            "and nothing may have been first-fitted onto the pack");
        }
    }
}
