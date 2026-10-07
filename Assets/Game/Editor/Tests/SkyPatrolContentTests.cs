// Assets/Game/Editor/Tests/SkyPatrolContentTests.cs
// The Sky Tribe's fliers as wired: the seeded sky-patrol (a skein of six to eight — a leader and five to seven
// wingmen — flying a loop high over the basin, never circling a waypoint, every leg clear of the rock spires), and the war-party tiers' escort fliers (2 / 4 / 6 on
// their own wings, never seated, never leading).
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.EditorTools;

namespace SpaceGame.Tests
{
    public class SkyPatrolContentTests
    {
        // The patrol cruises its cruiseHeight (180 m) over the ground under it with no look-ahead (NpcFlight.md
        // Gotchas); a corridor rising this far is still held to, so the loop stays in the open basin.
        private const float MaxRise = 40f;
        private const float CorridorHalfWidth = 40f;
        private const float CorridorStep = 40f;

        [Test]
        public void TheSkyPatrol_IsASeededAirPatrolOfSixToEight_FlyingTheAuthoredLoop()
        {
            NpcGroupTemplate patrol = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.SkyPatrolTemplateId);

            Assert.IsFalse(patrol.runtimeOnly, "seeded at startup");
            Assert.AreEqual(RosterAuthoring.SkyFactionPath, AssetDatabase.GetAssetPath(patrol.tribe));
            Assert.IsTrue(patrol.airPatrol.IsSet, "it has no route: it would walk");
            CollectionAssert.AreEqual(RosterAuthoring.SkyPatrolRoute, patrol.airPatrol.route);
            Assert.AreEqual(RosterAuthoring.SkyPatrolRoute[0], patrol.startPosition);
            Assert.IsNull(patrol.transport.smallVessel, "it flies its own craft, not a vessel");
            Assert.AreEqual(0, patrol.tasks.Length, "its route is its errand; a task list would fight it");

            Assert.AreEqual(1, patrol.members.Count(m => m.isLeader));
            int[] wingmen = patrol.members.Where(m => !m.isLeader).SelectMany(m => m.countWeights).Select(w => w.count).ToArray();
            Assert.AreEqual(5, wingmen.Min(), "user, 2026-10-07: 6-8 fliers");
            Assert.AreEqual(7, wingmen.Max(), "user, 2026-10-07: 6-8 fliers");
        }

        [Test]
        public void TheSkyPatrol_CruisesHighAndStraightOn_LikeASkeinOfGeese()
        {
            // User, 2026-10-07: "the formation of flyers should be high up in the sky ... They should just fly
            // through the sky, and not land. Basically like a flock of geese".
            NpcGroupTemplate patrol = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.SkyPatrolTemplateId);

            Assert.That(patrol.airPatrol.cruiseHeight, Is.InRange(150f, 250f), "high up, yet still a visible skein");
            Assert.AreEqual(RosterAuthoring.SkyPatrolCruiseHeight, patrol.airPatrol.cruiseHeight, 0.01f);
            Assert.AreEqual(Vector2.zero, patrol.airPatrol.waypointDwell, "geese fly on through a waypoint; they never circle it");
        }

        [Test]
        public void TheSkyPatrol_SpawnsWellInsideTheAirborneFoldRadius()
        {
            // Spawned at the edge and folded a short leg later, it would pop in and out of the sky.
            NpcGroupTemplate patrol = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.SkyPatrolTemplateId);
            float fold = StriderCityTemplateTests.ReadSimField<float>("airborneFoldRadius");

            Assert.AreEqual(RosterAuthoring.SkyPatrolSpawnRadius, patrol.airPatrol.spawnRadius, 0.01f);
            Assert.GreaterOrEqual(fold, 1.5f * patrol.airPatrol.spawnRadius,
                                  "the fold radius leaves no room between the spawn edge and the fold");
        }

        [Test]
        public void EveryPatrolLeg_IsLowEnoughToFlyAtCruise()
        {
            Vector3[] route = RosterAuthoring.SkyPatrolRoute;
            for (int i = 0; i < route.Length; i++)
            {
                Vector3 from = route[i], to = route[(i + 1) % route.Length];
                float highest = SkyFleetPlacement.HighestGroundAlong(from, to, CorridorHalfWidth, CorridorStep);
                Assert.IsFalse(float.IsNaN(highest), $"leg {i} has no ground under it at all - is it off the map?");
                Assert.Less(highest, from.y + MaxRise, $"leg {i} ({from} -> {to}) crosses ground at {highest:0} m: a spire it would fly into");
            }
        }

        [Test]
        public void EachSkyWarPartyTier_BringsItsEscortFliers_WhoNeverRideOrLead()
        {
            var roster = AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.SkyRosterPath);
            Assert.IsNotNull(roster, RosterAuthoring.SkyRosterPath);
            Assert.AreEqual(RosterAuthoring.SkyEscortFliers.Length, roster.warPartyTiers.Length);

            for (int t = 0; t < roster.warPartyTiers.Length; t++)
            {
                RoleCount[] roles = roster.warPartyTiers[t].roles;
                Assert.AreEqual(RosterAuthoring.SkyEscortFliers[t], roles.Where(r => r.ownWings).Sum(r => r.count),
                                $"tier {t} brings the wrong number of escort fliers - re-run Author Sky Tribe Roster");
                Assert.Greater(roles.Where(r => !r.ownWings).Sum(r => r.count), 0, $"tier {t} has nobody to ride the vessel");
            }
        }
    }
}
