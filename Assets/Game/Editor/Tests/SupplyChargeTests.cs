using NUnit.Framework;
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
    }
}
