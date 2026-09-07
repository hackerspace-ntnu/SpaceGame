using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// The rules the 2026-09-04 oxygen rework exists to make predictable: what a charge is, where
    /// it can live, and the order the two reservoirs are spent in.
    ///
    /// <para>
    /// Everything here is pure arithmetic or a plain object, deliberately. <c>Awake</c> does not run
    /// on an <c>AddComponent</c> in an EditMode test and <c>Update</c> never runs at all, so the
    /// rules were written as static helpers and a non-MonoBehaviour socket precisely so that this
    /// file can reach them without a player, a pack and a network session.
    /// </para>
    /// </summary>
    public class SupplyChargeTests
    {
        // ── The suit / tank order ────────────────────────────────────────────

        /// <summary>
        /// <b>The whole point of the suit being a last resort.</b> While the tank covers the tick,
        /// the reserve does not move at all — so a player with a full tank still has their full
        /// sixty seconds the moment it runs dry.
        /// </summary>
        [Test]
        public void AFullTankLeavesTheSuitReserveUntouched()
        {
            // One second's drain, entirely supplied by the tank.
            float after = SuitOxygen.SuitAfter(breathing: false, suit: 60f, capacity: 60f,
                                               refill: 0f, wanted: 1f, fromTank: 1f);

            Assert.AreEqual(60f, after, 0.001f,
                            "The reserve dropped while a tank was supplying the tick. The suit is " +
                            "meant to be spent only after the tank is dry.");
        }

        /// <summary>With no tank, the reserve is what drains — one second per second.</summary>
        [Test]
        public void WithNoTankTheReserveIsWhatDrains()
        {
            float after = SuitOxygen.SuitAfter(breathing: false, suit: 60f, capacity: 60f,
                                               refill: 0f, wanted: 1f, fromTank: 0f);

            Assert.AreEqual(59f, after, 0.001f, "The reserve did not drain with no tank connected.");
        }

        /// <summary>
        /// A tank that runs dry MID-TICK covers only part of it, and the reserve covers the rest.
        /// The handover has to be continuous, or the changeover loses or gains a frame of air.
        /// </summary>
        [Test]
        public void ATankRunningDryMidTickIsToppedUpByTheReserve()
        {
            float after = SuitOxygen.SuitAfter(breathing: false, suit: 60f, capacity: 60f,
                                               refill: 0f, wanted: 1f, fromTank: 0.25f);

            Assert.AreEqual(59.25f, after, 0.001f,
                            "The reserve did not cover exactly the part of the tick the tank " +
                            "could not.");
        }

        /// <summary>The reserve floors at zero. Below it is suffocation, not negative air.</summary>
        [Test]
        public void TheReserveNeverGoesNegative()
        {
            float after = SuitOxygen.SuitAfter(breathing: false, suit: 0.2f, capacity: 60f,
                                               refill: 0f, wanted: 1f, fromTank: 0f);

            Assert.AreEqual(0f, after, 0.001f, "The reserve went negative instead of running out.");
        }

        /// <summary>
        /// Shelter refills the reserve and — the half that matters — spends NO tank.
        ///
        /// <para>
        /// Walking into the ship must not cost tank charge. If it did, shelter would be a purchase
        /// and the correct play would be to stand outside.
        /// </para>
        /// </summary>
        [Test]
        public void ShelterRefillsTheReserveAndSpendsNoTank()
        {
            float after = SuitOxygen.SuitAfter(breathing: true, suit: 10f, capacity: 60f,
                                               refill: 6f, wanted: 0f, fromTank: 0f);

            Assert.AreEqual(16f, after, 0.001f, "Breathable air did not refill the reserve.");

            // `wanted` is zero inside shelter, which is what makes the tank untouchable there:
            // SuitOxygen never even asks the socket for a draw.
            Assert.AreEqual(60f,
                            SuitOxygen.SuitAfter(breathing: true, suit: 58f, capacity: 60f,
                                                 refill: 6f, wanted: 0f, fromTank: 0f),
                            0.001f,
                            "A refill overfilled the reserve past its capacity.");
        }

        // ── The charge value ─────────────────────────────────────────────────

        /// <summary>
        /// A charge survives the round trip to a byte finely enough for the whole percent every
        /// readout shows. One byte is what made the two container wire formats affordable.
        /// </summary>
        [Test]
        public void AChargeSurvivesTheWireAsOneByte()
        {
            for (int percent = 0; percent <= 100; percent++)
            {
                float charge = percent / 100f;
                float back = SupplyCharge.FromByte(SupplyCharge.ToByte(charge));

                Assert.AreEqual(percent, Mathf.RoundToInt(back * 100f),
                                "A charge of " + percent + "% came back as a different whole " +
                                "percent after a trip through the wire's byte.");
            }
        }

        /// <summary>
        /// "Not a supply" and "an empty supply" are different answers, and a bag never stores the
        /// former.
        ///
        /// <para>
        /// A rifle is not an empty tank. If <see cref="SupplyCharge.None"/> were written out as a
        /// number, every item in the game would carry a charge key into every save file — and the
        /// pack's restore could not tell a record that predates charges from one that means empty.
        /// </para>
        /// </summary>
        [Test]
        public void ABagNeverStoresNotASupply()
        {
            var state = new ItemState();

            SupplyCharge.Write(state, SupplyCharge.None);
            Assert.IsTrue(state.IsEmpty, "Writing 'not a supply' put a key in the bag.");
            Assert.Less(SupplyCharge.Read(state), 0f, "An absent charge did not read back as None.");

            SupplyCharge.Write(state, 0f);
            Assert.IsFalse(state.IsEmpty, "An EMPTY supply wrote nothing, so it is indistinguishable " +
                                          "from an item that holds nothing at all.");
            Assert.AreEqual(0f, SupplyCharge.Read(state), 0.001f);
        }

        // ── Instance keys ────────────────────────────────────────────────────

        /// <summary>
        /// <b>The bug the instance key exists to fix.</b> A container is keyed by placement, and
        /// <c>PackLayout</c> refuses a second placement under a key it already holds — so while the
        /// key WAS the asset id, no pack could carry two of anything.
        ///
        /// <para>
        /// Nobody noticed, because the only item worth two of was an oxygen tank, and a full one
        /// and an empty one were two different assets. Merging them would have taken the pack from
        /// two tanks to one, silently.
        /// </para>
        /// </summary>
        [Test]
        public void ASecondCopyOfOneAssetGetsItsOwnKey()
        {
            var placements = new System.Collections.Generic.List<PackPlacement>();

            string first = PackItemKey.Mint("abc123", placements);
            Assert.AreEqual("abc123", first,
                            "The FIRST copy is not the bare asset id, so every existing save file " +
                            "and every authored starting list now names something that does not " +
                            "resolve.");

            placements.Add(new PackPlacement(first, PackSurfaceId.Leaf, Vector2.zero, 0f));

            string second = PackItemKey.Mint("abc123", placements);
            Assert.AreNotEqual(first, second, "A second tank got the first one's key, so the pack " +
                                              "will refuse to hold both.");

            placements.Add(new PackPlacement(second, PackSurfaceId.Leaf, Vector2.one, 0f));

            string third = PackItemKey.Mint("abc123", placements);
            Assert.AreNotEqual(first, third);
            Assert.AreNotEqual(second, third);
        }

        /// <summary>
        /// Every key still names its asset, however many copies deep — that is what lets one lookup
        /// resolve a placement whether it was written this build or three builds ago.
        /// </summary>
        [Test]
        public void EveryKeyStillNamesItsAsset()
        {
            var placements = new System.Collections.Generic.List<PackPlacement>();

            for (int copy = 0; copy < 5; copy++)
            {
                string key = PackItemKey.Mint("abc123", placements);

                Assert.AreEqual("abc123", PackItemKey.AssetOf(key),
                                "Copy " + copy + "'s key does not resolve back to its asset.");
                Assert.IsTrue(PackItemKey.NamesAsset(key, "abc123"));

                placements.Add(new PackPlacement(key, PackSurfaceId.Leaf, Vector2.zero, 0f));
            }
        }

        /// <summary>
        /// A placement carries its charge, and moving it does not change it.
        ///
        /// <para>
        /// The charge rides the placement rather than a table beside the layout precisely so that
        /// every path which already moves an item correctly moves its contents too. A drag that
        /// emptied a tank would be the first thing a parallel table got wrong.
        /// </para>
        /// </summary>
        [Test]
        public void APlacementCarriesItsChargeAndAMoveKeepsIt()
        {
            var layout = new PackLayout();
            var size = new Vector2(1f, 1f);
            PackShape shape = PackShape.Rect(2, 2);

            Assert.IsTrue(layout.TryPlace("abc123", PackSurfaceId.Leaf, size, shape,
                                          new Vector2(0.2f, 0.2f), 0f, charge: 0.43f),
                          "precondition: the placement was refused");

            Assert.AreEqual(0.43f, layout.Placements[0].Charge, 0.001f,
                            "A placement did not keep the charge it was placed with.");

            Assert.IsTrue(layout.TryMove("abc123", PackSurfaceId.Leaf, size, shape,
                                         new Vector2(0.6f, 0.6f), 0f),
                          "precondition: the move was refused");

            Assert.AreEqual(0.43f, layout.Placements[0].Charge, 0.001f,
                            "Moving a tank across the mat changed how full it is.");
        }

        /// <summary>An item that holds nothing stays holding nothing, rather than becoming empty.</summary>
        [Test]
        public void AnItemThatHoldsNothingIsNotAnEmptyTank()
        {
            var layout = new PackLayout();

            Assert.IsTrue(layout.TryPlace("rifle", PackSurfaceId.Leaf, new Vector2(1f, 1f),
                                          PackShape.Rect(2, 2), new Vector2(0.2f, 0.2f), 0f));

            Assert.Less(layout.Placements[0].Charge, 0f,
                        "A rifle was placed reading 0% full, which is a real reading about a " +
                        "reservoir it does not have.");
        }

        // ── The reservoir, extracted from the verb ───────────────────────────

        /// <summary>A tool with a tank. The shape every sprayer in the game is built to.</summary>
        private sealed class TankedTool : ToolItem { }

        /// <summary>
        /// <b>The bug the extraction exists to fix.</b> A charge only reaches the owning client if
        /// <see cref="SupplyCharge.Carries"/> says the item holds one, and that question used to
        /// resolve a <c>DockableSupply</c> — which is itself a <c>UsableItem</c>, so an artifact
        /// with its own verb could not have one. A sprayer therefore held its fill in a component
        /// nothing on the wire, the pack or the save codec would look at, and a client's copy read
        /// the authored starting charge for ever while the server drained it.
        /// </summary>
        [Test]
        public void AToolWithATankCarriesACharge()
        {
            InventoryItem item = TankedItem(startingCharge: 0.5f, out GameObject prefab);
            try
            {
                Assert.IsNull(prefab.GetComponent<DockableSupply>(),
                              "precondition: this item must NOT be a dockable supply, or the test " +
                              "proves only the case that already worked");

                Assert.IsTrue(SupplyCharge.Carries(item),
                              "A tool with a reservoir does not carry a charge, so its fill reaches " +
                              "no client, no pack placement and no save record.");

                Assert.AreEqual(0.5f, SupplyCharge.StartingChargeOf(item), 0.001f,
                                "The item's authored starting charge is not what a container that " +
                                "has never seen it reads.");
            }
            finally
            {
                Cleanup(item, prefab);
            }
        }

        /// <summary>
        /// The fill goes into the slot's bag under the ONE key, through the item's own
        /// <c>UsableItem</c> — which is the only component anything ever asks for a state bag.
        /// </summary>
        [Test]
        public void ATanksFillRidesTheSlotsBagUnderTheOneKey()
        {
            InventoryItem item = TankedItem(startingCharge: 1f, out GameObject prefab);
            try
            {
                prefab.GetComponent<SupplyReservoir>().SetCharge(0.43f);

                var state = new ItemState();
                prefab.GetComponent<UsableItem>().CaptureItemState(state);

                Assert.AreEqual(0.43f, SupplyCharge.Read(state), 0.005f,
                                "A tool's fill did not reach the slot's bag under SupplyCharge's " +
                                "key, so nothing downstream of the bag will carry it.");

                // A second instance, as an equip is: a fresh Instantiate handed the same bag.
                var second = new GameObject("Second");
                try
                {
                    second.AddComponent<SupplyReservoir>();
                    var tool = second.AddComponent<TankedTool>();

                    tool.RestoreItemState(state);

                    Assert.AreEqual(0.43f, second.GetComponent<SupplyReservoir>().Charge, 0.005f,
                                    "A restored tool did not come back at the fill its bag said.");

                    tool.RestoreItemState(null);

                    Assert.AreEqual(1f, second.GetComponent<SupplyReservoir>().Charge, 0.001f,
                                    "A bag with no charge in it read as empty rather than as the " +
                                    "authored starting charge, which would drain every tank in " +
                                    "every existing save on its first load.");
                }
                finally
                {
                    Object.DestroyImmediate(second);
                }
            }
            finally
            {
                Cleanup(item, prefab);
            }
        }

        /// <summary>
        /// The restart threshold, which is the whole reason running dry reads as an interruption
        /// rather than a strobe (<c>GDC-L1-ECON-0002</c>): a tank below it refuses a NEW draw even
        /// though it is no longer empty.
        /// </summary>
        [Test]
        public void ATankThatRanDryRefusesANewDrawUntilItIsWorthStarting()
        {
            var go = new GameObject("Tank");
            try
            {
                var tank = go.AddComponent<SupplyReservoir>();
                var so = new SerializedObject(tank);
                so.FindProperty("drainPerSecond").floatValue = 1f;
                so.FindProperty("refillPerSecond").floatValue = 1f;
                so.FindProperty("restartFraction").floatValue = 0.15f;
                so.ApplyModifiedPropertiesWithoutUndo();

                tank.SetCharge(0.05f);
                Assert.IsFalse(tank.CanStart,
                               "A tank below its restart threshold agreed to a new draw, which is " +
                               "one frame of effect followed by silence, sixty times a second.");

                Assert.IsFalse(tank.Tick(1f, drawing: false),
                               "A refill tick reported that it delivered something.");
                Assert.IsTrue(tank.CanStart, "A refilled tank still refuses to start.");

                tank.SetCharge(0f);
                Assert.IsFalse(tank.Tick(1f, drawing: true),
                               "An empty tank claimed to deliver.");
                Assert.AreEqual(0f, tank.Charge, 0.001f,
                                "A trigger held on an empty tank refilled it, so it sputters for " +
                                "ever instead of running out.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// <b>Read off the assets, never off the class.</b> Every one of these numbers exists twice
        /// — once as a C# initialiser and once serialised on a prefab — and only the second one
        /// ships. A field whose NAME survived the move off <c>ArtifactTank</c> and
        /// <c>DockableSupply</c> would keep whatever it was authored with; a field whose name did
        /// not would silently fall back to a default. This is what tells those two apart.
        /// </summary>
        [Test]
        public void EveryShippedReservoirIsAuthoredOnItsOwnPrefab()
        {
            var expected = new Dictionary<string, (SupplyKind Kind, float Capacity, float Start,
                                                   float Drain, float Refill)>
            {
                ["Assets/Game/Prefabs/Items/Supplies/OxygenTank.prefab"] =
                    (SupplyKind.Oxygen, 30f * 60f, 1f, 0f, 0f),
                ["Assets/Game/Prefabs/Items/Supplies/Battery.prefab"] =
                    (SupplyKind.Power, 1000f, 1f, 0f, 0f),
                ["Assets/Game/Prefabs/Items/Artifacts/Gadgets/Flamethrower.prefab"] =
                    (SupplyKind.Reagent, 6.67f, 1f, 0.15f, 0.06f),
                ["Assets/Game/Prefabs/Items/Artifacts/Gadgets/FoamGun.prefab"] =
                    (SupplyKind.Reagent, 10f, 1f, 0.1f, 0.05f),
                ["Assets/Game/Prefabs/Items/Artifacts/Gadgets/SlickCan.prefab"] =
                    (SupplyKind.Reagent, 8.33f, 1f, 0.12f, 0.06f),
            };

            foreach (var pair in expected)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Key);
                Assert.IsNotNull(prefab, "No prefab at " + pair.Key);

                var tank = prefab.GetComponent<SupplyReservoir>();
                Assert.IsNotNull(tank, pair.Key + " has no SupplyReservoir on its root");

                Assert.AreEqual(pair.Value.Kind, tank.Kind, pair.Key + " holds the wrong kind");
                Assert.AreEqual(pair.Value.Capacity, tank.Capacity, 0.01f,
                                pair.Key + " capacity");
                Assert.AreEqual(pair.Value.Start, tank.StartingCharge, 0.001f,
                                pair.Key + " startingCharge");

                var so = new SerializedObject(tank);
                Assert.AreEqual(pair.Value.Drain, so.FindProperty("drainPerSecond").floatValue,
                                0.001f, pair.Key + " drainPerSecond");
                Assert.AreEqual(pair.Value.Refill, so.FindProperty("refillPerSecond").floatValue,
                                0.001f, pair.Key + " refillPerSecond");
            }
        }

        /// <summary>
        /// <see cref="SupplyKind"/> is persisted and sent as a byte, so an existing value that
        /// moved would rewrite every save on disk and every message in flight without a word.
        /// </summary>
        [Test]
        public void SupplyKindIsAppendOnly()
        {
            Assert.AreEqual(0, (byte)SupplyKind.Oxygen);
            Assert.AreEqual(1, (byte)SupplyKind.Power);
            Assert.AreEqual(2, (byte)SupplyKind.Reagent);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>An item whose prefab is a tool with a tank, and nothing else.</summary>
        private static InventoryItem TankedItem(float startingCharge, out GameObject prefab)
        {
            prefab = new GameObject("TankedTool");
            prefab.AddComponent<TankedTool>();

            var tank = prefab.AddComponent<SupplyReservoir>();
            var so = new SerializedObject(tank);
            so.FindProperty("startingCharge").floatValue = startingCharge;
            so.ApplyModifiedPropertiesWithoutUndo();

            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.name = "TankedTool";
            item.ID = "test-tanked-tool";
            item.itemPrefab = prefab;
            return item;
        }

        private static void Cleanup(InventoryItem item, GameObject prefab)
        {
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(item);
        }
    }
}
