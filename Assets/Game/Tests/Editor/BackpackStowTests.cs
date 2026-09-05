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

        /// <summary>
        /// A length that was authored against the pack's ORIGINAL 0.09 m cell, restated at
        /// whatever the cell is today.
        ///
        /// <para>
        /// The 2026-09-01 enlargement multiplied the cell, every face and every item's on-mat size
        /// by <see cref="PackScale.Factor"/> together and multiplied no cell COUNT by anything, so
        /// every figure below still means the cells it always meant. Wrapping them rather than
        /// re-typing them at 1.5x keeps the arithmetic in the comments checkable and makes the next
        /// scale change a one-line edit to <see cref="PackScale"/>. Same helper, same reasoning, as
        /// in <c>PackLayoutTests</c>.
        /// </para>
        /// </summary>
        private static float M(float metresAtTheOriginalCell) =>
            metresAtTheOriginalCell * (PackGrid.Cell / PackScale.LegacyCell);

        /// <summary>The one face these tests lay things on: 9 x 8 cells with a hem across.</summary>
        private static readonly Vector2 LeafSize = new(M(0.86f), M(0.72f));

        /// <summary>
        /// The block an item with no prefab occupies. <see cref="ItemFootprint"/> gives anything it
        /// cannot measure a small square, which is two cells either way at any scale.
        /// </summary>
        private static readonly Vector2Int UnmeasurableBlock = new(2, 2);

        /// <summary>
        /// The middle of a block of cells on <see cref="LeafSize"/> — a uv the layout will store
        /// UNCHANGED.
        ///
        /// <para>
        /// Load-bearing wherever a test asserts that an item landed at the exact spot it was
        /// pointed at. A uv between cells is snapped on the way in (<c>PackLayoutTests</c> pins
        /// that), so asking for a round number and then demanding the stored uv equal it would be
        /// testing the snap and failing. Asking for a cell centre asks the question the test's own
        /// message asks: did the transfer put the item where the cursor was.
        /// </para>
        /// </summary>
        private static Vector2 Spot(int x, int y) =>
            PackGrid.BlockCentreUv(LeafSize, new Vector2Int(x, y), UnmeasurableBlock);

        private readonly List<InventoryItem> created = new();
        private readonly List<GameObject> spawned = new();
        private readonly List<PackShapeLibrary> libraries = new();

        [SetUp]
        public void ClearMeasurementCache() => ItemFootprint.ClearCache();

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            foreach (InventoryItem item in created)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);

            foreach (PackShapeLibrary library in libraries)
                if (library != null) UnityEngine.Object.DestroyImmediate(library);

            spawned.Clear();
            created.Clear();
            libraries.Clear();
        }

        /// <summary>
        /// An item with an id, because the layout is keyed by id. With no prefab it measures the
        /// small square <see cref="ItemFootprint"/> gives anything it cannot measure — two cells
        /// either way, whatever the cell is — which is all these tests need.
        /// </summary>
        private InventoryItem Item(string name)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = name;
            item.ID = name;

            // Registered like a shipped item — the take path refuses an ID a populated registry
            // cannot resolve, and whether this domain's registry is populated depends on whether
            // play mode has run since the last reload. See BackpackSwapTests.Item.
            SpaceGame.Core.Registry<InventoryItem>.Register(item);

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

        private BackpackObject Pack() => Pack(LeafSize);

        /// <summary>
        /// Give one item a non-square authored footprint, so a test can tell "the yaw field was
        /// threaded through" from "the shape actually turned" — <see cref="Item"/>'s default
        /// footprint is a two-cell square, on which a 90 degree turn is geometrically a no-op. Mirrors
        /// the "rod" fixture <c>PackLayoutTests</c> uses for the same reason at the raw-layout
        /// level; this one goes through <see cref="BackpackObject.Shapes"/> because that is the
        /// field a real item's row would arrive on.
        /// </summary>
        private void GiveRodShape(BackpackObject pack, InventoryItem item, int width, int height)
        {
            var library = ScriptableObject.CreateInstance<PackShapeLibrary>();
            libraries.Add(library);

            library.Entries.Add(new PackShapeLibrary.Entry
            {
                item = item,
                width = width,
                height = height,
            });

            // PackContainer, not BackpackObject: `shapes` lives on the shared base now, and
            // Type.GetField with NonPublic does NOT search base types — asking the subclass for a
            // private field it inherited returns null, and the NRE lands here rather than anywhere
            // near the code that moved.
            typeof(PackContainer).GetField("shapes", Hidden).SetValue(pack, library);
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

            public event Action<InventoryItem, float> OnItemDropped
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

            Vector2 spot = Spot(3, 2);

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
            // Barely half a cell across, so it holds no whole cell at all: nothing fits on it at
            // any angle and the first-fit fallback has nowhere to go either.
            BackpackObject pack = Pack(new Vector2(M(0.05f), M(0.05f)));
            var hotbar = new Hotbar(4);

            InventoryItem carried = Item("carried");
            Assert.IsTrue(hotbar.TryAddItem(carried));

            Assert.IsFalse(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf,
                                                  new Vector2(M(0.02f), M(0.02f)), 0f));

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

            Vector2 spot = Spot(3, 2);
            Assert.IsTrue(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 0f));

            Assert.IsTrue(pack.TryTakeToHotbar(PackSurfaceId.Leaf, spot, hotbar));

            Assert.IsTrue(IsInSlot(hotbar, 0, carried), "the round trip must end where it started");
            Assert.AreEqual(0, pack.Layout.Placements.Count, "and leave nothing behind on the pack");
        }

        /// <summary>
        /// The whole point of removing the magnet: a stow goes where it was pointed, at the turn
        /// it was shown at, and nowhere else — and the turn has to be a real turn of the
        /// footprint, not just a number recorded beside it. A rod-shaped item proves that: 9 cells
        /// is taller than this pack's 8-cell face, so lying flat it cannot land ANYWHERE on it,
        /// and the only way it fits at all is turned on its side, where the face is 9 across.
        /// </summary>
        [Test]
        public void Stow_PlacesAtTheYawItWasGiven()
        {
            BackpackObject pack = Pack();
            var hotbar = new Hotbar(4);

            InventoryItem carried = Item("carried");
            GiveRodShape(pack, carried, width: 2, height: 9);
            Assert.IsTrue(hotbar.TryAddItem(carried));

            // Centred, so turned sideways the rod's 9 cells sit exactly on the face's 9 columns
            // with only the hem to spare either end — and not a position a 2 x 9 rod could occupy
            // lying flat at any yaw the pack understands as "flat".
            var spot = new Vector2(M(0.43f), M(0.3f));

            Assert.IsFalse(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 0f),
                           "flat, the rod is taller than the face and cannot land anywhere on it");

            Assert.IsTrue(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 90f),
                         "turned on its side the same rod fits — this only succeeds if the turn " +
                         "actually swapped its footprint's width and height");

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
            Vector2 spot = Spot(3, 2);
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
