using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The four claims the oxygen plant is worth having: it does nothing without a power cell, a
    /// filled bottle is a different item from a drained one, both supplies fit the backpack, and
    /// what is docked survives a save.
    ///
    /// <para>
    /// Driven through <c>RestoreDock</c> rather than through a press, deliberately. A press needs
    /// an <c>Interactor</c> on a player prefab with an inventory and a session behind it, none of
    /// which exists in EditMode — while <c>RestoreDock</c> reaches the same state through the same
    /// code (it publishes and then calls <c>RefreshFill</c>), which is what these tests are about.
    /// </para>
    /// </summary>
    public class OxygenSystemTests
    {
        private const string PlantPrefab =
            "Assets/Game/Prefabs/Environment/Structures/Facilities/OxygenGenerator.prefab";

        private const string TankPrefab = "Assets/Game/Prefabs/Items/Supplies/OxygenTank.prefab";
        private const string DrainedPrefab = "Assets/Game/Prefabs/Items/Supplies/OxygenTankEmpty.prefab";
        private const string CellPrefab = "Assets/Game/Prefabs/Items/Supplies/PowerCell.prefab";

        private const string RigPrefab = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        // The rig's two square faces, in cells. Anything wider than these fits neither, which for
        // a supply the player is meant to carry is the same as not fitting the pack at all.
        private const int LeafCells = 8;
        private const int RackCells = 9;

        private GameObject spawned;

        [TearDown]
        public void TearDown()
        {
            if (spawned != null) Object.DestroyImmediate(spawned);
            spawned = null;
            ItemFootprint.ClearCache();
        }

        private OxygenGenerator Plant()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlantPrefab);
            Assert.IsNotNull(prefab, "No plant at " + PlantPrefab +
                                     " — run Tools/SpaceGame/Build Oxygen System.");

            spawned = Object.Instantiate(prefab);
            return spawned.GetComponent<OxygenGenerator>();
        }

        /// <summary>
        /// The request's own rule: the machine only works with the battery in. A bottle sitting in
        /// an unpowered plant must stay drained forever, and the same bottle in a powered one must
        /// start filling with no second press.
        /// </summary>
        [Test]
        public void ThePlantOnlyFillsWithAPowerCellIn()
        {
            OxygenGenerator plant = Plant();

            plant.RestoreDock(false, OxygenGenerator.DockedTank.Drained);
            Assert.IsFalse(plant.Powered, "A plant with no cell reports itself powered.");
            Assert.IsFalse(plant.IsFilling,
                           "An unpowered plant is filling a bottle — the cell is not gating it.");

            plant.RestoreDock(true, OxygenGenerator.DockedTank.Drained);
            Assert.IsTrue(plant.Powered, "A fitted cell does not power the plant.");
            Assert.IsTrue(plant.IsFilling,
                          "A drained bottle in a powered plant is not filling. RefreshFill is the " +
                          "only thing that starts one, and it runs after every change.");

            // Taking the cell out again must stop it rather than leave a deadline nobody watches.
            plant.RestoreDock(false, OxygenGenerator.DockedTank.Drained);
            Assert.IsFalse(plant.IsFilling, "Pulling the cell left the fill running.");
        }

        /// <summary>
        /// A full bottle is a full bottle by IDENTITY. If the two ever collapse into one asset the
        /// charge has to live in an <c>ItemState</c> bag instead — which does not replicate, so only
        /// the server would ever know a bottle was full. See DockableSupply.
        /// </summary>
        [Test]
        public void AFilledBottleIsADifferentItemFromADrainedOne()
        {
            var charged = AssetDatabase.LoadAssetAtPath<GameObject>(TankPrefab);
            var drained = AssetDatabase.LoadAssetAtPath<GameObject>(DrainedPrefab);

            Assert.IsNotNull(charged, "No prefab at " + TankPrefab);
            Assert.IsNotNull(drained, "No prefab at " + DrainedPrefab);
            Assert.AreNotSame(charged, drained, "The two bottles are one prefab.");

            Assert.IsTrue(charged.GetComponent<DockableSupply>().Charged,
                          "The filled bottle is not authored charged, so its gauge reads empty.");
            Assert.IsFalse(drained.GetComponent<DockableSupply>().Charged,
                           "The drained bottle is authored charged, so a fill shows nothing.");

            // Same model, so they must cost the same cells: a player who fills a bottle must not
            // find the pack refuses to take it back.
            Assert.AreEqual(ItemFootprint.SizeOf(charged), ItemFootprint.SizeOf(drained),
                            "The two bottles are different sizes on the mat, so filling one can " +
                            "make it unstowable.");
        }

        /// <summary>
        /// Both supplies actually fit the pack — the request's own words. The failure this catches
        /// is a size nudged for the hand: <c>packSize</c> at 0 wires an item's share of the mat to
        /// the hand's bracket ladder, and the bottle would then eat a third of the rig.
        /// </summary>
        [Test]
        public void TheSupplyItemsFitTheBackpacksFaces()
        {
            foreach (string path in new[] { TankPrefab, DrainedPrefab, CellPrefab })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, "No prefab at " + path);

                Vector2 footprint = ItemFootprint.FootprintOf(prefab);
                int across = Mathf.CeilToInt(footprint.x / PackGrid.Cell);
                int up = Mathf.CeilToInt(footprint.y / PackGrid.Cell);

                Assert.That(across, Is.LessThanOrEqualTo(LeafCells),
                            path + " is " + across + " cells across, and the leaf is " + LeafCells +
                            " — an item wider than a face is unstorable everywhere.");
                Assert.That(up, Is.LessThanOrEqualTo(LeafCells),
                            path + " is " + up + " cells deep, and the leaf is " + LeafCells + ".");
                Assert.That(across * up, Is.LessThan(LeafCells * LeafCells / 2),
                            path + " costs " + (across * up) + " of the leaf's " +
                            LeafCells * LeafCells + " cells — over half a face for one supply.");
                Assert.That(Mathf.Max(across, up), Is.LessThanOrEqualTo(RackCells));
            }
        }

        /// <summary>
        /// The bottle lies down on the mat, gauge outward — and both halves of that are things a
        /// rotation gets wrong silently.
        ///
        /// <para>
        /// <b>Lying down</b> is what makes it usable on the pack at all. A surface seats an item
        /// with the ITEM's own up along the surface NORMAL, so a bottle modelled standing on its
        /// skirt stands straight out of a vertical back panel by its whole length. Laid down it
        /// lies flat against the panel, which is where the rig's own modelled bottle used to be.
        /// </para>
        /// <para>
        /// <b>The sign</b> is the half that looks plausible either way: +90 and -90 about X both
        /// lay a bottle down, and one of them buries its gauge in the surface it is lying on.
        /// Measured off the built prefab rather than read off the builder's constant.
        /// </para>
        /// </summary>
        [Test]
        public void TheBottleLiesDownWithItsGaugeOutward()
        {
            foreach (string path in new[] { TankPrefab, DrainedPrefab })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, "No prefab at " + path);

                Vector3 size = ItemFootprint.SizeOf(prefab);
                Assert.That(size.y, Is.LessThan(Mathf.Max(size.x, size.z)),
                            path + " is still taller than it is long, so it STANDS on a face: on a " +
                            "vertical back panel it would point " + size.y.ToString("F2") +
                            " m straight out of the wearer's back.");

                // The gauge is the flank the player reads. It has to end up on the item's own +Y,
                // because +Y is the axis every surface seats along its normal.
                var supply = prefab.GetComponent<DockableSupply>();
                Assert.IsNotNull(supply, path + " has no DockableSupply");
                Assert.IsNotNull(supply.Readout, path + " has no gauge to point anywhere");

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    Renderer gauge = instance.GetComponentsInChildren<Renderer>(true)
                        .First(r => r.name == supply.Readout.name);

                    // In the ROOT's frame, which is the frame a surface seats.
                    Vector3 local = instance.transform.InverseTransformPoint(gauge.bounds.center);
                    Vector3 middle = ItemFootprint.CentreOffsetOf(prefab);

                    Assert.That(local.y - middle.y, Is.GreaterThan(0f),
                                path + "'s gauge sits at y " + local.y.ToString("F3") +
                                " against a middle of " + middle.y.ToString("F3") +
                                " — it is on the underside, so lying on a face buries it and on a " +
                                "back panel it faces into the panel. The lay-down turn has the " +
                                "wrong SIGN.");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        /// <summary>
        /// The bottle has a socket on the pack, only the bottle may use it, and first-fit puts a
        /// bottle there rather than on the mat.
        ///
        /// <para>
        /// All three halves fail silently and differently. A socket that lost its reservation
        /// becomes an ordinary shelf and the first thing that fits goes in it. A socket first-fit
        /// does not PREFER stays empty while the bottle sits on the mat — which looks fine, and
        /// leaves the one place the pack is plumbed into unused. And a socket the bottle cannot
        /// enter is just a hole in the rig.
        /// </para>
        /// </summary>
        [Test]
        public void TheBottleHasASocketOnTheRigAndOnlyTheBottleMayUseIt()
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            Assert.IsNotNull(rigPrefab, "No rig at " + RigPrefab +
                                        " — run Tools/SpaceGame/Items/Build Expedition Rig Prefab.");

            spawned = Object.Instantiate(rigPrefab);
            var pack = spawned.GetComponent<PackContainer>();
            Assert.IsNotNull(pack, "The rig has no PackContainer.");

            PackSurface socket = pack.SurfaceFor(PackSurfaceId.BackPanelCentre);
            Assert.IsNotNull(socket, "The rig has no centre back socket — the bottle has nowhere " +
                                     "to go where the pack is plumbed into it.");
            Assert.AreEqual(new Vector2Int(3, 6), PackGrid.CellsOn(socket.Size),
                            "The socket is " + socket.Size.ToString("F3") + " m, which is not the " +
                            "3 x 6 cells a bottle lying down occupies.");

            var charged = AssetDatabase.LoadAssetAtPath<InventoryItem>(
                "Assets/Game/Resources/Items/Supplies/OxygenTank.asset");
            var drained = AssetDatabase.LoadAssetAtPath<InventoryItem>(
                "Assets/Game/Resources/Items/Supplies/OxygenTankEmpty.asset");
            Assert.IsNotNull(charged);
            Assert.IsNotNull(drained);

            // Both bottles, because a player takes a full one out and puts a drained one back.
            Assert.IsTrue(socket.AcceptsItem(charged), "The socket refuses the filled bottle.");
            Assert.IsTrue(socket.AcceptsItem(drained), "The socket refuses the drained bottle.");

            // And nothing else. Any other item on the roster will do as the witness.
            var other = AssetDatabase.LoadAssetAtPath<InventoryItem>(
                "Assets/Game/Resources/Items/Supplies/PowerCell.asset");
            Assert.IsNotNull(other);
            Assert.IsFalse(socket.AcceptsItem(other),
                           "The socket takes the power cell, so it is a shelf rather than the " +
                           "bottle's socket and the pack is plumbed into whatever fits.");

            // The preference, asked through the path the rig's own AUTHORED contents take.
            //
            // Deliberately not `TryStow`: that is the player-facing path and is gated on `Reaches`,
            // and a pack is WORN from its Awake — worn, the only face that exists is the rack, so
            // TryStow can only ever answer "rack" and would say nothing about the ordering this is
            // testing. `TryArrange` over every surface is exactly what `StowAuthored` calls.
            Assert.IsTrue(PackContainer.TryArrange(pack.Layout, pack.Surfaces, charged, pack.Shapes),
                          "The rig would not take a bottle at all.");
            Assert.IsTrue(pack.Layout.Placements.Any(
                              p => p.Surface == PackSurfaceId.BackPanelCentre && p.ItemId == charged.ID),
                          "A stowed bottle did not land in its socket — first-fit put it on " +
                          string.Join(", ", pack.Layout.Placements.Select(p => p.Surface.ToString())) +
                          ". TryArrange offers RESERVED faces in a pass of their own, before the " +
                          "general shelves, for exactly this reason.");

            // And the socket really is the reason, not the order of the table: a power cell offered
            // the same way must go somewhere else entirely.
            Assert.IsTrue(PackContainer.TryArrange(pack.Layout, pack.Surfaces, other, pack.Shapes),
                          "The rig would not take a power cell at all.");
            Assert.IsFalse(pack.Layout.Placements.Any(
                               p => p.Surface == PackSurfaceId.BackPanelCentre && p.ItemId == other.ID),
                           "First-fit put a power cell in the bottle's socket.");
        }

        /// <summary>
        /// Each dock draws and erases its own copy, the moment its own state changes.
        ///
        /// <para>
        /// The failure this catches is silent and looks like a rendering bug: one method rebuilt
        /// BOTH copies and <c>Adopt</c> called it only when the TANK changed, so fitting a cell lit
        /// the lamps and drew nothing, and the cell then appeared in the slot at the unrelated
        /// moment a bottle was docked or a fill landed. The last two assertions pin the other half
        /// of the split — a cell press must not rebuild the BOTTLE, because rebuilding it drops the
        /// gauge the fill is painting and fitting the cell is exactly what starts a fill.
        /// </para>
        /// </summary>
        [Test]
        public void EachDockDrawsItsOwnCopyWhenItsOwnStateChanges()
        {
            OxygenGenerator plant = Plant();

            Transform cellSeat = spawned.transform.Find("CellDock/Seat");
            Transform tankSeat = spawned.transform.Find("TankDock/Seat");
            Assert.IsNotNull(cellSeat, "No CellDock/Seat on the plant — rebuild it with " +
                                       "Tools/SpaceGame/Build Oxygen System.");
            Assert.IsNotNull(tankSeat, "No TankDock/Seat on the plant — rebuild it with " +
                                       "Tools/SpaceGame/Build Oxygen System.");

            plant.RestoreDock(true, OxygenGenerator.DockedTank.None);
            Assert.AreEqual(1, cellSeat.childCount,
                            "Fitting a cell lit the machine but drew no cell in the slot.");
            Assert.AreEqual(0, tankSeat.childCount, "A bottle appeared in an empty collar.");

            plant.RestoreDock(false, OxygenGenerator.DockedTank.None);
            Assert.AreEqual(0, cellSeat.childCount,
                            "Taking the cell out left its copy standing in the slot.");

            plant.RestoreDock(false, OxygenGenerator.DockedTank.Drained);
            Assert.AreEqual(1, tankSeat.childCount, "A docked bottle is not drawn in the collar.");
            Assert.AreEqual(0, cellSeat.childCount, "A cell appeared in an empty slot.");

            // Powering up must leave the bottle alone: it is about to start filling, and the fill
            // paints a gauge bound to the copy standing there now.
            Transform bottle = tankSeat.GetChild(0);
            plant.RestoreDock(true, OxygenGenerator.DockedTank.Drained);
            Assert.AreEqual(1, cellSeat.childCount, "Fitting a cell over a docked bottle drew no cell.");
            Assert.AreSame(bottle, tankSeat.GetChild(0),
                           "Fitting the cell rebuilt the docked bottle, which drops the gauge the " +
                           "fill it just started is painting.");
        }

        /// <summary>
        /// What is docked survives a reload. Both docks hold real items out of somebody's hotbar,
        /// so losing them is not a cosmetic reset — and the fill deadline deliberately does NOT
        /// survive: it is an instant on a clock the loaded session does not share, so a powered
        /// plant starts a fresh fill instead.
        /// </summary>
        [Test]
        public void TheSaverRoundTripsBothDocks()
        {
            OxygenGenerator plant = Plant();
            var saver = plant.GetComponent<OxygenGeneratorSaveable>();
            Assert.IsNotNull(saver, "The plant has no OxygenGeneratorSaveable baked in, so a " +
                                    "fitted cell is gone after every load.");

            plant.RestoreDock(true, OxygenGenerator.DockedTank.Charged);

            object captured = saver.CaptureState();
            Assert.IsNotNull(captured, "A loaded plant captured nothing.");

            // Through a real string, because that is what a save file is.
            JObject payload = JObject.Parse(JObject.FromObject(captured, SaveSerializer.Serializer).ToString());

            plant.RestoreDock(false, OxygenGenerator.DockedTank.None);
            Assert.IsFalse(plant.Powered, "precondition: the plant was not emptied");

            saver.RestoreState(payload);
            Assert.IsTrue(plant.Powered, "The restored plant lost its power cell.");
            Assert.AreEqual(OxygenGenerator.DockedTank.Charged, plant.Tank,
                            "The restored plant lost the bottle standing in it.");
            Assert.IsFalse(plant.IsFilling,
                           "A restored plant with a FULL bottle is filling it again.");

            // And an untouched machine writes no record at all, so every ship that never used it
            // does not carry one.
            plant.RestoreDock(false, OxygenGenerator.DockedTank.None);
            Assert.IsNull(saver.CaptureState(),
                          "An empty plant writes a record, which puts one on every unused ship.");
        }

        /// <summary>
        /// The saver is reached the way the game reaches it, not only by hand: the policy has to
        /// recognise the component, or a plant placed anywhere other than the ship is never wired.
        /// </summary>
        [Test]
        public void ThePolicyGivesAPlantItsSaver()
        {
            var host = new GameObject("plant");
            try
            {
                host.AddComponent<OxygenGenerator>();
                Assert.IsTrue(SaveablePolicy.NeedsSaving(host, out _),
                              "SaveablePolicy does not think an oxygen plant is world state.");

                SaveablePolicy.Ensure(host, out _);
                Assert.IsNotNull(host.GetComponent<OxygenGeneratorSaveable>(),
                                 "SaveablePolicy.Ensure did not add the plant's saver.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
