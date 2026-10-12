// Where every group seen from afar stands, on the wire: the state survives serialization bit for bit,
// is read straight off the group's record, and only opted-in groups still standing are published. The
// list rides the session's one NetworkObject.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class DistantGroupsTests
    {
        private const string SessionPrefabPath = "Assets/Game/Prefabs/Systems/NetworkGameManager.prefab";

        [Test]
        public void AState_SurvivesTheWire()
        {
            var state = new DistantGroupState
            {
                GroupHash = -7, TemplateHash = 42, RosterSeed = 123456789,
                Position = new Vector3(1234.5f, 67.25f, -890.125f), Yaw = -135.5f, Spawned = true,
            };
            using var writer = new FastBufferWriter(64, Allocator.Temp);
            writer.WriteNetworkSerializable(state);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out DistantGroupState back);

            Assert.AreEqual(state, back);
        }

        [Test]
        public void AState_IsReadOffTheGroupsRecord()
        {
            var group = new NpcGroup
            {
                Id = "strider-city", TemplateId = "strider-city", RosterSeed = 99,
                Position = new Vector3(10f, 2f, 20f), GoalPosition = new Vector3(110f, 2f, 20f), HasGoal = true,
            };

            DistantGroupState state = DistantGroupState.Of(group);

            Assert.AreEqual(NpcGroupTemplate.HashOf("strider-city"), state.GroupHash);
            Assert.AreEqual(NpcGroupTemplate.HashOf("strider-city"), state.TemplateHash);
            Assert.AreEqual(99, state.RosterSeed);
            Assert.AreEqual(group.Position, state.Position);
            Assert.AreEqual(90f, state.Yaw, 1e-4f, "heading east");
            Assert.Less(Vector3.Distance(group.Heading, state.Heading), 1e-4f);
            Assert.IsFalse(state.Spawned);
        }

        [Test]
        public void OnlyOptedInGroups_StillStanding_ArePublished()
        {
            var city = new NpcGroupTemplate { id = "strider-city", showFromAfar = true };
            var nomads = new NpcGroupTemplate { id = "sand-nomads" };
            var templates = new Dictionary<string, NpcGroupTemplate> { [city.id] = city, [nomads.id] = nomads };
            var groups = new List<NpcGroup>
            {
                new NpcGroup { Id = "strider-city", TemplateId = city.id, RosterSeed = 1 },
                new NpcGroup { Id = "sand-nomads", TemplateId = nomads.id, RosterSeed = 2 },
                new NpcGroup { Id = "strider-city-2", TemplateId = city.id, RosterSeed = 3, WipedOut = true },
                new NpcGroup { Id = "orphan", TemplateId = "no-such-template", RosterSeed = 4 },
            };
            var published = new List<DistantGroupState> { default };

            DistantGroups.Collect(groups, id => templates.TryGetValue(id, out NpcGroupTemplate t) ? t : null, published);

            Assert.AreEqual(1, published.Count, "Collect replaces what the list held");
            Assert.AreEqual(NpcGroupTemplate.HashOf("strider-city"), published[0].GroupHash);
            Assert.AreEqual(1, published[0].RosterSeed);
        }

        [Test]
        public void TheSessionObject_CarriesDistantGroups()
        {
            var session = AssetDatabase.LoadAssetAtPath<GameObject>(SessionPrefabPath);
            Assert.IsNotNull(session.GetComponent<NetworkObject>());
            Assert.IsNotNull(session.GetComponent<DistantGroups>(),
                             "a NetworkBehaviour must be on the session prefab, never added at runtime");
        }
    }
}
