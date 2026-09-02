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

        /// <summary>
        /// A length that was authored against the pack's ORIGINAL 0.09 m cell, restated at
        /// whatever the cell is today.
        ///
        /// <para>
        /// The wall took the 2026-09-01 enlargement with everything else, and was re-cut the same
        /// day to <c>30 * PackGrid.Cell</c> by <c>22 * PackGrid.Cell</c> so its fitting clears the
        /// lander's aft room. Wrapping the fixture's own small face and the uvs on it keeps them
        /// meaning the cells they always meant through both moves. Same helper, same reasoning, as
        /// in <c>PackLayoutTests</c>.
        /// </para>
        /// </summary>
        private static float M(float metresAtTheOriginalCell) =>
            metresAtTheOriginalCell * (PackGrid.Cell / PackScale.LegacyCell);

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
        /// small square <see cref="ItemFootprint"/> gives anything it cannot measure — two cells
        /// either way, whatever the cell is.
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
        /// A wall with one face on it. Small on purpose — the shipped face is 30 x 22 cells, and a
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

        /// <summary>
        /// A wall drawn larger than it reasons, the way the shipped one is.
        ///
        /// <para>
        /// Set through the serialized field rather than through a setter, because there is no
        /// setter and there should not be: the number is authored onto the prefab by
        /// <c>InventoryWallBuilder</c> and read, never written, at runtime. The face is told to
        /// forget its container because it may already have resolved one at the old value.
        /// </para>
        /// </summary>
        private WallInventory Wall(Vector2 size, float displayScale)
        {
            WallInventory wall = Wall(size);

            typeof(PackContainer).GetField("displayScale", Hidden).SetValue(wall, displayScale);
            wall.GetComponentInChildren<PackSurface>(true).ForgetContainer();

            return wall;
        }

        /// <summary>The fixture's face: 10 x 10 cells, edge to edge with no hem.</summary>
        private static readonly Vector2 FaceSize = new(M(0.90f), M(0.90f));

        private WallInventory Wall() => Wall(FaceSize);

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

            Assert.IsTrue(wall.TryPlace(crate, PackSurfaceId.WallGrid, new Vector2(M(0.45f), M(0.45f)), 0f));
            Assert.IsTrue(wall.Holds(crate.ID));
            Assert.IsTrue(wall.TryFindAt(PackSurfaceId.WallGrid, new Vector2(M(0.45f), M(0.45f)),
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

            Assert.IsTrue(wall.TryPlace(crate, PackSurfaceId.WallGrid, new Vector2(M(0.18f), M(0.18f)), 0f));
            Assert.IsFalse(wall.TryPlace(crate, PackSurfaceId.WallGrid, new Vector2(M(0.72f), M(0.72f)), 0f));
        }

        // ── Who owns the Use button ──────────────────────────────────────────

        /// <summary>
        /// <b>While the crosshair is on gear the player can act on, the wall owns Use — for BOTH
        /// its verbs.</b>
        ///
        /// <para>
        /// The button is shared with the item in the hand, and only one of them may answer a
        /// press. It used to be claimed only while a placement ghost was up, so the take verb was
        /// left sharing it: a click on an item lying on the wall lifted it off AND fired whatever
        /// the player was holding, point blank, at the wall they were standing in front of.
        /// Nothing about that was visible in either class on its own, which is why the rule is one
        /// argument-only function and why it is pinned here rather than left to a playtest.
        /// </para>
        /// <para>
        /// The last row is the one that must stay false: a press on bare canvas with an empty hand
        /// is not a verb the wall has, so taking the button there would swallow presses the wall
        /// never uses.
        /// </para>
        /// </summary>
        [TestCase(true,  true,  ExpectedResult = true,
                  TestName = "something in hand, aimed at placed gear — a stow, refused on red")]
        [TestCase(true,  false, ExpectedResult = true,
                  TestName = "something in hand, aimed at canvas — a stow")]
        [TestCase(false, true,  ExpectedResult = true,
                  TestName = "empty hand, aimed at placed gear — a take, NOT a trigger pull")]
        [TestCase(false, false, ExpectedResult = false,
                  TestName = "empty hand, aimed at canvas — the wall has no verb here")]
        public bool TheWallOwnsUseForBothOfItsVerbs(bool holdingSomething, bool overPlacedItem) =>
            WallAimController.WallOwnsUse(holdingSomething, overPlacedItem);

        /// <summary>
        /// And the deeper half of the same rule: an item lying on an inventory surface is not an
        /// item at all, so there is nothing on it for a press to reach even if one got through.
        ///
        /// <para>
        /// <see cref="BackpackItemVisual.Strip"/> is what guarantees it, and it guarantees it for
        /// the backpack mat and the gear wall at once — they build their display copies through
        /// the same function. A copy that kept its <c>UsableItem</c> would be a live artifact
        /// bolted to a shelf: it would run <c>Awake</c>, hold state, and answer an aim ray as
        /// something usable. Asserted on a MonoBehaviour count rather than on one named type,
        /// because the next component to sneak through will not be the one this bug was about.
        /// </para>
        /// </summary>
        [Test]
        public void GearLyingOnASurfaceCarriesNoUsableItem()
        {
            var stage = new GameObject("Stage");
            stage.SetActive(false);
            spawned.Add(stage);

            GameObject copy = UnityEngine.Object.Instantiate(Usable(), stage.transform);
            BackpackItemVisual.Strip(copy);

            Assert.IsNull(copy.GetComponentInChildren<UsableItem>(true),
                          "a display copy on an inventory surface still carries a UsableItem — " +
                          "clicking stowed gear can fire it.");

            Assert.IsEmpty(copy.GetComponentsInChildren<MonoBehaviour>(true),
                           "a display copy is scenery and must run no gameplay code at all.");
        }

        /// <summary>A prefab-shaped object carrying a usable item, for <see cref="Strip"/> to take off.</summary>
        private GameObject Usable()
        {
            var go = new GameObject("UsableFixture");
            go.SetActive(false);
            spawned.Add(go);

            go.AddComponent<StrippableUsable>();
            return go;
        }

        /// <summary>
        /// The smallest concrete <see cref="UsableItem"/> there is. <c>UsableItem</c> is abstract
        /// and every shipped subclass drags a prefab, an aim provider or a network channel in with
        /// it; the test is about the component being REMOVED, so the cheapest thing that is one
        /// is the honest fixture.
        /// </summary>
        private sealed class StrippableUsable : UsableItem
        {
            protected override void Use() { }
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

            Assert.IsTrue(wall.TryPlace(crate, PackSurfaceId.WallGrid, new Vector2(M(0.18f), M(0.18f)), 0f));
            Assert.IsTrue(wall.TryPlace(rod, PackSurfaceId.WallGrid, new Vector2(M(0.72f), M(0.63f)), 90f));

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

        /// <summary>
        /// A save written against the OLD, bigger face reopens on the smaller one with nothing
        /// lost — the item is simply somewhere else on it.
        ///
        /// <para>
        /// The wall's face was cut from 60 x 30 cells to 30 x 22 on 2026-09-01 so the fitting
        /// would clear the lander's aft room, and every save written before that names spots that
        /// are now off the end of it. <see cref="PackSaveCodec"/> has no file version for this and
        /// needs none: <c>RestoreOne</c> tries the stored spot, and when the spot is not on the
        /// face any more it falls through to <c>PackContainer.TryArrange</c> first-fit. Only an
        /// item that fits NOWHERE is logged and dropped.
        /// </para>
        /// <para>
        /// So the guarantee is "somewhere legal", not "where you left it", and that is the thing
        /// worth pinning: a shrink that silently deleted the far column of a player's gear wall
        /// would look exactly like a shrink that worked.
        /// </para>
        /// </summary>
        [Test]
        public void GearSavedNearTheOldWallsFarEdgeComesBackOnTheSmallerWall()
        {
            // The two real faces, in cells, so this test is about the actual re-cut and not about
            // two sizes that happen to differ.
            var oldFace = new Vector2(60 * PackGrid.Cell, 30 * PackGrid.Cell);
            var newFace = new Vector2(30 * PackGrid.Cell, 22 * PackGrid.Cell);

            WallInventory before = Wall(oldFace);
            InventoryItem crate = Item("crate");

            // The top far corner: on the old face, and past both edges of the new one.
            var corner = new Vector2(oldFace.x - PackGrid.Cell, oldFace.y - PackGrid.Cell);
            Assert.IsTrue(before.TryPlace(crate, PackSurfaceId.WallGrid, corner, 0f),
                          "the fixture did not manage to place the item on the OLD face, so the " +
                          "rest of this test would prove nothing");

            var saver = before.gameObject.AddComponent<WallInventorySaveable>();
            string json = JsonConvert(saver.CaptureState());

            WallInventory after = Wall(newFace);
            after.gameObject.AddComponent<WallInventorySaveable>().RestoreState(JObject.Parse(json));

            Assert.IsTrue(after.Holds(crate.ID),
                          "the item was on the old wall's far edge and is on no wall at all now — " +
                          "shrinking the face deleted a player's gear silently.");

            Assert.AreEqual(1, after.Layout.Placements.Count);
            PackPlacement placed = after.Layout.Placements[0];

            Assert.IsTrue(after.TryFindAt(PackSurfaceId.WallGrid, placed.Uv,
                                          out PackPlacement found) && found.ItemId == crate.ID,
                          "the item came back but is not reachable at its own uv.");

            // On the grid, not merely present: a uv the layout accepted but that lies off the face
            // would leave gear hanging in the air beside the fitting.
            Vector2Int shape = PackShapes.For(crate, null).Size;
            Vector2Int origin = PackGrid.BlockOrigin(newFace, placed.Uv, shape);
            Vector2Int cells = PackGrid.CellsOn(newFace);

            Assert.That(origin.x, Is.InRange(0, cells.x - shape.x));
            Assert.That(origin.y, Is.InRange(0, cells.y - shape.y));
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

            var uv = new Vector2(M(0.45f), M(0.45f));
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
            WallInventory wall = Wall(FaceSize);
            Listen(wall);

            (HotbarBehaviour hotbar, Interactor interactor) = Player();

            InventoryItem rod = Item("rod");
            Assert.IsTrue(hotbar.TryAddItem(rod));
            hotbar.SelectSlot(0);

            wall.RequestStow(0, PackSurfaceId.WallGrid, new Vector2(M(0.45f), M(0.45f)), 90f, interactor);

            Assert.IsTrue(wall.TryFindAt(PackSurfaceId.WallGrid, new Vector2(M(0.45f), M(0.45f)),
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
            var uv = new Vector2(M(0.45f), M(0.45f));
            Assert.IsTrue(wall.TryPlace(crate, PackSurfaceId.WallGrid, uv, 0f));

            wall.RequestTake(PackSurfaceId.WallGrid, uv, interactor);

            Assert.IsFalse(wall.Holds(crate.ID), "The take did not reach the wall.");
            Assert.AreSame(crate, hotbar.GetSlot(0).Item);
        }

        // ── Drawn larger, reasoned about identically ─────────────────────────
        //
        // The gear wall is drawn PackScale.WallDisplay times bigger than its own grid so it reads
        // from across the deck. Everything below is about the seam that makes that safe: the
        // enlargement reaches PackSurface's three conversion functions and NOTHING else, so the
        // face, its cells, every stored uv and every byte of save are exactly what they were.

        /// <summary>
        /// The board grows and the grid does not.
        ///
        /// <para>
        /// This is the whole design in one assertion pair. <c>Size</c> and <c>Cells</c> are the
        /// LOGICAL frame — what a placement, a save and the wire are written in — and must not
        /// move for any display scale. <c>ToWorld</c> is the DRAWN frame and must move by exactly
        /// the display scale. Getting this backwards is the failure that looks like nothing:
        /// scaling the prefab's root instead leaves <c>ToWorld</c> invariant, because it divides
        /// <c>lossyScale</c> out and the transform multiplies it straight back — so the board
        /// grows while the gear stays put and every item drifts off its own cell.
        /// </para>
        /// </summary>
        [Test]
        public void TheBoardIsDrawnLargerAndTheGridIsNotTouched()
        {
            WallInventory plain = Wall(FaceSize);
            WallInventory drawn = Wall(FaceSize, PackScale.WallDisplay);

            PackSurface a = plain.GetComponentInChildren<PackSurface>(true);
            PackSurface b = drawn.GetComponentInChildren<PackSurface>(true);

            Assert.AreEqual(a.Size, b.Size, "the logical rectangle moved with the display scale.");
            Assert.AreEqual(a.Cells, b.Cells,
                            "the cell COUNT moved with the display scale — capacity is not " +
                            "allowed to depend on how big the thing is drawn.");
            Assert.AreEqual(PackGrid.Hem(a.Size), PackGrid.Hem(b.Size));

            // The far corner, where a 6% error is biggest and so hardest to miss.
            Vector3 plainOffset = a.ToWorld(FaceSize, 0f) - a.ToWorld(Vector2.zero, 0f);
            Vector3 drawnOffset = b.ToWorld(FaceSize, 0f) - b.ToWorld(Vector2.zero, 0f);

            Assert.AreEqual(plainOffset.magnitude * PackScale.WallDisplay, drawnOffset.magnitude,
                            1e-4f,
                            "a uv's world offset did not grow with the display scale, so the " +
                            "model is enlarged and the gear on it is not.");
        }

        /// <summary>
        /// A uv survives the trip out to the drawn board and back, and the trip is not the
        /// identity.
        ///
        /// <para>
        /// <c>WallAimController</c> resolves what the crosshair is over by intersecting the look
        /// ray with the face and asking <see cref="PackSurface.ToUv"/> — so if <c>ToLocal</c>
        /// carries the display scale and <c>ToUv</c> does not divide it back out, the wall becomes
        /// unpointable: the highlighted cell drifts from the crosshair by 6% of the way across the
        /// board, which is more than a cell at the far edge. The second half of this test is what
        /// stops it passing vacuously — a stub that returned its argument would satisfy the round
        /// trip on its own.
        /// </para>
        /// </summary>
        [Test]
        public void AimingAtTheDrawnBoardResolvesTheCellUnderTheCrosshair()
        {
            WallInventory wall = Wall(FaceSize, PackScale.WallDisplay);
            PackSurface face = wall.GetComponentInChildren<PackSurface>(true);

            Vector2Int cells = face.Cells;
            var far = new Vector2Int(cells.x - 1, cells.y - 1);
            Vector2 wanted = PackGrid.CentreUv(face.Size, far);

            // Exactly what the aim does: a world point on the drawn board, read back as a uv.
            Vector2 resolved = face.ToUv(face.ToWorld(wanted, 0f));

            Assert.AreEqual(far, PackGrid.CellAt(face.Size, resolved),
                            "the crosshair was over cell " + far + " and the wall answered " +
                            PackGrid.CellAt(face.Size, resolved) + ".");

            // And the drawn board really is somewhere else: the same reading taken WITHOUT the
            // divide lands on a different cell, which is the bug this test exists for. Asserted so
            // the test cannot quietly stop biting if the fixture face is ever made small enough
            // that 6% is under half a cell.
            var undivided = new Vector2(resolved.x * PackScale.WallDisplay,
                                        resolved.y * PackScale.WallDisplay);

            Assert.AreNotEqual(far, PackGrid.CellAt(face.Size, undivided),
                               "forgetting the display scale in ToUv would land on the same cell " +
                               "here, so this test could not catch it — sample further from the " +
                               "origin.");
        }

        /// <summary>
        /// A wall save is byte-identical whatever the wall is drawn at, and reopens on the same
        /// cells.
        ///
        /// <para>
        /// The point of putting the enlargement in the drawn frame rather than in the cell was to
        /// need no <c>PackSaveCodec</c> version and no migration, unlike the 1.5x enlargement,
        /// which has to multiply every uv in a v2 payload on load. That claim is worth exactly as
        /// much as this test: a uv is metres, so an enlargement that reached the logical frame
        /// would move every saved item to a different cell without a word.
        /// </para>
        /// </summary>
        [Test]
        public void AWallSaveDoesNotNoticeTheEnlargementAtAll()
        {
            InventoryItem crate = Item("crate");
            InventoryItem rod = Item("rod");

            var spot = new Vector2(M(0.18f), M(0.18f));
            var other = new Vector2(M(0.72f), M(0.63f));

            WallInventory plain = Wall(FaceSize);
            Assert.IsTrue(plain.TryPlace(crate, PackSurfaceId.WallGrid, spot, 0f));
            Assert.IsTrue(plain.TryPlace(rod, PackSurfaceId.WallGrid, other, 90f));

            WallInventory drawn = Wall(FaceSize, PackScale.WallDisplay);
            Assert.IsTrue(drawn.TryPlace(crate, PackSurfaceId.WallGrid, spot, 0f));
            Assert.IsTrue(drawn.TryPlace(rod, PackSurfaceId.WallGrid, other, 90f));

            string plainJson =
                JsonConvert(plain.gameObject.AddComponent<WallInventorySaveable>().CaptureState());
            string drawnJson =
                JsonConvert(drawn.gameObject.AddComponent<WallInventorySaveable>().CaptureState());

            Assert.AreEqual(plainJson, drawnJson,
                            "the gear wall writes a different save when it is drawn larger, so " +
                            "the enlargement reached the logical frame after all.");

            // And the direction a player sees: a payload written before the enlargement, opened on
            // the enlarged wall.
            WallInventory reopened = Wall(FaceSize, PackScale.WallDisplay);
            reopened.gameObject.AddComponent<WallInventorySaveable>()
                    .RestoreState(JObject.Parse(plainJson));

            Assert.AreEqual(2, reopened.Layout.Placements.Count);

            foreach (PackPlacement was in plain.Layout.Placements)
            {
                Assert.IsTrue(
                    reopened.TryFindAt(PackSurfaceId.WallGrid, was.Uv, out PackPlacement now)
                    && now.ItemId == was.ItemId,
                    "'" + was.ItemId + "' was saved at " + was.Uv + " and is not there any more.");

                Assert.AreEqual(was.Uv, now.Uv,
                                "'" + was.ItemId + "' came back on a different spot — an old wall " +
                                "save must load onto exactly the cells it was written on.");
            }
        }

        /// <summary>
        /// The wall is drawn at exactly the size the user decided on 2026-09-02 — 20% over the
        /// model's baked <see cref="PackScale.WallModel"/> — and this is where a drift fails.
        ///
        /// <para>
        /// <b>The decision deliberately exceeds the aft room's measured budget.</b> The fitting is
        /// 2.580 m tall in the original 0.09 m frame (all-mesh bounds of
        /// <c>inventory_wall.blend</c>; 3.870 m at the 1.5 rig it was first cut against), and the
        /// 2026-09-02 ship's baked collision offers 4.383 m of headroom over its footprint at
        /// <c>PlayerShipBuilder.WallRibClearance</c> — a budget that allows 1.602 with the 0.25 m
        /// gap the old guard required. The user chose the bigger board over the clearance, so
        /// this test pins the DECISION rather than the room: it fails in the same edit that moves
        /// the constant, in either direction, before any prefab is rebuilt.
        /// <c>PlayerShipTests.PlayerShip_InventoryWallFaceIsAimableFromTheRoom</c>
        /// is the one that measures the built ship.
        /// </para>
        /// <para>
        /// <b>Stated against <c>WallDrawn</c>, not <c>WallDisplay</c>.</b> <c>WallDisplay</c> is a
        /// ratio to a LOGICAL frame that moves with <see cref="PackScale.Factor"/>, so pinning it
        /// instead would make the wall's size depend on the size of the backpack. The wall's real
        /// height in the ship is <c>WallDrawn</c> times its height in the original frame, and
        /// neither of those two numbers moves when the rig is resized — which is why the
        /// 2026-09-02 shrink of the rig left the wall exactly where it was.
        /// </para>
        /// </summary>
        [Test]
        public void TheWallIsDrawnAtItsDecidedSize()
        {
            const float DecidedOverModel = 1.2f;

            Assert.That(PackScale.WallDrawn, Is.GreaterThan(1f),
                        "the gear wall is meant to be drawn larger than the frame it is pinned in.");

            Assert.AreEqual(PackScale.WallModel * DecidedOverModel, PackScale.WallDrawn, 1e-5f,
                            "PackScale.WallDrawn is " + PackScale.WallDrawn.ToString("0.000") +
                            ", not the " + DecidedOverModel.ToString("0.0") + " x " +
                            PackScale.WallModel.ToString("0.00") + " decided on 2026-09-02. If " +
                            "this is a new decision, restate it here, rebuild both prefabs and " +
                            "re-run PlayerShipTests' wall probes; if it is not, put the " +
                            "constant back.");
        }

        /// <summary>
        /// A face with no display scale authored is drawn at its logical size — which is what keeps
        /// the backpack out of this entirely.
        ///
        /// The rig's own faces are never given one, so the default has to be the identity and has
        /// to survive an <c>AddComponent</c>, which leaves a serialized float at its C# default of
        /// zero rather than at the initialiser. A 0 reaching <c>ToLocal</c> would collapse every
        /// face on the rig to a point, and nothing on the rig would say why.
        /// </summary>
        [Test]
        public void AFaceWithNoDisplayScaleIsDrawnAtItsLogicalSize()
        {
            var go = new GameObject("SURF_Loose");
            spawned.Add(go);

            var loose = go.AddComponent<PackSurface>();
            typeof(PackSurface).GetField("size", Hidden).SetValue(loose, FaceSize);

            Assert.AreEqual(1f, loose.DisplayScale, 1e-6f,
                            "a face belonging to no container has no enlargement to read.");

            WallInventory unauthored = Wall(FaceSize);

            Assert.AreEqual(1f, unauthored.DisplayScale, 1e-6f,
                            "a container whose displayScale was never authored must draw at 1, " +
                            "not at the zero an AddComponent leaves the field on.");
            Assert.AreEqual(1f,
                            unauthored.GetComponentInChildren<PackSurface>(true).DisplayScale,
                            1e-6f);
        }

        private static string JsonConvert(object state) =>
            JObject.FromObject(state, SaveSerializer.Serializer).ToString();
    }
}
