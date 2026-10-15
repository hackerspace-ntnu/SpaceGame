// Lines a settlement's streets with its buildings, shoulder to shoulder and all facing the street.
// Streets are walked in the order they grew -- the main street out from the centre first -- both
// sides, each with a cursor that only moves outward; at every stop the largest building still waiting
// that fits is placed, so the big buildings end up on the main street near the centre and the small
// ones along the side streets and alleys. A building's front (its local +Z, where the nomad buildings
// have their main door) faces the street, set back `setback` from its edge.
//
// The town is dense at its core and loosens toward its rim: the gap after each house grows from
// minBuildingGap inside the style's denseCore to up to maxBuildingGap at the town's radius (estimated
// from the buildings' total footprint and the style's buildingCoverage). Inside the core a gap is now
// and then left as a passage, and once a round of street-front houses is placed a house is built behind
// each passage -- a back row, facing the street with its door on the passage's axis, so its path reaches
// the street between the two houses in front of it.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public sealed class SettlementStreetPlots
    {
        // How many of the waiting buildings are tried at one spot before the cursor moves on.
        private const int CandidatesPerSpot = 12;
        // How far the cursor moves when nothing fits at a spot.
        private const float CursorStep = 2f;

        public readonly struct Plot
        {
            public readonly GameObject prefab;
            public readonly Vector2 pivot;
            public readonly Quaternion rotation;
            /// <summary>World XZ.</summary>
            public readonly SettlementFootprint footprint;
            public readonly int street;
            /// <summary>Distance along the street to the middle of the building's front (its passage, for a back-row house).</summary>
            public readonly float arc;
            /// <summary>Faces a plaza's centre rather than its street, which it only belongs to for its level and trimming.</summary>
            public readonly bool facesPlaza;

            public Plot(GameObject prefab, Vector2 pivot, Quaternion rotation, SettlementFootprint footprint, int street, float arc,
                        bool facesPlaza = false)
            {
                this.prefab = prefab;
                this.pivot = pivot;
                this.rotation = rotation;
                this.footprint = footprint;
                this.street = street;
                this.arc = arc;
                this.facesPlaza = facesPlaza;
            }

            public Plot OnStreet(int newStreet, float newArc) => new Plot(prefab, pivot, rotation, footprint, newStreet, newArc, facesPlaza);
            public Plot OnStreet(int newStreet) => OnStreet(newStreet, arc);

            /// <summary>
            /// A building whose front's point <paramref name="alignX"/> (prefab-local X) stands at <paramref name="frontPoint"/>,
            /// its front (local +Z) facing along <paramref name="facing"/>.
            /// </summary>
            public static Plot Facing(in Building building, Vector2 frontPoint, Vector2 facing, float alignX, int street, float arc, bool facesPlaza)
            {
                Quaternion rotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up);
                Vector2 pivot = frontPoint - Rotate(new Vector2(alignX, building.local.yMax), rotation);
                var footprint = new SettlementFootprint(pivot + Rotate(building.local.center, rotation), building.local.size, rotation);
                return new Plot(building.prefab, pivot, rotation, footprint, street, arc, facesPlaza);
            }
        }

        public readonly struct Building
        {
            public readonly GameObject prefab;
            /// <summary>Footprint in the prefab's own frame (x = X, y = Z), pivot offset kept.</summary>
            public readonly Rect local;
            /// <summary>Prefab-local X of the main door on the front (+Z) side.</summary>
            public readonly float doorX;

            public Building(GameObject prefab, Rect local)
            {
                this.prefab = prefab;
                this.local = local;
                doorX = SettlementEntrance.LocalDoorX(prefab, local);
            }
        }

        private readonly struct Passage
        {
            public readonly int street, side;
            /// <summary>Arc of the passage's middle.</summary>
            public readonly float arc;

            public Passage(int street, int side, float arc)
            {
                this.street = street;
                this.side = side;
                this.arc = arc;
            }
        }

        public readonly List<Plot> plots = new();
        /// <summary>Houses built behind a passage rather than on a street front.</summary>
        public int BackRows { get; private set; }
        private readonly Dictionary<(int street, int side), float> cursors = new();
        private readonly List<Passage> passages = new();
        private readonly SettlementStreetStyle style;
        private readonly float minGap, maxGap;
        private readonly Vector2 center;
        private readonly float townRadius;
        private readonly float mainStartArc;

        /// <param name="townRadius">How far the town reaches; the density falls off over it.</param>
        /// <param name="mainStartArc">How far out from the centre the streets that start there are lined: past a plaza, else 0.</param>
        public SettlementStreetPlots(SettlementStreetStyle style, float minGap, float maxGap, Vector2 center, float townRadius,
                                     float mainStartArc = 0f)
        {
            this.style = style;
            this.minGap = minGap;
            this.maxGap = maxGap;
            this.center = center;
            this.townRadius = Mathf.Max(townRadius, 1f);
            this.mainStartArc = mainStartArc;
        }

        /// <summary>Adds plots placed elsewhere (round a plaza); streets are then lined around them.</summary>
        public void AddFixed(IEnumerable<Plot> fixedPlots) => plots.AddRange(fixedPlots);

        /// <summary>Gives every plaza-facing plot the street out of the centre nearest it, so it shares that street's level and keeps it from being trimmed away.</summary>
        public void AttachPlazaPlots(SettlementStreetNetwork network)
        {
            for (int i = 0; i < plots.Count; i++)
            {
                if (!plots[i].facesPlaza) continue;
                int best = -1;
                float bestDistance = float.PositiveInfinity, bestArc = 0f;
                for (int s = 0; s < network.streets.Count; s++)
                {
                    if (network.streets[s].parent >= 0) continue;
                    float arc = network.streets[s].Project(plots[i].footprint.center, out float distance);
                    if (distance >= bestDistance) continue;
                    best = s;
                    bestDistance = distance;
                    bestArc = arc;
                }
                if (best >= 0) plots[i] = plots[i].OnStreet(best, bestArc);
            }
        }

        /// <summary>The radius a town of these buildings covers at <paramref name="coverage"/> of its ground built on.</summary>
        public static float TownRadius(List<Building> buildings, float coverage)
        {
            float area = 0f;
            foreach (var building in buildings) area += building.local.width * building.local.height;
            return Mathf.Sqrt(area / (Mathf.PI * Mathf.Max(coverage, 0.01f)));
        }

        /// <summary>0 inside the dense core, rising to 1 at the town's radius.</summary>
        public float Looseness(Vector2 point)
        {
            float t = Vector2.Distance(point, center) / townRadius;
            return style.denseCore >= 1f ? 0f : Mathf.Clamp01((t - style.denseCore) / (1f - style.denseCore));
        }

        public List<SettlementFootprint> Footprints()
        {
            var footprints = new List<SettlementFootprint>(plots.Count);
            foreach (var plot in plots) footprints.Add(plot.footprint);
            return footprints;
        }

        /// <summary>
        /// Places as many of <paramref name="waiting"/> (largest first) as the streets have room for,
        /// removing each one placed, then a back-row house behind every passage left. Streets keep their
        /// cursors, so calling again after the network has grown carries on where this call stopped.
        /// </summary>
        public void Place(SettlementStreetNetwork network, List<Building> waiting, ref SettlementPlacementUtil.SeededRng rng)
        {
            List<SettlementFootprint> corridors = network.Corridors();
            for (int s = 0; s < network.streets.Count && waiting.Count > 0; s++)
            {
                for (int side = 0; side < 2 && waiting.Count > 0; side++)
                    LineStreetSide(network.streets[s], s, side, corridors, waiting, ref rng);
            }
            foreach (var passage in passages)
            {
                if (waiting.Count == 0) break;
                TryBackRow(network.streets[passage.street], passage, corridors, waiting);
            }
            passages.Clear();
        }

        /// <summary>Where along each street its farthest house ends, -1 for a street with none.</summary>
        public float[] LastHouseArcs(SettlementStreetNetwork network)
        {
            var last = new float[network.streets.Count];
            for (int s = 0; s < last.Length; s++) last[s] = -1f;
            foreach (var plot in plots)
            {
                Vector2 tangent = network.streets[plot.street].TangentAt(plot.arc);
                last[plot.street] = Mathf.Max(last[plot.street], plot.arc + plot.footprint.ExtentAlong(tangent));
            }
            return last;
        }

        /// <summary>Follows <see cref="SettlementStreetNetwork.Trim"/>'s renumbering.</summary>
        public void Renumber(int[] map)
        {
            for (int i = 0; i < plots.Count; i++) plots[i] = plots[i].OnStreet(map[plots[i].street]);
        }

        private void LineStreetSide(SettlementStreetNetwork.Street street, int index, int side, List<SettlementFootprint> corridors,
                                    List<Building> waiting, ref SettlementPlacementUtil.SeededRng rng)
        {
            var key = (index, side);
            if (!cursors.TryGetValue(key, out float cursor)) cursor = street.parent < 0 ? mainStartArc : 0f;
            while (waiting.Count > 0 && cursor < street.Length)
            {
                // Out of street for now: keep the cursor here, the street may grow further.
                float narrowest = float.PositiveInfinity;
                for (int i = 0; i < waiting.Count && i < CandidatesPerSpot; i++) narrowest = Mathf.Min(narrowest, waiting[i].local.width);
                if (cursor + narrowest > street.Length) break;

                bool placed = false;
                for (int i = 0; i < waiting.Count && i < CandidatesPerSpot; i++)
                {
                    float arc = cursor + waiting[i].local.width * 0.5f;
                    if (!TryPlot(street, index, side, arc, 0f, waiting[i], waiting[i].local.center.x, corridors, out Plot plot)) continue;
                    plots.Add(plot);
                    float end = arc + waiting[i].local.width * 0.5f;
                    waiting.RemoveAt(i);
                    cursor = end + NextGap(index, side, end, plot.footprint.center, waiting.Count, ref rng);
                    placed = true;
                    break;
                }
                if (!placed) cursor += CursorStep;
            }
            cursors[key] = cursor;
        }

        // The gap after a house whose side ends at `end`: a passage to a back row, or minGap..maxGap by how loose the town is there.
        private float NextGap(int street, int side, float end, Vector2 house, int stillWaiting, ref SettlementPlacementUtil.SeededRng rng)
        {
            float looseness = Looseness(house);
            if (looseness <= 0f && stillWaiting > 1 && rng.NextChance(style.backRowChance))
            {
                passages.Add(new Passage(street, side, end + style.passageWidth * 0.5f));
                return style.passageWidth;
            }
            return Mathf.Lerp(minGap, maxGap, looseness * rng.NextFloat01());
        }

        // A house behind the passage, set back past the deepest street-front house beside it, its door on the passage's axis.
        private void TryBackRow(SettlementStreetNetwork.Street street, in Passage passage, List<SettlementFootprint> corridors, List<Building> waiting)
        {
            Vector2 at = street.PointAt(passage.arc);
            Vector2 outward = Outward(street, passage.arc, passage.side);
            float behind = street.halfWidth + style.setback;
            foreach (var plot in plots)
            {
                if (plot.street != passage.street) continue;
                SettlementFootprint fp = plot.footprint;
                float along = Vector2.Dot(fp.center - at, street.TangentAt(passage.arc));
                if (Mathf.Abs(along) - fp.ExtentAlong(street.TangentAt(passage.arc)) > style.passageWidth) continue;
                float back = Vector2.Dot(fp.center - at, outward) + fp.ExtentAlong(outward);
                if (back > 0f) behind = Mathf.Max(behind, back);
            }

            for (int i = 0; i < waiting.Count && i < CandidatesPerSpot; i++)
            {
                if (!TryPlot(street, passage.street, passage.side, passage.arc, behind + style.backRowGap - (street.halfWidth + style.setback),
                             waiting[i], waiting[i].doorX, corridors, out Plot plot)) continue;
                plots.Add(plot);
                waiting.RemoveAt(i);
                BackRows++;
                return;
            }
        }

        // A plot whose front middle (or, for a back row, door) is at `arc`, `extraSetback` further back than the street front.
        private bool TryPlot(SettlementStreetNetwork.Street street, int index, int side, float arc, float extraSetback, Building building,
                             float alignX, List<SettlementFootprint> corridors, out Plot plot)
        {
            plot = default;
            float width = building.local.width;
            if (arc - width * 0.5f < 0f || arc + width * 0.5f > street.Length) return false;

            Vector2 outward = Outward(street, arc, side);
            // The building's front faces the street: its local +Z points back toward it.
            Vector2 frontPoint = street.PointAt(arc) + outward * (street.halfWidth + style.setback + extraSetback);
            Plot candidate = Plot.Facing(building, frontPoint, -outward, alignX, index, arc, facesPlaza: false);

            foreach (var corridor in corridors)
            {
                if (SettlementFootprint.Gap(candidate.footprint, corridor) < 0f) return false;
            }
            foreach (var other in plots)
            {
                if (SettlementFootprint.Gap(candidate.footprint, other.footprint) < minGap) return false;
            }
            plot = candidate;
            return true;
        }

        private static Vector2 Outward(SettlementStreetNetwork.Street street, float arc, int side)
        {
            Vector2 tangent = street.TangentAt(arc);
            return side == 0 ? new Vector2(-tangent.y, tangent.x) : new Vector2(tangent.y, -tangent.x);
        }

        private static Vector2 Rotate(Vector2 xz, Quaternion rotation)
        {
            Vector3 r = rotation * new Vector3(xz.x, 0f, xz.y);
            return new Vector2(r.x, r.z);
        }
    }
}
