// Which surface each stretch of a planned settlement's streets gets, by importance: the stretch of
// the main street nearest the centre is the town's spine and gets the road; slabs come next, on main
// and side streets out from the centre; everything else -- the outskirts, every alley -- is a stone
// path. Budgets are shares of the whole network's length, so a small town gets little paving, and a
// town below minBuildingsForRoad gets no road at all. Going out along a street the surface only ever
// steps down (road, then slabs, then path), never back up. Pure; decided once the streets are final.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementStreetSurfaces
    {
        /// <summary>A stretch of one street, arc <see cref="from"/> to <see cref="to"/>, with one surface.</summary>
        public readonly struct Section
        {
            public readonly float from, to;
            public readonly SettlementStreetStyle.StreetSurface surface;

            public Section(float from, float to, SettlementStreetStyle.StreetSurface surface)
            {
                this.from = from;
                this.to = to;
                this.surface = surface;
            }
        }

        /// <summary>Road above slabs above stone path: lower is more important.</summary>
        public static int Rank(SettlementStreetStyle.StreetSurface surface) => surface switch
        {
            SettlementStreetStyle.StreetSurface.Road => 0,
            SettlementStreetStyle.StreetSurface.Slabs => 1,
            _ => 2,
        };

        /// <summary>Every street's sections, in order along it, covering it end to end.</summary>
        /// <param name="buildings">How many buildings the settlement has; below the style's minimum it gets no road.</param>
        public static List<Section>[] Assign(IReadOnlyList<SettlementStreetNetwork.Street> streets, Vector2 center,
                                             SettlementStreetStyle style, int buildings)
        {
            // Every segment of every street, the most important first.
            var segments = new List<(int street, int index, float score)>();
            float total = 0f;
            for (int s = 0; s < streets.Count; s++)
            {
                var street = streets[s];
                for (int i = 0; i + 1 < street.points.Count; i++)
                {
                    Vector2 middle = (street.points[i] + street.points[i + 1]) * 0.5f;
                    segments.Add((s, i, Vector2.Distance(middle, center) + street.order * style.orderRankDistance));
                    total += street.arcs[i + 1] - street.arcs[i];
                }
            }
            segments.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score)
                                  : a.street != b.street ? a.street.CompareTo(b.street) : a.index.CompareTo(b.index));

            bool roads = buildings >= style.minBuildingsForRoad && HasPieces(style.road.tiles);
            bool slabs = HasPieces(style.slabs);
            float roadLeft = roads ? style.roadShare * total : 0f;
            float slabLeft = slabs ? style.slabShare * total : 0f;
            var surfaces = new SettlementStreetStyle.StreetSurface[streets.Count][];
            for (int s = 0; s < streets.Count; s++)
                surfaces[s] = new SettlementStreetStyle.StreetSurface[Mathf.Max(0, streets[s].points.Count - 1)];
            foreach (var (s, i, _) in segments)
            {
                var street = streets[s];
                float length = street.arcs[i + 1] - street.arcs[i];
                var surface = SettlementStreetStyle.StreetSurface.StonePath;
                if (street.order == SettlementStreetNetwork.MainStreet && roadLeft > 0f)
                {
                    surface = SettlementStreetStyle.StreetSurface.Road;
                    roadLeft -= length;
                }
                else if (street.order <= SettlementStreetNetwork.SideStreet && slabLeft > 0f)
                {
                    surface = SettlementStreetStyle.StreetSurface.Slabs;
                    slabLeft -= length;
                }
                surfaces[s][i] = surface;
            }

            var sections = new List<Section>[streets.Count];
            for (int s = 0; s < streets.Count; s++)
            {
                sections[s] = new List<Section>();
                var street = streets[s];
                var bySegment = surfaces[s];
                for (int i = 0; i < bySegment.Length; i++)
                {
                    // Outward along a street the surface never steps back up.
                    if (i > 0 && Rank(bySegment[i]) < Rank(bySegment[i - 1])) bySegment[i] = bySegment[i - 1];
                    var list = sections[s];
                    if (list.Count > 0 && list[list.Count - 1].surface == bySegment[i])
                        list[list.Count - 1] = new Section(list[list.Count - 1].from, street.arcs[i + 1], bySegment[i]);
                    else list.Add(new Section(street.arcs[i], street.arcs[i + 1], bySegment[i]));
                }
            }
            return sections;
        }

        /// <summary>The surface at <paramref name="arc"/> along a street with these sections.</summary>
        public static SettlementStreetStyle.StreetSurface At(List<Section> sections, float arc)
        {
            foreach (var section in sections)
            {
                if (arc <= section.to) return section.surface;
            }
            return sections.Count > 0 ? sections[sections.Count - 1].surface : SettlementStreetStyle.StreetSurface.StonePath;
        }

        /// <summary>Whether a street's level may step at <paramref name="arc"/>: anywhere but on a stone path, which follows the ground.</summary>
        public static bool IsTerraced(List<Section> sections, float arc) =>
            At(sections, arc) != SettlementStreetStyle.StreetSurface.StonePath;

        private static bool HasPieces(SettlementStreetStyle.WeightedPiece[] set)
        {
            foreach (var piece in set)
            {
                if (piece.prefab != null && piece.weight > 0f) return true;
            }
            return false;
        }
    }
}
