// The pure rules of settlement expeditions: plain values in, plain values out, no scene and no clock, so the
// same inputs give the same answer on every machine and in a test. Anything random takes a seed and draws
// from System.Random, never UnityEngine.Random.
//
// The warrior quota (spec §3.3): a large settlement keeps warriorQuotaLarge; a small one keeps between the
// two ends of warriorQuotaSmall, more the more beds it has, so a settlement one bed short of large keeps the
// most a small one may. A role quota (the profile's roleQuotas) counts the same way. Residents are recast to
// reach a quota boldest first: the nerve a warrior archetype asks for is high, so the highest nerve falls the
// least short of it, and a band's other members walk the same road.
//
// A newly generated settlement keeps its quotas from the start: of the character copies rolled for it, those certain
// to have a quota's role move in first (PlanMoveIn), then the rest fill the beds in the same seeded order as before.
//
// Every rule that reads an asset (a goal, the tuning) is a thin overload over a core that takes plain
// values; the cores are what the tests drive. Times are DayNightCycle.GameMinutesNow readings: game minutes
// since midnight of day 0, never wrapping, so the day is minutes / 1440 and the hour is what is left over.
using System;
using System.Collections.Generic;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents.Expeditions
{
    /// <summary>One goal as the draw sees it.</summary>
    public readonly struct GoalOption
    {
        public readonly string id;
        public readonly float weight;
        /// <summary>The goal can run right now (its site is known, its warriors can be spared).</summary>
        public readonly bool possible;

        public GoalOption(string id, float weight, bool possible) => (this.id, this.weight, this.possible) = (id, weight, possible);
    }

    /// <summary>What the director does for a band after <see cref="ExpeditionRules.Advance(ExpeditionRecord, double, double, bool, float)"/>.</summary>
    public enum StageStep
    {
        /// <summary>Nothing changed: keep walking to the current target, or keep holding.</summary>
        Continue,
        /// <summary>A Travel or Search began, or a Search reached a waypoint: choose the next target and walk to it.</summary>
        SetTarget,
        /// <summary>A Halt began: stand where the band is until it ends.</summary>
        Hold,
        /// <summary>ReturnHome began: walk to the record's hand-off point.</summary>
        Home,
        /// <summary>The last stage ended: the band is back at the hand-off point.</summary>
        Arrive,
    }

    /// <summary>One resident as a quota (warriors, or a role quota) sees it.</summary>
    public readonly struct RecruitCandidate
    {
        /// <summary>Its place in the settlement's roster (<c>Resident.index</c>).</summary>
        public readonly int residentIndex;
        /// <summary>Its nerve now, from its current archetype or its own override.</summary>
        public readonly float nerve;
        public readonly bool isAdult;
        /// <summary>Its body suits an archetype with the quota's role that it could be given here.</summary>
        public readonly bool suitsRole;
        /// <summary>It has the quota's role already, or a role it is never recast from (a warrior stays a warrior).</summary>
        public readonly bool holdsRole;

        public RecruitCandidate(int residentIndex, float nerve, bool isAdult, bool suitsRole, bool holdsRole) =>
            (this.residentIndex, this.nerve, this.isAdult, this.suitsRole, this.holdsRole) =
            (residentIndex, nerve, isAdult, suitsRole, holdsRole);

        /// <summary>Can be recast for the quota: an adult whose body suits the role, and who holds neither it nor one it is never recast from.</summary>
        public bool Eligible => isAdult && suitsRole && !holdsRole;
    }

    /// <summary>One rolled character copy as the move-in sees it.</summary>
    public readonly struct MoveInCandidate
    {
        /// <summary>The roles it is certain to have once it lives here; None when which archetype it is dealt is not certain.</summary>
        public readonly ExpeditionRole roles;
        /// <summary>Its prefab is made for one role, so its roles are certain by construction: taken first for a quota.</summary>
        public readonly bool fixedRole;

        public MoveInCandidate(ExpeditionRole roles, bool fixedRole) => (this.roles, this.fixedRole) = (roles, fixedRole);
    }

    public static class ExpeditionRules
    {
        // One salt per kind of roll, so the goal draw, the picks, the stages and the ring drawn from one band
        // seed are not the same numbers.
        private const int GoalSalt = 1, PickSalt = 2, StageSalt = 3, RingSalt = 4, TravelSalt = 5;

        private const double MinutesPerHour = DayNightCycle.MinutesPerDay / DayNightCycle.HoursPerDay;

        /// <summary>The warriors a settlement of <paramref name="beds"/> keeps, by its culture's profile.</summary>
        public static int WarriorQuota(ExpeditionProfile profile, int beds) =>
            QuotaForBeds(profile.warriorQuotaSmall, profile.warriorQuotaLarge, profile.largeFromBeds, beds);

        /// <summary>The residents with <paramref name="quota"/>'s role a settlement of <paramref name="beds"/> keeps, in <paramref name="profile"/>'s bed bands.</summary>
        public static int RoleQuotaCount(ExpeditionProfile profile, RoleQuota quota, int beds) =>
            QuotaForBeds(quota.small, quota.large, profile.largeFromBeds, beds);

        /// <summary>Every quota a settlement of <paramref name="beds"/> keeps: the warriors first, then <paramref name="profile"/>'s role quotas in order.</summary>
        public static List<(ExpeditionRole role, int count)> Quotas(ExpeditionProfile profile, int beds)
        {
            var quotas = new List<(ExpeditionRole role, int count)> { (ExpeditionRole.Warrior, WarriorQuota(profile, beds)) };
            foreach (RoleQuota quota in profile.roleQuotas) quotas.Add((quota.role, RoleQuotaCount(profile, quota, beds)));
            return quotas;
        }

        /// <summary>
        /// Any quota by beds: <paramref name="large"/> from <paramref name="largeFromBeds"/> beds up. Below that, the range
        /// <paramref name="small"/> (x to y) is cut into equal bands of beds, one per value, lowest first.
        /// </summary>
        public static int QuotaForBeds(Vector2Int small, int large, int largeFromBeds, int beds)
        {
            beds = Mathf.Max(0, beds);
            if (beds >= largeFromBeds) return large;

            int floor = Mathf.Min(small.x, small.y), ceiling = Mathf.Max(small.x, small.y);
            int values = ceiling - floor + 1;
            return Mathf.Min(ceiling, floor + values * beds / largeFromBeds);
        }

        /// <summary>
        /// The roster indices (<see cref="RecruitCandidate.residentIndex"/>) of up to <paramref name="needed"/>
        /// <see cref="RecruitCandidate.Eligible"/> candidates to recast for a quota, in pick order: highest nerve first,
        /// equal nerve in an order drawn from <paramref name="seed"/>. The order the candidates are listed in does
        /// not change the answer. Fewer eligible than needed: all of them. None needed: none.
        /// </summary>
        public static List<int> PickRecruits(IReadOnlyList<RecruitCandidate> candidates, int needed, int seed)
        {
            var eligible = new List<RecruitCandidate>();
            foreach (RecruitCandidate candidate in candidates)
                if (candidate.Eligible) eligible.Add(candidate);
            if (needed <= 0 || eligible.Count == 0) return new List<int>();

            // One draw per candidate in roster order, so the tiebreak does not depend on the order passed in.
            eligible.Sort((a, b) => a.residentIndex.CompareTo(b.residentIndex));
            var rng = new System.Random(seed);
            var draw = new Dictionary<int, int>(eligible.Count);
            foreach (RecruitCandidate candidate in eligible) draw[candidate.residentIndex] = rng.Next();

            eligible.Sort((a, b) =>
            {
                int byNerve = b.nerve.CompareTo(a.nerve);
                if (byNerve != 0) return byNerve;
                int byDraw = draw[a.residentIndex].CompareTo(draw[b.residentIndex]);
                return byDraw != 0 ? byDraw : a.residentIndex.CompareTo(b.residentIndex);
            });

            int count = Math.Min(needed, eligible.Count);
            var picks = new List<int>(count);
            for (int i = 0; i < count; i++) picks.Add(eligible[i].residentIndex);
            return picks;
        }

        /// <summary>
        /// Which of <paramref name="pool"/> (rolled copies, in their seeded order) move into <paramref name="room"/> beds,
        /// as indices in pool order. Quota by quota, in order, the earliest copies certain to have its role (copies
        /// made for a role before the rest) are taken until the quota is held, counting <paramref name="settled"/> (the
        /// roles of those moving in anyway) and every copy taken so far; then the earliest of the rest fill the beds.
        /// A pool short of a quota gives every copy it has with the role, and the beds are still filled. Never more
        /// than <paramref name="room"/>; with every quota already held it is the first <paramref name="room"/> copies.
        /// </summary>
        public static List<int> PlanMoveIn(IReadOnlyList<MoveInCandidate> pool, IReadOnlyList<(ExpeditionRole role, int count)> quotas,
                                           IReadOnlyList<ExpeditionRole> settled, int room)
        {
            room = Math.Min(Math.Max(0, room), pool.Count);
            var taken = new bool[pool.Count];
            int takenCount = 0;

            foreach ((ExpeditionRole role, int count) in quotas)
            {
                int held = 0;
                foreach (ExpeditionRole roles in settled)
                    if ((roles & role) != 0) held++;
                for (int i = 0; i < pool.Count; i++)
                    if (taken[i] && (pool[i].roles & role) != 0) held++;

                foreach (bool fixedRole in new[] { true, false })
                    for (int i = 0; i < pool.Count && held < count && takenCount < room; i++)
                    {
                        if (taken[i] || pool[i].fixedRole != fixedRole || (pool[i].roles & role) == 0) continue;
                        taken[i] = true;
                        takenCount++;
                        held++;
                    }
            }

            for (int i = 0; i < pool.Count && takenCount < room; i++)
            {
                if (taken[i]) continue;
                taken[i] = true;
                takenCount++;
            }

            var chosen = new List<int>(takenCount);
            for (int i = 0; i < pool.Count; i++)
                if (taken[i]) chosen.Add(i);
            return chosen;
        }

        // ---- The draw (spec §3.1) -------------------------------------------------------------------------

        /// <summary>The goal the next band runs, among <paramref name="goals"/> that are <paramref name="possible"/>; null when none is.</summary>
        public static ExpeditionGoal ChooseGoal(IReadOnlyList<ExpeditionGoal> goals, string lastGoalId, int seed,
                                                Func<ExpeditionGoal, bool> possible, float varietyPenalty)
        {
            var options = new GoalOption[goals.Count];
            for (int i = 0; i < goals.Count; i++)
            {
                ExpeditionGoal goal = goals[i];
                if (goal != null) options[i] = new GoalOption(goal.id, goal.weight, possible(goal));
            }

            int pick = ChooseGoalIndex(options, lastGoalId, seed, varietyPenalty);
            return pick < 0 ? null : goals[pick];
        }

        /// <summary>
        /// A weighted draw among the possible options; the one whose id is <paramref name="lastGoalId"/> weighs
        /// <paramref name="varietyPenalty"/> times its weight. -1 when no possible option has weight. A penalty
        /// that zeroes the only possible goal is dropped: running it again beats raising no band.
        /// </summary>
        public static int ChooseGoalIndex(IReadOnlyList<GoalOption> options, string lastGoalId, int seed, float varietyPenalty)
        {
            double roll = Rng(seed, GoalSalt).NextDouble();
            var weights = new float[options.Count];

            for (int i = 0; i < options.Count; i++)
            {
                GoalOption option = options[i];
                bool wasLast = !string.IsNullOrEmpty(lastGoalId) && option.id == lastGoalId;
                weights[i] = !option.possible ? 0f : wasLast ? option.weight * varietyPenalty : option.weight;
            }
            int pick = RosterDraw.PickWeighted(weights, roll);
            if (pick >= 0) return pick;

            for (int i = 0; i < options.Count; i++) weights[i] = options[i].possible ? options[i].weight : 0f;
            return RosterDraw.PickWeighted(weights, roll);
        }

        // ---- Who goes (spec §3.2, §3.3) -------------------------------------------------------------------

        /// <summary>Picks a band for <paramref name="goal"/>; see the core overload.</summary>
        public static bool TryPickMembers(ExpeditionGoal goal, IReadOnlyList<RosterEntry> roster, ISet<string> away,
                                          IReadOnlyDictionary<string, int> restUntilDay, int today, int warriorsHomeMin,
                                          float minHomeShare, int seed, out List<MemberRecord> members) =>
            TryPickMembers(goal.slots, goal.KitIndexOf, roster, away, restUntilDay, today, warriorsHomeMin, minHomeShare, seed, out members);

        /// <summary>
        /// Fills every slot's <c>min</c>, then tops each up to its <c>max</c>, from the residents who are home,
        /// adult and rested (<paramref name="restUntilDay"/> holds the first day each may go again;
        /// <paramref name="today"/> is DayNightCycle.Day). A slot that is not <c>any</c> takes only residents with
        /// its role — until every slot has had them: a <c>fillFromAnyAdult</c> slot still short of its <c>min</c> then
        /// takes any free adult for the rest, who stands in for the role. Nobody is taken who would leave fewer than
        /// <paramref name="warriorsHomeMin"/> warriors at home, or less than <paramref name="minHomeShare"/> of the
        /// roster. Once the first member is picked, its bonded residents are preferred; otherwise the order is drawn
        /// from <paramref name="seed"/>, independent of the order the roster is listed in. The leader is a Warrior of
        /// the band, drawn from the seed. Each member's kit is its slot role's row (<paramref name="kitIndexOf"/>) and
        /// its <see cref="MemberRecord.role"/> the slot's role, whatever its own. False, with no members, when a
        /// minimum cannot be met.
        /// </summary>
        public static bool TryPickMembers(IReadOnlyList<RoleSlot> slots, Func<ExpeditionRole, int> kitIndexOf,
                                          IReadOnlyList<RosterEntry> roster, ISet<string> away,
                                          IReadOnlyDictionary<string, int> restUntilDay, int today, int warriorsHomeMin,
                                          float minHomeShare, int seed, out List<MemberRecord> members)
        {
            members = new List<MemberRecord>();
            int atHome = 0, warriorsHome = 0;
            var free = new List<RosterEntry>();
            foreach (RosterEntry entry in roster)
            {
                if (away.Contains(entry.residentKey)) continue;
                atHome++;
                if (IsWarrior(entry)) warriorsHome++;
                if (entry.adult && (!restUntilDay.TryGetValue(entry.residentKey, out int restUntil) || today >= restUntil))
                    free.Add(entry);
            }

            // One draw per resident in key order, so the band does not depend on the order the roster is listed in.
            free.Sort((a, b) => string.CompareOrdinal(a.residentKey, b.residentKey));
            System.Random rng = Rng(seed, PickSalt);
            var draw = new Dictionary<string, int>(free.Count);
            foreach (RosterEntry entry in free) draw[entry.residentKey] = rng.Next();
            free.Sort((a, b) =>
            {
                int byDraw = draw[a.residentKey].CompareTo(draw[b.residentKey]);
                return byDraw != 0 ? byDraw : string.CompareOrdinal(a.residentKey, b.residentKey);
            });

            var picked = new List<RosterEntry>();
            var pickedRole = new List<ExpeditionRole>();
            int warriorsPicked = 0;
            var filled = new int[slots.Count];

            bool CanGo(RosterEntry entry)
            {
                bool warrior = IsWarrior(entry);
                if (warrior && warriorsHome - warriorsPicked - 1 < warriorsHomeMin) return false;
                // The outer cast rounds the quotient to float: Mono keeps it at double precision otherwise,
                // and 12/15 then falls just short of 0.8f, refusing a band that leaves exactly the share home.
                return (float)((float)(atHome - picked.Count - 1) / roster.Count) >= minHomeShare;
            }

            // Residents with the slot's role first: every minimum, then up to every maximum. Then the rest of the
            // minimum of a slot any adult may stand in for, from whoever is free.
            const int Minimums = 0, Maximums = 1, StandIns = 2;
            for (int pass = Minimums; pass <= StandIns; pass++)
            {
                for (int s = 0; s < slots.Count; s++)
                {
                    RoleSlot slot = slots[s];
                    if (pass == StandIns && !slot.fillFromAnyAdult) continue;

                    int wanted = pass == Maximums ? slot.max : slot.min;
                    while (filled[s] < wanted)
                    {
                        int next = free.FindIndex(entry =>
                            (pass == StandIns || slot.any || (entry.roles & slot.role) != 0) && CanGo(entry));
                        if (next < 0)
                        {
                            if (pass == StandIns || pass == Minimums && !slot.fillFromAnyAdult) return false;
                            break;
                        }

                        RosterEntry entry = free[next];
                        free.RemoveAt(next);
                        if (picked.Count == 0) PreferBonds(free, entry);
                        picked.Add(entry);
                        pickedRole.Add(slot.role);
                        if (IsWarrior(entry)) warriorsPicked++;
                        filled[s]++;
                    }
                }
            }
            if (picked.Count == 0) return false;

            var leaders = new List<int>();
            for (int i = 0; i < picked.Count; i++)
                if (IsWarrior(picked[i])) leaders.Add(i);
            int leader = leaders.Count > 0 ? leaders[rng.Next(leaders.Count)] : rng.Next(picked.Count);

            for (int i = 0; i < picked.Count; i++)
                members.Add(new MemberRecord
                {
                    residentKey = picked[i].residentKey,
                    archetypeIndex = picked[i].archetypeIndex,
                    kitIndex = kitIndexOf(pickedRole[i]),
                    isLeader = i == leader,
                    role = pickedRole[i],
                });
            return true;
        }

        private static bool IsWarrior(RosterEntry entry) => (entry.roles & ExpeditionRole.Warrior) != 0;

        /// <summary>Moves <paramref name="first"/>'s bonded residents to the front of <paramref name="free"/>, keeping the drawn order within each part.</summary>
        private static void PreferBonds(List<RosterEntry> free, RosterEntry first)
        {
            var bonded = new HashSet<string>();
            foreach (int index in first.bonds) bonded.Add(ResidentKey.ForAuthored(index));

            var close = free.FindAll(entry => bonded.Contains(entry.residentKey));
            free.RemoveAll(entry => bonded.Contains(entry.residentKey));
            free.InsertRange(0, close);
        }

        // ---- The trip (spec §5.1, §5.2) -------------------------------------------------------------------

        /// <summary>The stages of a band running <paramref name="goal"/>; see the core overload.</summary>
        public static StageRecord[] BuildStages(ExpeditionGoal goal, int seed) => BuildStages(goal.stages, seed);

        /// <summary>
        /// One stage per spec, in order. Minutes are rolled between the range's ends (both 0: no limit); a
        /// Search's waypoint count between the count range's ends, inclusive; other kinds visit no waypoints.
        /// </summary>
        public static StageRecord[] BuildStages(IReadOnlyList<StageSpec> specs, int seed)
        {
            System.Random rng = Rng(seed, StageSalt);
            var stages = new StageRecord[specs.Count];
            for (int i = 0; i < specs.Count; i++)
            {
                StageSpec spec = specs[i];
                // Both rolls are drawn for every stage, so editing one stage's ranges does not re-roll the others.
                float minutesRoll = (float)rng.NextDouble();
                int lowCount = Mathf.Min(spec.countRange.x, spec.countRange.y), highCount = Mathf.Max(spec.countRange.x, spec.countRange.y);
                int count = rng.Next(lowCount, highCount + 1);

                float lowMinutes = Mathf.Min(spec.minutesRange.x, spec.minutesRange.y), highMinutes = Mathf.Max(spec.minutesRange.x, spec.minutesRange.y);
                stages[i] = new StageRecord
                {
                    kind = spec.kind,
                    minutes = highMinutes <= 0f ? StageRecord.NoLimit : Mathf.Lerp(lowMinutes, highMinutes, minutesRoll),
                    waypoints = spec.kind == StageKind.Search ? count : 0,
                };
            }
            return stages;
        }

        /// <summary>Whether a band on the road walks at <paramref name="gameMinutes"/> (DayNightCycle.GameMinutesNow).</summary>
        public static bool IsTravelHour(double gameMinutes, ExpeditionTuning tuning) =>
            IsTravelHour(gameMinutes, tuning.travelDawn, tuning.travelDusk);

        /// <summary>From <paramref name="dawnHour"/> up to, not including, <paramref name="duskHour"/>; a dawn after the dusk spans midnight.</summary>
        public static bool IsTravelHour(double gameMinutes, float dawnHour, float duskHour)
        {
            double hour = MinuteOfDay(gameMinutes) / MinutesPerHour;
            return dawnHour <= duskHour ? hour >= dawnHour && hour < duskHour : hour >= dawnHour || hour < duskHour;
        }

        /// <summary>Advances <paramref name="band"/>; see the core overload.</summary>
        public static StageStep Advance(ExpeditionRecord band, double nowGameMinutes, double elapsedGameMinutes, bool arrived,
                                        ExpeditionTuning tuning) =>
            Advance(band, nowGameMinutes, elapsedGameMinutes, arrived, tuning.travelDawn);

        /// <summary>
        /// Moves <paramref name="band"/>'s trip on by the <paramref name="elapsedGameMinutes"/> that ended at
        /// <paramref name="nowGameMinutes"/>, and says what the director must do now. A trip that has not
        /// begun begins at the start of that window.
        /// <list type="bullet">
        /// <item>A stage with a timer ends when it runs out, at the minute it runs out. A Halt's timer runs to the
        /// first <paramref name="dawnHour"/> after it began, or its own minutes when they end sooner.</item>
        /// <item>Travel, Search and ReturnHome also end on <paramref name="arrived"/>: the band stands at its current
        /// target (a Search's current waypoint). A Search moves to its next waypoint until it has reached all of them.
        /// Arrival is taken as of now, so it is dropped when the stage it was for timed out inside the window.</item>
        /// <item>A long window (a clock jump, a simulated day) settles every timed stage end in order, and stops at
        /// the first stage that waits for an arrival. Advance never moves a band: the director moves the group
        /// (NpcWorldSim folds and walks it) and reports the arrival on a later call.</item>
        /// </list>
        /// Each call ends at least one stage or returns, so it never loops longer than the trip.
        /// </summary>
        public static StageStep Advance(ExpeditionRecord band, double nowGameMinutes, double elapsedGameMinutes, bool arrived,
                                        float dawnHour)
        {
            double elapsed = Math.Max(0d, elapsedGameMinutes);
            double clock = nowGameMinutes - elapsed;
            StageStep step = StageStep.Continue;
            if (band.stageIndex < 0) step = Enter(band, 0, clock, dawnHour);

            while (band.stageIndex < band.stages.Length)
            {
                double left = band.stageMinutesLeft;
                if (left >= 0d && left <= elapsed)
                {
                    clock += left;
                    elapsed -= left;
                    arrived = false;
                    step = Enter(band, band.stageIndex + 1, clock, dawnHour);
                    continue;
                }
                if (left >= 0d) band.stageMinutesLeft = (float)(left - elapsed);

                StageRecord stage = band.stages[band.stageIndex];
                if (!arrived || stage.kind == StageKind.Halt) return step;
                if (stage.kind == StageKind.Search && ++band.waypointsDone < stage.waypoints) return StageStep.SetTarget;
                return Enter(band, band.stageIndex + 1, nowGameMinutes, dawnHour);
            }
            return step;
        }

        /// <summary>Starts stage <paramref name="index"/> at <paramref name="clock"/>; past the last stage, the trip is over.</summary>
        private static StageStep Enter(ExpeditionRecord band, int index, double clock, float dawnHour)
        {
            band.stageIndex = index;
            band.waypointsDone = 0;
            band.stageMinutesLeft = StageRecord.NoLimit;
            if (index >= band.stages.Length) return StageStep.Arrive;

            StageRecord stage = band.stages[index];
            band.stageMinutesLeft = stage.minutes;
            switch (stage.kind)
            {
                case StageKind.Halt:
                    float untilDawn = (float)MinutesUntilHour(clock, dawnHour);
                    band.stageMinutesLeft = stage.minutes < 0f ? untilDawn : Mathf.Min(stage.minutes, untilDawn);
                    return StageStep.Hold;
                case StageKind.ReturnHome:
                    return StageStep.Home;
                default:
                    return StageStep.SetTarget;
            }
        }

        private static double MinuteOfDay(double gameMinutes)
        {
            double minute = gameMinutes % DayNightCycle.MinutesPerDay;
            return minute < 0d ? minute + DayNightCycle.MinutesPerDay : minute;
        }

        /// <summary>Game minutes from <paramref name="clock"/> to the next time the day reaches <paramref name="hour"/>; never 0.</summary>
        private static double MinutesUntilHour(double clock, float hour)
        {
            double until = hour * MinutesPerHour - MinuteOfDay(clock);
            return until > 0d ? until : until + DayNightCycle.MinutesPerDay;
        }

        /// <summary>Waypoints around <paramref name="centre"/>; see the core overload.</summary>
        public static IReadOnlyList<Vector3> SearchRing(Vector3 centre, int count, int seed, ExpeditionTuning tuning) =>
            SearchRing(centre, count, seed, tuning.searchRing);

        /// <summary>
        /// <paramref name="count"/> points at the centre's height, each between <paramref name="ring"/>'s two radii,
        /// at evenly spread bearings from a drawn start, so visiting them in order goes once round the centre. Not
        /// sampled: the caller puts each on the NavMesh.
        /// </summary>
        public static IReadOnlyList<Vector3> SearchRing(Vector3 centre, int count, int seed, Vector2 ring)
        {
            System.Random rng = Rng(seed, RingSalt);
            const float FullTurn = 2f * Mathf.PI;
            float start = (float)rng.NextDouble() * FullTurn;
            float inner = Mathf.Min(ring.x, ring.y), outer = Mathf.Max(ring.x, ring.y);

            var points = new Vector3[Mathf.Max(0, count)];
            for (int i = 0; i < points.Length; i++)
            {
                float bearing = start + i * FullTurn / points.Length;
                float radius = Mathf.Lerp(inner, outer, (float)rng.NextDouble());
                points[i] = centre + new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing)) * radius;
            }
            return points;
        }

        // ---- The rotation (spec §3.1, §3.3) ---------------------------------------------------------------

        /// <summary>Raised and not finished: announced, mustering, on the road or walking in.</summary>
        public static bool IsUnderway(ExpeditionRecord band) =>
            band.phase != ExpeditionPhase.Home && band.phase != ExpeditionPhase.Lost;

        /// <summary>Turned for home: on its ReturnHome stage, or walking in.</summary>
        public static bool IsHeadingHome(ExpeditionRecord band) =>
            band.phase == ExpeditionPhase.Returning
            || band.phase == ExpeditionPhase.Out && band.stageIndex >= 0 && band.stageIndex < band.stages.Length
               && band.stages[band.stageIndex].kind == StageKind.ReturnHome;

        /// <summary>Whether a settlement whose bands (any phase) are <paramref name="bands"/> raises its next one now.</summary>
        public static bool NextBandDue(IReadOnlyList<ExpeditionRecord> bands)
        {
            int underway = 0;
            bool headingHome = false;
            foreach (ExpeditionRecord band in bands)
            {
                if (!IsUnderway(band)) continue;
                underway++;
                headingHome = IsHeadingHome(band);
            }
            return NextBandDue(underway, headingHome);
        }

        /// <summary>None underway, or the only one has turned for home; never while two are underway.</summary>
        public static bool NextBandDue(int bandsUnderway, bool onlyBandHeadingHome) =>
            bandsUnderway == 0 || bandsUnderway == 1 && onlyBandHeadingHome;

        // ---- Muster and hand-off (spec §4.1) --------------------------------------------------------------

        /// <summary>
        /// Where <paramref name="count"/> members stand at the muster: one row across the spot, centred on it,
        /// <paramref name="spacing"/> metres apart, everyone facing out the way the spot does.
        /// </summary>
        public static Pose[] MusterRow(Pose muster, int count, float spacing)
        {
            var row = new Pose[Mathf.Max(0, count)];
            Vector3 across = muster.right;
            float middle = (row.Length - 1) * 0.5f;
            for (int i = 0; i < row.Length; i++)
                row[i] = new Pose(muster.position + across * ((i - middle) * spacing), muster.rotation);
            return row;
        }

        /// <summary>The road point a band walks out to and is handed off at: <paramref name="departRadius"/> out along the muster spot's facing.</summary>
        public static Vector3 RoadPoint(Pose muster, float departRadius) => muster.position + muster.forward * departRadius;

        /// <summary>Whether a walking-out band is handed off now; see the core overload.</summary>
        public static bool ShouldHandOff(float distanceFromMuster, bool observed, ExpeditionTuning tuning) =>
            ShouldHandOff(distanceFromMuster, observed, tuning.departRadius, tuning.maxHandoffDistance);

        /// <summary>Past <paramref name="departRadius"/> with nobody watching, or at <paramref name="maxHandoffDistance"/> regardless.</summary>
        public static bool ShouldHandOff(float distanceFromMuster, bool observed, float departRadius, float maxHandoffDistance) =>
            distanceFromMuster >= departRadius && !observed || distanceFromMuster >= maxHandoffDistance;

        /// <summary>
        /// The radius within which a player watching a walking-out band counts as watching (<see cref="ShouldHandOff(float, bool, float, float)"/>):
        /// <paramref name="observeRadius"/>, but never past <paramref name="despawnRadius"/>, within which the hand-off
        /// spawns stand-ins. A farther watcher would hold the band to its swap distance only to see the residents vanish
        /// there with nobody in their place, so the band is handed off as unwatched, as the sim folds any group beyond that
        /// distance, in view or not.
        /// </summary>
        public static float EffectiveObserveRadius(float observeRadius, float despawnRadius) => Mathf.Min(observeRadius, despawnRadius);

        /// <summary>Where a walking-out band is sent; see the core overload. It lies arriveRadius past maxHandoffDistance.</summary>
        public static Vector3 WalkOutPoint(Pose muster, Vector3 handoffPoint, ExpeditionTuning tuning) =>
            WalkOutPoint(muster, handoffPoint, tuning.maxHandoffDistance + tuning.arriveRadius);

        /// <summary>
        /// Where a walking-out band walks to: <paramref name="distance"/> from the muster spot along the road through
        /// <paramref name="handoffPoint"/> (along the spot's facing when the two coincide), at the spot's height. Past the
        /// hand-off point, so a band somebody watches all the way still walks on to the distance it is swapped at. Not
        /// sampled: the caller puts it on the NavMesh.
        /// </summary>
        public static Vector3 WalkOutPoint(Pose muster, Vector3 handoffPoint, float distance)
        {
            Vector3 road = handoffPoint - muster.position;
            road.y = 0f;
            if (road.sqrMagnitude < Mathf.Epsilon)
            {
                road = muster.forward;
                road.y = 0f;
            }
            return muster.position + road.normalized * distance;
        }

        /// <summary>Whether a walking-out band is handed off now; see the core overload.</summary>
        public static bool HandOffNow(float distanceFromMuster, bool observed, double minutesWalking, ExpeditionTuning tuning) =>
            HandOffNow(distanceFromMuster, observed, minutesWalking, tuning.departRadius, tuning.maxHandoffDistance, tuning.walkLimitMinutes);

        /// <summary>
        /// <see cref="ShouldHandOff(float, bool, float, float)"/>, or once the walk-out has lasted
        /// <paramref name="walkLimitMinutes"/> game minutes, wherever the band stands: a member stuck on the way never holds it.
        /// </summary>
        public static bool HandOffNow(float distanceFromMuster, bool observed, double minutesWalking, float departRadius,
                                      float maxHandoffDistance, float walkLimitMinutes) =>
            ShouldHandOff(distanceFromMuster, observed, departRadius, maxHandoffDistance) || minutesWalking >= walkLimitMinutes;

        /// <summary>Whether a walking-in band is home; see the core overload.</summary>
        public static bool WalkedIn(float distanceFromMuster, double minutesWalking, ExpeditionTuning tuning) =>
            WalkedIn(distanceFromMuster, minutesWalking, tuning.arriveRadius, tuning.walkLimitMinutes);

        /// <summary>Its middle is within <paramref name="arriveRadius"/> of the muster spot, or the walk-in has lasted <paramref name="walkLimitMinutes"/>.</summary>
        public static bool WalkedIn(float distanceFromMuster, double minutesWalking, float arriveRadius, float walkLimitMinutes) =>
            distanceFromMuster <= arriveRadius || minutesWalking >= walkLimitMinutes;

        /// <summary>The middle of a band: the mean of its members' positions. The origin for none.</summary>
        public static Vector3 Centroid(IReadOnlyList<Pose> poses)
        {
            if (poses.Count == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            foreach (Pose pose in poses) sum += pose.position;
            return sum / poses.Count;
        }

        /// <summary>Metres between two points on the ground plane, height ignored.</summary>
        public static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ---- The director's bookkeeping (plan Task 1.2) ----------------------------------------------------

        /// <summary>The game day (DayNightCycle.Day) <paramref name="gameMinutes"/> falls on.</summary>
        public static int DayOf(double gameMinutes) => (int)Math.Floor(gameMinutes / DayNightCycle.MinutesPerDay);

        /// <summary>The clock reading (game minutes) at which a band due to leave on <paramref name="day"/> leaves.</summary>
        public static double DepartureMinute(int day, float departHour) =>
            day * (double)DayNightCycle.MinutesPerDay + departHour * MinutesPerHour;

        /// <summary>The day of the next departure hour after <paramref name="gameMinutes"/>: today if it has not struck yet, else tomorrow.</summary>
        public static int NextDepartureDay(double gameMinutes, float departHour)
        {
            int today = DayOf(gameMinutes);
            return gameMinutes < DepartureMinute(today, departHour) ? today : today + 1;
        }

        /// <summary>Warriors that always stay home (spec §3.3): half the quota, rounded up.</summary>
        public static int WarriorsHomeMin(int warriorQuota) => (Math.Max(0, warriorQuota) + 1) / 2;

        /// <summary>The seed of a settlement's <paramref name="rotation"/>-th band: the same band again after a reload.</summary>
        public static int BandSeed(string settlementId, int rotation) =>
            (int)RosterDraw.Hash(RosterDraw.StableHash(settlementId ?? string.Empty), rotation);

        /// <summary>The seed one stage's rolls draw from (its destination, its waypoints), apart from every other stage's.</summary>
        public static int StageSeed(int bandSeed, int stageIndex) => (int)RosterDraw.Hash(bandSeed, stageIndex);

        /// <summary>
        /// The first day a member home on <paramref name="homeDay"/> may be picked again: back MORE than
        /// <paramref name="restDays"/> days ago (spec §3.2), so the day after those days have passed in full.
        /// </summary>
        public static int RestUntil(int homeDay, int restDays) => homeDay + Math.Max(0, restDays) + 1;

        /// <summary>Handed off and not yet home: on the road, or walking back in.</summary>
        public static bool IsPastHandOff(ExpeditionRecord band) =>
            band.phase == ExpeditionPhase.Out || band.phase == ExpeditionPhase.Returning;

        /// <summary>
        /// How many stage ends bring <paramref name="band"/> onto its next stage of <paramref name="kind"/> (the count
        /// to hand the director's AdvanceStage): 0 when it is on one, and from a trip not yet begun the first end
        /// begins stage 0. -1 when no such stage lies ahead.
        /// </summary>
        public static int StageEndsUntil(ExpeditionRecord band, StageKind kind)
        {
            for (int i = Math.Max(0, band.stageIndex); i < band.stages.Length; i++)
                if (band.stages[i].kind == kind) return i - band.stageIndex;
            return -1;
        }

        /// <summary>
        /// Whether <paramref name="residentKey"/> is away from home because of <paramref name="band"/>: every member
        /// from the hand-off until the homecoming completes (residents walking back in are still away to the planner,
        /// so no plan claims them mid-walk). A member who died stays away while the finished record is kept, so its
        /// body is never shown at home before the settlement has applied the death. Before the hand-off the members
        /// are home, mustering and walking out in view.
        /// </summary>
        public static bool IsAwayOn(ExpeditionRecord band, string residentKey)
        {
            foreach (MemberRecord member in band.members)
            {
                if (member.residentKey != residentKey) continue;
                return IsPastHandOff(band) || !IsUnderway(band) && member.dead;
            }
            return false;
        }

        /// <summary>
        /// Adds to <paramref name="into"/> every resident a new band may not take: the members of every band still
        /// underway (announced ones included: they are spoken for), and the dead of finished ones.
        /// </summary>
        public static void CollectSpokenFor(IReadOnlyList<ExpeditionRecord> bands, ISet<string> into)
        {
            foreach (ExpeditionRecord band in bands)
                foreach (MemberRecord member in band.members)
                    if (IsUnderway(band) || member.dead) into.Add(member.residentKey);
        }

        /// <summary>The indices into <c>members</c> of those still alive, in order: who a stand-in is spawned for.</summary>
        public static List<int> LivingMembers(ExpeditionRecord band)
        {
            var living = new List<int>(band.members.Length);
            for (int i = 0; i < band.members.Length; i++)
                if (!band.members[i].dead) living.Add(i);
            return living;
        }

        /// <summary>
        /// Whether a finished band's record may go once its settlement has applied the deaths it could. A dead member whose
        /// resident the settlement did not find (<paramref name="found"/> false for its key) keeps the record while
        /// <paramref name="roster"/> still lists that resident, so a later load, where the body turns up, still applies the
        /// death; a resident the roster no longer lists is gone, and its death holds nothing.
        /// </summary>
        public static bool MayRetire(ExpeditionRecord band, Func<string, bool> found, IReadOnlyList<RosterEntry> roster)
        {
            foreach (MemberRecord member in band.members)
                if (member.dead && !found(member.residentKey) && RosterLists(roster, member.residentKey)) return false;
            return true;
        }

        /// <summary>Whether <paramref name="roster"/> has a row for <paramref name="residentKey"/>.</summary>
        public static bool RosterLists(IReadOnlyList<RosterEntry> roster, string residentKey)
        {
            if (roster == null) return false;

            foreach (RosterEntry row in roster)
                if (row != null && row.residentKey == residentKey) return true;
            return false;
        }

        /// <summary>
        /// Where a Travel stage starting at <paramref name="from"/> goes: a site among <paramref name="sites"/> lying
        /// between <paramref name="reach"/>'s two distances, drawn from <paramref name="stageSeed"/> in id order (so the
        /// order the registry lists them in does not matter); with none, a drawn point on that ring. Not sampled: the
        /// caller puts it on the NavMesh.
        /// </summary>
        public static Vector3 TravelDestination(Vector3 from, IReadOnlyList<WorldSite> sites, int stageSeed, Vector2 reach)
        {
            float near = Mathf.Min(reach.x, reach.y), far = Mathf.Max(reach.x, reach.y);
            var inReach = new List<WorldSite>();
            foreach (WorldSite site in sites)
            {
                float distance = site.FlatDistanceTo(from);
                if (distance >= near && distance <= far) inReach.Add(site);
            }

            System.Random rng = Rng(stageSeed, TravelSalt);
            if (inReach.Count > 0)
            {
                inReach.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                return inReach[rng.Next(inReach.Count)].Position;
            }

            float bearing = (float)rng.NextDouble() * 2f * Mathf.PI;
            float radius = Mathf.Lerp(near, far, (float)rng.NextDouble());
            return from + new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing)) * radius;
        }

        /// <summary>
        /// The end of the next leg a band with stand-ins walks from <paramref name="from"/> toward <paramref name="target"/>:
        /// <paramref name="maxLength"/> metres along the straight line, measured on the ground plane, with its height
        /// between the two; <paramref name="target"/> itself once it is no farther (or with no positive limit), so the last
        /// leg ends on the target. Not sampled: the caller puts it on the NavMesh.
        /// </summary>
        public static Vector3 LegPoint(Vector3 from, Vector3 target, float maxLength)
        {
            float distance = FlatDistance(from, target);
            return maxLength <= 0f || distance <= maxLength ? target : Vector3.Lerp(from, target, maxLength / distance);
        }

        private static System.Random Rng(int seed, int salt) => new System.Random((int)RosterDraw.Hash(seed, salt));
    }
}
