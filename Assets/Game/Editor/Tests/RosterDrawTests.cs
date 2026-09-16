// Rosters: deterministic draws and weights. The shipped Sand roster is RosterAssetTests' job.
using System.Collections.Generic;
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
        public void Draw_SameSeedAndIndex_ReturnsTheSamePrefab()
        {
            GameObject a = Prefab("A"), b = Prefab("B"), c = Prefab("C");
            FactionRoster roster = Roster(
                new RosterMember { role = RosterRole.Warrior, prefab = a, weight = 1f },
                new RosterMember { role = RosterRole.Warrior, prefab = b, weight = 1f },
                new RosterMember { role = RosterRole.Scout, prefab = c, weight = 1f });

            for (int i = 0; i < 20; i++)
                Assert.AreSame(roster.Draw(RosterRole.Warrior, 77, i), roster.Draw(RosterRole.Warrior, 77, i));
        }

        [Test]
        public void Draw_OnlyReturnsTheAskedRole()
        {
            GameObject warrior = Prefab("W"), scout = Prefab("S");
            FactionRoster roster = Roster(
                new RosterMember { role = RosterRole.Warrior, prefab = warrior, weight = 1f },
                new RosterMember { role = RosterRole.Scout, prefab = scout, weight = 1f });

            for (int i = 0; i < 50; i++)
                Assert.AreSame(scout, roster.Draw(RosterRole.Scout, 5, i));
        }

        [Test]
        public void Draw_RoleWithNoMembers_LogsAndReturnsNull()
        {
            FactionRoster roster = Roster(new RosterMember { role = RosterRole.Warrior, prefab = Prefab("W") });

            LogAssert.Expect(LogType.Error, new Regex("no Elder members"));
            Assert.IsNull(roster.Draw(RosterRole.Elder, 1, 0));
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
