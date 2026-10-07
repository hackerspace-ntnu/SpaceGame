// An outrider's trip as plan segments: out from the door inside departWindow, one leg per TripKind flag
// to a baked trip point, then back to the door by returnBy. Legs are chained and sized from the baked
// travel table, so the walking time is already in the plan; a leg that cannot fit with its minimum
// dwell is dropped rather than squeezed. Trip points are not booked — they lie outside the settlement.
using System;
using System.Collections.Generic;

namespace SpaceGame.Agents.Residents
{
    public static class TripPlanner
    {
        /// <summary>
        /// Appends the trip to <paramref name="segs"/>, whose last segment is where the resident sets off from.
        /// Appends nothing when the resident has no trips, there are no trip points, or none fits the day.
        /// </summary>
        public static void AddTrip(List<PlanSegment> segs, PlannerResident r, int day, System.Random rng, IReadOnlyList<PlannerPlace> places,
                                   Func<int, int, float> travel, ResidentTuning t, float minDwellMinutes = 0f)
        {
            if (r.trips == TripKind.None || segs.Count == 0) return;
            var points = new List<int>();
            foreach (PlannerPlace place in places)
                if (place.kind == PlaceKind.Trip) points.Add(place.index);
            if (points.Count == 0) return;

            float dayStart = day * DayPlanner.MinutesPerDay, homeBy = dayStart + t.returnBy;
            PlanSegment setOff = segs[segs.Count - 1];
            float leaveAt = Math.Max(setOff.arrive + minDwellMinutes, dayStart + DayPlanner.Range(t.departWindow, rng));

            var route = new List<int>();
            int at = setOff.place, legs = LegCount(r.trips), offset = rng.Next(points.Count);
            float walked = 0f;
            for (int k = 0; k < points.Count && route.Count < legs; k++)
            {
                int point = points[(offset + k) % points.Count];
                float roundTrip = walked + travel(at, point) + travel(point, r.homeIndex);
                if (leaveAt + roundTrip + (route.Count + 1) * minDwellMinutes > homeBy) continue;
                route.Add(point);
                walked += travel(at, point);
                at = point;
            }
            if (route.Count == 0) return;

            walked += travel(at, r.homeIndex);
            float dwell = (homeBy - leaveAt - walked) / route.Count;
            float clock = leaveAt;
            int from = setOff.place;
            foreach (int point in route)
            {
                clock = DayPlanner.Append(segs, Activity.Trip, point, clock + travel(from, point), travel) + dwell;
                from = point;
            }
            DayPlanner.Append(segs, Activity.Break, r.homeIndex, clock + travel(from, r.homeIndex), travel);
        }

        private static int LegCount(TripKind trips)
        {
            int count = 0;
            for (int bits = (int)trips; bits != 0; bits &= bits - 1) count++;
            return count;
        }
    }
}
