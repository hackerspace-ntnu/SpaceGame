// Assets/Game/Editor/Tests/SkyPatrolContentTests.cs
// The Sky Tribe's fliers as wired: the seeded sky-patrol (a leader and two to four wingmen flying a loop low
// over the basin, every leg clear of the rock spires), and the war-party tiers' escort fliers (2 / 4 / 6 on
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
        // The patrol cruises NpcFlightModule.cruiseHeight (60 m) over the ground under it with no look-ahead
        // (NpcFlight.md Gotchas): a corridor whose ground rises this far over its lowest point would be clipped.
        private const float MaxRise = 40f;
        private const float CorridorHalfWidth = 40f;
        private const float CorridorStep = 40f;

        [Test]
        public void TheSkyPatrol_IsASeededAirPatrolOfThreeToFive_FlyingTheAuthoredLoop()
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
            Assert.AreEqual(2, wingmen.Min());
            Assert.AreEqual(4, wingmen.Max());
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
