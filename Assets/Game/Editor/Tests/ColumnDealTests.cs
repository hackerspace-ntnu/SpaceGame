// The column a group deals its shuffled members into: a seeded shuffle, the same for the same seed,
// that keeps each card's rules (ColumnDeal).
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class ColumnDealTests
    {
        private static readonly FormationShape Shape = new FormationShape { Lanes = 2, RowSpacing = 35f, LaneSpacing = 30f };

        private static ColumnCard Card(string kind = null, int withinFirstSlots = 0, bool keepsClear = false, float length = 4f,
                                       float width = 4f) =>
            new ColumnCard
            {
                shuffled = true,
                kind = kind,
                withinFirstSlots = withinFirstSlots,
                keepsClear = keepsClear,
                footprint = new Rect(-width * 0.5f, -length * 0.5f, width, length),
            };

        private static readonly ColumnCard Leader = new ColumnCard { footprint = new Rect(-2f, -2f, 4f, 4f) };

        private static int[] Slots(int n) => Enumerable.Range(0, n).ToArray();

        [Test]
        public void FirstBroken_ACardPastItsFirstSlots()
        {
            ColumnCard[] column = { Card(), Card(), Card(withinFirstSlots: 2) };
            StringAssert.Contains("slot 2", ColumnDeal.FirstBroken(Leader, column, Slots(3), Shape));
            Assert.IsNull(ColumnDeal.FirstBroken(Leader, column.Reverse().ToArray(), Slots(3), Shape));
        }

        [Test]
        public void FirstBroken_AKindBesideOrBehindItsOwn_ButKindlessGoAnywhere()
        {
            Assert.IsNotNull(ColumnDeal.FirstBroken(Leader, new[] { Card("house"), Card("house") }, Slots(2), Shape), "side by side in a row");
            Assert.IsNotNull(ColumnDeal.FirstBroken(Leader, new[] { Card("house"), Card(), Card("house") }, Slots(3), Shape), "nose to tail in a lane");
            Assert.IsNull(ColumnDeal.FirstBroken(Leader, new[] { Card("house"), Card(), Card(), Card("house") }, Slots(4), Shape), "diagonal");
            Assert.IsNull(ColumnDeal.FirstBroken(Leader, new[] { Card(), Card(), Card() }, Slots(3), Shape), "kindless");
        }

        [Test]
        public void FirstBroken_ACardThatKeepsClear_TouchingItsNeighbour()
        {
            // 70 m long and 16 m wide in 35 m rows: it reaches the staggered member behind it in its lane.
            ColumnCard[] column = { Card(keepsClear: true, length: 70f, width: 16f), Card(), Card() };
            StringAssert.Contains("overlap", ColumnDeal.FirstBroken(Leader, column, Slots(3), Shape));
            Assert.IsNull(ColumnDeal.FirstBroken(Leader, new[] { Card(keepsClear: true, length: 20f, width: 16f), Card(), Card() },
                                                 Slots(3), Shape));
        }

        [Test]
        public void TryDeal_KeepsTheRules_AndTheSameSeedDealsTheSameColumn()
        {
            ColumnCard[] deck = Deck();
            for (int seed = 0; seed < 100; seed++)
            {
                Assert.IsTrue(ColumnDeal.TryDeal(Leader, deck, Slots(deck.Length), Shape, seed, out int[] order), $"seed {seed}");
                Assert.IsNull(ColumnDeal.FirstBroken(Leader, order.Select(i => deck[i]).ToArray(), Slots(deck.Length), Shape));
                ColumnDeal.TryDeal(Leader, deck, Slots(deck.Length), Shape, seed, out int[] again);
                CollectionAssert.AreEqual(order, again, "a refold or a reload must bring back the same column");
                CollectionAssert.AreEquivalent(Enumerable.Range(0, deck.Length), order, "every card dealt once");
            }
        }

        /// The user, 2026-10-05: "like drawing from a card deck" — not sorted by kind, and not the same
        /// column in every world. Every card turns up in the first slot under some seed.
        [Test]
        public void TryDeal_AnotherSeed_DealsAnotherColumn()
        {
            ColumnCard[] deck = Enumerable.Range(0, 6).Select(_ => Card()).ToArray();
            var first = new HashSet<int>();
            var columns = new HashSet<string>();
            for (int seed = 0; seed < 200; seed++)
            {
                ColumnDeal.TryDeal(Leader, deck, Slots(deck.Length), Shape, seed, out int[] order);
                first.Add(order[0]);
                columns.Add(string.Join(",", order));
            }

            Assert.AreEqual(deck.Length, first.Count);
            Assert.Greater(columns.Count, 150);
        }

        [Test]
        public void TryDeal_NoColumnKeepsTheRules_LogsAndDealsNothing()
        {
            ColumnCard[] deck = { Card(withinFirstSlots: 1), Card(withinFirstSlots: 1) };
            LogAssert.Expect(LogType.Error, new Regex("No column"));
            Assert.IsFalse(ColumnDeal.TryDeal(Leader, deck, Slots(2), Shape, 1, out _));
        }

        /// A small city: carriers up front, kinds that may not touch, one that keeps clear.
        private static ColumnCard[] Deck() =>
            new[]
            {
                Card("house", 6), Card("house", 6), Card("barge", 6, keepsClear: true, length: 20f),
                Card("crawler"), Card("crawler"), Card(), Card(), Card(), Card(),
            };
    }
}
