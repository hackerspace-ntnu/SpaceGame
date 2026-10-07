// A planned settlement's heart, its town centre: an open, level plaza ringed by its largest buildings, all facing the
// centre, with a few lanes leaving it through gaps left in the ring at evenly spread angles. The rest of
// the buildings line those lanes. The ring's radius is whatever lets its buildings and lane gaps go
// round it without touching, never less than the style's minimum; buildings are spread evenly between
// the lanes, the largest first into the emptiest stretch. Pure placement: SettlementStreetLayout grows
// the lanes from the centre along the returned axes and lines them with the rest.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementPlaza
    {
        // The ring's radius grows by this factor until its buildings and lane gaps fit round it.
        private const float RadiusGrowth = 1.1f;
        private const int MaxRadiusTries = 40;
        // A plaza needs at least this many buildings round it to read as one.
        private const int MinRing = 3;

        public sealed class Layout
        {
            /// <summary>Distance from the centre to the fronts of the ring's buildings.</summary>
            public float radius;
            /// <summary>The ring's buildings, facing the centre; their street is set once the lanes exist.</summary>
            public readonly List<SettlementStreetPlots.Plot> ring = new();
            /// <summary>Unit directions of the lanes leaving the plaza, from its centre.</summary>
            public readonly List<Vector2> lanes = new();
            /// <summary>Buildings left for the lanes, largest first.</summary>
            public readonly List<SettlementStreetPlots.Building> rest = new();
        }

        /// <param name="buildings">Largest first.</param>
        /// <param name="laneHalfWidth">Half a lane's corridor: the gap left in the ring for it.</param>
        public static Layout Plan(List<SettlementStreetPlots.Building> buildings, SettlementStreetStyle style, float gap,
                                  Vector2 center, float laneHalfWidth, ref SettlementPlacementUtil.SeededRng rng)
        {
            var layout = new Layout();
            int wanted = Mathf.Clamp(Mathf.RoundToInt(buildings.Count * style.plazaHouseShare), Mathf.Min(buildings.Count, MinRing), buildings.Count);
            int laneCount = Mathf.Clamp(Mathf.CeilToInt((buildings.Count - wanted) / (float)style.plazaHousesPerLane) + 1,
                                        style.plazaMinLanes, Mathf.Max(style.plazaMinLanes, style.plazaMaxLanes));
            float laneStep = 360f / laneCount;

            // The largest buildings that still fit round a plaza no wider than plazaMaxRadius ring it; a building that
            // would not goes to a lane, and a smaller one may take its place. If fewer than MinRing fit, the smallest
            // left join them and the plaza grows for them.
            var ring = new List<SettlementStreetPlots.Building>();
            foreach (var building in buildings)
            {
                if (ring.Count < wanted)
                {
                    ring.Add(building);
                    Distribute(ring, laneCount, laneStep, style.plazaMaxRadius, gap, laneHalfWidth, out bool fitsMax);
                    if (fitsMax) continue;
                    ring.RemoveAt(ring.Count - 1);
                }
                layout.rest.Add(building);
            }
            while (ring.Count < Mathf.Min(MinRing, buildings.Count))
            {
                ring.Add(layout.rest[layout.rest.Count - 1]);
                layout.rest.RemoveAt(layout.rest.Count - 1);
            }

            float start = rng.NextFloat01() * 360f;
            for (int k = 0; k < laneCount; k++) layout.lanes.Add(Direction(start + k * laneStep));

            // The smallest radius at which every stretch between two lanes holds the buildings put in it.
            float radius = style.plazaMinRadius;
            List<int>[] stretches = null;
            for (int attempt = 0; attempt < MaxRadiusTries; attempt++, radius *= RadiusGrowth)
            {
                stretches = Distribute(ring, laneCount, laneStep, radius, gap, laneHalfWidth, out bool fits);
                if (fits) break;
            }
            layout.radius = radius;
            buildings = ring;

            // Each stretch's buildings spread evenly over the arc it has between its two lane gaps.
            float laneAngle = Angle(laneHalfWidth * 2f + gap, radius);
            for (int k = 0; k < laneCount; k++)
            {
                var stretch = stretches[k];
                if (stretch.Count == 0) continue;
                float used = 0f;
                foreach (int b in stretch) used += Angle(buildings[b].local.width + gap, radius);
                float free = laneStep - laneAngle - used;
                float spacing = free / (stretch.Count + 1);
                float at = start + k * laneStep + laneAngle * 0.5f + spacing;
                foreach (int b in stretch)
                {
                    float width = Angle(buildings[b].local.width + gap, radius);
                    Vector2 outward = Direction(at + width * 0.5f);
                    layout.ring.Add(SettlementStreetPlots.Plot.Facing(buildings[b], center + outward * radius, -outward,
                                                                       buildings[b].local.center.x, street: -1, arc: radius, facesPlaza: true));
                    at += width + spacing;
                }
            }
            return layout;
        }

        // The ring's buildings into the stretches between lanes, each to the emptiest; whether they all fit.
        private static List<int>[] Distribute(List<SettlementStreetPlots.Building> buildings, int laneCount, float laneStep,
                                              float radius, float gap, float laneHalfWidth, out bool fits)
        {
            var stretches = new List<int>[laneCount];
            var free = new float[laneCount];
            float laneAngle = Angle(laneHalfWidth * 2f + gap, radius);
            for (int k = 0; k < laneCount; k++)
            {
                stretches[k] = new List<int>();
                free[k] = laneStep - laneAngle;
            }
            fits = true;
            for (int b = 0; b < buildings.Count; b++)
            {
                int emptiest = 0;
                for (int k = 1; k < laneCount; k++) if (free[k] > free[emptiest]) emptiest = k;
                stretches[emptiest].Add(b);
                free[emptiest] -= Angle(buildings[b].local.width + gap, radius);
                if (free[emptiest] < 0f) fits = false;
            }
            return stretches;
        }

        // Degrees a chord of `width` takes at `radius`.
        private static float Angle(float width, float radius) => 2f * Mathf.Atan2(width * 0.5f, radius) * Mathf.Rad2Deg;

        private static Vector2 Direction(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }
    }
}
