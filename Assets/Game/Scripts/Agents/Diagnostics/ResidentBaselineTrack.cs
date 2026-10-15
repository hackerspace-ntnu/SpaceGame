// One resident, followed through the residents baseline: which plan segment it is in, how close it got
// to that segment's place, and what it did there. Writes one CSV row per segment boundary.
//
// The columns are the comparison contract between runs (Residents.md, "Baseline"), so they never change
// meaning. What fills them is read in exactly one place per column, below: when the agent restructure
// moves the routine onto the brain, ReadRoutine is the one method that is re-pointed.
using System.Globalization;
using System.Text;
using SpaceGame.Agents.Residents;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents
{
    internal sealed class ResidentBaselineTrack
    {
        public const string CsvHeader =
            "game_minute,day,clock,boundary,resident,name,role,plan_day,segment,plan_activity,plan_place,place_kind," +
            "routine_activity,presence_activity,held_place,dist_m,min_dist_m,reached,offstage,dead,band," +
            "errands_started,errands_finished,errand_stops,carries,lines_said,deeds_known,deeds_heard," +
            "conversations_opened,x,z";

        public const string BoundaryStart = "start";
        public const string BoundaryArrive = "arrive";
        public const string BoundaryLeave = "leave";
        public const string BoundaryReplan = "replan";
        public const string BoundaryCutoff = "cutoff";
        public const string BoundaryDayEnd = "dayend";

        private const string NoValue = "";

        private readonly Resident resident;
        private readonly ResidentRoutine routine;
        private readonly ResidentVoice voice;
        private readonly float reachedWithin;

        private bool hasSegment;
        private PlanSegment segment;
        private int planDay;
        private int segmentIndex;
        private bool arriveLogged;
        private float minDistance;
        private int stopsThisSegment;
        private bool wasAtPlace;
        private Activity lastShown;
        private byte lastProp;

        public int ErrandsStarted { get; private set; }
        public int ErrandsFinished { get; private set; }
        public int ErrandStops { get; private set; }
        public int Carries { get; private set; }
        public int SegmentsClosed { get; private set; }
        public int SegmentsReached { get; private set; }

        public ResidentBaselineTrack(Resident resident, float reachedWithin)
        {
            this.resident = resident;
            this.reachedWithin = reachedWithin;
            routine = resident.GetComponent<ResidentRoutine>();
            voice = resident.GetComponent<ResidentVoice>();
        }

        public Resident Resident => resident;

        // As last sampled: a summary written while play mode ends cannot read destroyed residents.
        public int LinesSaid { get; private set; }
        public bool WasDead { get; private set; }
        public bool WasOffstage { get; private set; }

        /// <summary>Reads the resident once: closes a segment that ended, notes an arrival, accumulates counters.</summary>
        public void Sample(SettlementSociety society, double now, ResidentBaselineCounts counts, StringBuilder csv)
        {
            bool inSegment = TryCurrentSegment(society, now, out PlanSegment current, out int currentDay, out int currentIndex);
            bool same = inSegment && hasSegment && currentDay == planDay && current.arrive == segment.arrive;

            if (hasSegment && !same)
                Close(society, now, counts, csv, now < segment.leave ? BoundaryReplan : BoundaryLeave);

            if (inSegment && !same)
                Open(current, currentDay, currentIndex);

            LinesSaid = voice != null ? voice.LinesSaid : 0;
            WasDead = resident.IsDead;
            WasOffstage = resident.IsOffstage;

            if (!hasSegment) return;

            ReadRoutine(out _, out bool atPlace, out _);
            Activity shown = resident.Presence != null ? resident.Presence.Activity : Activity.None;
            byte prop = resident.Presence != null ? resident.Presence.Prop : (byte)0;

            if (prop != 0 && lastProp == 0) Carries++;
            lastProp = prop;

            if (now >= segment.arrive)
            {
                minDistance = Mathf.Min(minDistance, DistanceToPlace(society));
                if (IsErrand(segment.activity) && atPlace && !wasAtPlace) stopsThisSegment++;
                if (!arriveLogged)
                {
                    arriveLogged = true;
                    WriteRow(society, now, counts, csv, BoundaryArrive);
                }
            }

            wasAtPlace = atPlace;
            lastShown = shown;
        }

        /// <summary>Writes the resident's state as it stands, without touching the segment it is in.</summary>
        public void WriteSnapshot(SettlementSociety society, double now, ResidentBaselineCounts counts, StringBuilder csv,
                                  string boundary) =>
            WriteRow(society, now, counts, csv, boundary);

        /// <summary>The run ends: the open segment is reported as far as it got.</summary>
        public void Cut(SettlementSociety society, double now, ResidentBaselineCounts counts, StringBuilder csv)
        {
            if (hasSegment) Close(society, now, counts, csv, BoundaryCutoff);
        }

        private void Open(in PlanSegment current, int day, int index)
        {
            hasSegment = true;
            segment = current;
            planDay = day;
            segmentIndex = index;
            arriveLogged = false;
            minDistance = float.PositiveInfinity;
            stopsThisSegment = 0;
            wasAtPlace = false;
        }

        private void Close(SettlementSociety society, double now, ResidentBaselineCounts counts, StringBuilder csv, string boundary)
        {
            if (IsErrand(segment.activity) && stopsThisSegment > 0)
            {
                ErrandsStarted++;
                ErrandStops += stopsThisSegment;
                // Ran its course: still out on the errand when the segment ended, not cut off by a fight or a shelter.
                if (boundary == BoundaryLeave && lastShown == segment.activity) ErrandsFinished++;
            }

            if (boundary != BoundaryReplan && arriveLogged)
            {
                SegmentsClosed++;
                if (minDistance <= reachedWithin) SegmentsReached++;
                counts.NoteSegment(segment.activity, minDistance <= reachedWithin);
            }

            WriteRow(society, now, counts, csv, boundary);
            hasSegment = false;
        }

        private bool TryCurrentSegment(SettlementSociety society, double now, out PlanSegment current, out int day, out int index)
        {
            current = default;
            day = 0;
            index = -1;

            DayPlan plan = society.PlanFor(resident);
            if (plan == null || !plan.At(now, out current)) return false;

            day = plan.day;
            for (int i = 0; i < plan.segments.Count; i++)
                if (plan.segments[i].arrive == current.arrive) index = i;
            return true;
        }

        // The routine's view of the resident: the segment it is following, whether it stands at it, and the place
        // it holds. Today that is ResidentRoutine; after the restructure it is the FollowPlan goal.
        private void ReadRoutine(out Activity? following, out bool atPlace, out int heldPlace)
        {
            if (routine == null)
            {
                following = null;
                atPlace = false;
                heldPlace = ResidentPresence.NoPlace;
                return;
            }

            following = routine.Current.HasValue ? routine.Current.Value.activity : (Activity?)null;
            atPlace = routine.AtPlace;
            heldPlace = routine.HeldPlace;
        }

        private static bool IsErrand(Activity activity) => activity is Activity.Chore or Activity.Patrol or Activity.Amble;

        private float DistanceToPlace(SettlementSociety society)
        {
            SettlementPlace place = society.Place(segment.place);
            return place != null ? Vector3.Distance(resident.transform.position, place.Position) : float.PositiveInfinity;
        }

        private void WriteRow(SettlementSociety society, double now, ResidentBaselineCounts counts, StringBuilder csv, string boundary)
        {
            ReadRoutine(out Activity? following, out _, out int heldPlace);
            SettlementPlace place = hasSegment ? society.Place(segment.place) : null;
            float distance = hasSegment ? DistanceToPlace(society) : float.PositiveInfinity;
            bool closing = boundary is BoundaryLeave or BoundaryReplan or BoundaryCutoff;
            Vector3 position = resident.transform.position;

            int known = 0, heard = 0;
            if (resident.Memory != null)
                foreach (ResidentMemory.Deed deed in resident.Memory.Deeds)
                {
                    known++;
                    if (deed.heard) heard++;
                }

            csv.Append(Number(now, "0.0")).Append(',')
               .Append(society.Day).Append(',')
               .Append(Clock(now)).Append(',')
               .Append(boundary).Append(',')
               .Append(resident.index).Append(',')
               .Append(Field(resident.DisplayName)).Append(',')
               .Append(Field(resident.RoleName)).Append(',')
               .Append(hasSegment ? planDay.ToString(CultureInfo.InvariantCulture) : NoValue).Append(',')
               .Append(hasSegment ? segmentIndex.ToString(CultureInfo.InvariantCulture) : NoValue).Append(',')
               .Append(hasSegment ? segment.activity.ToString() : NoValue).Append(',')
               .Append(hasSegment ? segment.place.ToString(CultureInfo.InvariantCulture) : NoValue).Append(',')
               .Append(place != null ? place.Kind.ToString() : NoValue).Append(',')
               .Append(following.HasValue ? following.Value.ToString() : NoValue).Append(',')
               .Append(resident.Presence != null ? resident.Presence.Activity.ToString() : NoValue).Append(',')
               .Append(heldPlace).Append(',')
               .Append(Distance(distance)).Append(',')
               .Append(closing || boundary == BoundaryArrive ? Distance(minDistance) : NoValue).Append(',')
               .Append(closing && arriveLogged ? (minDistance <= reachedWithin ? "1" : "0") : NoValue).Append(',')
               .Append(resident.IsOffstage ? 1 : 0).Append(',')
               .Append(resident.IsDead ? 1 : 0).Append(',')
               .Append(resident.Provocation != null ? resident.Provocation.Band.ToString() : NoValue).Append(',')
               .Append(ErrandsStarted).Append(',')
               .Append(ErrandsFinished).Append(',')
               .Append(ErrandStops).Append(',')
               .Append(Carries).Append(',')
               .Append(LinesSaid).Append(',')
               .Append(known).Append(',')
               .Append(heard).Append(',')
               .Append(society.ConversationsOpened).Append(',')
               .Append(Number(position.x, "0.00")).Append(',')
               .Append(Number(position.z, "0.00"))
               .AppendLine();

            counts.NoteRow(boundary);
        }

        private static string Clock(double minutes)
        {
            double ofDay = minutes % DayNightCycle.MinutesPerDay;
            int hour = (int)(ofDay / 60d);
            int minute = (int)(ofDay % 60d);
            return hour.ToString("00", CultureInfo.InvariantCulture) + ":" + minute.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Distance(float metres) =>
            float.IsInfinity(metres) ? NoValue : Number(metres, "0.00");

        private static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        // Names are free text from the culture; a comma or a quote would split the row.
        private static string Field(string text)
        {
            if (string.IsNullOrEmpty(text)) return NoValue;
            if (text.IndexOf(',') < 0 && text.IndexOf('"') < 0) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
