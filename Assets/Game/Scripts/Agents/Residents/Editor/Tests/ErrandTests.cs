// What the errand layer promises: a chore replaces a break or a stroll only where a settlement has both ends of
// it, a patrol exists only where there is a ring and spreads its pairs round it, ambles are never booked,
// companions share a day, the ring stands the asked distance outside the buildings, and a prop index
// round-trips through the tuning. All pure — no scene, no NavMesh.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents.Tests
{
    public class ErrandTests
    {
        private const int Seed = 4321, DaysChecked = 6;
        private const float DaySeconds = 1600f;
        private const int Garden = 4, FirstWell = 5, FirstPlant = 7, FirstStroll = 11, FirstRing = 17, RingPoints = 6;

        private ResidentTuning tuning;
        private SpotUse garden, well, plant;
        private ChoreDefinition water;
        private List<PlannerPlace> places;

        [SetUp]
        public void SetUp()
        {
            tuning = ScriptableObject.CreateInstance<ResidentTuning>();
            tuning.ambleChance = 0f;
            garden = Use(SpotRole.Work);
            well = Use(SpotRole.Errand);
            plant = Use(SpotRole.Errand);
            water = ScriptableObject.CreateInstance<ChoreDefinition>();
            (water.source, water.target) = (well, plant);

            places = new List<PlannerPlace>();
            for (int i = 0; i < FirstRing + RingPoints; i++)
            {
                PlaceKind kind = i < 4 ? PlaceKind.Door : i == Garden ? PlaceKind.Post : i < FirstPlant ? PlaceKind.Errand
                    : i < FirstStroll ? PlaceKind.Errand : i < FirstRing ? PlaceKind.Stroll : PlaceKind.Patrol;
                SpotUse use = i == Garden ? garden : i < FirstPlant && kind == PlaceKind.Errand ? well : kind == PlaceKind.Errand ? plant : null;
                places.Add(new PlannerPlace { index = i, kind = kind, post = use });
            }
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(tuning);
            Object.DestroyImmediate(garden);
            Object.DestroyImmediate(well);
            Object.DestroyImmediate(plant);
            Object.DestroyImmediate(water);
        }

        [Test]
        public void AWorkerWithAChore_SpendsItsBreaksOnTheChore_AtASource()
        {
            var gardener = new PlannerResident { index = 0, homeIndex = 1, seed = 11, lifestyle = Lifestyle.Stationed, post = garden, chore = water };
            int chores = 0;
            for (int day = 0; day < DaysChecked; day++)
            {
                DayPlan plan = Build(day, gardener)[0];
                Assert.IsTrue(plan.segments.Exists(s => s.activity == Activity.Work), $"day {day}: still works its post");
                foreach (PlanSegment s in plan.segments.FindAll(s => s.activity == Activity.Chore))
                {
                    chores++;
                    Assert.AreEqual(PlaceKind.Errand, places[s.place].kind);
                    Assert.AreSame(well, places[s.place].post, "a round starts at the source");
                }
            }
            Assert.Greater(chores, 0);
        }

        [Test]
        public void ARoamerWithAChore_FillsItsFreeTimeWithRounds()
        {
            var hauler = new PlannerResident { index = 0, homeIndex = 1, seed = 12, lifestyle = Lifestyle.Roamer, chore = water };
            DayPlan plan = Build(2, hauler)[0];
            Assert.IsTrue(plan.segments.Exists(s => s.activity == Activity.Chore));
        }

        [Test]
        public void ChoreWithoutBothEnds_IsLeftOut_AndTheBreakStaysABreak()
        {
            places.RemoveAll(p => p.post == plant);
            for (int i = 0; i < places.Count; i++) { PlannerPlace p = places[i]; p.index = i; places[i] = p; }
            var gardener = new PlannerResident { index = 0, homeIndex = 1, seed = 11, lifestyle = Lifestyle.Stationed, post = garden, chore = water };
            for (int day = 0; day < DaysChecked; day++)
                Assert.IsFalse(Build(day, gardener)[0].segments.Exists(s => s.activity == Activity.Chore), $"day {day}");
        }

        [Test]
        public void SameUseChore_NeedsTwoStops()
        {
            water.target = well;
            places.RemoveAt(FirstWell + 1);
            for (int i = 0; i < places.Count; i++) { PlannerPlace p = places[i]; p.index = i; places[i] = p; }
            var hauler = new PlannerResident { index = 0, homeIndex = 1, seed = 12, lifestyle = Lifestyle.Roamer, chore = water };
            Assert.IsFalse(Build(2, hauler)[0].segments.Exists(s => s.activity == Activity.Chore), "one stop is nowhere to carry to");
        }

        [Test]
        public void GuardPairs_PatrolFromPointsSpreadRoundTheRing()
        {
            PlannerResident Guard(int index, int slot) => new PlannerResident
            {
                index = index, homeIndex = 1, seed = 20 + index, lifestyle = Lifestyle.Stationed, duty = ResidentDuty.Patrol, slot = slot, slots = 2,
            };

            DayPlan[] plans = Build(3, Guard(0, 0), Guard(1, 0), Guard(2, 1), Guard(3, 1));
            var starts = new int[plans.Length];
            for (int i = 0; i < plans.Length; i++) starts[i] = plans[i].segments.Find(s => s.activity == Activity.Patrol).place;
            Assert.AreEqual(starts[0], starts[1], "a pair starts together");
            Assert.AreEqual(starts[2], starts[3]);
            Assert.AreNotEqual(starts[0], starts[2], "pairs start apart");
            foreach (int start in starts) Assert.AreEqual(PlaceKind.Patrol, places[start].kind);
        }

        [Test]
        public void NoRing_NoPatrol()
        {
            places.RemoveAll(p => p.kind == PlaceKind.Patrol);
            var guard = new PlannerResident { index = 0, homeIndex = 1, seed = 20, lifestyle = Lifestyle.Stationed, duty = ResidentDuty.Patrol, slots = 1 };
            Assert.IsFalse(Build(3, guard)[0].segments.Exists(s => s.activity == Activity.Patrol));
        }

        [Test]
        public void Ambles_AreNeverBooked_SoTwoResidentsMayShareOne()
        {
            tuning.ambleChance = 1f;
            PlannerResident Roamer(int index) => new PlannerResident { index = index, homeIndex = 1, seed = 30 + index, lifestyle = Lifestyle.Roamer };
            DayPlan[] plans = Build(2, Roamer(0), Roamer(1), Roamer(2), Roamer(3));
            Assert.IsTrue(plans[0].segments.Exists(s => s.activity == Activity.Amble));
            foreach (DayPlan plan in plans)
                foreach (PlanSegment s in plan.segments.FindAll(s => s.activity == Activity.Amble))
                    Assert.AreEqual(PlaceKind.Stroll, places[s.place].kind);
        }

        [Test]
        public void Companions_WalkTheSameAmblesAtTheSameTimes()
        {
            var leader = new PlannerResident { index = 6, homeIndex = 2, seed = 66, lifestyle = Lifestyle.Roamer, paired = true };
            var follower = new PlannerResident { index = 7, homeIndex = 2, seed = 77, shareSeed = 66, lifestyle = Lifestyle.Roamer, paired = true };
            for (int day = 0; day < DaysChecked; day++)
            {
                DayPlan[] plans = Build(day, leader, follower);
                var mine = plans[0].segments.FindAll(s => s.activity == Activity.Amble);
                var theirs = plans[1].segments.FindAll(s => s.activity == Activity.Amble);
                Assert.Greater(mine.Count, 0, $"day {day}: a pair ambles");
                Assert.AreEqual(mine.Count, theirs.Count, $"day {day}");
                for (int i = 0; i < mine.Count; i++)
                {
                    Assert.AreEqual(mine[i].arrive, theirs[i].arrive, 1e-2f, $"day {day}, amble {i}");
                    Assert.AreEqual(mine[i].leave, theirs[i].leave, 1e-2f, $"day {day}, amble {i}");
                }
            }
        }

        [Test]
        public void GuardsPairOff_InRosterOrder_AndAnOddOneStaysSolo()
        {
            var candidates = new List<CompanionCandidate>();
            foreach (int index in new[] { 11, 3, 8, 5, 9 }) candidates.Add(new CompanionCandidate(index, true, false, null));

            Dictionary<int, int> leaderOf = Companions.Pair(candidates);
            Assert.AreEqual(2, leaderOf.Count);
            Assert.AreEqual(3, leaderOf[5]);
            Assert.AreEqual(8, leaderOf[9]);
            Assert.IsFalse(leaderOf.ContainsKey(11));

            Dictionary<int, int> slots = Companions.PatrolSlots(candidates, out int count);
            Assert.AreEqual(3, count);
            Assert.AreEqual(0, slots[3]);
            Assert.AreEqual(0, slots[5]);
            Assert.AreEqual(1, slots[8]);
            Assert.AreEqual(2, slots[11]);
        }

        [Test]
        public void Roamers_PairWithAFriend_ButNobodyIsPairedTwice()
        {
            var candidates = new List<CompanionCandidate>
            {
                new CompanionCandidate(1, false, true, new[] { 2 }),
                new CompanionCandidate(2, false, true, new[] { 1 }),
                new CompanionCandidate(4, false, true, new[] { 2 }),
                new CompanionCandidate(6, true, false, new[] { 1 }),
            };
            Dictionary<int, int> leaderOf = Companions.Pair(candidates);
            Assert.AreEqual(1, leaderOf[2]);
            Assert.IsFalse(leaderOf.ContainsKey(4), "its only friend is taken");
            Assert.IsFalse(leaderOf.ContainsKey(1));
        }

        [Test]
        public void PerimeterRing_StandsTheOffsetOutsideTheBuildings()
        {
            var footprints = new List<Bounds> { new Bounds(Vector3.zero, new Vector3(10f, 5f, 10f)) };
            List<Vector3> ring = PerimeterRing.Compute(Vector3.zero, footprints, 5f, 4);

            Assert.AreEqual(4, ring.Count);
            Assert.AreEqual(10f, ring[0].x, 1e-3f, "5 m half-width + 5 m offset, along +x");
            Assert.AreEqual(10f, ring[1].z, 1e-3f);
            Assert.AreEqual(-10f, ring[2].x, 1e-3f);
            foreach (Vector3 point in ring)
                Assert.GreaterOrEqual(Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.z)) - 5f, 5f - 1e-3f, "never closer than the offset");
        }

        [Test]
        public void PerimeterRing_HugsTheFarthestBuildingOnEachBearing()
        {
            var footprints = new List<Bounds>
            {
                new Bounds(Vector3.zero, new Vector3(10f, 5f, 10f)),
                new Bounds(new Vector3(30f, 0f, 0f), new Vector3(10f, 5f, 10f)),
            };
            List<Vector3> ring = PerimeterRing.Compute(Vector3.zero, footprints, 5f, 4);
            Assert.AreEqual(40f, ring[0].x, 1e-3f, "past the outlying building: 35 + 5");
            Assert.AreEqual(10f, ring[1].z, 1e-3f);
        }

        [Test]
        public void ChoreRounds_WalkNearestFirst_AndWrap()
        {
            var points = new List<Vector3> { new Vector3(10f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(5f, 0f, 0f) };
            int[] order = ChoreRounds.Chain(Vector3.zero, points);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, order);

            int cursor = 0;
            CollectionAssert.AreEqual(new[] { 1, 2 }, ChoreRounds.Take(order, ref cursor, 2));
            CollectionAssert.AreEqual(new[] { 0, 1 }, ChoreRounds.Take(order, ref cursor, 2), "the next round carries on and wraps");
        }

        [Test]
        public void CarriedItem_RoundTripsThroughTheTuning()
        {
            var bucket = ScriptableObject.CreateInstance<InventoryItem>();
            var crate = ScriptableObject.CreateInstance<InventoryItem>();
            var stranger = ScriptableObject.CreateInstance<InventoryItem>();
            try
            {
                tuning.tripKinds = new TripKindRow[2];
                tuning.carryItems = new[] { bucket, crate };
                Assert.AreEqual(4, tuning.PropIndexOf(crate), "after the two trip rows, 1-based");
                Assert.AreSame(crate, tuning.CarryItemAt(4));
                Assert.AreSame(bucket, tuning.CarryItemAt(3));
                Assert.AreEqual(0, tuning.PropIndexOf(null));
                Assert.AreEqual(0, tuning.PropIndexOf(stranger), "not in the list carries nothing");
                Assert.IsNull(tuning.CarryItemAt(0));
                Assert.IsNull(tuning.CarryItemAt(2), "a trip row is not a carried item");
                Assert.IsNull(tuning.CarryItemAt(9));
                Assert.IsNull(tuning.PropAt(4), "a carried item is not a trip row's prop");
            }
            finally
            {
                Object.DestroyImmediate(bucket);
                Object.DestroyImmediate(crate);
                Object.DestroyImmediate(stranger);
            }
        }

        private DayPlan[] Build(int day, params PlannerResident[] residents) =>
            DayPlanner.BuildAll(Seed, day, residents, places, Travel, tuning, DaySeconds);

        private static float Travel(int from, int to) => Mathf.Abs(from - to);

        private static SpotUse Use(SpotRole role)
        {
            var use = ScriptableObject.CreateInstance<SpotUse>();
            use.role = role;
            return use;
        }
    }
}
