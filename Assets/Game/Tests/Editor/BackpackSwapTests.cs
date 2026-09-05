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

        /// <summary>
        /// A length that was authored against the pack's ORIGINAL 0.09 m cell, restated at
        /// whatever the cell is today.
        ///
        /// <para>
        /// The 2026-09-01 enlargement multiplied the cell, every face and every item's on-mat size
        /// by <see cref="PackScale.Factor"/> together and multiplied no cell COUNT by anything, so
        /// every face and every spot below still means the cells it always meant. Same helper, same
        /// reasoning, as in <c>PackLayoutTests</c>.
        /// </para>
        /// </summary>
        private static float M(float metresAtTheOriginalCell) =>
            metresAtTheOriginalCell * (PackGrid.Cell / PackScale.LegacyCell);

        /// <summary>The default face: 9 x 8 cells with a hem across.</summary>
        private static readonly Vector2 LeafSize = new(M(0.86f), M(0.72f));

        /// <summary>
        /// The block an item with no prefab occupies — <see cref="ItemFootprint"/>'s square for
        /// anything it cannot measure, which is two cells either way at any scale.
        /// </summary>
        private static readonly Vector2Int UnmeasurableBlock = new(2, 2);

        /// <summary>
        /// The middle of a block of cells on <see cref="LeafSize"/>, which is a uv the layout
        /// stores UNCHANGED.
        ///
        /// <para>
        /// Load-bearing for the swap: the displaced item is offered the vacated placement's own
        /// SNAPPED uv, so a test that asks for a round number and then asserts the two are equal
        /// is comparing an asked uv with a snapped one and fails by half a cell. Asking for a cell
        /// centre asks the question the assertion's message actually asks.
        /// </para>
        /// </summary>
        private static Vector2 Spot(int x, int y) =>
            PackGrid.BlockCentreUv(LeafSize, new Vector2Int(x, y), UnmeasurableBlock);

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

            // Registered like a shipped item, because the take path refuses an item whose ID a
            // populated registry cannot resolve (PackContainer.HotbarCanResolve) — and whether
            // this domain's registry is populated depends on whether play mode has run since the
            // last reload, which no test may depend on.
            SpaceGame.Core.Registry<InventoryItem>.Register(item);

            created.Add(item);
            return item;
        }

        /// <summary>
        /// A prefab that measures <paramref name="size"/> metres. The mesh has to hang off a CHILD:
        /// ItemBounds measures in the root's own local space, so a mesh on the root itself comes
        /// back at its raw mesh bounds however the root is scaled.
        ///
        /// <para>
        /// It carries an <see cref="ItemGrip"/> sized to its own longest axis, and that is what
        /// makes <paramref name="size"/> mean anything. A prefab with NO grip is one nobody ever
        /// sized for a hand, so <see cref="ItemFootprint"/> throws its mesh bounds away and gives
        /// it the default 0.30 m instead — which would make every fixture here the same size as
        /// every other and quietly turn "a pebble and a crate" into two identical blocks.
        /// </para>
        /// </summary>
        private GameObject Prefab(Vector3 size)
        {
            var root = new GameObject("prefab");
            spawned.Add(root);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = size;

            var grip = root.AddComponent<ItemGrip>();
            typeof(ItemGrip).GetField("holdSize", Hidden)
                            .SetValue(grip, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));

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

        private BackpackObject Pack() => Pack(LeafSize);

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
            Vector2 spot = Spot(3, 2);
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

            Vector2 spot = Spot(1, 1);
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
            Vector2 spot = Spot(5, 5);
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
            Vector2 spot = Spot(2, 2);
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
            Vector2 spot = Spot(4, 3);
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

            pack.TryPlace(Item("stowed"), PackSurfaceId.Leaf, Spot(0, 0), 0f);

            // Cell (8, 7): the far corner of the 9 x 8 face, four cells clear of the item.
            Assert.IsFalse(pack.TryTakeToHotbar(PackSurfaceId.Leaf,
                                                PackGrid.CentreUv(LeafSize, new Vector2Int(8, 7)),
                                                hotbar));
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
            // 13 x 13 cells, with a hem: big enough for the crate and then some.
            var face = new Vector2(M(1.2f), M(1.2f));
            BackpackObject pack = Pack(face);

            // A small thing in the corner, and a big thing in the hand: 2 x 2 cells against
            // 9 x 9. The corner cannot take the big thing — half of it would hang off the edge —
            // but the middle of the face can.
            InventoryItem stowed = Item("pebble", Prefab(new Vector3(0.1f, 0.1f, 0.1f)));
            InventoryItem bulky = Item("crate", Prefab(new Vector3(0.8f, 0.3f, 0.8f)));

            var hotbar = new Hotbar(1);
            hotbar.TryAddItem(bulky);
            hotbar.SelectSlot(0);

            // Cell (0, 0) named exactly, not a round number near it: on a hemmed face a round
            // number can land precisely half a cell from the nearest block centre, where the
            // rounding is a tie and the two answers straddle the edge of the grid.
            Vector2 corner = PackGrid.BlockCentreUv(face, Vector2Int.zero, UnmeasurableBlock);
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
            // 5 x 5 cells, against a girder that is 23 cells long: nowhere on this face, at any
            // quarter turn, and the leaf is a strict face so the length cannot overhang either.
            var face = new Vector2(M(0.5f), M(0.5f));
            BackpackObject pack = Pack(face);

            InventoryItem stowed = Item("pebble", Prefab(new Vector3(0.1f, 0.1f, 0.1f)));
            InventoryItem enormous = Item("girder", Prefab(new Vector3(2f, 0.1f, 0.1f)));

            var hotbar = new Hotbar(1);
            hotbar.TryAddItem(enormous);
            hotbar.SelectSlot(0);

            // The middle of the face, named as a cell rather than as a round number. See the
            // corner in the test above for why.
            Vector2 spot = PackGrid.BlockCentreUv(face, new Vector2Int(1, 1), UnmeasurableBlock);
            pack.TryPlace(stowed, PackSurfaceId.Leaf, spot, 0f);

            Assert.IsFalse(pack.TryTakeToHotbar(PackSurfaceId.Leaf, spot, hotbar));

            Assert.AreSame(enormous, hotbar.GetSlot(0).Item, "the hotbar must be exactly as it was");
            Assert.AreEqual(0, hotbar.SelectedSlotIndex, "including the selection");
            Assert.IsTrue(IsOnThePack(pack, stowed), "and the pack item must still be on the pack");
        }
    }
}
