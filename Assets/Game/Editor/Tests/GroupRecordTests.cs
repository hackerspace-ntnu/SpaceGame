// A group's record carries the roster seed, the quarry and the tier through a save, and a member
// stamped before its spawn is who its loadout roll and the ledger think it is.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class GroupRecordTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        [Test]
        public void Record_RoundTripsTheWarPartyFields_ThroughTheSaveSerializer()
        {
            var group = new NpcGroup
            {
                Id = "warparty:sand:p1:1",
                TemplateId = "sand-war-party",
                RosterSeed = 918273,
                QuarryProfileId = "p1",
                Tier = 2,
                WipedOut = true,
                Delivered = true,
            };

            JObject json = JObject.FromObject(group.ToRecord(), SaveSerializer.Serializer);
            NpcGroup.Record back = json.ToObject<NpcGroup.Record>(SaveSerializer.Serializer);

            var restored = new NpcGroup { Id = back.id, TemplateId = back.templateId };
            restored.ApplyRecord(in back);

            Assert.AreEqual(918273, restored.RosterSeed);
            Assert.AreEqual("p1", restored.QuarryProfileId);
            Assert.AreEqual(2, restored.Tier);
            Assert.IsTrue(restored.IsWarParty);
            Assert.IsTrue(restored.WipedOut, "a party wiped out just before the save must not respawn on load");
            Assert.IsTrue(restored.Delivered, "a party already dropped off comes back on foot, not in a vessel");
        }

        [Test]
        public void Record_FromAnOlderSave_ReadsDefaults()
        {
            var old = JObject.Parse("{\"id\":\"nomad-caravan\",\"templateId\":\"nomad-caravan\",\"taskIndex\":1}");
            NpcGroup.Record record = old.ToObject<NpcGroup.Record>(SaveSerializer.Serializer);

            var group = new NpcGroup { Id = record.id, TemplateId = record.templateId };
            group.ApplyRecord(in record);

            // A bare group has no seed of its own, so a missing one leaves 0 (ApplyRecord keeps, never zeroes).
            Assert.AreEqual(0, group.RosterSeed);
            Assert.AreEqual(string.Empty, group.QuarryProfileId);
            Assert.AreEqual(0, group.Tier);
            Assert.IsFalse(group.IsWarParty);
            Assert.IsFalse(group.WipedOut);
            Assert.IsFalse(group.Delivered, "an older save's party has not been dropped off");
        }

        [Test]
        public void ApplyRecord_WithNoSeed_KeepsTheGroupsOwn_ButASavedSeedWins()
        {
            var group = new NpcGroup { Id = "caravan", RosterSeed = RosterDraw.StableHash("caravan") };

            group.ApplyRecord(new NpcGroup.Record { id = "caravan" });
            Assert.AreEqual(RosterDraw.StableHash("caravan"), group.RosterSeed, "an older save has no seed to give");

            group.ApplyRecord(new NpcGroup.Record { id = "caravan", rosterSeed = 42 });
            Assert.AreEqual(42, group.RosterSeed);
        }

        [Test]
        public void Stamp_OnFootMember_IsAFighter_CountsAndTakesTheTribe()
        {
            var tribe = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(tribe);

            var member = new GameObject("Nomad");
            junk.Add(member);
            member.AddComponent<EntityFaction>();

            var group = new NpcGroup { Id = "g" };
            GroupMembership membership = GroupMembership.Stamp(member, group, 3, tribe);

            Assert.IsTrue(membership.IsFighter);
            Assert.AreEqual(3, membership.MemberIndex);
            Assert.AreEqual(1, group.FightersSpawned);
            Assert.AreSame(tribe, member.GetComponent<EntityFaction>().Faction);
            Assert.AreEqual("g", GroupMembership.GroupIdOf(member.transform));
            CollectionAssert.AreEqual(new[] { member }, group.Fighters, "a fighter is tracked by its group, not only by its spawn list");
        }

        [Test]
        public void Stamp_OnAMount_IsNotAFighter_AndIsNotTracked()
        {
            var mount = new GameObject("Ostrich");
            junk.Add(mount);
            mount.AddComponent<NpcPassenger>();

            var group = new NpcGroup { Id = "g" };
            GroupMembership membership = GroupMembership.Stamp(mount, group, 0, null);

            Assert.IsFalse(membership.IsFighter);
            Assert.AreEqual(0, group.FightersSpawned);
            CollectionAssert.IsEmpty(group.Fighters);
        }

        [Test]
        public void CountStanding_SkipsTheDeadAndTheDestroyed()
        {
            var standing = new GameObject("Standing");
            junk.Add(standing);
            standing.AddComponent<HealthComponent>();

            var dead = new GameObject("Dead");
            junk.Add(dead);
            var so = new SerializedObject(dead.AddComponent<HealthComponent>());
            so.FindProperty("currentHealth").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            var destroyed = new GameObject("Destroyed");
            Object.DestroyImmediate(destroyed);

            Assert.AreEqual(1, GroupMembership.CountStanding(new List<GameObject> { standing, dead, destroyed, null }));
        }

        [Test]
        public void GroupIdOf_ClimbsFromAChildTransform_AndIsNullForStrangers()
        {
            var member = new GameObject("Nomad");
            junk.Add(member);
            var hand = new GameObject("Hand");
            hand.transform.SetParent(member.transform);
            member.AddComponent<EntityFaction>();

            GroupMembership.Stamp(member, new NpcGroup { Id = "g" }, 0, null);

            Assert.AreEqual("g", GroupMembership.GroupIdOf(hand.transform));
            Assert.IsNull(GroupMembership.GroupIdOf(null));

            var stranger = new GameObject("Stranger");
            junk.Add(stranger);
            Assert.IsNull(GroupMembership.GroupIdOf(stranger.transform));
        }

        [Test]
        public void NpcSpawn_RunsBeforeSpawn_OnTheInstance_BeforeReturning()
        {
            var prefab = new GameObject("Template");
            junk.Add(prefab);

            GameObject seen = null;
            GameObject instance = NpcSpawn.Create(prefab, Vector3.zero, Quaternion.identity, null,
                                                  go => seen = go);
            junk.Add(instance);

            Assert.AreSame(instance, seen);
        }

        // A seat is usually in mid-air, and a NavMeshAgent that wakes enabled off the NavMesh logs
        // "Failed to create agent" inside Instantiate — before beforeSpawn could switch it off.
        [Test]
        public void NpcSpawn_Seated_WakesWithItsNavMeshAgentAlreadyOff()
        {
            var prefab = new GameObject("Template", typeof(UnityEngine.AI.NavMeshAgent));
            junk.Add(prefab);

            bool agentOnInBeforeSpawn = true;
            GameObject instance = NpcSpawn.Create(prefab, new Vector3(0f, 500f, 0f), Quaternion.identity, null,
                go => agentOnInBeforeSpawn = go.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled,
                seated: true);
            junk.Add(instance);

            Assert.IsFalse(agentOnInBeforeSpawn, "the agent must already be off when beforeSpawn runs");
            Assert.IsFalse(instance.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled);
            Assert.IsNull(instance.transform.parent, "the NPC must come out at the scene root");
            Assert.IsTrue(instance.activeInHierarchy);
            Assert.AreEqual(new Vector3(0f, 500f, 0f), instance.transform.position);
        }

        [Test]
        public void NpcSpawn_NotSeated_LeavesTheNavMeshAgentAsAuthored()
        {
            var prefab = new GameObject("Template", typeof(UnityEngine.AI.NavMeshAgent));
            junk.Add(prefab);

            GameObject instance = NpcSpawn.Create(prefab, Vector3.zero, Quaternion.identity);
            junk.Add(instance);

            Assert.IsTrue(instance.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled);
        }

        [Test]
        public void SeededPick_IsTheSameForTheSameGroupAndMember()
        {
            const int seed = 55, member = 2, candidates = 7;
            Assert.AreEqual(RosterDraw.IndexFor(seed, member, candidates),
                            RosterDraw.IndexFor(seed, member, candidates));
        }
    }
}
