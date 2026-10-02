// Builds every resident's DayPlan for one day from settlement data only — roster rows, place rows, walking
// times and seed ⊕ day — so reload, stream-in, late join and a time jump are all "rebuild, then At(now)".
// Seats and coverage are solved here, once: a place (doors and trip points excepted) is booked for the
// whole block it is planned for, so no two residents ever share one; an always-manned post staggers its
// workers' breaks so their absences never overlap; one worker per night-manned post takes the night,
// which belongs to the day it starts on. Arrivals are anchored to the clock and departures pay the walk.
// Free time goes, when it can, to a circle a friend is already planned into: friends meet at the fire.
// Three things are NOT booked, because any number of residents may do them at once: a chore (the rounds of
// a ChoreDefinition, run on the spot by the errand runner; the plan only says when and roughly where), an
// amble (wandering with no seat) and a patrol (the perimeter ring). Companions share a seed, so a pair's days
// draw the same numbers and stay in step.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents
{
    /// <summary>
    /// One resident as the planner sees it. <c>friends</c> are the indices it likes to spend free time near;
    /// <c>shareSeed</c> (0 = none) is a companion's seed, drawn from instead of its own so a pair's days match;
    /// <c>slot</c> of <c>slots</c> is the patrol pair it walks with, so pairs start spread round the ring.
    /// </summary>
    public struct PlannerResident
    {
        public int index, homeIndex, seed, shareSeed, slot, slots;
        public Lifestyle lifestyle;
        public SpotUse post;
        public TripKind trips;
        public ChoreDefinition chore;
        public ResidentDuty duty;
        public bool paired, dead;
        public float bedtimeOffset;
        public int[] friends;

        internal int PlanSeed => shareSeed != 0 ? shareSeed : seed;
    }
    /// <summary>One place as the planner sees it. <c>group</c> is its circle (0 = none): friends are planned into one together.</summary>
    public struct PlannerPlace { public int index; public PlaceKind kind; public SpotUse post; public int seatIndex; public int group; }

    public static class DayPlanner
    {
        public const float MinutesPerDay = 1440f;
        private const float MinutesPerHour = 60f;
        private const int DaySalt = 1, WakeSalt = 2;

        /// <param name="daySeconds">Real seconds per game day for the min-dwell conversion; 0 = the live cycle.</param>
        public static DayPlan[] BuildAll(int seed, int day, IReadOnlyList<PlannerResident> residents, IReadOnlyList<PlannerPlace> places,
                                         Func<int, int, float> travelMinutes, ResidentTuning tuning, float daySeconds = 0f)
        {
            float secondsPerMinute = daySeconds > 0f ? daySeconds / MinutesPerDay : tuning.GameMinutesToSeconds(1f);
            var builder = new Builder(seed, day, places, travelMinutes, tuning, tuning.minDwellRealSeconds / secondsPerMinute);
            HashSet<int> tonight = builder.NightWorkers(day, residents), lastNight = builder.NightWorkers(day - 1, residents);
            builder.BookNights(residents, tonight);
            var order = new List<int>(residents.Count);
            for (int i = 0; i < residents.Count; i++) order.Add(i);
            order.Sort((a, b) => residents[a].index.CompareTo(residents[b].index));
            var plans = new DayPlan[residents.Count];
            foreach (int i in order)
                plans[i] = builder.Plan(residents[i], tonight.Contains(residents[i].index), lastNight.Contains(residents[i].index));
            return plans;
        }

        /// <summary>When a resident who slept at home leaves the door on <paramref name="day"/>.</summary>
        public static float Wake(int seed, int day, int residentSeed, ResidentTuning t) =>
            day * MinutesPerDay + Range(t.wake, new System.Random(Hash(seed, day, residentSeed, WakeSalt)));

        /// <summary>When the night shift that starts on <paramref name="day"/> hands over, the next morning.</summary>
        public static float NightEnd(int day, ResidentTuning t) => (day + 1) * MinutesPerDay + t.wake.y;

        internal static float Range(Vector2 range, System.Random rng) => range.x + (range.y - range.x) * (float)rng.NextDouble();

        /// <summary>Adds a segment arriving at <paramref name="arrive"/>; the previous one leaves when the walk must start.</summary>
        internal static float Append(List<PlanSegment> segments, Activity activity, int place, float arrive, Func<int, int, float> travel)
        {
            PlanSegment previous = segments[segments.Count - 1];
            float walk = travel(previous.place, place);
            float depart = Math.Max(arrive - walk, previous.arrive);
            previous.leave = depart;
            segments[segments.Count - 1] = previous;
            segments.Add(new PlanSegment { depart = depart, arrive = depart + walk, leave = depart + walk, activity = activity, place = place });
            return depart + walk;
        }

        private static int Hash(int seed, int day, int residentSeed, int salt)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)seed) * 16777619u;
                h = (h ^ (uint)day) * 16777619u;
                h = (h ^ (uint)residentSeed) * 16777619u;
                h = (h ^ (uint)salt) * 16777619u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
                return (int)h;
            }
        }

        private sealed class Builder
        {
            private readonly int seed, day;
            private readonly float dayStart, minDwell;
            private readonly IReadOnlyList<PlannerPlace> places;
            private readonly Func<int, int, float> travel;
            private readonly ResidentTuning t;
            private readonly Dictionary<int, PlaceKind> kinds = new();
            private readonly Dictionary<int, int> groups = new();
            private readonly Dictionary<int, List<(float from, float to, int owner)>> booked = new();
            private readonly Dictionary<SpotUse, List<(float from, float to)>> absences = new();
            private readonly Dictionary<SpotUse, List<int>> seats = new();
            private readonly List<int> hearths = new(), strolls = new(), fillers = new(), patrolPoints = new();
            private readonly Dictionary<SpotUse, List<int>> errands = new();
            private PlannerResident r;
            private System.Random rng;
            private List<PlanSegment> segs;

            public Builder(int seed, int day, IReadOnlyList<PlannerPlace> places, Func<int, int, float> travel, ResidentTuning tuning, float minDwell)
            {
                (this.seed, this.day, this.places, this.travel, t, this.minDwell) = (seed, day, places, travel, tuning, minDwell);
                dayStart = day * MinutesPerDay;
                var postSeats = new List<PlannerPlace>();
                foreach (PlannerPlace place in places)
                {
                    kinds[place.index] = place.kind;
                    groups[place.index] = place.group;
                    if (place.kind == PlaceKind.Hearth) hearths.Add(place.index);
                    else if (place.kind == PlaceKind.Stroll) strolls.Add(place.index);
                    else if (place.kind == PlaceKind.Post && place.post != null) postSeats.Add(place);
                    else if (place.kind == PlaceKind.Patrol) patrolPoints.Add(place.index);
                    else if (place.kind == PlaceKind.Errand && place.post != null)
                    {
                        if (!errands.TryGetValue(place.post, out List<int> stops)) errands[place.post] = stops = new List<int>();
                        stops.Add(place.index);
                    }
                }
                postSeats.Sort((a, b) => a.seatIndex != b.seatIndex ? a.seatIndex.CompareTo(b.seatIndex) : a.index.CompareTo(b.index));
                foreach (PlannerPlace seat in postSeats)
                {
                    if (!seats.TryGetValue(seat.post, out List<int> list)) seats[seat.post] = list = new List<int>();
                    list.Add(seat.index);
                }
                fillers.AddRange(strolls);
                fillers.AddRange(hearths);
            }

            /// <summary>One living worker per seated night-manned post, rotating through the workers by day.</summary>
            public HashSet<int> NightWorkers(int forDay, IReadOnlyList<PlannerResident> residents)
            {
                var workers = new Dictionary<SpotUse, List<int>>();
                foreach (PlannerResident resident in residents)
                {
                    if (resident.dead || resident.lifestyle != Lifestyle.Stationed || resident.post == null || !resident.post.nightManned || !seats.ContainsKey(resident.post)) continue;
                    if (!workers.TryGetValue(resident.post, out List<int> list)) workers[resident.post] = list = new List<int>();
                    list.Add(resident.index);
                }
                var chosen = new HashSet<int>();
                foreach (List<int> list in workers.Values)
                {
                    list.Sort();
                    chosen.Add(list[((forDay % list.Count) + list.Count) % list.Count]);
                }
                return chosen;
            }

            /// <summary>Books tonight's seats before anyone is planned, so no evening can run into them.</summary>
            public void BookNights(IReadOnlyList<PlannerResident> residents, HashSet<int> tonight)
            {
                foreach (PlannerResident resident in residents)
                    if (tonight.Contains(resident.index))
                        Book(seats[resident.post][0], dayStart + t.bedtimeSpread.x, NightEnd(day, t), resident.index);
            }

            public DayPlan Plan(PlannerResident resident, bool nightTonight, bool workedLastNight)
            {
                var plan = new DayPlan { residentIndex = resident.index, day = day };
                if (resident.dead) return plan;
                (r, segs, rng) = (resident, plan.segments, new System.Random(Hash(seed, day, resident.PlanSeed, DaySalt)));
                float start = workedLastNight ? NightEnd(day - 1, t) : Wake(seed, day, resident.PlanSeed, t);
                segs.Add(new PlanSegment { depart = start, arrive = start, leave = start, activity = workedLastNight ? Activity.Work : Activity.Sleep,
                                           place = workedLastNight ? seats[resident.post][0] : resident.homeIndex });
                float cursor = start;
                if (workedLastNight)
                {
                    DayPlanner.Append(segs, Activity.Sleep, resident.homeIndex, start, travel);
                    cursor = start + MinutesPerDay - (t.bedtimeSpread.x - t.wake.y);   // a normal night's length
                }
                float end = nightTonight ? dayStart + t.bedtimeSpread.x
                    : dayStart + Mathf.Clamp(Range(t.bedtimeSpread, rng) + resident.bedtimeOffset, t.bedtimeSpread.x, t.bedtimeSpread.y);
                float shift = ((float)rng.NextDouble() * 2f - 1f) * t.windowJitter;
                DayBlock[] blocks = (DayBlock[])(t.TemplateFor(resident.lifestyle)?.blocks?.Clone() ?? Array.Empty<DayBlock>());
                Array.Sort(blocks, (a, b) => a.startHour.CompareTo(b.startHour));
                foreach (DayBlock block in blocks)
                {
                    if (block.activity == Activity.Sleep) continue;   // wake and bedtime come from the tuning, per resident
                    float from = Math.Max(cursor, dayStart + block.startHour * MinutesPerHour + shift);
                    float to = Math.Min(end, dayStart + block.endHour * MinutesPerHour + shift);
                    if (block.activity == Activity.Work)   // clear of the night shift on either side
                        (from, to) = (Math.Max(from, dayStart + t.wake.y), Math.Min(to, dayStart + t.bedtimeSpread.x));
                    if (to - from < minDwell) continue;
                    if (from - cursor >= minDwell) Strolls(cursor, from);
                    else from = cursor;
                    cursor = Expand(block.activity, from, to, nightTonight);
                }
                if (end - cursor >= minDwell) Strolls(cursor, end);
                if (nightTonight) Night(Math.Max(end, cursor));
                else
                {
                    DayPlanner.Append(segs, Activity.Sleep, resident.homeIndex, Math.Max(end, cursor), travel);
                    Close(Wake(seed, day + 1, resident.PlanSeed, t));
                }
                MergeShort();
                return plan;
            }

            private PlanSegment Last => segs[segs.Count - 1];

            private void Close(float leave)
            {
                PlanSegment last = Last;
                last.leave = Math.Max(leave, last.arrive);
                segs[segs.Count - 1] = last;
            }

            private float Expand(Activity activity, float from, float to, bool nightTonight)
            {
                if (activity == Activity.Work && r.duty == ResidentDuty.Patrol && !nightTonight) PatrolBlock(from, to);
                else if (activity == Activity.Work && r.lifestyle == Lifestyle.Stationed && r.post != null && !nightTonight) WorkBlock(from, to);
                else if (activity == Activity.Stroll && r.post == null && r.duty == ResidentDuty.None && ChoreAnchor() >= 0) ChoreBlock(from, to);
                else if (activity == Activity.Hearth) Single(Activity.Hearth, hearths, from, to);
                else if (activity == Activity.Trip && r.lifestyle == Lifestyle.Outrider) return Trip(from, to);
                else Strolls(from, to);
                return to;
            }

            private void Night(float from)
            {
                DayPlanner.Append(segs, Activity.Work, seats[r.post][0], from, travel);
                Close(NightEnd(day, t));
            }

            // The seat is held for the whole block (a worker on a break keeps it); breaks at an always-manned
            // post are pushed later until this worker's absence overlaps no other worker's.
            private void WorkBlock(float from, float to)
            {
                int seat = seats.TryGetValue(r.post, out List<int> postSeats) ? Choose(postSeats, from, to, -1, false) : -1;
                if (seat < 0) { Strolls(from, to); return; }
                Book(seat, from, to, r.index);
                if (!absences.TryGetValue(r.post, out List<(float from, float to)> away)) absences[r.post] = away = new List<(float, float)>();
                int choreAt = ChoreAnchor();   // an errand takes the place of the break: out and about, then back to the seat
                float clock = from;
                while (true)
                {
                    float arrived = DayPlanner.Append(segs, Activity.Work, seat, clock, travel);
                    if (r.post.elevated) return;   // up a ladder: no walking down for a break
                    float breakStart = Math.Max(arrived, clock) + Range(t.workSession, rng), length = Range(choreAt >= 0 ? t.choreSession : t.breakLength, rng);
                    int spot = -1;
                    for (int attempt = 0; attempt <= away.Count && spot < 0; attempt++)
                    {
                        if (breakStart + length + t.workSession.x > to) return;
                        spot = choreAt >= 0 ? choreAt : Choose(fillers, breakStart, breakStart + length, seat, true);
                        if (spot < 0) return;
                        float leaveSeat = breakStart - travel(seat, spot), clearAt = leaveSeat;
                        if (r.post.alwaysManned)
                            foreach ((float awayFrom, float awayTo) in away)
                                if (leaveSeat < awayTo && awayFrom < breakStart + length) clearAt = Math.Max(clearAt, awayTo);
                        if (clearAt > leaveSeat) { breakStart = clearAt + travel(seat, spot); spot = -1; continue; }
                        if (r.post.alwaysManned) away.Add((leaveSeat, breakStart + length));
                    }
                    if (spot < 0) return;
                    Book(spot, breakStart, breakStart + length, r.index);
                    DayPlanner.Append(segs, choreAt >= 0 ? Activity.Chore : Activity.Break, spot, breakStart, travel);
                    clock = breakStart + length;
                }
            }

            // The place a chore's first round starts from (a source spot picked by index), or -1 when this resident has
            // no chore or the settlement lacks the places for it: both ends, and two different ones when they are one use.
            private int ChoreAnchor()
            {
                ChoreDefinition chore = r.chore;
                if (chore == null || !chore.IsComplete || !errands.TryGetValue(chore.source, out List<int> sources) || sources.Count == 0) return -1;
                if (!errands.TryGetValue(chore.target, out List<int> targets) || targets.Count == 0) return -1;
                if (chore.source == chore.target && targets.Count < 2) return -1;
                return sources[r.index % sources.Count];
            }

            // Rounds of an errand for a resident with no post, each followed by a rest, until the block ends.
            private void ChoreBlock(float from, float to)
            {
                int anchor = ChoreAnchor();
                float clock = from;
                while (to - clock >= minDwell)
                {
                    float until = Math.Min(to, clock + Range(t.choreSession, rng));
                    if (to - until < minDwell) until = to;
                    float arrived = DayPlanner.Append(segs, Activity.Chore, anchor, clock, travel);
                    clock = Math.Max(until, arrived + minDwell);
                    float rest = Math.Min(to, clock + Range(t.breakLength, rng));
                    if (to - rest < minDwell) break;
                    Strolls(clock, rest);
                    clock = Math.Max(rest, Last.arrive + minDwell);
                }
            }

            // One segment for the whole shift: the runner walks the ring from the point this pair starts at.
            private void PatrolBlock(float from, float to)
            {
                if (patrolPoints.Count == 0) { Strolls(from, to); return; }

                int slots = Math.Max(1, r.slots), at = Math.Min(patrolPoints.Count - 1, r.slot * patrolPoints.Count / slots);
                DayPlanner.Append(segs, Activity.Patrol, patrolPoints[at], from, travel);
            }

            private float Trip(float from, float to)
            {
                DayPlanner.Append(segs, Activity.Break, r.homeIndex, from, travel);
                int before = segs.Count;
                TripPlanner.AddTrip(segs, r, day, rng, places, travel, t, minDwell);
                if (segs.Count > before) return Math.Max(to, Last.arrive + minDwell);
                segs.RemoveAt(before - 1);
                Strolls(from, to);
                return to;
            }

            private void Single(Activity activity, List<int> candidates, float from, float to)
            {
                int place = Choose(candidates, from, to, -1, false);
                if (place < 0) { Strolls(from, to); return; }
                Book(place, from, to, r.index);
                DayPlanner.Append(segs, activity, place, from, travel);
            }

            private void Strolls(float from, float to)
            {
                for (float clock = from; to - clock >= minDwell;)
                {
                    float until = Math.Min(to, clock + Range(t.breakLength, rng));
                    if (to - until < minDwell) until = to;
                    bool amble = rng.NextDouble() < (r.paired ? 1f : t.ambleChance) && fillers.Count > 0;
                    int place = amble ? fillers[rng.Next(fillers.Count)] : Choose(fillers, clock, until, -1, false);
                    float arrived;
                    if (amble) arrived = DayPlanner.Append(segs, Activity.Amble, place, clock, travel);
                    else if (place < 0) arrived = DayPlanner.Append(segs, Activity.Break, r.homeIndex, clock, travel);   // nothing free: wait at the door
                    else
                    {
                        Book(place, clock, until, r.index);
                        arrived = DayPlanner.Append(segs, kinds[place] == PlaceKind.Hearth ? Activity.Hearth : Activity.Stroll, place, clock, travel);
                    }
                    // A walk longer than its slot arrives late; the next slot starts once this visit has had its
                    // dwell. Advancing by the slot alone let lateness pile up stroll after stroll until bedtime
                    // fell past dawn, and the merge pass then folded the whole day into one walk home.
                    clock = Math.Max(until, arrived + minDwell);
                }
            }

            // A free candidate, starting at a seeded offset, that leaves the current segment its minimum dwell
            // (and, given a seat to return to, keeps its own) — one in a friend's circle first, if any fits;
            // `strict` refuses a candidate that only fits the booking.
            private int Choose(List<int> candidates, float from, float to, int returnTo, bool strict)
            {
                if (candidates.Count == 0) return -1;
                int offset = rng.Next(candidates.Count), first = -1, withFriend = -1, fallback = -1;
                PlanSegment previous = Last;
                for (int k = 0; k < candidates.Count && withFriend < 0; k++)
                {
                    int place = candidates[(offset + k) % candidates.Count];
                    if (place == previous.place || !IsFree(place, from, to)) continue;
                    bool fits = (segs.Count == 1 || from - travel(previous.place, place) - previous.arrive >= minDwell)
                                && (returnTo < 0 || to - from - travel(place, returnTo) >= minDwell);
                    if (!fits) { if (fallback < 0) fallback = place; continue; }
                    if (first < 0) first = place;
                    if (FriendInCircle(place, from, to)) withFriend = place;
                }
                if (withFriend >= 0) return withFriend;
                if (first >= 0) return first;
                return strict ? -1 : fallback;
            }

            // Another place in this one's circle is booked by a friend for part of [from, to).
            private bool FriendInCircle(int place, float from, float to)
            {
                if (r.friends == null || r.friends.Length == 0 || !groups.TryGetValue(place, out int group) || group == 0) return false;
                foreach (KeyValuePair<int, List<(float from, float to, int owner)>> other in booked)
                {
                    if (other.Key == place || !groups.TryGetValue(other.Key, out int otherGroup) || otherGroup != group) continue;
                    foreach ((float bookedFrom, float bookedTo, int owner) in other.Value)
                        if (from < bookedTo && bookedFrom < to && Array.IndexOf(r.friends, owner) >= 0) return true;
                }
                return false;
            }

            private bool IsFree(int place, float from, float to)
            {
                if (!booked.TryGetValue(place, out var list)) return true;
                foreach ((float bookedFrom, float bookedTo, int owner) in list)
                    if (owner != r.index && from < bookedTo && bookedFrom < to) return false;
                return true;
            }

            private void Book(int place, float from, float to, int owner)
            {
                if (to <= from || !kinds.TryGetValue(place, out PlaceKind kind) ||
                    kind is PlaceKind.Door or PlaceKind.Trip or PlaceKind.Camp or PlaceKind.Errand or PlaceKind.Patrol) return;
                if (!booked.TryGetValue(place, out var list)) booked[place] = list = new List<(float, float, int)>();
                list.Add((from, to, owner));
            }

            // A segment too short to read as a visit is merged: the previous one stays longer when its place is
            // still free (never a trip leg — that would break "home by returnBy"), else the next walk starts early.
            private void MergeShort()
            {
                for (int i = 0; i < segs.Count - 1;)
                {
                    PlanSegment brief = segs[i], next = segs[i + 1];
                    if (brief.Dwell >= minDwell) { i++; continue; }
                    if (i > 0 && segs[i - 1].activity != Activity.Trip)
                    {
                        PlanSegment previous = segs[i - 1];
                        float depart = next.arrive - travel(previous.place, next.place);
                        if (depart >= previous.arrive && IsFree(previous.place, previous.leave, depart))
                        {
                            Book(previous.place, previous.leave, depart, r.index);
                            previous.leave = next.depart = depart;
                            if (previous.place == next.place && previous.activity == next.activity) { previous.leave = next.leave; segs.RemoveAt(i + 1); }
                            else segs[i + 1] = next;
                            segs[i - 1] = previous;
                            segs.RemoveAt(i);
                            continue;
                        }
                    }
                    next.depart = brief.depart;
                    segs[i + 1] = next;
                    segs.RemoveAt(i);
                }
            }
        }
    }
}
