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

        /// <summary>
        /// A length that was authored against the pack's ORIGINAL 0.09 m cell, restated at
        /// whatever the cell is today.
        ///
        /// <para>
        /// The 2026-09-01 enlargement multiplied the cell and every face by
        /// <see cref="PackScale.Factor"/> together and multiplied no cell COUNT by anything, so the
        /// face and the spots below still name the cells they always named. Nothing about the WIRE
        /// changed with them — a uv is still metres — which is what the round-trip test at the
        /// bottom of this file has to keep saying. Same helper, same reasoning, as in
        /// <c>PackLayoutTests</c>.
        /// </para>
        /// </summary>
        private static float M(float metresAtTheOriginalCell) =>
            metresAtTheOriginalCell * (PackGrid.Cell / PackScale.LegacyCell);

        /// <summary>The one face these tests lay things on: 9 x 8 cells with a hem across.</summary>
        private static readonly Vector2 LeafSize = new(M(0.86f), M(0.72f));

        /// <summary>
        /// The block an item with no prefab occupies — <see cref="ItemFootprint"/>'s square for
        /// anything it cannot measure, which is two cells either way at any scale. Every item in
        /// this file is a bare <see cref="InventoryItem"/> with nothing to measure.
        /// </summary>
        private static readonly Vector2Int UnmeasurableBlock = new(2, 2);

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

        /// <summary>
        /// An item with an id. The pack's layout is keyed by <see cref="InventoryItem.ID"/> rather
        /// than by a slot number, so an item without one cannot be placed at all.
        /// </summary>
        private InventoryItem Item(string itemName)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = itemName;
            item.ID = itemName;

            // Registered like a shipped item — the take path refuses an ID a populated registry
            // cannot resolve, and whether this domain's registry is populated depends on whether
            // play mode has run since the last reload. See BackpackSwapTests.Item.
            SpaceGame.Core.Registry<InventoryItem>.Register(item);

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

            // One face to lay things on. BackpackObject falls back to every PackSurface under the
            // rig when its authored array is empty, so a child is all it takes — and PackSurface
            // keeps its id and size in private serialized fields, which a test that is not loading
            // a prefab has to write directly.
            var surfaceGo = new GameObject("SURF_Leaf");
            surfaceGo.transform.SetParent(packGo.transform, false);

            var surface = surfaceGo.AddComponent<PackSurface>();
            surface.GetType().GetField("id", Hidden).SetValue(surface, PackSurfaceId.Leaf);
            surface.GetType().GetField("size", Hidden).SetValue(surface, LeafSize);

            var pack = packGo.AddComponent<BackpackObject>();
            Invoke(pack, "Awake");

            // Deployed, which is what the name says and what every test here assumes. A pack
            // defaults to WORN, and Reaches answers a worn pack with its exterior face alone —
            // every face of a folded rig but that one is inside the fold.
            pack.SetWorn(false);

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

        /// <summary>
        /// Where a test item is laid down, and the point a take names it by.
        ///
        /// <para>
        /// A cell-block centre rather than a round number of metres, because the layout stores the
        /// SNAPPED uv and a swap hands the displaced item the vacated placement's stored uv — so a
        /// test that lays an item down at a round number and then asserts the displaced one came
        /// back "in the spot the player was aiming at" is comparing an asked uv with a snapped one
        /// and fails by up to half a cell. The face has a hem across it, so the round number was
        /// never on the lattice to begin with. Same reasoning, same spelling, as <c>Spot</c> in
        /// <c>BackpackSwapTests</c>.
        /// </para>
        /// </summary>
        private static readonly Vector2 Spot =
            PackGrid.BlockCentreUv(LeafSize, new Vector2Int(3, 2), UnmeasurableBlock);

        [Test]
        public void OneItemGoesToOneTakerNoMatterHowManyAskForIt()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            InventoryItem cell = Item("Water Cell");
            Assert.IsTrue(pack.TryPlace(cell, PackSurfaceId.Leaf, Spot, 0f));

            controller.RequestTake(PackSurfaceId.Leaf, Spot, interactor);
            controller.RequestTake(PackSurfaceId.Leaf, Spot, interactor);

            Assert.AreEqual(1, hotbar.Count(cell),
                "The second request is what the loser of a race between two players looks like " +
                "from the server's side. It has to find nothing under that point any more and " +
                "answer no, or the last water cell in a pack is handed to both of them.");
            Assert.AreEqual(0, pack.Layout.Placements.Count);
        }

        /// <summary>
        /// A take names a POINT, not a list index. Indices are the thing that cannot be trusted
        /// across a reconcile: the list is republished whole on every change, so the client's
        /// element 3 and the server's element 3 are the same item only while nobody else is
        /// touching the pack. A point means whatever is under it when the server reads it.
        /// </summary>
        [Test]
        public void ATakeNamesTheSpotThePlayerClicked()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            InventoryItem cell = Item("Water Cell");
            pack.TryPlace(cell, PackSurfaceId.Leaf, Spot, 0f);

            // Bare canvas: the far corner of the 9 x 8 face, four cells clear of the only thing on
            // the mat. Named as a cell for the same reason Spot is.
            controller.RequestTake(PackSurfaceId.Leaf,
                                   PackGrid.CentreUv(LeafSize, new Vector2Int(8, 7)), interactor);

            Assert.AreEqual(0, hotbar.Count(cell), "a click on nothing must take nothing");
            Assert.AreEqual(1, pack.Layout.Placements.Count, "…and disturb nothing");
        }

        [Test]
        public void AShoulderedPackCannotBeReachedInto()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            SetAutoProperty(controller, "CurrentState", BackpackController.State.Shouldered);

            InventoryItem cell = Item("Water Cell");
            pack.TryPlace(cell, PackSurfaceId.Leaf, Spot, 0f);

            controller.RequestTake(PackSurfaceId.Leaf, Spot, interactor);

            Assert.AreEqual(0, hotbar.Count(cell),
                "The state is re-checked on the server, not trusted from the machine that asked — " +
                "the pack can have been re-shouldered while that request was in flight.");
        }

        /// <summary>
        /// The pack routes a take through its wearer rather than moving the item where it stands.
        ///
        /// This used to be BackpackSlotView's job, and the view is gone with the crosshair path to
        /// individual items — but the rule it was proving outlives it, and focus mode's cursor will
        /// call exactly this method.
        /// </summary>
        [Test]
        public void ThePackAsksItsOwnerRatherThanMovingTheItemItself()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            InventoryItem cell = Item("Water Cell");
            pack.TryPlace(cell, PackSurfaceId.Leaf, Spot, 0f);

            // Open enough to be handled, but the wearer has already asked for it back — so the
            // server will refuse. A pack that moved the item itself could not know that: it would
            // reach into whatever IPlayerInventory it could find on the interactor and hand it over.
            SetAutoProperty(pack, "IsOpen", true);
            SetAutoProperty(controller, "CurrentState", BackpackController.State.Stowing);

            pack.RequestTake(PackSurfaceId.Leaf, Spot, interactor);

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

            pack.TryPlace(cell, PackSurfaceId.Leaf, Spot, 0f);

            controller.RequestTake(PackSurfaceId.Leaf, Spot, interactor);

            Assert.AreEqual(1, hotbar.Count(cell), "The swap must hand over the pack's item.");
            Assert.AreEqual(0, hotbar.SelectedSlotIndex,
                "PlayerInventoryNetwork.SelectSlot is a TOGGLE — re-selecting a slot that is " +
                "already selected DESELECTS it. Re-selecting unconditionally after a swap therefore " +
                "left the player holding nothing on exactly the implementation that ships.");

            Assert.AreEqual(1, pack.Layout.Placements.Count);
            Assert.AreEqual(held.ID, pack.Layout.Placements[0].ItemId,
                "…and the displaced item goes onto the pack, in the spot the player was aiming at.");
            Assert.AreEqual(Spot, pack.Layout.Placements[0].Uv);
        }

        // ─────────── Clicked onto a NAMED hotbar slot ───────────
        //
        // A take can go to "wherever it fits" or to the exact hotbar slot the player clicked to
        // put a held item down. Same message, same channel, same contest — the only difference on
        // the wire is NetArg.B: -1 for an unaimed take, a slot index when the player named one.

        [Test]
        public void AClickOntoAnEmptySlotLandsInThatSlotAndDisturbsNoOther()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            InventoryItem rope = Item("Rope");
            Assert.IsTrue(hotbar.TryAddItem(rope), "slot 0 is the first free one");

            InventoryItem cell = Item("Water Cell");
            Assert.IsTrue(pack.TryPlace(cell, PackSurfaceId.Leaf, Spot, 0f));

            controller.RequestTake(PackSurfaceId.Leaf, Spot, interactor, hotbarSlot: 1);

            Assert.AreSame(cell, hotbar.GetSlot(1).Item,
                "The item has to land in the slot the player clicked. An unaimed take fills the " +
                "first hole instead, which here is the same slot every time whatever the player " +
                "aimed at.");
            Assert.AreSame(rope, hotbar.GetSlot(0).Item,
                "…and the rest of the hotbar is not rewritten on the way past.");
            Assert.AreEqual(0, pack.Layout.Placements.Count);
        }

        [Test]
        public void AClickOntoAnOccupiedSlotSwapsWithWhatWasInIt()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            InventoryItem lamp = Item("Lamp");
            hotbar.TryAddItem(lamp);

            InventoryItem cell = Item("Water Cell");
            pack.TryPlace(cell, PackSurfaceId.Leaf, Spot, 0f);

            controller.RequestTake(PackSurfaceId.Leaf, Spot, interactor, hotbarSlot: 0);

            Assert.AreSame(cell, hotbar.GetSlot(0).Item, "the clicked item takes the box");
            Assert.AreEqual(0, hotbar.Count(lamp), "and the one that was in it leaves the hotbar");

            Assert.AreEqual(1, pack.Layout.Placements.Count);
            Assert.AreEqual(lamp.ID, pack.Layout.Placements[0].ItemId,
                "The displaced item goes onto the pack, into the space the clicked one vacated — " +
                "not onto the floor, and not into limbo.");
            Assert.AreEqual(Spot, pack.Layout.Placements[0].Uv);
        }

        [Test]
        public void AClickOntoASlotIsIdempotentLikeEveryOtherTake()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            InventoryItem cell = Item("Water Cell");
            pack.TryPlace(cell, PackSurfaceId.Leaf, Spot, 0f);

            controller.RequestTake(PackSurfaceId.Leaf, Spot, interactor, hotbarSlot: 1);
            controller.RequestTake(PackSurfaceId.Leaf, Spot, interactor, hotbarSlot: 1);

            Assert.AreEqual(1, hotbar.Count(cell),
                "The second request is the losing half of a race between two players. It has to " +
                "find nothing under the point and answer no, exactly as an unaimed take does.");
            Assert.AreEqual(0, pack.Layout.Placements.Count);
        }

        [Test]
        public void AClickOntoASlotOutsideTheHotbarChangesNothing()
        {
            (BackpackController controller, BackpackObject pack) = DeployedPack();
            (Interactor interactor, FakeHotbar hotbar) = Taker();

            InventoryItem cell = Item("Water Cell");
            pack.TryPlace(cell, PackSurfaceId.Leaf, Spot, 0f);

            controller.RequestTake(PackSurfaceId.Leaf, Spot, interactor, hotbarSlot: 9);

            Assert.AreEqual(0, hotbar.Count(cell));
            Assert.AreEqual(1, pack.Layout.Placements.Count,
                "A slot index off the end of the hotbar is a malformed request. Degrading it to " +
                "first-fit would put gear somewhere the sender never asked for.");
        }

        // ─────────── Contents on the wire ───────────

        /// <summary>
        /// Every placement converts to the wire without losing anything, and two that differ in any
        /// one field compare as different.
        ///
        /// <para>
        /// The equality half is the one that would fail silently. NetworkList uses
        /// <c>IEquatable</c> to decide whether a write is a change worth telling anyone about, so a
        /// comparison that ignored — say — yaw would replicate a placement's position and never its
        /// rotation, and only for the players who were not watching when it was made.
        /// </para>
        /// </summary>
        [Test]
        public void APlacementSurvivesTheRoundTripThroughTheWireStruct()
        {
            MethodInfo toWire = typeof(BackpackNetwork).GetMethod("ToWire", HiddenStatic);
            Assert.IsNotNull(toWire, "BackpackNetwork.ToWire was renamed; rename it here too.");

            var placement = new PackPlacement("abc123", PackSurfaceId.WingRight,
                                              new Vector2(M(0.43f), M(0.36f)), 39f);
            var wire = (PackPlacementWire)toWire.Invoke(null, new object[] { placement });

            Assert.AreEqual("abc123", wire.ItemId.Value);
            Assert.AreEqual((byte)PackSurfaceId.WingRight, wire.Surface);
            Assert.AreEqual(M(0.43f), wire.U, 1e-5f);
            Assert.AreEqual(M(0.36f), wire.V, 1e-5f);
            Assert.AreEqual(39f, wire.Yaw, 1e-5f);

            Assert.IsTrue(wire.Equals(wire));

            var moved = (PackPlacementWire)toWire.Invoke(null, new object[]
            {
                new PackPlacement("abc123", PackSurfaceId.WingRight,
                                  new Vector2(M(0.43f), M(0.36f)), 40f)
            });

            Assert.IsFalse(wire.Equals(moved), "a yaw change is a change and has to replicate");
        }

        /// <summary>
        /// An empty item id must reach the wire as <c>default(FixedString64Bytes)</c>.
        ///
        /// <c>cond ? item.ID : default</c> types the whole expression as string, so the empty arm
        /// converts <c>default(string)</c> — null — and FixedString64Bytes throws an NRE on null
        /// from inside Unity.Collections. It took the entire inventory restore down once already.
        /// </summary>
        [Test]
        public void AnEmptyItemIdDoesNotThrowOnTheWay()
        {
            MethodInfo toWire = typeof(BackpackNetwork).GetMethod("ToWire", HiddenStatic);

            Assert.DoesNotThrow(() => toWire.Invoke(null, new object[]
            {
                new PackPlacement(null, PackSurfaceId.Leaf, Vector2.zero, 0f)
            }));
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
            public event Action<InventoryItem, float> OnItemDropped;

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

            public bool TrySetSlot(int index, InventoryItem item)
            {
                if (index < 0 || index >= bag.GetSize()) return false;
                bag.RestoreSlot(index, item);
                OnSlotChanged?.Invoke(index, bag.GetSlot(index));
                return true;
            }

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
                OnItemDropped?.Invoke(null, SupplyCharge.None);
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
