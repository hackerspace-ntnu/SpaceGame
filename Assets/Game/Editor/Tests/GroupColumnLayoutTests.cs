// Where each planned member of a group stands: the leader at the origin, every other member that
// spawns in the next follower slot, the whole column turning rigidly with its heading. The live spawn
// and the distant silhouette both stand members here, so the hand-over between them cannot jump.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class GroupColumnLayoutTests
    {
        private static readonly FormationShape Shape = new FormationShape
        {
            Lanes = 2, RowSpacing = 35f, LaneSpacing = 30f,
            LateralJitter = 2f, LongitudinalJitter = 3f, DriftAmplitude = 1f, DriftRate = 0.05f,
        };

        private GameObject prefab;

        [SetUp]
        public void SetUp() => prefab = new GameObject("Member");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(prefab);

        [Test]
        public void TheLeaderStandsAtTheOrigin_AndEveryOtherSpawningMemberInTheNextFollowerSlot()
        {
            var plan = new List<PlannedMember>
            {
                new PlannedMember(prefab, true),
                new PlannedMember(prefab, false),
                new PlannedMember(null, false),
                new PlannedMember(prefab, false, crew: true),
                new PlannedMember(prefab, false),
            };
            var origin = new Vector3(100f, 5f, -40f);
            Vector3 heading = new Vector3(1f, 0f, 1f).normalized;

            List<ColumnPlace> places = GroupColumnLayout.Places(plan, origin, heading, Shape);

            CollectionAssert.AreEqual(new[] { 0, 1, 3, 4 }, places.Select(p => p.PlanIndex), "nothing drawn takes no slot");
            Assert.IsTrue(places[0].Leads);
            Assert.AreEqual(origin, places[0].Position);
            for (int k = 0; k < 3; k++)
            {
                Assert.IsFalse(places[k + 1].Leads);
                Assert.AreEqual(FormationMath.SlotPosition(k, origin, heading, Shape, k * 7919, 0f), places[k + 1].Position,
                                $"follower {k}: the slot NpcWorldSim.Spawn always gave it");
            }
        }

        [Test]
        public void ASecondLeader_Follows()
        {
            var plan = new List<PlannedMember> { new PlannedMember(prefab, true), new PlannedMember(prefab, true) };
            List<ColumnPlace> places = GroupColumnLayout.Places(plan, Vector3.zero, Vector3.forward, Shape);
            Assert.IsTrue(places[0].Leads);
            Assert.IsFalse(places[1].Leads);
            Assert.AreEqual(FormationMath.SlotPosition(0, Vector3.zero, Vector3.forward, Shape, 0, 0f), places[1].Position);
        }

        [Test]
        public void TheColumnTurnsWithItsHeading_AsOneRigidShape([Values(0f, 37f, 90f, 200f, 315f)] float yaw)
        {
            var plan = new List<PlannedMember> { new PlannedMember(prefab, true) };
            for (int i = 0; i < 17; i++) plan.Add(new PlannedMember(prefab, false));
            var origin = new Vector3(-812f, 33f, 1530f);
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);

            List<ColumnPlace> local = GroupColumnLayout.Places(plan, Vector3.zero, Vector3.forward, Shape);
            List<ColumnPlace> world = GroupColumnLayout.Places(plan, origin, turn * Vector3.forward, Shape);

            for (int i = 0; i < plan.Count; i++)
                Assert.Less(Vector3.Distance(origin + turn * local[i].Position, world[i].Position), 1e-3f, $"member {i} at {yaw} deg");
        }

        [Test]
        public void ATemplate_IsFoundByTheHashOfItsId()
        {
            var city = new NpcGroupTemplate { id = "strider-city" };
            var nomads = new NpcGroupTemplate { id = "sand-nomads" };

            Assert.AreEqual(RosterDraw.StableHash("strider-city"), city.IdHash);
            Assert.AreEqual(NpcGroupTemplate.HashOf("sand-nomads"), nomads.IdHash);
            Assert.AreSame(nomads, NpcGroupTemplate.FindByIdHash(new[] { city, null, nomads }, nomads.IdHash));
            Assert.IsNull(NpcGroupTemplate.FindByIdHash(new[] { city, nomads }, NpcGroupTemplate.HashOf("no-such-group")));
            Assert.IsFalse(new NpcGroupTemplate().showFromAfar, "opt-in: no group is drawn from afar unless its template says so");
        }
    }
}
