// The expedition director's save and its answers (settlement expeditions plan, Task 1.2): the state survives the
// save serializer, a band on the road comes back with its own group, a load raises no second band, who is away is
// decided by the record phase, and the war director never adopts a band's group.
//
// ExpeditionDirectorRulesTests is pure (no native Unity object). ExpeditionPersistenceTests drives a real
// NpcWorldSim and ExpeditionDirector and needs the Editor.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.World;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    public class ExpeditionDirectorRulesTests
    {
        private static MemberRecord Member(string key, bool dead = false) => new MemberRecord { residentKey = key, dead = dead };

        private static ExpeditionRecord Band(ExpeditionPhase phase, params MemberRecord[] members) =>
            new ExpeditionRecord { id = "b", settlementId = "s", phase = phase, members = members };

        [Test]
        public void IsAway_TrueOnlyForMembersOfBandsPastTheHandOff()
        {
            // Before the hand-off the members are home, mustering and walking out in view.
            Assert.IsFalse(ExpeditionRules.IsAwayOn(Band(ExpeditionPhase.Announced, Member("r:1")), "r:1"));
            Assert.IsFalse(ExpeditionRules.IsAwayOn(Band(ExpeditionPhase.Departing, Member("r:1")), "r:1"));

            Assert.IsTrue(ExpeditionRules.IsAwayOn(Band(ExpeditionPhase.Out, Member("r:1")), "r:1"));
            Assert.IsTrue(ExpeditionRules.IsAwayOn(Band(ExpeditionPhase.Returning, Member("r:1")), "r:1"), "walking in: still not planned");
            Assert.IsFalse(ExpeditionRules.IsAwayOn(Band(ExpeditionPhase.Out, Member("r:1")), "r:2"), "not a member");

            ExpeditionRecord home = Band(ExpeditionPhase.Home, Member("r:1"), Member("r:2", dead: true));
            Assert.IsFalse(ExpeditionRules.IsAwayOn(home, "r:1"), "home");
            Assert.IsTrue(ExpeditionRules.IsAwayOn(home, "r:2"), "dead: never shown at home before the death is applied");
            Assert.IsTrue(ExpeditionRules.IsAwayOn(Band(ExpeditionPhase.Lost, Member("r:3", dead: true)), "r:3"));
        }

        [Test]
        public void SpokenFor_HoldsUnderwayMembersAndTheDead()
        {
            var bands = new[]
            {
                Band(ExpeditionPhase.Announced, Member("r:1")),
                Band(ExpeditionPhase.Out, Member("r:2")),
                Band(ExpeditionPhase.Home, Member("r:3"), Member("r:4", dead: true)),
            };
            var spoken = new HashSet<string>();

            ExpeditionRules.CollectSpokenFor(bands, spoken);

            CollectionAssert.AreEquivalent(new[] { "r:1", "r:2", "r:4" }, spoken);
        }

        [Test]
        public void LivingMembers_SkipTheDead_InOrder()
        {
            ExpeditionRecord band = Band(ExpeditionPhase.Out, Member("r:1"), Member("r:2", dead: true), Member("r:3"));

            CollectionAssert.AreEqual(new[] { 0, 2 }, ExpeditionRules.LivingMembers(band));
        }

        [Test]
        public void WarriorsHomeMin_IsHalfTheQuotaRoundedUp()
        {
            Assert.AreEqual(6, ExpeditionRules.WarriorsHomeMin(12));
            Assert.AreEqual(4, ExpeditionRules.WarriorsHomeMin(7));
            Assert.AreEqual(0, ExpeditionRules.WarriorsHomeMin(0));
        }

        [Test]
        public void BandSeed_StableForSettlementAndRotation()
        {
            Assert.AreEqual(ExpeditionRules.BandSeed("abc", 3), ExpeditionRules.BandSeed("abc", 3));
            Assert.AreNotEqual(ExpeditionRules.BandSeed("abc", 3), ExpeditionRules.BandSeed("abc", 4));
            Assert.AreNotEqual(ExpeditionRules.BandSeed("abc", 3), ExpeditionRules.BandSeed("abd", 3));
        }

        [Test]
        public void Departure_TodayBeforeTheHour_ElseTomorrow()
        {
            const float hour = 7.5f;
            Assert.AreEqual(2 * 1440d + 450d, ExpeditionRules.DepartureMinute(2, hour));
            Assert.AreEqual(2, ExpeditionRules.NextDepartureDay(2 * 1440d + 60d, hour), "01:00");
            Assert.AreEqual(3, ExpeditionRules.NextDepartureDay(2 * 1440d + 450d, hour), "07:30 has struck");
            Assert.AreEqual(2, ExpeditionRules.DayOf(2 * 1440d + 1439d));
        }

        [Test]
        public void RestUntil_IsTheDayAfterTheRestDays()
        {
            Assert.AreEqual(8, ExpeditionRules.RestUntil(5, 2), "home on day 5, back more than 2 days ago from day 8");
        }

        [Test]
        public void TravelDestination_PrefersASiteInReach_WhateverTheListOrder()
        {
            var near = new WorldSite("a", SiteKind.Ruin, new Vector3(100f, 0f, 0f), 10f, "near");
            var inReach1 = new WorldSite("b", SiteKind.Ruin, new Vector3(1000f, 0f, 0f), 10f, "one");
            var inReach2 = new WorldSite("c", SiteKind.Landmark, new Vector3(0f, 0f, 1200f), 10f, "two");
            var reach = new Vector2(800f, 1600f);

            Vector3 picked = ExpeditionRules.TravelDestination(Vector3.zero, new[] { near, inReach1, inReach2 }, 42, reach);
            Vector3 reordered = ExpeditionRules.TravelDestination(Vector3.zero, new[] { inReach2, near, inReach1 }, 42, reach);

            Assert.That(picked == inReach1.Position || picked == inReach2.Position, "only a site within reach");
            Assert.AreEqual(picked, reordered);
        }

        [Test]
        public void TravelDestination_WithoutSites_IsOnTheReachRing()
        {
            var from = new Vector3(50f, 3f, -20f);
            Vector3 point = ExpeditionRules.TravelDestination(from, Array.Empty<WorldSite>(), 7, new Vector2(800f, 1600f));
            float distance = new Vector2(point.x - from.x, point.z - from.z).magnitude;

            Assert.That(distance, Is.InRange(800f - 0.01f, 1600f + 0.01f));
            Assert.AreEqual(point, ExpeditionRules.TravelDestination(from, Array.Empty<WorldSite>(), 7, new Vector2(800f, 1600f)));
        }
    }

    public class ExpeditionPersistenceTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Settlement = "settlement-1";
        // Day 1, noon: a travel hour.
        private const double Noon = 1440d + 720d;

        private readonly List<Object> junk = new();
        private readonly List<string> absences = new();
        private NpcWorldSim sim;
        private ExpeditionDirector director;
        private FactionDefinition sand;

        [SetUp]
        public void SetUp()
        {
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            sand.ID = sand.factionName = "Sand";
            junk.Add(sand);

            var simGo = new GameObject("Sim");
            junk.Add(simGo);
            sim = simGo.AddComponent<NpcWorldSim>();
            var templates = new[]
            {
                // A tribe on the band's template only so a war director that ignored the owner would find one to adopt it for.
                new NpcGroupTemplate { id = ExpeditionDirector.TemplateId, tribe = sand, runtimeOnly = true },
                new NpcGroupTemplate { id = "sand-war-party", tribe = sand, runtimeOnly = true, bountyHunters = true },
            };
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, templates);
            Invoke(sim, "Awake");

            director = simGo.AddComponent<ExpeditionDirector>();
            typeof(ExpeditionDirector).GetField("catalog", Private).SetValue(director, TestCatalog());
            var tuning = ScriptableObject.CreateInstance<ExpeditionTuning>();
            junk.Add(tuning);
            typeof(ExpeditionDirector).GetField("tuning", Private).SetValue(director, tuning);
            Invoke(director, "Awake");
            Invoke(director, "Seed", (IReadOnlyList<WorldSiteCatalog.SettlementEntry>)new[] { Entry() });

            ExpeditionDirector.AbsenceChanged += absences.Add;
        }

        [TearDown]
        public void TearDown()
        {
            ExpeditionDirector.AbsenceChanged -= absences.Add;
            absences.Clear();
            Invoke(director, "OnDestroy");
            Invoke(sim, "OnDestroy");
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private static object Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        private ExpeditionCatalog TestCatalog()
        {
            var goal = ScriptableObject.CreateInstance<ExpeditionGoal>();
            goal.id = "scout";
            goal.slots = new[]
            {
                new RoleSlot { role = ExpeditionRole.Warrior, min = 2, max = 2 },
                new RoleSlot { role = ExpeditionRole.Scout, min = 1, max = 1 },
            };
            goal.stages = new[] { new StageSpec { kind = StageKind.Travel }, new StageSpec { kind = StageKind.ReturnHome } };

            var profile = ScriptableObject.CreateInstance<ExpeditionProfile>();
            profile.goals = new[] { goal };

            var catalog = ScriptableObject.CreateInstance<ExpeditionCatalog>();
            catalog.profiles = new[] { profile };
            junk.AddRange(new Object[] { goal, profile, catalog });
            return catalog;
        }

        private static WorldSiteCatalog.SettlementEntry Entry() => new WorldSiteCatalog.SettlementEntry
        {
            settlementId = Settlement,
            hasMuster = true,
            musterPosition = Vector3.zero,
            musterForward = Vector3.forward,
            beds = 20,
            cultureProfileIndex = 0,
            roster = Roster(),
        };

        /// <summary>Twelve warriors, two scouts and twenty-six others: room for two bands of three under every bound.</summary>
        private static RosterEntry[] Roster() => Enumerable.Range(0, 40).Select(i => new RosterEntry
        {
            residentKey = ResidentKey.ForAuthored(i),
            roles = i < 12 ? ExpeditionRole.Warrior : i < 14 ? ExpeditionRole.Scout : ExpeditionRole.None,
        }).ToArray();

        private static SettlementState State() => new SettlementState
        {
            settlementId = Settlement,
            roster = Roster(),
            rotation = 3,
            restUntilDay = new Dictionary<string, int> { ["r:4"] = 5 },
            lastGoalId = "scout",
        };

        private static ExpeditionRecord OutBand(int stageIndex, string groupId = "exp:settlement-1:2") => new ExpeditionRecord
        {
            id = "exp:settlement-1:2",
            settlementId = Settlement,
            goalId = "scout",
            seed = 99,
            phase = ExpeditionPhase.Out,
            stages = new[]
            {
                new StageRecord { kind = StageKind.Travel, minutes = StageRecord.NoLimit },
                new StageRecord { kind = StageKind.ReturnHome, minutes = StageRecord.NoLimit },
            },
            stageIndex = stageIndex,
            members = new[]
            {
                new MemberRecord { residentKey = "r:0", isLeader = true, health01 = 0.5f },
                new MemberRecord { residentKey = "r:1", dead = true, health01 = 0f },
                new MemberRecord { residentKey = "r:8" },
            },
            departDay = 1,
            groupId = groupId,
            handoffPoint = new Vector3(0f, 0f, 150f),
            target = new Vector3(900f, 0f, 400f),
        };

        private int ExpeditionGroups() => sim.Groups.Count(g => g.Owner == NpcGroup.OwnerExpedition && !g.DisbandWhenFolded);

        [Test]
        public void State_RoundTripsThroughSaveSerializer()
        {
            var state = new ExpeditionSaveable.State { settlements = new[] { State() }, bands = new[] { OutBand(0) } };

            string json = JObject.FromObject(state, SaveSerializer.Serializer).ToString();
            var back = JObject.Parse(json).ToObject<ExpeditionSaveable.State>(SaveSerializer.Serializer);

            SettlementState settlement = back.settlements.Single();
            Assert.AreEqual(Settlement, settlement.settlementId);
            Assert.AreEqual(3, settlement.rotation);
            Assert.AreEqual(5, settlement.restUntilDay["r:4"]);
            Assert.AreEqual("scout", settlement.lastGoalId);
            Assert.AreEqual(40, settlement.roster.Length);
            Assert.AreEqual(ExpeditionRole.Scout, settlement.roster[12].roles);

            ExpeditionRecord band = back.bands.Single();
            Assert.AreEqual(ExpeditionPhase.Out, band.phase);
            Assert.AreEqual(StageKind.ReturnHome, band.stages[1].kind);
            Assert.AreEqual(0, band.stageIndex);
            Assert.IsTrue(band.members[1].dead);
            Assert.AreEqual(0.5f, band.members[0].health01);
            Assert.IsTrue(band.members[0].isLeader);
            Assert.AreEqual(new Vector3(0f, 0f, 150f), band.handoffPoint);
            Assert.AreEqual(new Vector3(900f, 0f, 400f), band.target);
        }

        [Test]
        public void Restore_OutBand_RecreatesGroupWithOwner()
        {
            director.Restore(new[] { State() }, new[] { OutBand(0) });
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("had no group"));

            Invoke(director, "Step", Noon);

            NpcGroup group = sim.FindGroup("exp:settlement-1:2");
            Assert.IsNotNull(group, "a band on the road always has a group");
            Assert.AreEqual(NpcGroup.OwnerExpedition, group.Owner);
            Assert.AreEqual(2, group.PlannedOverride.Count, "the dead are never spawned again");
            Assert.IsTrue(group.PlannedOverride[0].Leads);
            Assert.IsNotNull(group.MemberStamp);
            Assert.IsNotNull(group.ReadBack);
            Assert.IsTrue(group.HasGoal, "noon: the band walks");
            Assert.AreEqual(new Vector3(900f, 0f, 400f), group.GoalPosition, "the saved target, not a new roll");
        }

        [Test]
        public void Restore_DoesNotDoubleRaise_AfterLoad()
        {
            // Saved heading home, its group saved with it: the next band is due, but not before the load is applied.
            sim.RestoreRecords(new[]
            {
                new NpcGroup { Id = "exp:settlement-1:2", TemplateId = ExpeditionDirector.TemplateId, Owner = NpcGroup.OwnerExpedition }.ToRecord(),
            });
            director.Restore(new[] { State() }, new[] { OutBand(1) });
            Invoke(director, "HoldRotationUntilHydrated", new WorldSaveStore());

            Assert.AreEqual(1, absences.Count(id => id == Settlement), "one absence notice per settlement per load");

            Invoke(director, "Step", Noon);
            Assert.AreEqual(1, director.Bands.Count, "no band raised while the load pass is still running");
            Assert.AreEqual(1, ExpeditionGroups(), "the restored group adopted, not doubled");

            Invoke(director, "OnSceneHydrated", SceneKey.Persistent, default(Scene));
            Invoke(director, "Step", Noon + 1d);
            Invoke(director, "Step", Noon + 2d);

            Assert.AreEqual(2, director.Bands.Count, "the next band, once");
            Assert.AreEqual(ExpeditionPhase.Announced, director.Bands[1].phase);
            Assert.AreEqual("exp:settlement-1:3", director.Bands[1].id, "seeded from the saved rotation");
            Assert.AreEqual(1, ExpeditionGroups());
        }

        [Test]
        public void Restore_ThenLoadCompletesWithoutAPlayerRecord_RotationRuns()
        {
            // A load whose players have no records never raises SaveManager.OnLoadApplied; the persistent scene's
            // hydrate pass ends on every load, and that alone releases the rotation.
            var store = new WorldSaveStore();
            director.Restore(new[] { State() }, Array.Empty<ExpeditionRecord>());
            Invoke(director, "HoldRotationUntilHydrated", store);

            Invoke(director, "OnSceneHydrated", SceneKey.ForChunk(new Vector2Int(6, 3)), default(Scene));
            Invoke(director, "Step", Noon);
            Assert.AreEqual(0, director.Bands.Count, "a chunk's hydrate is not the end of the load pass");

            Invoke(director, "OnSceneHydrated", SceneKey.Persistent, default(Scene));
            Invoke(director, "Step", Noon + 1d);
            Assert.AreEqual(1, director.Bands.Count, "the rotation runs once the persistent scene is hydrated");
        }

        [Test]
        public void Restore_OutsideALoad_DoesNotHoldTheRotation()
        {
            director.Restore(new[] { State() }, Array.Empty<ExpeditionRecord>());

            Invoke(director, "Step", Noon);

            Assert.AreEqual(1, director.Bands.Count, "no load pass to wait for (no SaveManager)");
        }

        [Test]
        public void Restore_ResolvesAMusterAsDeparted_AndAWalkInAsHome()
        {
            ExpeditionRecord departing = OutBand(ExpeditionRecord.NotStarted, groupId: string.Empty);
            departing.phase = ExpeditionPhase.Departing;
            ExpeditionRecord returning = OutBand(2);
            returning.id = "exp:settlement-1:1";
            returning.phase = ExpeditionPhase.Returning;

            director.Restore(new[] { State() }, new[] { departing, returning });

            Assert.AreEqual(ExpeditionPhase.Out, departing.phase);
            Assert.AreEqual(ExpeditionPhase.Home, returning.phase);
            Assert.IsTrue(director.Settlements[0].restUntilDay.ContainsKey("r:0"), "the living rest");
            Assert.IsTrue(ExpeditionDirector.IsAway(Settlement, "r:8"), "the departed band's members are away");
        }

        [Test]
        public void IsAway_TrueOnlyForMembersOfBandsPastTheHandOff()
        {
            ExpeditionRecord announced = OutBand(ExpeditionRecord.NotStarted);
            announced.id = "a";
            announced.phase = ExpeditionPhase.Announced;
            announced.members = new[] { new MemberRecord { residentKey = "r:5" } };
            director.Restore(new[] { State() }, new[] { announced, OutBand(0) });

            Assert.IsFalse(ExpeditionDirector.IsAway(Settlement, "r:5"), "announced: still home");
            Assert.IsTrue(ExpeditionDirector.IsAway(Settlement, "r:0"));
            Assert.IsTrue(ExpeditionDirector.IsAway(Settlement, "r:8"));
            Assert.IsFalse(ExpeditionDirector.IsAway(Settlement, "r:9"), "not on a band");
            Assert.IsFalse(ExpeditionDirector.IsAway("another-settlement", "r:0"), "keys are per settlement");
        }

        [Test]
        public void WarDirector_DoesNotAdoptExpeditionGroups()
        {
            var war = sim.gameObject.AddComponent<WarPartyDirector>();
            Invoke(war, "Awake");
            try
            {
                AssertNotAdopted(war);
            }
            finally
            {
                Invoke(war, "OnDestroy");
            }
        }

        private void AssertNotAdopted(WarPartyDirector war)
        {
            // Even carrying a quarry, a group another director owns is not a war party (spec §4.4).
            var band = new NpcGroup
            {
                Id = "exp:settlement-1:2", TemplateId = ExpeditionDirector.TemplateId,
                Owner = NpcGroup.OwnerExpedition, QuarryProfileId = "profile-a",
            };
            sim.RestoreRecords(new[] { band.ToRecord() });
            Assert.IsNotNull(sim.FindGroup(band.Id), "the director claimed its owner, so the record survives the restore");

            Invoke(war, "AdoptRestoredParties");

            Assert.AreEqual(0, war.Book.Wars.Count);
        }
    }
}
