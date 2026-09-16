// Runtime groups: created, found, released and disbanded; never seeded at startup; restored from a
// save without duplicates; and a war party's lead never goes cold.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;

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
            Assert.AreNotEqual(0, sim.Groups[0].RosterSeed, "a seeded group takes a seed from its id");
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
        public void ReleaseGroup_OfASpawnedGroup_ClearsTheQuarry_AndWaitsForTheFold()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";
            group.Spawned = true;

            sim.ReleaseGroup("w");

            Assert.AreSame(group, sim.FindGroup("w"));
            Assert.IsFalse(group.IsWarParty);
            Assert.IsTrue(group.DisbandWhenFolded);
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
        public void RestoreRecords_FromAnOlderSave_KeepsEveryGroupsOwnSeed()
        {
            Call("Start");

            // Older saves have no rosterSeed: it reads 0, which must not overwrite the seed a group
            // already draws from, or the caravan's faces and guns change once and for good.
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
        public void ReportSighting_DoesNotSteerAWarParty()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";

            sim.ReportSighting(new Vector3(1f, 0f, 1f));

            Assert.IsFalse(group.HasLead);
        }
    }
}
