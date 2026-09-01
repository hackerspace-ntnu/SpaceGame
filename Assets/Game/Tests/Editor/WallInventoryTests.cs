using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Persistence;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The ship's gear wall.
    ///
    /// <para>
    /// Most of what a wall does is <see cref="PackContainer"/>'s, and that half is already covered
    /// by the backpack's suite — which is the point of the extraction, and which is why this file
    /// is short. What is tested here is only what a WALL answers differently: every face reachable
    /// where a rig's depend on its fold, the stow-target encoding its requests ride on, and the
    /// save record it writes under its own key.
    /// </para>
    /// <para>
    /// In <c>Tests/Editor/</c> rather than <c>Tests/EditMode/</c>, like every other backpack test:
    /// the code under test is in the predefined <c>Assembly-CSharp</c>, which an asmdef cannot
    /// reference, so a test beside the others fails with CS0234 on the whole
    /// <c>SpaceGame.Items</c> namespace.
    /// </para>
    /// </summary>
    public class WallInventoryTests
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
        /// 0.1 m square <see cref="ItemFootprint"/> gives anything it cannot measure.
        ///
        /// <para>
        /// REGISTERED, and that is not incidental. The save codec stores ids and resolves them back
        /// through <see cref="Registry{T}"/> — it is handed a layout and surfaces, never a
        /// container, so it cannot ask the wall what it knows. An unregistered double is therefore
        /// indistinguishable from an item whose asset was deleted, and a round-trip test written
        /// with one restores nothing and blames the codec.
        /// </para>
        /// </summary>
        private InventoryItem Item(string name)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.name = name;
            item.itemName = name;
            item.ID = name;
            created.Add(item);

            Registry<InventoryItem>.Register(item);
            return item;
        }

        /// <summary>
        /// A wall with one face on it. Small on purpose — the shipped face is 60 x 30 cells, and a
        /// test that has to fill one to prove "full" is a slow test that proves the same thing.
        /// </summary>
        private WallInventory Wall(Vector2 size)
        {
            var go = new GameObject("TestWall");
            spawned.Add(go);

            var surfaceGo = new GameObject("SURF_WallGrid");
            surfaceGo.transform.SetParent(go.transform, false);

            var surface = surfaceGo.AddComponent<PackSurface>();
            typeof(PackSurface).GetField("id", Hidden).SetValue(surface, PackSurfaceId.WallGrid);
            typeof(PackSurface).GetField("size", Hidden).SetValue(surface, size);

            // No SetWorn equivalent and none needed: a wall does not fold, which is the behaviour
            // the first test below pins down.
            return go.AddComponent<WallInventory>();
        }

        private WallInventory Wall() => Wall(new Vector2(0.90f, 0.90f));

        // ── What a wall answers differently ──────────────────────────────────

        /// <summary>
        /// Every face is reachable, always. The rig's <c>Reaches</c> is a reading of which way its
        /// leaf is folded; a wall has nothing that folds, so the base class's "true" is the whole
        /// answer — and a wall that inherited the rig's rule would refuse its own only face.
        /// </summary>
        [Test]
        public void EveryFaceIsReachable()
        {
            WallInventory wall = Wall();

            foreach (PackSurfaceId id in Enum.GetValues(typeof(PackSurfaceId)))
                Assert.IsTrue(wall.Reaches(id), $"a wall should reach {id}");
        }

        [Test]
        public void PlacedItemIsHeldAtThePointItWasPutDown()
        {
            WallInventory wall = Wall();
            InventoryItem crate = Item("crate");

            Assert.IsTrue(wall.TryPlace(crate, PackSurfaceId.WallGrid, new Vector2(0.45f, 0.45f), 0f));
            Assert.IsTrue(wall.Holds(crate.ID));
            Assert.IsTrue(wall.TryFindAt(PackSurfaceId.WallGrid, new Vector2(0.45f, 0.45f),
                                         out PackPlacement placement));
            Assert.AreEqual(crate.ID, placement.ItemId);
        }

        /// <summary>
        /// The layout is keyed by item id, so one wall cannot hold two of the same asset. Pinned
        /// because the aim controller's green/red readout asks exactly this question before it
        /// offers a placement — an offer the server would then refuse is a press that does nothing.
        /// </summary>
        [Test]
        public void TheSameAssetCannotBePlacedTwice()
        {
            WallInventory wall = Wall();
            InventoryItem crate = Item("crate");

            Assert.IsTrue(wall.TryPlace(crate, PackSurfaceId.WallGrid, new Vector2(0.18f, 0.18f), 0f));
            Assert.IsFalse(wall.TryPlace(crate, PackSurfaceId.WallGrid, new Vector2(0.72f, 0.72f), 0f));
        }

        // ── The wire's encoding ──────────────────────────────────────────────

        /// <summary>
        /// Slot and surface share one <c>NetArg</c> int, so the pair has to survive the trip. A
        /// slot that overflowed its byte would silently corrupt the surface beside it — which is
        /// why <see cref="WallInventory.RequestStow"/> refuses one rather than packing it.
        /// </summary>
        [Test]
        public void StowTargetSurvivesEncoding()
        {
            foreach (int slot in new[] { 0, 1, 3, 9, 255 })
            foreach (PackSurfaceId id in Enum.GetValues(typeof(PackSurfaceId)))
            {
                int packed = WallInventory.EncodeStowTarget(slot, id);
                WallInventory.DecodeStowTarget(packed, out int decodedSlot, out int decodedSurface);

                Assert.AreEqual(slot, decodedSlot, $"slot {slot} on {id}");
                Assert.AreEqual((int)id, decodedSurface, $"surface {id} with slot {slot}");
            }
        }

        // ── The save record ──────────────────────────────────────────────────

        /// <summary>
        /// Capture, through real JSON text, back onto a FRESH wall — the fixpoint every saver here
        /// has to hold. Real text and not the object, because the failure this catches is a
        /// Newtonsoft one: a Vector2 without a converter serialises into a recursive mess of
        /// <c>normalized</c> and <c>magnitude</c>, which is exactly why the codec stores three
        /// plain floats.
        /// </summary>
        [Test]
        public void ContentsSurviveASaveRoundTrip()
        {
            WallInventory wall = Wall();
            InventoryItem crate = Item("crate");
            InventoryItem rod = Item("rod");

            Assert.IsTrue(wall.TryPlace(crate, PackSurfaceId.WallGrid, new Vector2(0.18f, 0.18f), 0f));
            Assert.IsTrue(wall.TryPlace(rod, PackSurfaceId.WallGrid, new Vector2(0.72f, 0.63f), 90f));

            var saver = wall.gameObject.AddComponent<WallInventorySaveable>();
            object captured = saver.CaptureState();
            Assert.IsNotNull(captured, "a wall with gear on it must write a record");

            string json = JsonConvert(captured);

            WallInventory restored = Wall();
            var restoredSaver = restored.gameObject.AddComponent<WallInventorySaveable>();
            restoredSaver.RestoreState(JObject.Parse(json));

            Assert.AreEqual(2, restored.Layout.Placements.Count);
            Assert.IsTrue(restored.Holds(crate.ID));
            Assert.IsTrue(restored.Holds(rod.ID));
        }

        /// <summary>An empty wall writes nothing at all, so a ship nobody has used stays out of the
        /// save file entirely.</summary>
        [Test]
        public void AnEmptyWallWritesNoRecord()
        {
            WallInventory wall = Wall();
            var saver = wall.gameObject.AddComponent<WallInventorySaveable>();

            Assert.IsNull(saver.CaptureState());
        }

        // ── The press, end to end ────────────────────────────────────────────
        //
        // The one path nothing else covers, and the one that shipped broken: a player pointing at
        // a wall with something in hand, pressing the button, and the item ending up on the wall.
        // It runs offline, where NetMessaging dispatches straight to the local handler, so this
        // exercises RequestStow -> the wire encoding -> OnStowRequested -> TryStowFromHotbar for
        // real rather than calling the last one directly.

        /// <summary>
        /// A hotbar that is a COMPONENT.
        ///
        /// The other fixtures here use a plain C# adapter, which is enough when the pack is handed
        /// the interface directly. It is not enough here: the server side resolves the hotbar off
        /// the body named in the message, with <c>GetComponentInChildren</c>, so a hotbar that is
        /// not a component is invisible to exactly the code under test.
        /// </summary>
        private sealed class HotbarBehaviour : MonoBehaviour, IPlayerInventory
        {
            private readonly PlayerInventory inner = new(4);

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

        /// <summary>
        /// A body with a hotbar and an interactor, which is what a request names.
        /// </summary>
        private (HotbarBehaviour hotbar, Interactor interactor) Player()
        {
            var go = new GameObject("TestPlayer");
            spawned.Add(go);

            var hotbar = go.AddComponent<HotbarBehaviour>();

            // On a child, as it is on the real player, where the Interactor lives on the camera
            // rig. NetChannel.RootOf walks up from it, so a request minted here names the body.
            var rig = new GameObject("CameraRig");
            rig.transform.SetParent(go.transform, false);

            return (hotbar, rig.AddComponent<Interactor>());
        }

        /// <summary>
        /// Wire the wall up the way OnEnable does at runtime.
        ///
        /// Unity does not deliver OnEnable to a plain MonoBehaviour in edit mode, so without this
        /// the wall listens for nothing, every request is dropped by a channel that was never
        /// created, and the test passes vacuously by asserting on a wall nobody asked anything of.
        /// </summary>
        private static void Listen(WallInventory wall) =>
            typeof(WallInventory)
                .GetMethod("OnEnable", Hidden)
                .Invoke(wall, null);

        [Test]
        public void PressingWithAnItemInHandPutsItOnTheWall()
        {
            WallInventory wall = Wall();
            Listen(wall);

            (HotbarBehaviour hotbar, Interactor interactor) = Player();

            InventoryItem crate = Item("crate");
            Assert.IsTrue(hotbar.TryAddItem(crate));
            hotbar.SelectSlot(0);

            var uv = new Vector2(0.45f, 0.45f);
            wall.RequestStow(0, PackSurfaceId.WallGrid, uv, 0f, interactor);

            Assert.IsTrue(wall.Holds(crate.ID),
                          "The press did not reach the wall — the item is still in the hotbar.");
            Assert.IsTrue(hotbar.GetSlot(0).IsEmpty,
                          "The item is on the wall and still in the hand: it was copied, not moved.");
        }

        /// <summary>
        /// The yaw rides the message as a rotation, because NetArg has a spare one and no spare
        /// int. A quarter turn that arrived as zero would place every item square and look like a
        /// preview that lied.
        /// </summary>
        [Test]
        public void TheTurnSurvivesTheRequest()
        {
            WallInventory wall = Wall(new Vector2(0.90f, 0.90f));
            Listen(wall);

            (HotbarBehaviour hotbar, Interactor interactor) = Player();

            InventoryItem rod = Item("rod");
            Assert.IsTrue(hotbar.TryAddItem(rod));
            hotbar.SelectSlot(0);

            wall.RequestStow(0, PackSurfaceId.WallGrid, new Vector2(0.45f, 0.45f), 90f, interactor);

            Assert.IsTrue(wall.TryFindAt(PackSurfaceId.WallGrid, new Vector2(0.45f, 0.45f),
                                         out PackPlacement placement));
            Assert.AreEqual(90f, placement.Yaw, 0.01f);
        }

        /// <summary>
        /// And back off again, which is the same press with an empty hand.
        /// </summary>
        [Test]
        public void PressingOnAPlacedItemTakesItBack()
        {
            WallInventory wall = Wall();
            Listen(wall);

            (HotbarBehaviour hotbar, Interactor interactor) = Player();

            InventoryItem crate = Item("crate");
            var uv = new Vector2(0.45f, 0.45f);
            Assert.IsTrue(wall.TryPlace(crate, PackSurfaceId.WallGrid, uv, 0f));

            wall.RequestTake(PackSurfaceId.WallGrid, uv, interactor);

            Assert.IsFalse(wall.Holds(crate.ID), "The take did not reach the wall.");
            Assert.AreSame(crate, hotbar.GetSlot(0).Item);
        }

        private static string JsonConvert(object state) =>
            JObject.FromObject(state, SaveSerializer.Serializer).ToString();
    }
}
