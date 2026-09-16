// Which prefabs a group spawns: explicit prefabs as authored, roles from the tribe's roster, and a
// war party's people from its tier.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class NpcGroupCompositionTests
    {
        private readonly List<Object> junk = new();
        private GameObject scoutA, scoutB, warrior, explicitPrefab;
        private FactionDefinition tribe;

        [SetUp]
        public void SetUp()
        {
            scoutA = Make("ScoutA");
            scoutB = Make("ScoutB");
            warrior = Make("Warrior");
            explicitPrefab = Make("Explicit");

            tribe = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(tribe);

            var roster = ScriptableObject.CreateInstance<FactionRoster>();
            junk.Add(roster);
            roster.faction = tribe;
            roster.members = new[]
            {
                new RosterMember { role = RosterRole.Scout, prefab = scoutA, weight = 1f },
                new RosterMember { role = RosterRole.Scout, prefab = scoutB, weight = 1f },
                new RosterMember { role = RosterRole.Warrior, prefab = warrior, weight = 1f },
            };
            roster.warPartyTiers = new[]
            {
                new WarPartyTier { roles = new[] { new RoleCount { role = RosterRole.Scout, count = 2 } } },
                new WarPartyTier { roles = new[]
                {
                    new RoleCount { role = RosterRole.Warrior, count = 3 },
                    new RoleCount { role = RosterRole.Scout, count = 1 },
                } },
            };
            tribe.roster = roster;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            junk.Add(go);
            return go;
        }

        [Test]
        public void WarParty_TakesItsTiersComposition_AndTheFirstLeads()
        {
            var template = new NpcGroupTemplate { tribe = tribe };
            var group = new NpcGroup { QuarryProfileId = "p", Tier = 1, RosterSeed = 4 };

            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, template);

            Assert.AreEqual(4, plan.Count);
            Assert.IsTrue(plan[0].Leads);
            Assert.IsTrue(plan.Skip(1).All(p => !p.Leads));
            Assert.AreEqual(3, plan.Take(3).Count(p => p.Prefab == warrior));
            Assert.That(plan[3].Prefab, Is.SameAs(scoutA).Or.SameAs(scoutB));
        }

        [Test]
        public void WarParty_SameSeed_SamePeople()
        {
            var template = new NpcGroupTemplate { tribe = tribe };
            var a = NpcGroupComposition.Resolve(new NpcGroup { QuarryProfileId = "p", RosterSeed = 99 }, template);
            var b = NpcGroupComposition.Resolve(new NpcGroup { QuarryProfileId = "p", RosterSeed = 99 }, template);

            CollectionAssert.AreEqual(a.Select(p => p.Prefab), b.Select(p => p.Prefab));
        }

        [Test]
        public void Caravan_ExplicitPrefabWins_RoleOnlyIsDrawn()
        {
            var template = new NpcGroupTemplate
            {
                tribe = tribe,
                members = new[]
                {
                    new NpcGroupMemberSpec { prefab = explicitPrefab, isLeader = true, count = 1 },
                    new NpcGroupMemberSpec { role = RosterRole.Warrior, count = 2 },
                },
            };

            List<PlannedMember> plan = NpcGroupComposition.Resolve(new NpcGroup(), template);

            Assert.AreEqual(3, plan.Count);
            Assert.AreSame(explicitPrefab, plan[0].Prefab);
            Assert.IsTrue(plan[0].Leads);
            Assert.AreSame(warrior, plan[1].Prefab);
            Assert.AreSame(warrior, plan[2].Prefab);
        }

        [Test]
        public void WarParty_WithoutARoster_LogsAndPlansNothing()
        {
            var template = new NpcGroupTemplate { tribe = null };
            LogAssert.Expect(LogType.Error, new Regex("has no tier"));

            Assert.IsEmpty(NpcGroupComposition.Resolve(new NpcGroup { Id = "w", QuarryProfileId = "p" }, template));
        }
    }
}
