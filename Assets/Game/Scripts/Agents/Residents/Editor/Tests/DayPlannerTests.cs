// The day planner's promises, checked on a small settlement over several days: plans are a pure function
// of seed and day, no seat is ever double-booked, an always-manned post is never empty inside its work
// window, every walk pays its travel time, nothing is shorter than the minimum dwell, a night-manned post
// keeps exactly one worker past midnight, today hands over seamlessly from yesterday, trips are home
// by returnBy, and friends meet in one circle more than strangers would.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents.Tests
{
    public class DayPlannerTests
    {
        private const int Seed = 1234, Today = 5, DaysChecked = 8;
        private const float DaySeconds = 1600f, Epsilon = 1e-2f;
        private const int FirstStallSeat = 4, StallSeats = 3, GateSeat = 7, FirstStroll = 15, FirstTrip = 25, OutriderIndex = 9, DeadIndex = 10;
        private const int RoamerA = 6, RoamerB = 7;

        private ResidentTuning tuning;
        private SpotUse stall, gate, forge;
        private List<PlannerPlace> places;
        private List<PlannerResident> residents;

        [SetUp]
        public void SetUp()
        {
            tuning = ScriptableObject.CreateInstance<ResidentTuning>();
            tuning.ambleChance = 0f;   // these promises are about booked seats; ErrandTests covers ambling
            stall = Post(alwaysManned: true, nightManned: false);
            gate = Post(alwaysManned: true, nightManned: true);
            forge = Post(alwaysManned: false, nightManned: false);

            places = new List<PlannerPlace>();
            for (int i = 0; i < 28; i++)
            {
                PlaceKind kind = i < 4 ? PlaceKind.Door : i < 9 ? PlaceKind.Post : i < FirstStroll ? PlaceKind.Hearth : i < FirstTrip ? PlaceKind.Stroll : PlaceKind.Trip;
                SpotUse post = i >= FirstStallSeat && i < FirstStallSeat + StallSeats ? stall : i == GateSeat ? gate : i == 8 ? forge : null;
                places.Add(new PlannerPlace { index = i, kind = kind, post = post, seatIndex = post == stall ? i - FirstStallSeat : 0 });
            }

            residents = new List<PlannerResident>
            {
                Worker(0, 0, stall), Worker(1, 1, stall), Worker(2, 2, stall), Worker(3, 3, gate), Worker(4, 0, gate), Worker(5, 1, forge),
                Person(6, 2, Lifestyle.Roamer), Person(7, 3, Lifestyle.Roamer), Person(8, 0, Lifestyle.Roamer),
                new PlannerResident { index = OutriderIndex, homeIndex = 1, seed = 909, lifestyle = Lifestyle.Outrider, trips = TripKind.Hunt | TripKind.Scout },
                new PlannerResident { index = DeadIndex, homeIndex = 2, seed = 1010, lifestyle = Lifestyle.Roamer, dead = true },
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(tuning);
            Object.DestroyImmediate(stall);
            Object.DestroyImmediate(gate);
            Object.DestroyImmediate(forge);
        }

        [Test]
        public void SameSeedAndDay_BuildsIdenticalPlans()
        {
            DayPlan[] first = Build(Today), second = Build(Today);
            for (int i = 0; i < first.Length; i++)
                CollectionAssert.AreEqual(first[i].segments, second[i].segments, $"resident {first[i].residentIndex}");
        }

        [Test]
        public void NoSeat_IsEverDoubleBooked()
        {
            for (int day = 0; day < DaysChecked; day++)
            {
                var visits = new List<(int resident, PlanSegment segment)>();
                foreach (DayPlan plan in Build(day))
                    foreach (PlanSegment segment in plan.segments)
                        if (places[segment.place].kind != PlaceKind.Door && places[segment.place].kind != PlaceKind.Trip) visits.Add((plan.residentIndex, segment));
                foreach (var a in visits)
                    foreach (var b in visits)
                        if (a.resident != b.resident && a.segment.place == b.segment.place)
                            Assert.IsFalse(a.segment.arrive < b.segment.leave - Epsilon && b.segment.arrive < a.segment.leave - Epsilon,
                                           $"day {day}: residents {a.resident} and {b.resident} share place {a.segment.place}");
            }
        }

        [Test]
        public void AlwaysMannedPost_IsNeverEmpty_InsideItsWorkWindow()
        {
            for (int day = 0; day < DaysChecked; day++)
            {
                var shifts = new List<List<PlanSegment>>();
                foreach (DayPlan plan in Build(day))
                {
                    List<PlanSegment> work = plan.segments.FindAll(s => s.activity == Activity.Work && s.place >= FirstStallSeat && s.place < FirstStallSeat + StallSeats);
                    if (work.Count > 0) shifts.Add(work);
                }
                Assert.GreaterOrEqual(shifts.Count, 2, $"day {day}: the stall needs two workers for the rule to apply");

                float windowStart = float.MinValue, windowEnd = float.MaxValue;
                foreach (List<PlanSegment> work in shifts)
                    (windowStart, windowEnd) = (Mathf.Max(windowStart, work[0].arrive), Mathf.Min(windowEnd, work[work.Count - 1].leave));
                Assert.Greater(windowEnd - windowStart, 60f, $"day {day}: work window");

                for (float minute = windowStart; minute < windowEnd; minute += 1f)
                    Assert.IsTrue(shifts.Exists(work => work.Exists(s => minute >= s.arrive && minute < s.leave)), $"day {day}: stall empty at {minute}");
            }
        }

        [Test]
        public void EveryWalk_IncludesTheTravelTime()
        {
            foreach (DayPlan plan in Build(Today))
                for (int i = 1; i < plan.segments.Count; i++)
                {
                    PlanSegment previous = plan.segments[i - 1], segment = plan.segments[i];
                    Assert.AreEqual(previous.leave, segment.depart, Epsilon, "segments are contiguous");
                    Assert.GreaterOrEqual(segment.arrive - segment.depart, Travel(previous.place, segment.place) - Epsilon);
                }
        }

        [Test]
        public void EverySegment_DwellsAtLeastTheMinimum()
        {
            float minDwell = tuning.minDwellRealSeconds * DayPlanner.MinutesPerDay / DaySeconds;
            for (int day = 0; day < DaysChecked; day++)
                foreach (DayPlan plan in Build(day))
                    foreach (PlanSegment segment in plan.segments)
                        Assert.GreaterOrEqual(segment.Dwell, minDwell - Epsilon, $"day {day}, resident {plan.residentIndex}, {segment.activity}");
        }

        [Test]
        public void NightMannedPost_KeepsExactlyOneWorker_PastMidnight()
        {
            float midnight = (Today + 1) * DayPlanner.MinutesPerDay;
            int nightWorkers = 0;
            foreach (DayPlan plan in Build(Today))
                if (plan.segments.Exists(s => s.activity == Activity.Work && s.place == GateSeat && s.arrive < midnight && s.leave > midnight)) nightWorkers++;
            Assert.AreEqual(1, nightWorkers);
        }

        [Test]
        public void BeforeTodaysFirstSegment_AtIsFalse_AndYesterdayCoversTheGap()
        {
            DayPlan[] today = Build(Today), yesterday = Build(Today - 1);
            for (int i = 0; i < today.Length; i++)
            {
                if (today[i].residentIndex == DeadIndex) continue;
                float start = today[i].FirstStart;
                Assert.IsFalse(today[i].At(start - 1f, out _), $"resident {today[i].residentIndex}");
                Assert.IsTrue(today[i].At(start, out _));
                Assert.AreEqual(start, yesterday[i].LastLeave, Epsilon, "yesterday's night ends where today starts");
                Assert.IsTrue(yesterday[i].At(start - 1f, out _));
            }
        }

        [Test]
        public void Outrider_IsHomeByReturnBy()
        {
            int tripDays = 0;
            for (int day = 0; day < DaysChecked; day++)
            {
                DayPlan plan = Build(day)[OutriderIndex];
                int last = plan.segments.FindLastIndex(s => s.activity == Activity.Trip);
                if (last < 0) continue;
                tripDays++;
                PlanSegment leg = plan.segments[last];
                Assert.LessOrEqual(leg.leave + Travel(leg.place, 1), day * DayPlanner.MinutesPerDay + tuning.returnBy + Epsilon, $"day {day}");
            }
            Assert.Greater(tripDays, 0, "no day had a trip");
        }

        [Test]
        public void TripPlanner_ChainsOneLegPerTripKind_AndReturnsByReturnBy()
        {
            tuning.departWindow = new Vector2(7f * 60f, 7f * 60f);
            float dayStart = Today * DayPlanner.MinutesPerDay;
            var segments = new List<PlanSegment> { new PlanSegment { depart = dayStart, arrive = dayStart, leave = dayStart, activity = Activity.Break, place = 1 } };
            TripPlanner.AddTrip(segments, residents[OutriderIndex], Today, new System.Random(Seed), places, Travel, tuning, 18f);

            Assert.AreEqual(2, segments.FindAll(s => s.activity == Activity.Trip).Count);
            PlanSegment home = segments[segments.Count - 1];
            Assert.AreEqual(Activity.Break, home.activity);
            Assert.AreEqual(1, home.place);
            Assert.LessOrEqual(home.arrive, dayStart + tuning.returnBy + Epsilon);
            Assert.AreEqual(dayStart + 7f * 60f, segments[1].depart, Epsilon, "departs inside the window");
        }

        [Test]
        public void WalksLongerThanTheirSlots_NeverPushBedtimePastTheLongestWalk()
        {
            for (int day = 0; day < DaysChecked; day++)
            foreach (DayPlan plan in DayPlanner.BuildAll(Seed, day, residents, places, LongWalk, tuning, DaySeconds))
            {
                if (plan.segments.Count == 0) continue;
                for (int i = 1; i < plan.segments.Count; i++)
                    Assert.LessOrEqual(plan.segments[i - 1].arrive, plan.segments[i].depart + Epsilon, $"day {day}, resident {plan.residentIndex}, segment {i}");
                PlanSegment night = plan.segments.FindLast(s => s.activity == Activity.Sleep);
                if (night.activity != Activity.Sleep) continue;
                Assert.LessOrEqual(night.arrive, day * DayPlanner.MinutesPerDay + tuning.bedtimeSpread.y + 2f * LongWalkMinutes + Epsilon,
                    $"day {day}, resident {plan.residentIndex} gets home at {night.arrive}");
            }
        }

        [Test]
        public void DeadResident_HasAnEmptyPlan()
        {
            DayPlan plan = Build(Today)[DeadIndex];
            Assert.IsEmpty(plan.segments);
            Assert.IsFalse(plan.At(Today * DayPlanner.MinutesPerDay + 600f, out _));
        }

        [Test]
        public void Friends_SpendMoreFreeTimeInOneCircle_ThanStrangersWould()
        {
            for (int i = 0; i < places.Count; i++)
            {
                PlannerPlace place = places[i];
                if (place.kind == PlaceKind.Stroll) place.group = 1 + (i - FirstStroll) / 2;   // five two-seat circles
                places[i] = place;
            }
            float strangers = SharedCircleMinutes(RoamerA, RoamerB);

            residents[RoamerA] = Befriend(residents[RoamerA], RoamerB);
            residents[RoamerB] = Befriend(residents[RoamerB], RoamerA);
            float friends = SharedCircleMinutes(RoamerA, RoamerB);

            Assert.Greater(friends, strangers, "friends are planned into the circle a friend already sits in");
        }

        private DayPlan[] Build(int day) => DayPlanner.BuildAll(Seed, day, residents, places, Travel, tuning, DaySeconds);

        // Minutes, over DaysChecked days, that both are at places of one circle at once.
        private float SharedCircleMinutes(int a, int b)
        {
            float shared = 0f;
            for (int day = 0; day < DaysChecked; day++)
            {
                DayPlan[] plans = Build(day);
                foreach (PlanSegment mine in plans[a].segments)
                    foreach (PlanSegment theirs in plans[b].segments)
                    {
                        int circle = places[mine.place].group;
                        if (circle == 0 || circle != places[theirs.place].group) continue;
                        shared += Mathf.Max(0f, Mathf.Min(mine.leave, theirs.leave) - Mathf.Max(mine.arrive, theirs.arrive));
                    }
            }
            return shared;
        }

        private static PlannerResident Befriend(PlannerResident resident, int friend)
        {
            resident.friends = new[] { friend };
            return resident;
        }

        // Settlement places sit a game minute apart on a line; trip points lie well outside. A metric, so
        // the triangle inequality holds as it does for baked NavMesh path times.
        private static float Travel(int from, int to) => Mathf.Abs(Position(from) - Position(to));

        // Every walk longer than a stroll slot, as across a real settlement on a 1600 s day.
        private const float LongWalkMinutes = 60f;

        private static float LongWalk(int from, int to) => from == to ? 0f : LongWalkMinutes;

        private static float Position(int place) => place < FirstTrip ? place : 40f + (place - FirstTrip) * 10f;

        private static SpotUse Post(bool alwaysManned, bool nightManned)
        {
            var post = ScriptableObject.CreateInstance<SpotUse>();
            (post.alwaysManned, post.nightManned) = (alwaysManned, nightManned);
            return post;
        }

        private static PlannerResident Worker(int index, int home, SpotUse post) =>
            new PlannerResident { index = index, homeIndex = home, seed = 100 + index * 7, lifestyle = Lifestyle.Stationed, post = post };

        private static PlannerResident Person(int index, int home, Lifestyle lifestyle) =>
            new PlannerResident { index = index, homeIndex = home, seed = 100 + index * 7, lifestyle = lifestyle };
    }
}
