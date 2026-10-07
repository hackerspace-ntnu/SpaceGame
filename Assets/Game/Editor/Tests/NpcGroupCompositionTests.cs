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

        [Test]
        public void CrewSpecs_ArePlannedAsCrew_OthersAreNot()
        {
            var house = new GameObject("House");
            var person = new GameObject("Person");
            try
            {
                var template = new NpcGroupTemplate
                {
                    id = "city",
                    members = new[]
                    {
                        new NpcGroupMemberSpec { prefab = house, isLeader = true },
                        new NpcGroupMemberSpec { prefab = person, crew = true, count = 2 },
                    },
                };
                var plan = NpcGroupComposition.Resolve(new NpcGroup { Id = "city" }, template);
                Assert.AreEqual(3, plan.Count);
                Assert.IsFalse(plan[0].Crew);
                Assert.IsTrue(plan[1].Crew && plan[2].Crew);
                Assert.AreEqual(1, plan.Count(NpcGroupComposition.Rides),
                    "crew ride their carriers: a crewed flying template must not size its vessel by them");
            }
            finally { Object.DestroyImmediate(house); Object.DestroyImmediate(person); }
        }

        /// <summary>A walking-city-like template: a leader, shuffled vehicles of three kinds, then crew.</summary>
        private NpcGroupTemplate ShuffledTemplate(out GameObject[] vehicles)
        {
            GameObject house = Make("House"), crawler = Make("Crawler"), wheel = Make("Wheel"), person = Make("Person");
            vehicles = new[] { house, crawler, wheel };
            ColumnCard Shuffled(string kind) => new ColumnCard { shuffled = true, kind = kind, footprint = new Rect(-2f, -2f, 4f, 4f) };
            return new NpcGroupTemplate
            {
                id = "city",
                tribe = tribe,
                formation = new FormationShape { Lanes = 2, RowSpacing = 35f, LaneSpacing = 30f },
                members = new[]
                {
                    new NpcGroupMemberSpec { prefab = house, isLeader = true },
                    new NpcGroupMemberSpec { prefab = house, count = 2, column = Shuffled("house") },
                    new NpcGroupMemberSpec { prefab = crawler, count = 2, column = Shuffled("crawler") },
                    new NpcGroupMemberSpec { prefab = wheel, count = 4, column = Shuffled(null) },
                    new NpcGroupMemberSpec { prefab = person, crew = true, count = 3 },
                },
            };
        }

        /// The user, 2026-10-05: the city's vehicles must not march sorted by type, but "like drawing
        /// from a card deck". The shuffled members are dealt into their own places; the leader stays
        /// first and the crew after them.
        [Test]
        public void ShuffledMembers_AreDealtFromTheGroupsSeed_LeaderFirst_CrewLast()
        {
            NpcGroupTemplate template = ShuffledTemplate(out GameObject[] vehicles);
            var columns = new HashSet<string>();
            for (int seed = 1; seed <= 60; seed++)
            {
                List<PlannedMember> plan = NpcGroupComposition.Resolve(new NpcGroup { Id = "city", RosterSeed = seed }, template);
                Assert.AreEqual(12, plan.Count);
                Assert.IsTrue(plan[0].Leads && plan[0].Prefab == vehicles[0]);
                Assert.IsTrue(plan.Skip(9).All(p => p.Crew), "crew after every carrier");
                List<GameObject> column = plan.Skip(1).Take(8).Select(p => p.Prefab).ToList();
                Assert.AreEqual(2, column.Count(p => p == vehicles[0]));
                Assert.AreEqual(2, column.Count(p => p == vehicles[1]));
                Assert.AreEqual(4, column.Count(p => p == vehicles[2]));
                Assert.IsNull(ColumnDeal.FirstBroken(template.members[0].column,
                    plan.Skip(1).Take(8).Select(p => template.members.First(m => m.prefab == p.Prefab && !m.isLeader).column).ToList(),
                    Enumerable.Range(0, 8).ToList(), template.formation));
                columns.Add(string.Join(",", column.Select(p => p.name)));
            }

            Assert.Greater(columns.Count, 10, "every world deals its own column");
        }

        /// The order is a pure function of the saved roster seed: a refold, or a reload from the
        /// group's record, brings back the column the player saw.
        [Test]
        public void ShuffledMembers_ARestoredGroup_DealsTheSameColumn()
        {
            NpcGroupTemplate template = ShuffledTemplate(out _);
            var live = new NpcGroup { Id = "city", RosterSeed = 918273 };
            var restored = new NpcGroup { Id = "city" };
            restored.ApplyRecord(live.ToRecord());

            CollectionAssert.AreEqual(NpcGroupComposition.Resolve(live, template).Select(p => p.Prefab),
                                      NpcGroupComposition.Resolve(restored, template).Select(p => p.Prefab));
        }

        /// The roster's draws for a role are dealt from a deck, so a war party of five Riders from a
        /// roster of five monowheels rides every one of them (the user, 2026-10-05: none too rare).
        [Test]
        public void WarParty_DealsItsRiders_EveryKindBeforeAnyTwice()
        {
            GameObject[] wheels = { Make("W1"), Make("W2"), Make("W3"), Make("W4"), Make("W5") };
            tribe.roster.members = wheels.Select((w, i) => new RosterMember { role = RosterRole.Rider, prefab = w, weight = i < 3 ? 3f : 1f })
                .ToArray();
            tribe.roster.warPartyTiers = new[] { new WarPartyTier { roles = new[] { new RoleCount { role = RosterRole.Rider, count = 5 } } } };
            var template = new NpcGroupTemplate { tribe = tribe };

            for (int seed = 0; seed < 50; seed++)
                CollectionAssert.AreEquivalent(wheels,
                    NpcGroupComposition.Resolve(new NpcGroup { QuarryProfileId = "p", RosterSeed = seed }, template).Select(p => p.Prefab));
        }

        private static readonly WeightedCount[] ElderCounts =
        {
            new WeightedCount { count = 1, weight = 0.65f },
            new WeightedCount { count = 2, weight = 0.25f },
            new WeightedCount { count = 3, weight = 0.10f },
        };

        [Test]
        public void NoCountWeights_UsesCount()
        {
            var spec = new NpcGroupMemberSpec { count = 4 };
            for (int seed = 0; seed < 50; seed++) Assert.AreEqual(4, spec.DrawCount(seed, 7));
        }

        [Test]
        public void CountWeights_StayInRange_AndAreDeterministic()
        {
            var spec = new NpcGroupMemberSpec { count = 1, countWeights = ElderCounts };
            for (int seed = 0; seed < 500; seed++)
            {
                int n = spec.DrawCount(seed, 30);
                Assert.That(n, Is.InRange(1, 3));
                Assert.AreEqual(n, spec.DrawCount(seed, 30), "a reloaded city must draw the same elders");
            }
        }

        [Test]
        public void CountWeights_MatchTheirWeights_OverManySeeds()
        {
            var spec = new NpcGroupMemberSpec { count = 1, countWeights = ElderCounts };
            var tally = new int[4];
            const int seeds = 10000;
            for (int seed = 0; seed < seeds; seed++) tally[spec.DrawCount(RosterDraw.StableHash("city" + seed), 30)]++;
            Assert.AreEqual(0.65, tally[1] / (double)seeds, 0.02);
            Assert.AreEqual(0.25, tally[2] / (double)seeds, 0.02);
            Assert.AreEqual(0.10, tally[3] / (double)seeds, 0.02);
        }

        [Test]
        public void Resolve_PlansTheDrawnCount()
        {
            var elder = new GameObject("Elder");
            try
            {
                var template = new NpcGroupTemplate
                {
                    id = "city",
                    members = new[] { new NpcGroupMemberSpec { prefab = elder, crew = true, count = 1, countWeights = ElderCounts } },
                };
                for (int seed = 0; seed < 40; seed++)
                {
                    var group = new NpcGroup { Id = "city", RosterSeed = seed };
                    Assert.AreEqual(template.members[0].DrawCount(seed, 0), NpcGroupComposition.Resolve(group, template).Count);
                }
            }
            finally { Object.DestroyImmediate(elder); }
        }
    }
}
