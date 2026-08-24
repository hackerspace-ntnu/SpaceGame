using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Taking an item off the pack when the hotbar is already full.
    ///
    /// The hotbar under test is the REAL <see cref="PlayerInventory"/> behind a thin adapter, not a
    /// stand-in. That matters: the one thing this feature can plausibly get wrong is that
    /// PlayerInventory.TryRemoveItem quietly clears SelectedSlotIndex when it removes the selected
    /// slot, and a hand-written fake would have to reproduce that bug on purpose to catch it.
    ///
    /// <para>
    /// Free placement gave the swap a second thing it can get wrong. Under fixed slots the pocket
    /// the outgoing item vacated was, by construction, exactly the right shape for the incoming
    /// one. It is not any more — a 1.35 m staff coming off does not leave a canister-shaped hole —
    /// so where the displaced item goes is now a decision, and a swap that cannot place it at all
    /// has to refuse without having moved anything.
    /// </para>
    /// </summary>
    public class BackpackSwapTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<InventoryItem> created = new();
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void ClearMeasurementCache()
        {
            // Footprints are cached per prefab GameObject, and every test here mints and destroys
            // its own. Left alone the cache would carry a previous test's answer for a prefab that
            // no longer exists.
            ItemFootprint.ClearCache();
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

        /// <summary>
        /// An item with an id, because the layout is keyed by id rather than by a slot number. An
        /// item without one cannot be placed at all.
        /// </summary>
        private InventoryItem Item(string name, GameObject prefab = null)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = name;
            item.ID = name;
            item.itemPrefab = prefab;
            created.Add(item);
            return item;
        }

        /// <summary>
        /// A prefab that measures <paramref name="size"/> metres. The mesh has to hang off a CHILD:
        /// ItemBounds measures in the root's own local space, so a mesh on the root itself comes
        /// back at its raw mesh bounds however the root is scaled.
        /// </summary>
        private GameObject Prefab(Vector3 size)
        {
            var root = new GameObject("prefab");
            spawned.Add(root);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = size;

            return root;
        }

        /// <summary>
        /// A pack with one face on it. <see cref="PackSurface"/> authors its id and size through
        /// private serialized fields, so a test that is not loading a prefab has to write them
        /// directly.
        /// </summary>
        private BackpackObject Pack(Vector2 size, PackSurfaceId id = PackSurfaceId.Leaf)
        {
            var go = new GameObject("TestPack");
            spawned.Add(go);

            var surfaceGo = new GameObject("SURF_" + id);
            surfaceGo.transform.SetParent(go.transform, false);

            var surface = surfaceGo.AddComponent<PackSurface>();
            typeof(PackSurface).GetField("id", Hidden).SetValue(surface, id);
            typeof(PackSurface).GetField("size", Hidden).SetValue(surface, size);

            // No Awake: AddComponent outside play mode never runs one, which is also why the pack's
            // Layout is built lazily. Leaving it unrun keeps the display out of these tests, and
            // UnityEngine.Object.Destroy is illegal from edit mode anyway.
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

        private static PackPlacement PlacementOf(BackpackObject pack, InventoryItem item)
        {
            foreach (PackPlacement placement in pack.Layout.Placements)
                if (placement.ItemId == item.ID) return placement;

            return default;
        }

        private static bool IsOnThePack(BackpackObject pack, InventoryItem item) =>
            PlacementOf(pack, item).ItemId == item.ID;

        // ---------------------------------------------------------------- the swap

        [Test]
        public void FullHotbar_SwapsWithTheSelectedSlot()
        {
            BackpackObject pack = Pack();
            Hotbar hotbar = FullHotbar(4, out InventoryItem[] held);
            hotbar.SelectSlot(2);

            InventoryItem stowed = Item("stowed");
            var spot = new Vector2(0.4f, 0.3f);
            Assert.IsTrue(pack.TryPlace(stowed, PackSurfaceId.Leaf, spot, 0f));

            Assert.IsTrue(pack.TryTakeToHotbar(PackSurfaceId.Leaf, spot, hotbar));

            Assert.AreSame(stowed, hotbar.GetSlot(2).Item, "the pack item belongs in the selected slot");

            PackPlacement displaced = PlacementOf(pack, held[2]);
            Assert.AreEqual(held[2].ID, displaced.ItemId,
                            "the held item belongs on the pack, not on the floor");
            Assert.AreEqual(spot, displaced.Uv,
                            "and in the spot that was aimed at, not the first free one — anything " +
                            "else reads as the pack shuffling its own contents");
        }

        [Test]
        public void FullHotbar_KeepsTheSlotSelected()
        {
            BackpackObject pack = Pack();
            Hotbar hotbar = FullHotbar(4, out _);
            hotbar.SelectSlot(1);

            var spot = new Vector2(0.2f, 0.2f);
            pack.TryPlace(Item("stowed"), PackSurfaceId.Leaf, spot, 0f);
            pack.TryTakeToHotbar(PackSurfaceId.Leaf, spot, hotbar);

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
            var spot = new Vector2(0.6f, 0.5f);
            pack.TryPlace(stowed, PackSurfaceId.Leaf, spot, 0f);

            pack.TryTakeToHotbar(PackSurfaceId.Leaf, spot, hotbar);

            var everything = new List<InventoryItem>();
            for (int i = 0; i < hotbar.GetInventorySize(); i++)
                if (!hotbar.GetSlot(i).IsEmpty) everything.Add(hotbar.GetSlot(i).Item);

            foreach (PackPlacement placement in pack.Layout.Placements)
                everything.Add(pack.ItemFor(placement.ItemId));

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
            var spot = new Vector2(0.3f, 0.3f);
            pack.TryPlace(stowed, PackSurfaceId.Leaf, spot, 0f);

            Assert.IsTrue(pack.TryTakeToHotbar(PackSurfaceId.Leaf, spot, hotbar));
            Assert.AreSame(stowed, hotbar.GetSlot(0).Item);
            Assert.IsTrue(IsOnThePack(pack, held[0]));
        }

        [Test]
        public void RoomInTheHotbar_StillTakesWithoutSwapping()
        {
            BackpackObject pack = Pack();
            var hotbar = new Hotbar(4);
            hotbar.TryAddItem(Item("only"));

            InventoryItem stowed = Item("stowed");
            var spot = new Vector2(0.5f, 0.4f);
            pack.TryPlace(stowed, PackSurfaceId.Leaf, spot, 0f);

            Assert.IsTrue(pack.TryTakeToHotbar(PackSurfaceId.Leaf, spot, hotbar));
            Assert.AreEqual(0, pack.Layout.Placements.Count,
                            "with room to spare the pack must simply give the item up, not receive anything back");
            Assert.AreSame(stowed, hotbar.GetSlot(1).Item);
        }

        /// <summary>
        /// The equivalent of aiming at an empty pocket, which under free placement is aiming at
        /// bare canvas. Nothing is under the point, so nothing happens.
        /// </summary>
        [Test]
        public void BareCanvas_TakesNothingAndSwapsNothing()
        {
            BackpackObject pack = Pack();
            Hotbar hotbar = FullHotbar(4, out InventoryItem[] held);
            hotbar.SelectSlot(0);

            pack.TryPlace(Item("stowed"), PackSurfaceId.Leaf, new Vector2(0.1f, 0.1f), 0f);

            Assert.IsFalse(pack.TryTakeToHotbar(PackSurfaceId.Leaf, new Vector2(0.8f, 0.6f), hotbar));
            Assert.AreSame(held[0], hotbar.GetSlot(0).Item, "a miss must not disturb the hotbar");
            Assert.AreEqual(1, pack.Layout.Placements.Count, "…or the pack");
        }

        // ------------------------------------------------- what free placement added

        /// <summary>
        /// The displaced item does not fit where the outgoing one was, but does fit elsewhere on
        /// the same face. Under fixed slots this case could not arise; it is now the common one for
        /// any pair of items that are not the same size.
        /// </summary>
        [Test]
        public void Swap_PutsTheHeldItemElsewhereWhenItCannotFitTheSpotVacated()
        {
            BackpackObject pack = Pack(new Vector2(1.2f, 1.2f));

            // A small thing in the corner, and a big thing in the hand. The corner cannot take the
            // big thing — half of it would hang off the edge — but the middle of the face can.
            InventoryItem stowed = Item("pebble", Prefab(new Vector3(0.1f, 0.1f, 0.1f)));
            InventoryItem bulky = Item("crate", Prefab(new Vector3(0.8f, 0.3f, 0.8f)));

            var hotbar = new Hotbar(1);
            hotbar.TryAddItem(bulky);
            hotbar.SelectSlot(0);

            var corner = new Vector2(0.06f, 0.06f);
            Assert.IsTrue(pack.TryPlace(stowed, PackSurfaceId.Leaf, corner, 0f));

            Assert.IsTrue(pack.TryTakeToHotbar(PackSurfaceId.Leaf, corner, hotbar));

            Assert.AreSame(stowed, hotbar.GetSlot(0).Item, "the pack item still has to reach the hand");

            PackPlacement moved = PlacementOf(pack, bulky);
            Assert.AreEqual(bulky.ID, moved.ItemId, "the displaced item must not be destroyed");
            Assert.AreNotEqual(corner, moved.Uv,
                               "it cannot have gone where it does not fit; TryFindSpot has to have run");
        }

        /// <summary>
        /// Nowhere on the face takes the displaced item. The swap has to be refused whole — and the
        /// order matters: the hotbar side must not have been committed before the pack side was
        /// known to be possible.
        /// </summary>
        [Test]
        public void Swap_RefusedOutrightWhenTheHeldItemFitsNowhere()
        {
            BackpackObject pack = Pack(new Vector2(0.5f, 0.5f));

            InventoryItem stowed = Item("pebble", Prefab(new Vector3(0.1f, 0.1f, 0.1f)));
            InventoryItem enormous = Item("girder", Prefab(new Vector3(2f, 0.1f, 0.1f)));

            var hotbar = new Hotbar(1);
            hotbar.TryAddItem(enormous);
            hotbar.SelectSlot(0);

            var spot = new Vector2(0.25f, 0.25f);
            pack.TryPlace(stowed, PackSurfaceId.Leaf, spot, 0f);

            Assert.IsFalse(pack.TryTakeToHotbar(PackSurfaceId.Leaf, spot, hotbar));

            Assert.AreSame(enormous, hotbar.GetSlot(0).Item, "the hotbar must be exactly as it was");
            Assert.AreEqual(0, hotbar.SelectedSlotIndex, "including the selection");
            Assert.IsTrue(IsOnThePack(pack, stowed), "and the pack item must still be on the pack");
        }
    }
}
