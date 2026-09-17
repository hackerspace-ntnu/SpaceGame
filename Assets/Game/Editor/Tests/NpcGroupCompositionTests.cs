// Which prefabs a group spawns: explicit prefabs as authored, roles from the tribe's roster, a war
// party's people from its tier, and the vessel a flying party boards.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
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

        [TestCase(0, "SkySkiffTransport")]
        [TestCase(1, "SkySkiffTransport")]
        [TestCase(2, "SkyFreighterTransport")]
        public void SkyWarParty_BoardsTheSmallVesselWhenItsTierFits_ElseTheLargeOne(int tier, string vessel)
        {
            var sky = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.SkyFactionPath);
            var transport = new NpcGroupTransport
            {
                smallVessel = AssetDatabase.LoadAssetAtPath<GameObject>(SkyVesselBuilder.Skiff.PrefabPath),
                largeVessel = AssetDatabase.LoadAssetAtPath<GameObject>(SkyVesselBuilder.Freighter.PrefabPath),
            };
            Assert.IsNotNull(sky, $"No {RosterAuthoring.SkyFactionPath}.");
            Assert.IsNotNull(transport.smallVessel, "Run Tools/SpaceGame/Vehicles/Build Sky Transports.");
            Assert.IsNotNull(transport.largeVessel, "Run Tools/SpaceGame/Vehicles/Build Sky Transports.");

            var template = new NpcGroupTemplate { tribe = sky, transport = transport };
            var group = new NpcGroup { QuarryProfileId = "p", Tier = tier, RosterSeed = 7 };

            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, template);

            // The sim counts riders with this same predicate before it chooses the vessel.
            Assert.AreEqual(vessel, transport.VesselFor(plan.Count(NpcGroupComposition.Rides)).name);
        }

        [Test]
        public void ARunThatCameHomeWithItsPartyAboard_IsNoDelivery_AndFliesAgainAfterTheDelay()
        {
            Assert.IsFalse(NpcGroupTransport.IsDelivered(vesselExists: true, wrecked: false, aboard: 3),
                "no landing site: the party is still aboard, not dropped off");
            Assert.IsFalse(NpcGroupTransport.ShouldRelaunch(runDone: true, wrecked: false, aboard: 3, parkedFor: 14f, retryDelay: 15f));
            Assert.IsTrue(NpcGroupTransport.ShouldRelaunch(runDone: true, wrecked: false, aboard: 3, parkedFor: 15f, retryDelay: 15f));

            Assert.IsTrue(NpcGroupTransport.IsDelivered(true, false, 0), "everyone off");
            Assert.IsTrue(NpcGroupTransport.IsDelivered(true, true, 2), "shot down: everyone was dropped");
            Assert.IsTrue(NpcGroupTransport.IsDelivered(false, false, 0), "the hull is gone and put its riders down");
            Assert.IsFalse(NpcGroupTransport.ShouldRelaunch(runDone: false, wrecked: false, aboard: 3, parkedFor: 99f, retryDelay: 15f),
                "still flying its run");
            Assert.IsFalse(NpcGroupTransport.ShouldRelaunch(runDone: true, wrecked: false, aboard: 0, parkedFor: 99f, retryDelay: 15f),
                "an empty parked hull has nobody to fly anywhere");
        }

        [Test]
        public void DockSlots_NeverOverlap_AndTheDefaultSpacingClearsTheLargestFootprint()
        {
            const float spacing = 45f;
            var home = new Vector3(3704f, 228f, 1182f);
            var docks = Enumerable.Range(0, 40).Select(i => NpcGroupTransport.DockPoint(home, i, spacing)).ToList();

            Assert.AreEqual(home, docks[0]);
            for (int a = 0; a < docks.Count; a++)
                for (int b = a + 1; b < docks.Count; b++)
                    Assert.GreaterOrEqual(Vector3.Distance(docks[a], docks[b]), spacing - 0.01f, $"slots {a} and {b}");

            Assert.GreaterOrEqual(new NpcGroupTransport().dockSpacing, 2f * SkyVesselBuilder.Freighter.FootprintRadius,
                "two parked freighters must not share ground");
            Assert.AreEqual(2, NpcGroupTransport.FirstFreeDock(new HashSet<int> { 0, 1, 3 }));
        }

        [Test]
        public void Transport_WithOneVessel_UsesItWhateverTheCount_AndWithNoneThePartyWalks()
        {
            GameObject only = Make("Only");

            Assert.IsTrue(new NpcGroupTransport { largeVessel = only }.Flies);
            Assert.AreSame(only, new NpcGroupTransport { largeVessel = only }.VesselFor(99));
            Assert.AreSame(only, new NpcGroupTransport { smallVessel = only }.VesselFor(99));
            Assert.IsFalse(new NpcGroupTransport().Flies);
        }
    }
}
