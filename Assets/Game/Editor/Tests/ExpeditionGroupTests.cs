// The world sim's hooks for groups another director owns (settlement expeditions spec §4.3–§4.4): the
// owner survives a save, a claimed owner's runtime records survive a restore, a planned member list
// replaces the template's, and the war director never mistakes such a group for one of its parties.
//
// ExpeditionGroupTests is pure (no native Unity object); ExpeditionGroupSimTests drives a real
// NpcWorldSim and needs the Editor.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class ExpeditionGroupTests
    {
        private static readonly HashSet<string> Claimed = new() { NpcGroup.OwnerExpedition };

        [Test]
        public void Record_OwnerRoundTrips()
        {
            var group = new NpcGroup { Id = "exp:1", TemplateId = "settlement-expedition", Owner = NpcGroup.OwnerExpedition };

            JObject json = JObject.FromObject(group.ToRecord(), SaveSerializer.Serializer);
            NpcGroup.Record back = json.ToObject<NpcGroup.Record>(SaveSerializer.Serializer);

            var restored = new NpcGroup { Id = back.id, TemplateId = back.templateId };
            restored.ApplyRecord(in back);

            Assert.AreEqual(NpcGroup.OwnerExpedition, restored.Owner);
        }

        [Test]
        public void Record_OldSaveWithoutOwner_ReadsEmpty()
        {
            var old = JObject.Parse("{\"id\":\"warparty:sand:p:1\",\"templateId\":\"sand-war-party\",\"quarryProfileId\":\"p\"}");
            NpcGroup.Record record = old.ToObject<NpcGroup.Record>(SaveSerializer.Serializer);

            var group = new NpcGroup { Id = record.id, TemplateId = record.templateId };
            group.ApplyRecord(in record);

            Assert.AreEqual(string.Empty, group.Owner);
            Assert.IsTrue(group.IsOwnedByWar, "an older save's war party is still the war director's");
        }

        [Test]
        public void ApplyRecord_CarriesTheOwner_AndAMissingOneReadsEmpty()
        {
            var group = new NpcGroup { Owner = NpcGroup.OwnerExpedition };
            var restored = new NpcGroup();

            restored.ApplyRecord(group.ToRecord());
            Assert.AreEqual(NpcGroup.OwnerExpedition, restored.Owner);

            restored.ApplyRecord(new NpcGroup.Record { id = "warparty:sand:p:1" });
            Assert.AreEqual(string.Empty, restored.Owner);
        }

        [Test]
        public void Restore_RuntimeRecordWithClaimedOwner_IsKept()
        {
            var band = new NpcGroup.Record { id = "exp:1", templateId = "settlement-expedition", owner = NpcGroup.OwnerExpedition };

            Assert.IsTrue(NpcWorldSim.KeepsRuntimeRecord(in band, Claimed));
        }

        [Test]
        public void Restore_RuntimeRecordWithoutQuarryOrOwner_IsStillSkipped()
        {
            var released = new NpcGroup.Record { id = "warparty:sand:p:2", templateId = "sand-war-party" };
            var unclaimed = new NpcGroup.Record { id = "x:1", templateId = "x", owner = "nobody-claimed-this" };
            var releasedWarOwned = new NpcGroup.Record { id = "warparty:sand:p:3", templateId = "sand-war-party", owner = NpcGroup.OwnerWar };
            var hunting = new NpcGroup.Record { id = "warparty:sand:p:4", templateId = "sand-war-party", quarryProfileId = "p" };

            Assert.IsFalse(NpcWorldSim.KeepsRuntimeRecord(in released, Claimed), "a released party saved mid-fold was leaving");
            Assert.IsFalse(NpcWorldSim.KeepsRuntimeRecord(in unclaimed, Claimed), "no director keeps it, so the quarry rule decides");
            Assert.IsFalse(NpcWorldSim.KeepsRuntimeRecord(in releasedWarOwned, Claimed), "the war director claims nothing");
            Assert.IsTrue(NpcWorldSim.KeepsRuntimeRecord(in hunting, Claimed));
        }

        [Test]
        public void Composition_PlannedOverride_WinsOverTemplate()
        {
            var template = new NpcGroupTemplate
            {
                id = "settlement-expedition",
                members = new[] { new NpcGroupMemberSpec { count = 5, isLeader = true } },
            };
            var planned = new List<PlannedMember> { new(null, false), new(null, true) };
            var group = new NpcGroup { Id = "exp:1", QuarryProfileId = "p", PlannedOverride = planned };

            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, template);

            CollectionAssert.AreEqual(new[] { false, true }, plan.Select(m => m.Leads));
            Assert.AreNotSame(planned, plan, "the sim must not hand back the owner's own list");

            group.PlannedOverride = null;
            group.QuarryProfileId = string.Empty;
            Assert.AreEqual(5, NpcGroupComposition.Resolve(group, template).Count, "no override: the template again");
        }

        [Test]
        public void IsOwnedByWar_OnlyForAWarPartyNoOtherDirectorOwns()
        {
            Assert.IsTrue(new NpcGroup { QuarryProfileId = "p" }.IsOwnedByWar, "an older save's party");
            Assert.IsTrue(new NpcGroup { QuarryProfileId = "p", Owner = NpcGroup.OwnerWar }.IsOwnedByWar);
            Assert.IsFalse(new NpcGroup { QuarryProfileId = "p", Owner = NpcGroup.OwnerExpedition }.IsOwnedByWar,
                           "a band hunting someone is still the expedition director's");
            Assert.IsFalse(new NpcGroup { Owner = NpcGroup.OwnerWar }.IsOwnedByWar,
                           "a released party (quarry cleared) is nobody's war party any more");
            Assert.IsFalse(new NpcGroup().IsOwnedByWar);
        }
    }

    public class ExpeditionGroupSimTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<Object> junk = new();
        private NpcWorldSim sim;
        private NpcGroupTemplate warParty, expedition;
        private FactionDefinition sand;

        [SetUp]
        public void SetUp()
        {
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(sand);

            warParty = new NpcGroupTemplate { id = "sand-war-party", tribe = sand, runtimeOnly = true, bountyHunters = true };
            expedition = new NpcGroupTemplate { id = "settlement-expedition", tribe = sand, runtimeOnly = true, travelSpeed = 3.5f };

            var go = new GameObject("Sim");
            junk.Add(go);
            sim = go.AddComponent<NpcWorldSim>();
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { warParty, expedition });
            Call(sim, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        private NpcGroup Band(string id)
        {
            NpcGroup group = sim.CreateGroup(expedition, id, Vector3.zero);
            group.Owner = NpcGroup.OwnerExpedition;
            return group;
        }

        [Test]
        public void RestoreRecords_KeepsAClaimedOwnersRuntimeGroup_WithItsOwner()
        {
            sim.ClaimRuntimeOwner(NpcGroup.OwnerExpedition);

            var band = new NpcGroup { Id = "exp:1", TemplateId = "settlement-expedition", Owner = NpcGroup.OwnerExpedition };
            var stranger = new NpcGroup { Id = "x:1", TemplateId = "settlement-expedition", Owner = "nobody-claimed-this" };

            sim.RestoreRecords(new[] { band.ToRecord(), stranger.ToRecord() });

            CollectionAssert.AreEqual(new[] { "exp:1" }, sim.Groups.Select(g => g.Id));
            Assert.AreEqual(NpcGroup.OwnerExpedition, sim.FindGroup("exp:1").Owner);
        }

        [Test]
        public void AFoldedExpeditionGroup_OnlyWalksToItsGoal_AndHaltsWithoutOne()
        {
            NpcGroup band = Band("exp:1");
            band.GoalPosition = new Vector3(100f, 0f, 0f);
            band.HasGoal = true;

            Call(sim, "TickGroup", band, 1f);
            Assert.AreEqual(3.5f, band.Position.x, 0.01f);

            band.HasGoal = false;
            for (int i = 0; i < 10; i++) Call(sim, "TickGroup", band, 1f);

            Assert.IsFalse(band.HasGoal, "the director owns the goal; the sim must not pick an errand or a roam point");
            Assert.AreEqual(3.5f, band.Position.x, 0.01f, "halted");
        }

        [Test]
        public void ReportSighting_DoesNotSteerAnExpeditionGroup()
        {
            expedition.bountyHunters = true;
            NpcGroup band = Band("exp:1");

            sim.ReportSighting(new Vector3(1f, 0f, 1f));

            Assert.IsFalse(band.HasLead);
        }

        [Test]
        public void WarDirector_AdoptsAnOldSavesParty_ButNeverAnExpeditionGroup()
        {
            var director = sim.gameObject.AddComponent<WarPartyDirector>();
            Call(director, "Awake");

            NpcGroup band = Band("exp:1");
            band.QuarryProfileId = "p";   // a band hunting someone is still not a war party
            NpcGroup oldParty = sim.CreateGroup(warParty, "warparty:sand:p:1", Vector3.zero);
            oldParty.QuarryProfileId = "q";

            Call(director, "AdoptRestoredParties");

            Assert.IsNull(director.Book.FindByGroup("exp:1"));
            Assert.IsNotNull(director.Book.FindByGroup("warparty:sand:p:1"));
            Assert.AreSame(band, sim.FindGroup("exp:1"), "not released as a second party either");
        }

        [Test]
        public void Spawn_PutsMembersAtTheirPoses_StampsEach_AndUsesThePosesOnce()
        {
            var prefab = new GameObject("StandIn");
            junk.Add(prefab);

            // Far below any NavMesh an open scene might hold, so the spawn's NavMesh sample finds nothing.
            NpcGroup band = Band("exp:1");
            band.Position = new Vector3(0f, -10000f, 0f);
            band.PlannedOverride = new List<PlannedMember> { new(prefab, true), new(prefab, false), new(prefab, false) };
            var first = new Pose(new Vector3(30f, -10000f, 5f), Quaternion.identity);
            var posed = new Pose(new Vector3(40f, -10000f, 7f), Quaternion.Euler(0f, 90f, 0f));
            band.SpawnPoses = new List<Pose> { first, posed };

            var stamped = new Dictionary<int, GameObject>();
            band.MemberStamp = (instance, index) =>
            {
                stamped[index] = instance;
                junk.Add(instance);
                Assert.AreEqual(index, instance.GetComponent<GroupMembership>().MemberIndex,
                                "the owner's stamp runs after the sim's own");
            };

            Call(sim, "Spawn", band, expedition);

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, stamped.Keys);
            Assert.AreEqual(first.position, stamped[0].transform.position);
            Assert.AreEqual(posed.position, stamped[1].transform.position);
            Assert.AreEqual(posed.rotation.eulerAngles.y, stamped[1].transform.eulerAngles.y, 0.01f);
            Assert.AreNotEqual(posed.position, stamped[2].transform.position, "no pose: a formation slot");
            Assert.IsNull(band.SpawnPoses, "one-shot: a later spawn uses formation slots again");
        }

        [Test]
        public void Despawn_ReadsEveryMemberBack_TheDeadIncluded()
        {
            var prefab = new GameObject("StandIn", typeof(HealthComponent));
            junk.Add(prefab);

            NpcGroup band = Band("exp:1");
            band.PlannedOverride = new List<PlannedMember> { new(prefab, true), new(prefab, false), new(prefab, false) };
            band.MemberStamp = (instance, _) => junk.Add(instance);
            Call(sim, "Spawn", band, expedition);

            // A corpse is deactivated by HealthReactionModule, not destroyed, so it is still in Live.
            GameObject dead = band.Live[1];
            var health = new SerializedObject(dead.GetComponent<HealthComponent>());
            health.FindProperty("currentHealth").intValue = 0;
            health.ApplyModifiedPropertiesWithoutUndo();
            dead.SetActive(false);

            var alive = new Dictionary<int, bool>();
            band.ReadBack = (member, index) => alive[index] = member.GetComponent<HealthComponent>().Alive;

            // Outside play mode the fold's Destroy logs "use DestroyImmediate"; the instances are junk.
            LogAssert.ignoreFailingMessages = true;
            Call(sim, "Despawn", band, expedition);

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, alive.Keys);
            Assert.IsFalse(alive[1], "a dead stand-in's read-back must still be able to record its death");
            Assert.IsTrue(alive[0] && alive[2]);
            Assert.IsFalse(band.Spawned);
        }
    }
}
