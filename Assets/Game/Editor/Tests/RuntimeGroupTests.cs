// Runtime groups: created, found, released and disbanded; never seeded at startup; restored from a
// save without duplicates; a war party's lead never goes cold; and a flying party travels at its
// vessel's speed and remembers whether it was dropped off.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class RuntimeGroupTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<Object> junk = new();
        private NpcWorldSim sim;
        private NpcGroupTemplate caravan, warParty;
        private FactionDefinition sand;

        [SetUp]
        public void SetUp()
        {
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(sand);

            caravan = new NpcGroupTemplate { id = "caravan", tribe = sand, useStartPosition = true };
            warParty = new NpcGroupTemplate { id = "sand-war-party", tribe = sand, runtimeOnly = true, bountyHunters = true };

            var go = new GameObject("Sim");
            junk.Add(go);
            sim = go.AddComponent<NpcWorldSim>();
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { caravan, warParty });
            Call("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private object Call(string method, params object[] args) =>
            typeof(NpcWorldSim).GetMethod(method, Private).Invoke(sim, args);

        [Test]
        public void Seeding_SkipsRuntimeOnlyTemplates()
        {
            Call("Start");

            CollectionAssert.AreEqual(new[] { "caravan" }, sim.Groups.Select(g => g.Id));
            Assert.AreNotEqual(0, sim.Groups[0].RosterSeed, "0 is what an older save reads for no seed");
        }

        /// A seeded group's seed is the new world's own (the Strider city's column, its elders, a
        /// caravan's faces), not its id's: two worlds seeded from the same templates differ.
        [Test]
        public void Seeding_EachNewWorld_DrawsItsOwnSeeds()
        {
            var seeds = new HashSet<int>();
            for (int world = 0; world < 8; world++)
            {
                ((List<NpcGroup>)typeof(NpcWorldSim).GetField("groups", Private).GetValue(sim)).Clear();
                Call("Start");
                seeds.Add(sim.FindGroup("caravan").RosterSeed);
            }

            Assert.Greater(seeds.Count, 1);
        }

        [Test]
        public void CreateGroup_AddsAFindableGroup_AndRefusesADuplicateId()
        {
            NpcGroup group = sim.CreateGroup(warParty, "warparty:sand:p:1", new Vector3(10f, 0f, 20f));

            Assert.AreSame(group, sim.FindGroup("warparty:sand:p:1"));
            Assert.AreEqual("sand-war-party", group.TemplateId);
            Assert.AreEqual(RosterDraw.StableHash("warparty:sand:p:1"), group.RosterSeed);

            LogAssert.Expect(LogType.Error, new Regex("already exists"));
            Assert.IsNull(sim.CreateGroup(warParty, "warparty:sand:p:1", Vector3.zero));
        }

        [Test]
        public void ReleaseGroup_OfAFoldedGroup_RemovesItAtOnce()
        {
            sim.CreateGroup(warParty, "w", Vector3.zero);
            sim.ReleaseGroup("w");

            Assert.IsNull(sim.FindGroup("w"));
        }

        [Test]
        public void ReleaseGroup_OfASpawnedGroup_EndsTheHunt_AndWaitsForTheFold()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";
            group.Spawned = true;

            sim.ReleaseGroup("w");

            Assert.AreSame(group, sim.FindGroup("w"));
            Assert.IsFalse(group.IsWarParty);
            Assert.IsFalse(group.IsOwnedByWar, "a released party must never be adopted back into a war");
            Assert.IsTrue(group.DisbandWhenFolded);
        }

        [Test]
        public void ReleaseGroup_OfASpawnedGroup_KeepsWhomItHunted_SoItsSurvivorsStaySelfDefence()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";
            group.Spawned = true;

            sim.ReleaseGroup("w");

            Assert.IsTrue(group.Released);
            Assert.AreEqual("p", group.QuarryProfileId,
                            "its bodies are still standing and still hostile: hitting them must stay free for the quarry");
        }

        [Test]
        public void AReleasedParty_IsSavedAsHuntingNobody_SoALoadDropsIt()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";
            group.Spawned = true;
            sim.ReleaseGroup("w");

            NpcGroup.Record record = group.ToRecord();

            Assert.IsTrue(string.IsNullOrEmpty(record.quarryProfileId));
            Assert.IsFalse(NpcWorldSim.KeepsRuntimeRecord(in record, new HashSet<string>()));
        }

        [Test]
        public void WarPartyTemplateFor_FindsTheTribesRuntimeHunterTemplate()
        {
            Assert.AreSame(warParty, sim.WarPartyTemplateFor(sand));
            Assert.AreSame(sand, sim.TribeOf(sim.CreateGroup(warParty, "w", Vector3.zero)));
        }

        [Test]
        public void RestoreRecords_DropsRuntimeGroupsTheSaveDoesNotHave_AndSkipsReleasedRecords()
        {
            Call("Start");
            sim.CreateGroup(warParty, "stale", Vector3.zero).QuarryProfileId = "p";

            var hunting = new NpcGroup { Id = "warparty:sand:p:3", TemplateId = "sand-war-party", QuarryProfileId = "p", Tier = 1 };
            var released = new NpcGroup { Id = "warparty:sand:p:2", TemplateId = "sand-war-party" };

            sim.RestoreRecords(new[] { sim.Groups[0].ToRecord(), hunting.ToRecord(), released.ToRecord() });

            CollectionAssert.AreEquivalent(new[] { "caravan", "warparty:sand:p:3" }, sim.Groups.Select(g => g.Id));
            Assert.AreEqual(1, sim.FindGroup("warparty:sand:p:3").Tier);
        }

        [Test]
        public void RestoreRecords_RebuildsARuntimeGroupFresh_EvenWhenItsIdWasAlreadyLive()
        {
            // Same-session reload: ids like this repeat, and the live instance under "w" carries this
            // session's stale runtime-only flags. Restoring it in place would freeze it forever
            // (WipedOut) or delete it next tick (DisbandWhenFolded) instead of resuming the hunt.
            NpcGroup stale = sim.CreateGroup(warParty, "w", Vector3.zero);
            stale.QuarryProfileId = "p";
            stale.WipedOut = true;
            stale.DisbandWhenFolded = true;

            // A second live group whose save record turns out to be a released mid-fold record: it
            // must not be restored just because a live group already held that id.
            sim.CreateGroup(warParty, "r", Vector3.zero).QuarryProfileId = "p";

            var hunting = new NpcGroup { Id = "w", TemplateId = "sand-war-party", QuarryProfileId = "p", Tier = 1 };
            var released = new NpcGroup { Id = "r", TemplateId = "sand-war-party" };

            sim.RestoreRecords(new[] { hunting.ToRecord(), released.ToRecord() });

            NpcGroup restored = sim.FindGroup("w");
            Assert.IsNotNull(restored);
            Assert.IsFalse(restored.WipedOut);
            Assert.IsFalse(restored.DisbandWhenFolded);
            Assert.AreEqual(1, restored.Tier);
            Assert.IsTrue(restored.IsWarParty);

            Assert.IsNull(sim.FindGroup("r"),
                "a record for a runtime id with an empty quarry must not be restored even when a " +
                "live group already held that id");
        }

        [Test]
        public void RestoreRecords_FromAnOlderSave_DrawsFromEachGroupsId()
        {
            Call("Start");

            // Older saves have no rosterSeed: it reads 0. Those worlds drew every group from its id's
            // hash, so that is the seed it keeps — not this session's new-world seed, or the caravan's
            // faces and guns would change once and for good.
            var oldCaravan = new NpcGroup { Id = "caravan", TemplateId = "caravan" };
            var oldParty = new NpcGroup { Id = "warparty:sand:p:3", TemplateId = "sand-war-party", QuarryProfileId = "p" };

            sim.RestoreRecords(new[] { oldCaravan.ToRecord(), oldParty.ToRecord() });

            Assert.AreEqual(RosterDraw.StableHash("caravan"), sim.FindGroup("caravan").RosterSeed);
            Assert.AreEqual(RosterDraw.StableHash("warparty:sand:p:3"), sim.FindGroup("warparty:sand:p:3").RosterSeed);
        }

        [Test]
        public void RestoreRecords_ASavedSeed_StillWins()
        {
            Call("Start");

            var saved = new NpcGroup { Id = "caravan", TemplateId = "caravan", RosterSeed = 42 };
            sim.RestoreRecords(new[] { saved.ToRecord() });

            Assert.AreEqual(42, sim.FindGroup("caravan").RosterSeed);
        }

        [Test]
        public void AFoldedCity_CallsItsCrewBackAboard_WhenItsStopEnds()
        {
            Call("Start");
            NpcGroup group = sim.FindGroup("caravan");
            group.CrewAshore = true;
            group.DwellRemaining = 5f;

            Call("TickGroup", group, 1f);
            Assert.IsTrue(group.CrewAshore, "still at its stop: the crew stay ashore");

            for (int i = 0; i < 5; i++) Call("TickGroup", group, 1f);
            Assert.IsFalse(group.CrewAshore, "a record that left its stop must not unfold mid-march with its crew on foot");
        }

        [Test]
        public void ACityFoldedMidStop_KeepsItsCrewAshore_ForTheRestOfTheLeadersStay()
        {
            Call("Start");
            NpcGroup group = sim.FindGroup("caravan");
            group.DwellRemaining = 0f;   // stale: only a virtual arrival ever sets it

            // A live leader still working its stop, and a house with crew ashore.
            var house = new GameObject("House"); junk.Add(house);
            var seat = new GameObject("Seat_0").transform; seat.SetParent(house.transform);
            var seats = house.AddComponent<VesselSeats>();
            var so = new UnityEditor.SerializedObject(seats);
            so.FindProperty("seats").arraySize = 1;
            so.FindProperty("seats").GetArrayElementAtIndex(0).objectReferenceValue = seat;
            so.ApplyModifiedPropertiesWithoutUndo();
            house.AddComponent<FormationModule>().SetFormation("fold-mid-stop", true);
            var tasks = house.AddComponent<NpcTaskModule>();
            tasks.ResumeTask(new[] { new NpcTask { targetSite = SiteKind.Ruin } }, -1, travelling: false, dwellRemaining: 0f, siteId: null);
            tasks.RestoreTaskState(NpcTaskModule.Phase.Dwelling, 0, "site", "site-1", 30f, 0f, -1, true, Vector3.zero);
            var person = new GameObject("Crew"); junk.Add(person);
            house.AddComponent<CrewShift>().Take(person, aboard: false);
            group.Live.Add(house);

            typeof(NpcWorldSim).GetMethod("ReadBackCrew", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { group });
            group.Live.Clear();

            Assert.AreEqual(30f, group.DwellRemaining, 0.001f, "the leader's stay is carried into the record");
            Call("TickGroup", group, 1f);
            Assert.IsTrue(group.CrewAshore, "folded mid-stop, the crew stay ashore until the stay runs out");
        }

        [Test]
        public void Seeding_StartsAGroupAtItsStop_ForItsInitialStay()
        {
            caravan.initialStaySeconds = 600f;
            Call("Start");
            NpcGroup group = sim.FindGroup("caravan");

            Assert.AreEqual(600f, group.DwellRemaining, 0.001f);
            Call("TickGroup", group, 1f);
            Assert.IsFalse(group.HasGoal, "still at its first stop: it has not chosen where to go next");
        }

        [Test]
        public void AGroupSpawnedMidStay_HandsTheRestOfTheStayToItsLeader()
        {
            Call("Start");
            NpcGroup group = sim.FindGroup("caravan");
            group.DwellRemaining = 40f;

            var leader = new GameObject("Leader"); junk.Add(leader);
            leader.AddComponent<FormationModule>();
            var tasks = leader.AddComponent<NpcTaskModule>();
            Call("Configure", leader, group, caravan, true);

            Assert.IsTrue(tasks.AtStop, "spawned in the middle of a stop, the leader must not set off at once");
            Assert.AreEqual(40f, tasks.PhaseTimer, 0.001f);
            Assert.AreEqual(0f, group.DwellRemaining, "the leader owns the stay now; ReadBackCrew writes back what is left");
        }

        [Test]
        public void SteerSpawned_PointsTheRecordAtTheNewGoal()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.Spawned = true;

            sim.SteerSpawned(group, new Vector3(300f, 0f, 40f), 20f);

            Assert.IsTrue(group.HasGoal);
            Assert.AreEqual(new Vector3(300f, 0f, 40f), group.GoalPosition);
            Assert.AreEqual(20f, group.ArriveRadius);
        }

        [Test]
        public void SteerSpawned_SteersTheMemberTheColumnFollows_WhenTheFlaggedLeaderIsParked()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.Spawned = true;
            GameObject flagged = ColumnMember(group, "LeadWheel", leader: true);
            GameObject next = ColumnMember(group, "NextWheel", leader: false);
            flagged.GetComponent<FormationModule>().enabled = false;   // as MonowheelDriverGate parks a driverless wheel

            sim.SteerSpawned(group, new Vector3(300f, 0f, 40f), 20f);

            Assert.IsTrue(next.GetComponent<AgentGoal>().HasGoal, "the wheel that took the lead is sent after the quarry");
            Assert.IsFalse(flagged.GetComponent<AgentGoal>().HasGoal, "the parked wheel cannot drive, so steering it stops the party");
        }

        [Test]
        public void AHandedOnLead_PicksUpTheGroupsGoal_OnTheNextTick()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            GameObject flagged = ColumnMember(group, "LeadWheel", leader: true);
            GameObject next = ColumnMember(group, "NextWheel", leader: false);
            group.GoalPosition = new Vector3(0f, 0f, 90f);
            group.ArriveRadius = 5f;
            group.HasGoal = true;
            flagged.GetComponent<FormationModule>().enabled = false;

            Call("HandGoalToSteeringLeader", group);

            Assert.IsTrue(next.GetComponent<AgentGoal>().HasGoal, "without it the party waits for the director's next out-of-sight trail");
        }

        [Test]
        public void AFlaggedLeader_IsNeverHandedTheGroupsGoal()
        {
            NpcGroup group = sim.CreateGroup(caravan, "c", Vector3.zero);
            GameObject flagged = ColumnMember(group, "Leader", leader: true);
            group.GoalPosition = new Vector3(0f, 0f, 90f);
            group.HasGoal = true;

            Call("HandGoalToSteeringLeader", group);

            Assert.IsFalse(flagged.GetComponent<AgentGoal>().HasGoal, "a caravan's task list clears its goal at every stop");
        }

        private GameObject ColumnMember(NpcGroup group, string name, bool leader)
        {
            var go = new GameObject(name);
            junk.Add(go);
            go.AddComponent<FormationModule>().SetFormation(group.Id, leader);
            go.AddComponent<AgentGoal>();
            group.Live.Add(go);
            return go;
        }

        [Test]
        public void WarParty_LeadNeverGoesCold_WhileFolded()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";
            group.Lead = new Vector3(5000f, 0f, 0f);
            group.HasLead = true;

            for (int i = 0; i < 400; i++) Call("TickGroup", group, 1f);

            Assert.IsTrue(group.HasLead);
            Assert.Greater(group.LeadAge, 300f);
            Assert.Greater(group.Position.x, 0f, "it walked toward the lead");
        }

        [Test]
        public void AFoldedFlyingParty_TravelsAtTheTransportSpeed_UntilDelivered()
        {
            var vessel = new GameObject("Vessel");
            junk.Add(vessel);
            warParty.travelSpeed = 3f;
            warParty.transport = new NpcGroupTransport { smallVessel = vessel, travelSpeed = 28f };

            NpcGroup flying = sim.CreateGroup(warParty, "flying", Vector3.zero);
            NpcGroup walking = sim.CreateGroup(warParty, "walking", Vector3.zero);
            foreach (NpcGroup group in new[] { flying, walking })
            {
                group.QuarryProfileId = "p";
                group.Lead = new Vector3(5000f, 0f, 0f);
                group.HasLead = true;
            }
            walking.Delivered = true;

            Call("TickGroup", flying, 1f);
            Call("TickGroup", walking, 1f);

            Assert.AreEqual(28f, flying.Position.x, 0.01f);
            Assert.AreEqual(3f, walking.Position.x, 0.01f, "dropped off, the party walks like any other");
        }

        [Test]
        public void ReleaseGroup_OfAFoldedGroupWhoseVesselIsStillOut_WaitsForTheVessel()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";
            group.Delivered = true;
            group.Transport = new GameObject("Vessel");
            junk.Add(group.Transport);

            sim.ReleaseGroup("w");

            Assert.AreSame(group, sim.FindGroup("w"), "removing it now would leave the vessel nobody's");
            Assert.IsTrue(group.DisbandWhenFolded);
            Assert.IsFalse(group.IsWarParty);
        }

        [Test]
        public void RestoreRecords_KeepsWhetherAPartyWasDelivered()
        {
            var inTheAir = new NpcGroup { Id = "warparty:sky:p:1", TemplateId = "sand-war-party", QuarryProfileId = "p" };
            var onFoot = new NpcGroup { Id = "warparty:sky:p:2", TemplateId = "sand-war-party", QuarryProfileId = "q", Delivered = true };

            sim.RestoreRecords(new[] { inTheAir.ToRecord(), onFoot.ToRecord() });

            Assert.IsFalse(sim.FindGroup("warparty:sky:p:1").Delivered);
            Assert.IsTrue(sim.FindGroup("warparty:sky:p:2").Delivered);
        }

        [Test]
        public void ReportSighting_DoesNotSteerAWarParty()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";

            sim.ReportSighting(new Vector3(1f, 0f, 1f));

            Assert.IsFalse(group.HasLead);
        }

        [Test]
        public void DespawnOrder_CrewBeforeTheirCarriers()
        {
            var group = new NpcGroup { Id = "city" };
            var carrier = new GameObject("Carrier"); var crewman = new GameObject("Crew");
            junk.Add(carrier); junk.Add(crewman);
            group.Live.Add(carrier);
            group.Live.Add(crewman);

            var members = (List<GameObject>)typeof(NpcWorldSim)
                .GetMethod("DespawnOrder", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { group });

            CollectionAssert.AreEqual(new[] { crewman, carrier }, members,
                "a carrier despawned first would strand its seated crew at the scene root");
        }

        [Test]
        public void ReadBackCrew_MarksTheGroupAshore_WhenAnyHouseIs()
        {
            var group = new NpcGroup { Id = "city" };
            var house = new GameObject("House"); junk.Add(house);
            var seat = new GameObject("Seat_0").transform; seat.SetParent(house.transform);
            var seats = house.AddComponent<VesselSeats>();
            var so = new UnityEditor.SerializedObject(seats);
            so.FindProperty("seats").arraySize = 1;
            so.FindProperty("seats").GetArrayElementAtIndex(0).objectReferenceValue = seat;
            so.ApplyModifiedPropertiesWithoutUndo();
            var shift = house.AddComponent<CrewShift>();
            var person = new GameObject("Crew"); junk.Add(person);
            shift.Take(person, aboard: false);
            group.Live.Add(house);

            typeof(NpcWorldSim).GetMethod("ReadBackCrew", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { group });

            Assert.IsTrue(group.CrewAshore);
        }
    }
}
