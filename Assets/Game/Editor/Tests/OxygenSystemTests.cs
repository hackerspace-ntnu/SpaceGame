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
        private const string CellPrefab = "Assets/Game/Prefabs/Items/Supplies/Battery.prefab";

        private const string RigPrefab = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        private const string PlayerPrefab =
            "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";

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
        /// The request's own rule: the machine only works with the battery in. A tank sitting in an
        /// unpowered plant must stay where it is forever, and the same tank in a powered one must
        /// start filling with no second press.
        /// </summary>
        [Test]
        public void ThePlantOnlyFillsWithABatteryIn()
        {
            OxygenGenerator plant = Plant();

            plant.RestoreDock(-1f, 0f);
            Assert.IsFalse(plant.Powered, "A plant with no battery reports itself powered.");
            Assert.IsFalse(plant.IsFilling,
                           "An unpowered plant is filling a tank — the battery is not gating it.");

            plant.RestoreDock(1f, 0f);
            Assert.IsTrue(plant.Powered, "A fitted battery does not power the plant.");
            Assert.IsTrue(plant.IsFilling,
                          "An empty tank in a powered plant is not filling. RefreshFill is the " +
                          "only thing that starts one, and it runs after every change.");

            // Taking the battery out again must stop it rather than leave a deadline nobody watches.
            plant.RestoreDock(-1f, 0f);
            Assert.IsFalse(plant.IsFilling, "Pulling the battery left the fill running.");
        }

        /// <summary>
        /// A FLAT battery is not a missing one, and it is not a working one either.
        ///
        /// <para>
        /// The three states have to stay distinct: no battery (nothing to take back), a flat
        /// battery (an item the player can still retrieve, machine dark), and a charged one. A
        /// machine that looked powered and refused to fill would be the least explainable state it
        /// could be in, so <c>Powered</c> asks about the CHARGE and not about the slot.
        /// </para>
        /// </summary>
        [Test]
        public void AFlatBatteryIsFittedButNotPowered()
        {
            OxygenGenerator plant = Plant();

            plant.RestoreDock(0f, 0f);

            Assert.IsTrue(plant.HasBattery, "A flat battery is not reported as fitted, so the " +
                                            "player cannot take back the item they put in.");
            Assert.IsFalse(plant.Powered, "A flat battery powers the plant.");
            Assert.IsFalse(plant.IsFilling, "A flat battery is filling a tank out of nothing.");
        }

        /// <summary>
        /// The fill is PROPORTIONAL in both time and cost, which is the rule the whole economy
        /// rests on: a battery is worth a fixed number of tanks however the player chooses to take
        /// them, and topping up is never punished.
        ///
        /// <para>
        /// A flat cost per press would teach players to run every tank to zero before coming back,
        /// which is the opposite of the behaviour the plant exists to encourage.
        /// </para>
        /// </summary>
        [Test]
        public void APartialFillCostsAndTakesItsOwnFraction()
        {
            OxygenGenerator plant = Plant();

            // Three quarters full: a quarter of a tank to move.
            plant.RestoreDock(1f, 0.75f);

            Assert.IsTrue(plant.IsFilling, "A part-full tank in a powered plant is not topping up.");

            // The deadline is on the machine's own clock, so the elapsed span is what is asserted
            // rather than a wall-clock wait: a quarter of a tank is a quarter of the fill time.
            Assert.AreEqual(plant.FillSeconds * 0.25f, plant.SecondsUntilFilled, 0.05f,
                            "A quarter-tank top-up is not taking a quarter of the fill time.");
        }

        /// <summary>
        /// A battery with less charge than the tank has room fills what it can and stops, rather
        /// than filling the whole tank for free or refusing outright.
        /// </summary>
        [Test]
        public void AFillIsBoundedByWhatTheBatteryCanPayFor()
        {
            OxygenGenerator plant = Plant();

            // Half of one tank's cost left in the battery, against a completely empty tank.
            plant.RestoreDock(plant.FillCostPerTank * 0.5f, 0f);

            Assert.IsTrue(plant.IsFilling, "A battery with some charge left is not filling at all.");
            Assert.AreEqual(plant.FillSeconds * 0.5f, plant.SecondsUntilFilled, 0.05f,
                            "The fill is not bounded by what the battery can pay for — it is " +
                            "either running to a full tank on half the power, or refusing.");
        }

        /// <summary>
        /// There is exactly ONE tank item, at every fill level.
        ///
        /// <para>
        /// It used to be two — <c>OxygenTank</c> and <c>OxygenTankEmpty</c> — because a charge that
        /// lived in an <c>ItemState</c> bag would have been a value only the server could see. That
        /// cannot express a tank read to a percent, so the charge is a fraction carried by
        /// <see cref="SupplyCharge"/> through every container instead. If a second tank asset ever
        /// reappears, the two can diverge in <c>packSize</c> and filling a tank becomes a way to
        /// make it unstowable in the pack that took it.
        /// </para>
        /// </summary>
        [Test]
        public void ThereIsExactlyOneTankItem()
        {
            var tank = AssetDatabase.LoadAssetAtPath<GameObject>(TankPrefab);
            Assert.IsNotNull(tank, "No prefab at " + TankPrefab);

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(
                              "Assets/Game/Prefabs/Items/Supplies/OxygenTankEmpty.prefab"),
                          "OxygenTankEmpty.prefab is back. A tank's charge is a number on the " +
                          "instance now; a second asset is a second way for a tank to exist.");

            var supply = tank.GetComponent<DockableSupply>();
            Assert.IsNotNull(supply, "The tank has no DockableSupply, so it holds nothing.");
            Assert.AreEqual(SupplyKind.Oxygen, supply.Kind, "The tank does not hold oxygen.");
            Assert.Greater(supply.Capacity, 0f, "The tank has no capacity, so it can never fill.");
        }

        /// <summary>
        /// Thirty minutes of air, one minute of reserve, and one second spent per second — read off
        /// the assets the game actually ships, never off the C# defaults.
        ///
        /// <para>
        /// <b>The distinction is the whole point of this test.</b> An <c>AddComponent</c> in an
        /// EditMode test constructs the class, so it reads the field initialisers — which were
        /// correct all along. The PREFAB is a separate copy of those numbers, and a field whose
        /// NAME survives a rework keeps whatever it serialised years ago: <c>drainPerSecond</c> came
        /// through this rework still holding <c>0.167</c> from the old arbitrary-units model, which
        /// would have made a "thirty minute" tank last three hours and the "sixty second" reserve
        /// last six minutes. Nothing failed, nothing logged, and a test against the defaults would
        /// have passed.
        /// </para>
        /// </summary>
        [Test]
        public void TheSurvivalBudgetIsWhatTheShippedAssetsSay()
        {
            var tank = AssetDatabase.LoadAssetAtPath<GameObject>(TankPrefab);
            Assert.IsNotNull(tank, "No prefab at " + TankPrefab);

            Assert.AreEqual(30f * 60f, tank.GetComponent<DockableSupply>().Capacity, 0.5f,
                            "A full tank is no longer thirty minutes of air.");

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(player, "No prefab at " + PlayerPrefab);

            SuitOxygen suit = player.GetComponentInChildren<SuitOxygen>(true);
            Assert.IsNotNull(suit, "The player prefab carries no SuitOxygen, so nobody breathes.");

            var so = new SerializedObject(suit);

            Assert.AreEqual(60f, so.FindProperty("suitSeconds").floatValue, 0.5f,
                            "The suit's last-resort reserve is no longer sixty seconds.");

            Assert.AreEqual(1f, so.FindProperty("drainPerSecond").floatValue, 0.001f,
                            "The drain is not one second of air per second, so a tank's CAPACITY " +
                            "is no longer its duration and every number in Oxygen.md is a lie.");

            Assert.AreEqual(5, so.FindProperty("suffocationDamage").intValue,
                            "Suffocation is not 5 damage a tick.");
            Assert.AreEqual(1f, so.FindProperty("suffocationInterval").floatValue, 0.001f,
                            "Suffocation does not tick once a second.");
            Assert.AreEqual(0.10f, so.FindProperty("warnFraction").floatValue, 0.001f,
                            "The visor no longer warns at 10% of the connected tank.");
        }

        /// <summary>
        /// Both supplies actually fit the pack — the request's own words. The failure this catches
        /// is a size nudged for the hand: <c>packSize</c> at 0 wires an item's share of the mat to
        /// the hand's bracket ladder, and the bottle would then eat a third of the rig.
        /// </summary>
        [Test]
        public void TheSupplyItemsFitTheBackpacksFaces()
        {
            foreach (string path in new[] { TankPrefab, CellPrefab })
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
            foreach (string path in new[] { TankPrefab })
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

            var tank = AssetDatabase.LoadAssetAtPath<InventoryItem>(
                "Assets/Game/Resources/Items/Supplies/OxygenTank.asset");
            Assert.IsNotNull(tank);

            // ONE tank, at every fill level. There used to be two entries here because a charge was
            // an item identity; the socket reserving two assets was the only way a player could
            // take a full one out and put an empty one back.
            Assert.IsTrue(socket.AcceptsItem(tank), "The socket refuses the oxygen tank.");
            Assert.AreEqual(1, socket.AcceptsOnly.Count,
                            "The socket reserves more than one asset. A tank's charge is a number " +
                            "now, so a second reserved asset is a second way for a tank to exist.");

            // And nothing else. Any other item on the roster will do as the witness.
            var other = AssetDatabase.LoadAssetAtPath<InventoryItem>(
                "Assets/Game/Resources/Items/Supplies/Battery.asset");
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
            Assert.IsTrue(PackContainer.TryArrange(pack.Layout, pack.Surfaces, tank, pack.Shapes),
                          "The rig would not take a tank at all.");
            Assert.IsTrue(pack.Layout.Placements.Any(
                              p => p.Surface == PackSurfaceId.BackPanelCentre
                                   && PackItemKey.NamesAsset(p.ItemId, tank.ID)),
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

            plant.RestoreDock(1f, -1f);
            Assert.AreEqual(1, cellSeat.childCount,
                            "Fitting a battery lit the machine but drew no battery in the slot.");
            Assert.AreEqual(0, tankSeat.childCount, "A tank appeared in an empty collar.");

            plant.RestoreDock(-1f, -1f);
            Assert.AreEqual(0, cellSeat.childCount,
                            "Taking the battery out left its copy standing in the slot.");

            plant.RestoreDock(-1f, 0f);
            Assert.AreEqual(1, tankSeat.childCount, "A docked tank is not drawn in the collar.");
            Assert.AreEqual(0, cellSeat.childCount, "A battery appeared in an empty slot.");

            // Powering up must leave the tank alone: it is about to start filling, and the fill
            // paints a gauge bound to the copy standing there now.
            Transform bottle = tankSeat.GetChild(0);
            plant.RestoreDock(1f, 0f);
            Assert.AreEqual(1, cellSeat.childCount, "Fitting a battery over a docked tank drew none.");
            Assert.AreSame(bottle, tankSeat.GetChild(0),
                           "Fitting the battery rebuilt the docked tank, which drops the gauge the " +
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

            // A PART-charged pair, which is the whole reason both are floats now: the old
            // format had three enum values and nothing partial to lose.
            plant.RestoreDock(0.6f, 1f);

            object captured = saver.CaptureState();
            Assert.IsNotNull(captured, "A loaded plant captured nothing.");

            // Through a real string, because that is what a save file is.
            JObject payload = JObject.Parse(JObject.FromObject(captured, SaveSerializer.Serializer).ToString());

            plant.RestoreDock(-1f, -1f);
            Assert.IsFalse(plant.Powered, "precondition: the plant was not emptied");

            saver.RestoreState(payload);
            Assert.IsTrue(plant.Powered, "The restored plant lost its battery.");
            Assert.AreEqual(0.6f, plant.BatteryCharge, 0.01f,
                            "The restored battery came back at a different charge — a partial " +
                            "charge is expressible now, so rounding it is a real loss.");
            Assert.AreEqual(1f, plant.TankCharge, 0.01f,
                            "The restored plant lost the tank standing in it.");
            Assert.IsFalse(plant.IsFilling,
                           "A restored plant with a FULL tank is filling it again.");

            // And an untouched machine writes no record at all, so every ship that never used it
            // does not carry one.
            plant.RestoreDock(-1f, -1f);
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
