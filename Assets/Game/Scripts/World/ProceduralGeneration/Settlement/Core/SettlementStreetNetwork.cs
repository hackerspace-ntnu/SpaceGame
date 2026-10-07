// Grows a settlement's streets the way Parish & Müller's extended L-system grows a city's roads.
// A street is a module that rewrites into "one segment + the same street again", and now and then
// also into a branch module one order down (main street -> side street -> alley) leaving at roughly a
// right angle. Every proposed segment then has to pass local constraints before it is accepted: it
// stops where it meets another street or comes within snapDistance of one (a T junction), and it
// never runs through a building already placed. Proposals are taken in order of when they fall due,
// and branches are due later than the street they leave, so the main street runs out first and the
// side streets and alleys fill in behind it. Growth stops once there is enough street frontage for
// the buildings asked for, and can be resumed if they did not all fit.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public sealed class SettlementStreetNetwork
    {
        public const int MainStreet = 0, SideStreet = 1, Alley = 2;
        // How much later than its parent's next segment a new branch falls due, per order.
        private const float BranchDelay = 3f;
        // A truncated segment shorter than this is dropped instead of kept as a stub.
        private const float ShortestSegment = 1f;

        public sealed class Street
        {
            public readonly int order;
            public readonly float halfWidth;
            /// <summary>World XZ, from where the street starts outward.</summary>
            public readonly List<Vector2> points = new();
            /// <summary>Distance along the street to each point.</summary>
            public readonly List<float> arcs = new();
            /// <summary>The street this one branched off (-1 for the main street) and where along it.</summary>
            public readonly int parent;
            public readonly float parentArc;
            /// <summary>The street this one ran into (-1 if it just ends) and where along it.</summary>
            public int endsOn = -1;
            public float endsOnArc;
            internal readonly float[] lastBranch = { 0f, 0f };

            public Street(int order, float halfWidth, int parent, float parentArc, Vector2 start)
            {
                this.order = order;
                this.halfWidth = halfWidth;
                this.parent = parent;
                this.parentArc = parentArc;
                points.Add(start);
                arcs.Add(0f);
            }

            public float Length => arcs[arcs.Count - 1];

            internal void Append(Vector2 point)
            {
                arcs.Add(Length + Vector2.Distance(points[points.Count - 1], point));
                points.Add(point);
            }

            public Vector2 PointAt(float s)
            {
                int i = SegmentAt(s);
                if (i < 0) return points[0];
                float length = arcs[i + 1] - arcs[i];
                return Vector2.Lerp(points[i], points[i + 1], length > 0f ? (s - arcs[i]) / length : 0f);
            }

            public Vector2 TangentAt(float s)
            {
                int i = SegmentAt(s);
                return i < 0 ? Vector2.up : (points[i + 1] - points[i]).normalized;
            }

            /// <summary>Distance along the street to the point nearest <paramref name="p"/>, and how far that is.</summary>
            public float Project(Vector2 p, out float distance)
            {
                distance = float.PositiveInfinity;
                float best = 0f;
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    Vector2 a = points[i], ab = points[i + 1] - a;
                    float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    float d = Vector2.Distance(p, a + ab * t);
                    if (d >= distance) continue;
                    distance = d;
                    best = arcs[i] + t * ab.magnitude;
                }
                return best;
            }

            private int SegmentAt(float s)
            {
                if (points.Count < 2) return -1;
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    if (s <= arcs[i + 1]) return i;
                }
                return points.Count - 2;
            }
        }

        private struct Proposal
        {
            public int street;            // -1: a new street, created if its first segment is accepted
            public Vector2 from, direction;
            public int order, parent;
            public float parentArc;
        }

        public readonly List<Street> streets = new();
        private readonly SettlementStreetStyle style;
        private readonly float[] halfWidthOfOrder;
        private readonly System.Func<Vector2, float> groundHeight;
        private readonly List<Proposal> proposals = new();
        private readonly List<(int proposal, float due)> queue = new();
        private int segmentCount;

        /// <param name="halfWidthOfOrder">Half the width of a main street, side street and alley, verges included.</param>
        /// <param name="groundHeight">Natural ground height at a world XZ, for the contour bias.</param>
        /// <param name="axes">Directions the main streets leave the centre in (a plaza's lanes); null = one main street
        /// running both ways at a random angle.</param>
        public SettlementStreetNetwork(SettlementStreetStyle style, Vector2 center, float[] halfWidthOfOrder,
                                       System.Func<Vector2, float> groundHeight, ref SettlementPlacementUtil.SeededRng rng,
                                       IReadOnlyList<Vector2> axes = null)
        {
            this.style = style;
            this.halfWidthOfOrder = halfWidthOfOrder;
            this.groundHeight = groundHeight;

            if (axes == null)
            {
                // Axiom: one main street running both ways out of the centre.
                float angle = rng.NextFloat01() * Mathf.PI * 2f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                axes = new[] { direction, -direction };
            }
            foreach (var axis in axes)
                Propose(new Proposal { street = -1, from = center, direction = axis, order = MainStreet, parent = -1 }, 0f);
        }

        public bool CanGrow => queue.Count > 0 && segmentCount < style.maxSegments;

        /// <summary>Street frontage so far: both sides of every street.</summary>
        public float Frontage
        {
            get
            {
                float total = 0f;
                foreach (var street in streets) total += 2f * street.Length;
                return total;
            }
        }

        /// <summary>Grows until <see cref="Frontage"/> reaches <paramref name="target"/> or nothing more can grow.</summary>
        /// <param name="obstacles">Buildings already placed, in world XZ; no street runs through them.</param>
        public void Grow(float target, List<SettlementFootprint> obstacles, ref SettlementPlacementUtil.SeededRng rng)
        {
            while (Frontage < target && CanGrow)
            {
                int next = 0;
                for (int i = 1; i < queue.Count; i++) if (queue[i].due < queue[next].due) next = i;
                var (index, due) = queue[next];
                queue.RemoveAt(next);
                Expand(proposals[index], due, obstacles, ref rng);
            }
        }

        /// <summary>
        /// How far along each street to keep once its houses are placed: to <paramref name="lastHouse"/> (arc
        /// where its farthest house ends, negative if it has none) plus <paramref name="margin"/>, and at least
        /// as far as a kept street leaves it or runs into it untrimmed. 0 = drop the street. If nothing would
        /// be kept, the main street is kept whole.
        /// </summary>
        public static float[] KeepLengths(IReadOnlyList<Street> streets, float[] lastHouse, float margin)
        {
            var keep = new float[streets.Count];
            for (int s = 0; s < streets.Count; s++)
                keep[s] = lastHouse[s] < 0f ? 0f : Mathf.Min(streets[s].Length, lastHouse[s] + margin);

            // A street a kept one branches off or runs into stays long enough to meet it; repeat until nothing moves.
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int c = 0; c < streets.Count; c++)
                {
                    if (keep[c] <= 0f) continue;
                    changed |= Extend(keep, streets, streets[c].parent, streets[c].parentArc);
                    bool reaches = keep[c] >= streets[c].Length - ShortestSegment;
                    if (reaches) changed |= Extend(keep, streets, streets[c].endsOn, streets[c].endsOnArc);
                }
            }

            bool any = false;
            foreach (float k in keep) any |= k > 0f;
            if (!any && streets.Count > 0) keep[0] = streets[0].Length;
            return keep;
        }

        private static bool Extend(float[] keep, IReadOnlyList<Street> streets, int street, float arc)
        {
            if (street < 0) return false;
            float needed = Mathf.Min(streets[street].Length, Mathf.Max(arc, ShortestSegment));
            if (keep[street] >= needed) return false;
            keep[street] = needed;
            return true;
        }

        /// <summary>
        /// Cuts every street back to <paramref name="keep"/> metres and removes those kept at 0; growth is
        /// over after this. A street cut short of the street it ran into no longer ends on it. Returns each
        /// old index's new one, -1 for a removed street.
        /// </summary>
        public int[] Trim(float[] keep)
        {
            queue.Clear();
            var map = new int[streets.Count];
            var kept = new List<Street>();
            for (int s = 0; s < streets.Count; s++)
            {
                map[s] = keep[s] > 0f ? kept.Count : -1;
                if (keep[s] <= 0f) continue;
                Street old = streets[s];
                var cut = new Street(old.order, old.halfWidth, old.parent, old.parentArc, old.points[0]);
                for (int i = 1; i < old.points.Count && old.arcs[i - 1] < keep[s]; i++)
                    cut.Append(old.arcs[i] <= keep[s] ? old.points[i] : old.PointAt(keep[s]));
                if (keep[s] >= old.Length - ShortestSegment)
                {
                    cut.endsOn = old.endsOn;
                    cut.endsOnArc = old.endsOnArc;
                }
                kept.Add(cut);
            }

            streets.Clear();
            foreach (var street in kept)
            {
                streets.Add(street.parent < 0 && street.endsOn < 0 ? street
                    : Remapped(street, street.parent < 0 ? -1 : map[street.parent], street.endsOn < 0 ? -1 : map[street.endsOn]));
            }
            return map;
        }

        private static Street Remapped(Street street, int parent, int endsOn)
        {
            var copy = new Street(street.order, street.halfWidth, parent, street.parentArc, street.points[0]);
            for (int i = 1; i < street.points.Count; i++) copy.Append(street.points[i]);
            copy.endsOn = endsOn;
            copy.endsOnArc = street.endsOnArc;
            return copy;
        }

        /// <summary>Every street segment as a rectangle as wide as its street, in world XZ.</summary>
        public List<SettlementFootprint> Corridors()
        {
            var corridors = new List<SettlementFootprint>();
            foreach (var street in streets)
            {
                for (int i = 0; i + 1 < street.points.Count; i++)
                    corridors.Add(Corridor(street.points[i], street.points[i + 1], street.halfWidth));
            }
            return corridors;
        }

        private void Propose(Proposal proposal, float due)
        {
            proposals.Add(proposal);
            queue.Add((proposals.Count - 1, due));
        }

        private void Expand(Proposal p, float due, List<SettlementFootprint> obstacles, ref SettlementPlacementUtil.SeededRng rng)
        {
            Vector2 direction = Bend(p.from, p.direction, ref rng);
            Vector2 end = p.from + direction * style.segmentLength;
            int own = p.street;

            // Local constraints: stop at the first street it would cross, else snap onto one it ends near.
            int endsOn = -1;
            float endsOnArc = 0f;
            if (FirstCrossing(p.from, end, own, out Vector2 hit, out int hitStreet))
            {
                end = hit;
                endsOn = hitStreet;
            }
            else if (NearestStreet(end, own, p.from, out Vector2 snapped, out int snapStreet))
            {
                end = snapped;
                endsOn = snapStreet;
            }
            if (endsOn >= 0) endsOnArc = streets[endsOn].Project(end, out _);

            float halfWidth = halfWidthOfOrder[p.order];
            if (Vector2.Distance(p.from, end) < ShortestSegment) return;
            SettlementFootprint corridor = Corridor(p.from, end, halfWidth);
            foreach (var obstacle in obstacles)
            {
                if (SettlementFootprint.Gap(corridor, obstacle) < 0f) return;
            }

            if (own < 0)
            {
                streets.Add(new Street(p.order, halfWidth, p.parent, p.parentArc, p.from));
                own = streets.Count - 1;
            }
            Street street = streets[own];
            street.Append(end);
            segmentCount++;

            if (endsOn >= 0)
            {
                street.endsOn = endsOn;
                street.endsOnArc = endsOnArc;
                return;
            }

            Propose(new Proposal { street = own, from = end, direction = direction, order = p.order, parent = p.parent, parentArc = p.parentArc }, due + 1f);
            if (p.order >= Alley) return;

            float chance = p.order == MainStreet ? style.sideStreetChance : style.alleyChance;
            for (int side = 0; side < 2; side++)
            {
                if (street.Length - street.lastBranch[side] < style.branchSpacing) continue;
                if (!rng.NextChance(chance)) continue;
                street.lastBranch[side] = street.Length;
                float turn = (side == 0 ? 90f : -90f) + rng.NextRange(-style.branchAngleJitter, style.branchAngleJitter);
                Propose(new Proposal
                {
                    street = -1, from = end, direction = Rotate(direction, turn),
                    order = p.order + 1, parent = own, parentArc = street.Length,
                }, due + 1f + BranchDelay * (p.order + 1));
            }
        }

        /// <summary>Straight on, or up to maxTurn either way; the flattest of the three when the contour bias rolls.</summary>
        private Vector2 Bend(Vector2 from, Vector2 direction, ref SettlementPlacementUtil.SeededRng rng)
        {
            Vector2[] options = { direction, Rotate(direction, style.maxTurn), Rotate(direction, -style.maxTurn) };
            if (!rng.NextChance(style.contourBias)) return options[rng.NextIndex(options.Length)];

            float here = groundHeight(from);
            Vector2 flattest = direction;
            float least = float.PositiveInfinity;
            foreach (var option in options)
            {
                float climb = Mathf.Abs(groundHeight(from + option * style.segmentLength) - here);
                if (climb >= least) continue;
                least = climb;
                flattest = option;
            }
            return flattest;
        }

        /// <summary>The first point where from→to crosses another street's segment (not its own street, and not at its own start).</summary>
        private bool FirstCrossing(Vector2 from, Vector2 to, int own, out Vector2 hit, out int street)
        {
            hit = to;
            street = -1;
            float nearest = 1f;
            for (int s = 0; s < streets.Count; s++)
            {
                if (s == own) continue;
                var points = streets[s].points;
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    if (!SegmentsCross(from, to, points[i], points[i + 1], out float t)) continue;
                    if (t <= 0.02f || t >= nearest) continue;   // leaving the street it branched off is not a crossing
                    nearest = t;
                    street = s;
                }
            }
            if (street < 0) return false;
            hit = Vector2.Lerp(from, to, nearest);
            return true;
        }

        /// <summary>The nearest point on another street within snapDistance of <paramref name="end"/>, ignoring the one the segment starts on.</summary>
        private bool NearestStreet(Vector2 end, int own, Vector2 from, out Vector2 snapped, out int street)
        {
            snapped = end;
            street = -1;
            float best = style.snapDistance;
            for (int s = 0; s < streets.Count; s++)
            {
                if (s == own) continue;
                float arc = streets[s].Project(end, out float distance);
                if (distance >= best) continue;
                Vector2 point = streets[s].PointAt(arc);
                if (Vector2.Distance(point, from) < style.snapDistance) continue;   // the street it just left
                best = distance;
                snapped = point;
                street = s;
            }
            return street >= 0;
        }

        private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t)
        {
            t = 0f;
            Vector2 r = b - a, s = d - c;
            float denominator = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denominator) < 1e-6f) return false;
            Vector2 ac = c - a;
            t = (ac.x * s.y - ac.y * s.x) / denominator;
            float u = (ac.x * r.y - ac.y * r.x) / denominator;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        public static SettlementFootprint Corridor(Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 along = b - a;
            Quaternion yaw = Quaternion.LookRotation(new Vector3(along.x, 0f, along.y), Vector3.up);
            return new SettlementFootprint((a + b) * 0.5f, new Vector2(halfWidth * 2f, along.magnitude), yaw);
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
