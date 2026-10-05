// Where a resident stands for a spot: a stand point validated on the NavMesh within the tuning's tolerances (never the
// 12 m snap of a plain goal), an unusable spot that keeps its place index but is never planned onto, an exact-stand goal
// that only counts as arrived on its point, and a sitter lifted until its hips rest on the seat's sit point.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents.Tests
{
    public class StandPointTests
    {
        private const float FloorSize = 20f, FloorThickness = 0.1f, Epsilon = 1e-3f;
        private static readonly Vector3 Heart = new Vector3(3f, 0f, 3f);

        private NavMeshDataInstance instance;
        private NavMeshData data;

        [SetUp]
        public void SetUp()
        {
            var floor = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = Vector3.one,
                transform = Matrix4x4.TRS(new Vector3(0f, -FloorThickness * 0.5f, 0f), Quaternion.identity, new Vector3(FloorSize, FloorThickness, FloorSize)),
                area = 0,
            };
            data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { floor },
                                                   new Bounds(Vector3.zero, new Vector3(FloorSize * 2f, 20f, FloorSize * 2f)),
                                                   Vector3.zero, Quaternion.identity);
            instance = NavMesh.AddNavMeshData(data);
        }

        [TearDown]
        public void TearDown()
        {
            instance.Remove();
            Object.DestroyImmediate(data);
        }

        [Test]
        public void ASpotJustAboveTheFloor_StandsOnTheFloor()
        {
            Assert.IsTrue(SettlementPlaces.TryStand(new Vector3(0f, 0.3f, 0f), false, Heart, out Vector3 stand, out string why), why);
            Assert.AreEqual(0f, stand.y, 0.1f);
            Assert.AreEqual(0f, stand.x, 0.1f);
        }

        [Test]
        public void ASpotBeyondTheNavMesh_IsUnusable_NotSnappedAcrossTheWorld()
        {
            Assert.IsFalse(SettlementPlaces.TryStand(new Vector3(FloorSize, 0f, 0f), false, Heart, out _, out string why));
            StringAssert.Contains("NavMesh", why);
        }

        [Test]
        public void ASpotAHeightAboveTheFloor_IsUnusable_NotSnappedDownFromARoof()
        {
            float aboveABody = ResidentTuning.Instance.standSnapHeight + 1f;
            Assert.IsFalse(SettlementPlaces.TryStand(new Vector3(0f, aboveABody, 0f), false, Heart, out _, out _));
        }

        [Test]
        public void ASpotWhoseNavMeshCannotBeWalkedTo_IsUnusable_UnlessItIsADeck()
        {
            var cutOff = new Vector3(0f, 0f, 0f);
            var unreachableHeart = new Vector3(0f, 0f, FloorSize * 3f);
            Assert.IsFalse(SettlementPlaces.TryStand(cutOff, false, unreachableHeart, out _, out string why));
            StringAssert.Contains("island", why);
            Assert.IsTrue(SettlementPlaces.TryStand(cutOff, true, unreachableHeart, out _, out _), "a deck is stood at without a path");
        }

        [Test]
        public void AnUnusablePlace_KeepsItsIndexAndFlipsOnce()
        {
            var place = new SettlementPlace(PlaceKind.Post, ScriptableObject.CreateInstance<SpotUse>(), 0, 0, Vector3.zero, null);
            Assert.IsTrue(place.Usable, "not knowing is not a fault");
            Assert.IsFalse(place.Resolved);

            Assert.IsTrue(place.Resolve(Vector3.one, false), "usable -> unusable is a change the planner must hear about");
            Assert.IsFalse(place.Resolve(Vector3.one, false), "the same answer again is not");
            Assert.AreEqual(Vector3.one, place.Position);
            Object.DestroyImmediate(place.Use);
        }

        [Test]
        public void ExactStandGoal_CountsAsArrivedOnlyOnItsPoint()
        {
            var host = new GameObject("goal");
            try
            {
                AgentGoal goal = host.AddComponent<AgentGoal>();
                goal.Set(new Vector3(2f, 0f, 0f), 0.6f, "sit", true, Vector3.forward, 1f, true);
                Assert.IsTrue(goal.ExactStand);

                host.transform.position = new Vector3(2f - AgentGoal.ExactArrivalRadius - Epsilon, 0f, 0f);
                Assert.IsFalse(goal.HasArrived, "a loose hold would count 1.35 m as there; an exact stand must not");
                host.transform.position = new Vector3(2f - AgentGoal.AlignedWithin, 0f, 0f);
                Assert.IsTrue(goal.HasArrived);

                goal.Set(new Vector3(2f, 0f, 0f), 0.6f, "walk", false, null, 1f, true);
                Assert.IsFalse(goal.ExactStand, "only a holding goal can be exact");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void SeatedBody_IsLiftedUntilItsHipsRestOnTheSitPoint()
        {
            ResidentTuning tuning = ScriptableObject.CreateInstance<ResidentTuning>();
            try
            {
                float hipsOfTheLoop = 0.2f, sitPoint = 0.45f;
                float lift = SeatedBodyFit.TargetLift(sitPoint, hipsOfTheLoop, 1f, tuning.seatHipsAboveSurface, 0f);
                Assert.AreEqual(sitPoint + tuning.seatHipsAboveSurface - hipsOfTheLoop, lift, Epsilon);

                Assert.AreEqual(0f, SeatedBodyFit.TargetLift(0.02f, hipsOfTheLoop, 1f, tuning.seatHipsAboveSurface, 0f), "hips already above the sit point are never pushed down into the floor");
                Assert.AreEqual(2f * lift, SeatedBodyFit.TargetLift(2f * sitPoint, 2f * hipsOfTheLoop, 2f, tuning.seatHipsAboveSurface, 0f), Epsilon, "a bigger body scales the fit");
            }
            finally { Object.DestroyImmediate(tuning); }
        }

        [Test]
        public void StoolSitter_IsLetDownOntoTheSeat_ButNeverThroughTheFloor()
        {
            ResidentTuning tuning = ScriptableObject.CreateInstance<ResidentTuning>();
            try
            {
                // The Raxy's chair sit on a 0.564 m pot (scale 1.216): the hips come out 0.864 m up, 0.083 m above where they should rest.
                float rest = 0.564f + tuning.stoolHipsAboveSurface * 1.216f;
                float drop = SeatedBodyFit.TargetLift(0.564f, 0.864f, 1.216f, tuning.stoolHipsAboveSurface, tuning.stoolMaxDrop);
                Assert.AreEqual(rest - 0.864f, drop, Epsilon, "the body goes down until the hips rest on the seat");
                Assert.Less(drop, 0f);

                float blendingOutOfStanding = SeatedBodyFit.TargetLift(0.564f, 1.2f, 1.216f, tuning.stoolHipsAboveSurface, tuning.stoolMaxDrop);
                Assert.AreEqual(-tuning.stoolMaxDrop * 1.216f, blendingOutOfStanding, Epsilon, "a standing pose's hips are far above the seat: the drop is limited");
            }
            finally { Object.DestroyImmediate(tuning); }
        }
    }
}
