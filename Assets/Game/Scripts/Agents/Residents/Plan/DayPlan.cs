// One resident's day as timed segments, in ABSOLUTE game minutes since day 0 (day d starts at d·1440),
// so a night shift that runs past midnight is still one segment of the day it started on. Segments are
// contiguous — each one departs when the previous one leaves — so between FirstStart and LastLeave there
// is always exactly one answer to "where should this resident be?". Before FirstStart the answer lives in
// yesterday's plan (its last segment is the night that ends where today's first segment departs).
using System;
using System.Collections.Generic;

namespace SpaceGame.Agents.Residents
{
    /// <summary>
    /// Walk to <see cref="place"/> during [depart, arrive), be there doing <see cref="activity"/> during
    /// [arrive, leave). <c>arrive − depart</c> is the baked travel time from the previous segment's place.
    /// </summary>
    [Serializable]
    public struct PlanSegment
    {
        public float depart, arrive, leave;
        public Activity activity;
        public int place;

        /// <summary>Minutes spent at the place, walk excluded.</summary>
        public float Dwell => leave - arrive;

        public bool Contains(double minutes) => minutes >= depart && minutes < leave;
    }

    public sealed class DayPlan
    {
        public int residentIndex;
        public int day;
        public List<PlanSegment> segments = new List<PlanSegment>();

        /// <summary>When today's first walk starts; +∞ for an empty plan (a vacancy).</summary>
        public float FirstStart => segments.Count > 0 ? segments[0].depart : float.PositiveInfinity;

        /// <summary>When the last segment (the night) ends — the next day's FirstStart.</summary>
        public float LastLeave => segments.Count > 0 ? segments[segments.Count - 1].leave : float.NegativeInfinity;

        /// <summary>The segment whose [depart, leave) holds <paramref name="minutes"/>; false outside the plan.</summary>
        public bool At(double minutes, out PlanSegment segment)
        {
            int low = 0, high = segments.Count - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                PlanSegment candidate = segments[middle];
                if (minutes < candidate.depart) high = middle - 1;
                else if (minutes >= candidate.leave) low = middle + 1;
                else
                {
                    segment = candidate;
                    return true;
                }
            }
            segment = default;
            return false;
        }

        /// <summary>The first segment that departs after <paramref name="minutes"/>, or null when none does.</summary>
        public PlanSegment? Next(double minutes)
        {
            foreach (PlanSegment segment in segments)
                if (segment.depart > minutes) return segment;
            return null;
        }
    }
}
