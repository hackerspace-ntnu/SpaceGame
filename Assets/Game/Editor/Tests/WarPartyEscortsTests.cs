// Assets/Game/Editor/Tests/WarPartyEscortsTests.cs
// A flying war party's escort fliers: drawn from a tier's ownWings roles, never seated in the vessel and never
// leading; flying an even ring round the hull; sent down beside the drop when the vessel goes down to unload or
// has nothing left to escort. And what they leave when they die: the pack always, the gun only on a roll.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class WarPartyEscortsTests
    {
        private const float Tolerance = 0.01f;
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private (NpcGroup group, NpcGroupTemplate template) WarPartyWithFliers(int riders, int fliers)
        {
            var rider = new GameObject("Rider");
            var flier = new GameObject("Flier");
            junk.Add(rider);
            junk.Add(flier);

            var roster = ScriptableObject.CreateInstance<FactionRoster>();
            var tribe = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(roster);
            junk.Add(tribe);
            roster.members = new[]
            {
                new RosterMember { role = RosterRole.Scout, prefab = rider, weight = 1f },
                new RosterMember { role = RosterRole.Warrior, prefab = flier, weight = 1f },
            };
            // Fliers listed FIRST: still none of them may lead.
            roster.warPartyTiers = new[]
            {
                new WarPartyTier
                {
                    roles = new[]
                    {
                        new RoleCount { role = RosterRole.Warrior, count = fliers, ownWings = true },
                        new RoleCount { role = RosterRole.Scout, count = riders },
                    },
                },
            };
            roster.faction = tribe;
            tribe.roster = roster;

            var group = new NpcGroup { QuarryProfileId = "player", Tier = 0, RosterSeed = 7 };
            return (group, new NpcGroupTemplate { tribe = tribe });
        }

        [Test]
        public void Fliers_NeverRideTheVessel_AndNeverLead()
        {
            (NpcGroup group, NpcGroupTemplate template) = WarPartyWithFliers(riders: 2, fliers: 4);
            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, template);

            Assert.AreEqual(6, plan.Count);
            Assert.AreEqual(4, plan.Count(m => m.OwnWings));
            Assert.AreEqual(2, plan.Count(NpcGroupComposition.Rides), "a flier was counted for a vessel seat");
            Assert.IsFalse(plan.Any(m => m.OwnWings && m.Leads), "a flier leads the party");
            Assert.AreEqual(1, plan.Count(m => m.Leads), "the party has no single leader among its riders");
        }

        [Test]
        public void TheEscortRing_IsEven_AtItsRadiusAndHeight_AndNobodySitsOnTheBow()
        {
            const int count = 4;
            var offsets = Enumerable.Range(0, count).Select(i => NpcGroupTransport.EscortOffset(i, count, 40f, 12f)).ToList();

            foreach (Vector3 offset in offsets)
            {
                Assert.AreEqual(12f, offset.y, Tolerance);
                Assert.AreEqual(40f, new Vector2(offset.x, offset.z).magnitude, Tolerance);
                Assert.Greater(Vector3.Angle(Vector3.forward, new Vector3(offset.x, 0f, offset.z)), 1f, "a flier dead ahead of the bow");
            }

            float side = Vector3.Distance(offsets[0], offsets[1]);
            for (int i = 0; i < count; i++)
                Assert.AreEqual(side, Vector3.Distance(offsets[i], offsets[(i + 1) % count]), Tolerance, "the ring is uneven");
        }

        [Test]
        public void TheLandings_SpreadRoundTheDrop_ClearOfTheHull()
        {
            Vector3 drop = new Vector3(100f, 5f, 100f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 landing = NpcGroupTransport.EscortLanding(drop, i, 6, 30f);
                Assert.AreEqual(drop.y, landing.y, Tolerance);
                Assert.AreEqual(30f, Vector3.Distance(drop, landing), Tolerance);
            }
        }

        [Test]
        public void TheEscortGoesDown_WhenTheVesselDoes_OrHasNothingLeftToEscort()
        {
            Assert.IsFalse(WarPartyEscorts.ShouldLand(true, false, false, VesselMissionState.Cruise));
            Assert.IsFalse(WarPartyEscorts.ShouldLand(true, false, false, VesselMissionState.Approach));
            Assert.IsFalse(WarPartyEscorts.ShouldLand(true, false, false, VesselMissionState.Depart),
                           "a run turning for home still loaded lost its escort");
            Assert.IsTrue(WarPartyEscorts.ShouldLand(true, false, false, VesselMissionState.Descend));
            Assert.IsTrue(WarPartyEscorts.ShouldLand(true, false, false, VesselMissionState.Unload));
            Assert.IsTrue(WarPartyEscorts.ShouldLand(false, false, false, VesselMissionState.Done), "no vessel, still escorting");
            Assert.IsTrue(WarPartyEscorts.ShouldLand(true, true, false, VesselMissionState.Cruise), "escorting a wreck");
            Assert.IsTrue(WarPartyEscorts.ShouldLand(true, false, true, VesselMissionState.Climb), "escorting an empty vessel home");
        }

        [Test]
        public void AWarFlier_AlwaysShedsItsPack_ButOnlyAFlierShotDownElseDoes()
        {
            Assert.IsTrue(EntityLootTable.DropsWorn(isWingPack: false, diedAloft: false, warFlier: false), "the gauntlet stopped dropping");
            Assert.IsFalse(EntityLootTable.DropsWorn(isWingPack: true, diedAloft: false, warFlier: false), "a Sky nomad killed on foot dropped its pack");
            Assert.IsTrue(EntityLootTable.DropsWorn(isWingPack: true, diedAloft: true, warFlier: false), "a flier shot down kept its pack");
            Assert.IsTrue(EntityLootTable.DropsWorn(isWingPack: true, diedAloft: false, warFlier: true), "a war flier killed on foot kept its pack");
        }
    }
}
