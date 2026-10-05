// Rosters: deterministic draws, weights, and the deck a group is dealt from.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class RosterDrawTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private GameObject Prefab(string name)
        {
            var go = new GameObject(name);
            junk.Add(go);
            return go;
        }

        private FactionRoster Roster(params RosterMember[] members)
        {
            var roster = ScriptableObject.CreateInstance<FactionRoster>();
            roster.members = members;
            junk.Add(roster);
            return roster;
        }

        [Test]
        public void Hash_IsStableForTheSameInputs()
        {
            Assert.AreEqual(RosterDraw.Hash(42, 7), RosterDraw.Hash(42, 7));
        }

        [Test]
        public void Hash_SpreadsAcrossIndices()
        {
            var seen = new HashSet<uint>();
            for (int i = 0; i < 100; i++) seen.Add(RosterDraw.Hash(42, i));
            Assert.Greater(seen.Count, 95);
        }

        [Test]
        public void IndexFor_StaysInRange_AndRefusesEmpty()
        {
            for (int i = 0; i < 500; i++)
            {
                int index = RosterDraw.IndexFor(9, i, 7);
                Assert.That(index, Is.InRange(0, 6));
            }
            Assert.AreEqual(-1, RosterDraw.IndexFor(9, 0, 0));
        }

        [Test]
        public void PickWeighted_HoldsWeightsOverTenThousandDraws()
        {
            var weights = new List<float> { 1f, 3f, 0f };
            int second = 0;
            for (int i = 0; i < 10000; i++)
            {
                int pick = RosterDraw.PickWeighted(weights, RosterDraw.Roll01(1234, i));
                Assert.AreNotEqual(2, pick, "a zero weight must never be drawn");
                if (pick == 1) second++;
            }
            Assert.That(second / 10000f, Is.InRange(0.72f, 0.78f));
        }

        [Test]
        public void PickWeighted_AllZero_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, RosterDraw.PickWeighted(new List<float> { 0f, 0f }, 0.5));
        }

        [Test]
        public void StableHash_IsStable_AndDistinguishesIds()
        {
            Assert.AreEqual(RosterDraw.StableHash("warparty:a:p:1"), RosterDraw.StableHash("warparty:a:p:1"));
            Assert.AreNotEqual(RosterDraw.StableHash("warparty:a:p:1"), RosterDraw.StableHash("warparty:a:p:2"));
        }

        [Test]
        public void Deal_SameSeedAndCount_ReturnsTheSamePrefab()
        {
            GameObject a = Prefab("A"), b = Prefab("B"), c = Prefab("C");
            FactionRoster roster = Roster(
                new RosterMember { role = RosterRole.Warrior, prefab = a, weight = 1f },
                new RosterMember { role = RosterRole.Warrior, prefab = b, weight = 1f },
                new RosterMember { role = RosterRole.Scout, prefab = c, weight = 1f });

            for (int i = 0; i < 20; i++)
                Assert.AreSame(roster.Deal(RosterRole.Warrior, 77, i), roster.Deal(RosterRole.Warrior, 77, i));
        }

        [Test]
        public void Deal_OnlyReturnsTheAskedRole()
        {
            GameObject warrior = Prefab("W"), scout = Prefab("S");
            FactionRoster roster = Roster(
                new RosterMember { role = RosterRole.Warrior, prefab = warrior, weight = 1f },
                new RosterMember { role = RosterRole.Scout, prefab = scout, weight = 1f });

            for (int i = 0; i < 50; i++)
                Assert.AreSame(scout, roster.Deal(RosterRole.Scout, 5, i));
        }

        [Test]
        public void Deal_RoleWithNoMembers_LogsAndReturnsNull()
        {
            FactionRoster roster = Roster(new RosterMember { role = RosterRole.Warrior, prefab = Prefab("W") });

            LogAssert.Expect(LogType.Error, new Regex("no Elder members"));
            Assert.IsNull(roster.Deal(RosterRole.Elder, 1, 0));
        }

        /// The user, 2026-10-05: "like drawing from a card deck", and no monowheel too rare. Every
        /// round of the deck deals each member once, whatever its weight, before any comes again.
        [Test]
        public void Deal_EveryRound_DealsEachMemberOnce_HoweverTheyAreWeighted()
        {
            GameObject[] wheels = { Prefab("Runner"), Prefab("Hauler"), Prefab("Patched"), Prefab("Double"), Prefab("DoubleWide") };
            FactionRoster roster = Roster(
                new RosterMember { role = RosterRole.Rider, prefab = wheels[0], weight = 3f },
                new RosterMember { role = RosterRole.Rider, prefab = wheels[1], weight = 3f },
                new RosterMember { role = RosterRole.Rider, prefab = wheels[2], weight = 3f },
                new RosterMember { role = RosterRole.Rider, prefab = wheels[3], weight = 1f },
                new RosterMember { role = RosterRole.Rider, prefab = wheels[4], weight = 1f },
                new RosterMember { role = RosterRole.Rider, prefab = Prefab("Never"), weight = 0f });

            for (int seed = 0; seed < 200; seed++)
            for (int round = 0; round < 3; round++)
            {
                var dealt = new List<GameObject>();
                for (int i = 0; i < wheels.Length; i++) dealt.Add(roster.Deal(RosterRole.Rider, seed, round * wheels.Length + i));
                CollectionAssert.AreEquivalent(wheels, dealt, $"seed {seed}, round {round}");
            }
        }

        [Test]
        public void Deal_TheRoundsDiffer_AndTheSeedsDiffer()
        {
            GameObject[] people = { Prefab("A"), Prefab("B"), Prefab("C"), Prefab("D") };
            FactionRoster roster = Roster(people.Select(p => new RosterMember { role = RosterRole.Scout, prefab = p, weight = 1f }).ToArray());

            List<GameObject> Round(int seed, int round) =>
                Enumerable.Range(0, people.Length).Select(i => roster.Deal(RosterRole.Scout, seed, round * people.Length + i)).ToList();

            Assert.IsTrue(Enumerable.Range(1, 5).Any(r => !Round(3, 0).SequenceEqual(Round(3, r))), "every round is shuffled anew");
            Assert.IsTrue(Enumerable.Range(1, 5).Any(s => !Round(0, 0).SequenceEqual(Round(s, 0))), "another seed, another deck");
        }

        /// Weights only order a round: the heavier member tends to come first, so a small party sees it more.
        [Test]
        public void Deal_TheFirstCard_FollowsTheWeights()
        {
            GameObject light = Prefab("Light"), heavy = Prefab("Heavy");
            FactionRoster roster = Roster(
                new RosterMember { role = RosterRole.Warrior, prefab = light, weight = 1f },
                new RosterMember { role = RosterRole.Warrior, prefab = heavy, weight = 3f });

            int heavyFirst = Enumerable.Range(0, 10000).Count(seed => roster.Deal(RosterRole.Warrior, seed, 0) == heavy);
            Assert.That(heavyFirst / 10000f, Is.InRange(0.72f, 0.78f));
        }

        [Test]
        public void TierAt_ClampsToTheLastTier()
        {
            FactionRoster roster = Roster();
            roster.warPartyTiers = new[] { new WarPartyTier(), new WarPartyTier() };

            Assert.AreEqual(1, roster.MaxTier);
            Assert.AreSame(roster.warPartyTiers[1], roster.TierAt(5));
            Assert.AreSame(roster.warPartyTiers[0], roster.TierAt(-1));
        }
    }
}
