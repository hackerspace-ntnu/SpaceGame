// Levels the ground under a planned settlement: a grid of cells, each on a whole level (level k
// stands at baseY + k * stepHeight). Streets are locked to their own profile (flat runs, a few levels
// apart at most, so stairs can climb each change; a chain of flights gets its corridor locked as a
// ramp under its nosings) and every plot to the level of its street, so
// doors open onto the street at street height. Nothing else is terraced: free ground takes the level
// of the nearest street or plot only as a correction to its natural height, fading to nothing over
// gradeDistance. A retaining wall is needed only where two plots on different levels stand close
// together -- a building on each side -- and there the ground steps for real. Contours are where such
// walls may go; TerrainHeight is what the sculptor writes.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public sealed class SettlementTerraceField
    {
        /// <summary>A street's levels as runs: run i starts starts[i] metres along the street.</summary>
        public sealed class StreetProfile
        {
            public readonly List<float> starts = new();
            public readonly List<int> levels = new();

            public int LevelAt(float s)
            {
                int i = 0;
                while (i + 1 < starts.Count && starts[i + 1] <= s) i++;
                return levels[i];
            }

            /// <summary>Every change of level along the street, in order.</summary>
            public List<Step> Steps()
            {
                var steps = new List<Step>(Mathf.Max(0, starts.Count - 1));
                for (int j = 1; j < starts.Count; j++) steps.Add(new Step(starts[j], levels[j - 1], levels[j]));
                return steps;
            }
        }

        /// <summary>Where a street changes level, <see cref="arc"/> metres along it, from one level to another.</summary>
        public readonly struct Step
        {
            public readonly float arc;
            public readonly int fromLevel;
            public readonly int toLevel;

            public Step(float arc, int fromLevel, int toLevel)
            {
                this.arc = arc;
                this.fromLevel = fromLevel;
                this.toLevel = toLevel;
            }

            /// <summary>Levels climbed going along the street: positive up, negative down.</summary>
            public int Rise => toLevel - fromLevel;
        }

        /// <summary>A line between level <see cref="upperLevel"/> and the level below it, in world XZ.</summary>
        public readonly struct Contour
        {
            public readonly int upperLevel;
            public readonly List<Vector2> points;

            public Contour(int upperLevel, List<Vector2> points)
            {
                this.upperLevel = upperLevel;
                this.points = points;
            }
        }

        private static readonly Vector2Int[] Neighbours =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        };

        private readonly Vector2 origin;   // world XZ of cell (0, 0)'s corner
        private readonly int size;
        private readonly float cell;
        private readonly float baseY;
        private readonly float step;
        private readonly Func<Vector2, float> rawHeight;
        private readonly Func<Vector2, float> naturalHeight;
        private readonly int[] level;
        private readonly bool[] locked;
        private readonly bool[] plotOwned;   // locked by a plot, or nearest to one
        private readonly float[] lockedHeight;   // a plot's own ground height where it locked the cell; NaN for a street's cell
        private int[] source;                // nearest locked cell
        private float[] distance;            // metres to it
        private float[] correction;          // metres added to the natural ground
        private float[] held;                // ground held low behind a retaining wall; NaN elsewhere

        /// <param name="naturalHeight">Ground height before any shaping, at a world XZ.</param>
        /// <param name="groundSmoothing">Radius (m) the natural ground is averaged over before it is cut into levels.</param>
        public SettlementTerraceField(Vector2 center, float halfExtent, float cellSize, float stepHeight, float groundSmoothing,
                                      Func<Vector2, float> naturalHeight)
        {
            cell = cellSize;
            step = stepHeight;
            size = Mathf.Max(2, Mathf.CeilToInt(2f * halfExtent / cellSize));
            origin = center - new Vector2(halfExtent, halfExtent);
            level = new int[size * size];
            locked = new bool[size * size];
            plotOwned = new bool[size * size];
            lockedHeight = new float[size * size];
            Array.Fill(lockedHeight, float.NaN);
            rawHeight = naturalHeight;

            // Every bump in the ground would otherwise become its own ragged little terrace; the
            // levels follow the ground's broad shape instead, and the streets sample the same surface.
            var heights = new float[size * size];
            for (int i = 0; i < heights.Length; i++) heights[i] = naturalHeight(CentreOf(i));
            int radius = Mathf.RoundToInt(groundSmoothing / cellSize);
            for (int pass = 0; pass < SmoothingPasses; pass++) BoxBlur(heights, radius);
            this.naturalHeight = xz => heights[CellOf(xz)];
            baseY = this.naturalHeight(center);
        }

        // Box blurs in a row approximate a gaussian.
        private const int SmoothingPasses = 2;

        private void BoxBlur(float[] values, int radius)
        {
            if (radius <= 0) return;
            var temp = new float[values.Length];
            for (int axis = 0; axis < 2; axis++)
            {
                for (int line = 0; line < size; line++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int k = 0; k < radius && k < size; k++) { sum += values[Index(axis, line, k)]; count++; }
                    for (int k = 0; k < size; k++)
                    {
                        int add = k + radius, remove = k - radius - 1;
                        if (add < size) { sum += values[Index(axis, line, add)]; count++; }
                        if (remove >= 0) { sum -= values[Index(axis, line, remove)]; count--; }
                        temp[Index(axis, line, k)] = sum / count;
                    }
                }
                Array.Copy(temp, values, values.Length);
            }
        }

        private int Index(int axis, int line, int k) => axis == 0 ? line * size + k : k * size + line;

        public float HeightOf(int k) => baseY + k * step;

        /// <summary>The height a locked cell is levelled to: its plot's own ground, else its level's.</summary>
        private float LockedHeight(int c) => float.IsNaN(lockedHeight[c]) ? HeightOf(level[c]) : lockedHeight[c];

        /// <summary>The height of the terrace the cell at <paramref name="xz"/> belongs to: that of the locked cell nearest it.</summary>
        public float TerraceHeightAt(Vector2 xz) => LockedHeight(source[CellOf(xz)]);

        public int NaturalLevel(Vector2 xz) => Mathf.RoundToInt((naturalHeight(xz) - baseY) / step);

        public int LevelAt(Vector2 xz) => level[CellOf(xz)];

        /// <summary>
        /// A street's levels: its natural level metre by metre, forced to <paramref name="startLevel"/>
        /// / <paramref name="endLevel"/> over the first / last <paramref name="minRun"/> metres where it
        /// joins another street, runs shorter than minRun merged into their neighbours, and neighbouring
        /// runs at most <paramref name="maxStep"/> levels apart (see <see cref="ClampSteps"/>).
        /// </summary>
        /// <param name="chainLength">Length of the stairs climbing a given number of levels at once, which
        /// must fit on the lower side of their change; null when every change is a single flight.</param>
        /// <param name="terraced">Whether the street may step at an arc; where not (a stone path) its levels just
        /// follow the ground, unclamped. Null = terraced everywhere.</param>
        /// <param name="startHold">How far from its start the street is held at <paramref name="startLevel"/> at least
        /// (across a plaza); minRun is held anyway.</param>
        public StreetProfile Profile(SettlementStreetNetwork.Street street, int? startLevel, int? endLevel, float minRun,
                                     int maxStep, Func<int, float> chainLength, Func<float, bool> terraced, float startHold = 0f)
        {
            float held = Mathf.Max(minRun, startHold);
            var runs = new List<(float start, float end, int level)>();
            float length = street.Length;
            int samples = Mathf.Max(1, Mathf.CeilToInt(length));
            for (int i = 0; i < samples; i++)
            {
                float s = Mathf.Min(i + 0.5f, length);
                int k = NaturalLevel(street.PointAt(s));
                if (startLevel.HasValue && s < held) k = startLevel.Value;
                if (endLevel.HasValue && s > length - minRun) k = endLevel.Value;
                float start = i, end = Mathf.Min(i + 1f, length);
                if (runs.Count > 0 && runs[runs.Count - 1].level == k) runs[runs.Count - 1] = (runs[runs.Count - 1].start, end, k);
                else runs.Add((start, end, k));
            }

            while (runs.Count > 1)
            {
                int shortest = -1;
                for (int i = 0; i < runs.Count; i++)
                {
                    float runLength = runs[i].end - runs[i].start;
                    if (runLength < minRun && (shortest < 0 || runLength < runs[shortest].end - runs[shortest].start)) shortest = i;
                }
                if (shortest < 0) break;
                int into = shortest == 0 ? 1
                    : shortest == runs.Count - 1 ? shortest - 1
                    : Mathf.Abs(runs[shortest - 1].level - runs[shortest].level) <= Mathf.Abs(runs[shortest + 1].level - runs[shortest].level) ? shortest - 1 : shortest + 1;
                var target = runs[into];
                runs[into] = (Mathf.Min(target.start, runs[shortest].start), Mathf.Max(target.end, runs[shortest].end), target.level);
                runs.RemoveAt(shortest);
                MergeEqual(runs);
            }

            ClampSteps(runs, maxStep, chainLength, minRun, terraced);

            var profile = new StreetProfile();
            foreach (var run in runs)
            {
                profile.starts.Add(run.start);
                profile.levels.Add(run.level);
            }
            return profile;
        }

        /// <summary>
        /// Limits every change between neighbouring runs to <paramref name="maxStep"/> levels, going along the
        /// street, so a steep stretch climbs several levels at one place instead of trailing behind the hill.
        /// With <paramref name="chainLength"/>, a change also shrinks until its stairs fit on its lower side --
        /// the end of the run before a climb, the start of the run after a descent -- with half of
        /// <paramref name="minRun"/> to spare; a change that cannot fit even one level is dropped. A change
        /// where <paramref name="terraced"/> says no (a stone path, which has no stairs and follows the ground)
        /// is left as it is. Runs that end up on the same level merge.
        /// </summary>
        public static void ClampSteps(List<(float start, float end, int level)> runs, int maxStep, Func<int, float> chainLength, float minRun,
                                      Func<float, bool> terraced = null)
        {
            float runStart = runs.Count > 0 ? runs[0].start : 0f;   // where the run a change leaves really starts, merges included
            float claimed = 0f;                                      // metres at its start already taken by a descending chain
            for (int i = 1; i < runs.Count; i++)
            {
                int previous = runs[i - 1].level;
                bool stairs = terraced == null || terraced(runs[i].start);
                int delta = stairs ? Mathf.Clamp(runs[i].level - previous, -maxStep, maxStep) : runs[i].level - previous;
                if (chainLength != null && stairs)
                {
                    float lower = delta > 0 ? runs[i - 1].end - runStart - claimed : runs[i].end - runs[i].start;
                    while (delta != 0 && chainLength(Mathf.Abs(delta)) + minRun * 0.5f > lower) delta -= Math.Sign(delta);
                }
                runs[i] = (runs[i].start, runs[i].end, previous + delta);
                if (delta == 0) continue;
                runStart = runs[i].start;
                claimed = delta < 0 && stairs && chainLength != null ? chainLength(-delta) : 0f;
            }
            MergeEqual(runs);
        }

        private static void MergeEqual(List<(float start, float end, int level)> runs)
        {
            for (int i = runs.Count - 1; i > 0; i--)
            {
                if (runs[i].level != runs[i - 1].level) continue;
                runs[i - 1] = (runs[i - 1].start, runs[i].end, runs[i].level);
                runs.RemoveAt(i);
            }
        }

        /// <summary>
        /// Locks every free cell within the street's half-width plus <paramref name="margin"/> to its
        /// profile level at that point. A margin of one heightmap sample keeps the terrain's interpolation
        /// between the street's samples and the ground beside it from rising over pieces laid at the
        /// street's exact level.
        /// </summary>
        /// <param name="terraced">Where false (a stone path), the street is held at the smoothed natural ground
        /// instead of its level, so it climbs with the hill. Null = terraced everywhere.</param>
        public void Lock(SettlementStreetNetwork.Street street, StreetProfile profile, float margin, Func<float, bool> terraced)
        {
            float reach = street.halfWidth + margin;
            for (int i = 0; i + 1 < street.points.Count; i++)
            {
                Vector2 a = street.points[i], ab = street.points[i + 1] - a;
                float segmentLength = ab.magnitude;
                if (segmentLength <= 0f) continue;
                float arcStart = street.arcs[i];
                ForCellsNear(Vector2.Min(a, a + ab), Vector2.Max(a, a + ab), reach, c =>
                {
                    Vector2 p = CentreOf(c);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / (segmentLength * segmentLength));
                    if (Vector2.Distance(p, a + ab * t) > reach) return;
                    float arc = arcStart + t * segmentLength;
                    if (terraced == null || terraced(arc)) LockCell(c, profile.LevelAt(arc), plot: false);
                    else LockCell(c, NaturalLevel(p), plot: false, naturalHeight(p));
                });
            }
        }

        /// <summary>
        /// Locks every free cell within <paramref name="padding"/> of <paramref name="footprint"/> to level
        /// <paramref name="k"/> (which decides where walls and stairs go) and levels it to <paramref name="height"/>.
        /// </summary>
        public void Lock(in SettlementFootprint footprint, float padding, int k, float height)
        {
            SettlementFootprint fp = footprint;
            float reach = fp.Circumradius + padding;
            ForCellsNear(fp.center, fp.center, reach, c =>
            {
                if (fp.SignedDistance(CentreOf(c)) <= padding) LockCell(c, k, plot: true, height);
            });
        }

        /// <summary>
        /// Locks the free cells of a stair chain's corridor -- <paramref name="halfWidth"/> either side of the
        /// line from <paramref name="top"/> along <paramref name="forward"/> for <paramref name="length"/>
        /// metres -- to <paramref name="topY"/> minus <paramref name="dropAt"/> metres from the top. The
        /// sculptor then cuts a ramp down the hill under the stairs instead of a cliff beside them. Lock a
        /// street's chains before the street itself, which keeps its level everywhere else.
        /// </summary>
        public void LockRamp(Vector2 top, Vector2 forward, float halfWidth, float length, float topY, Func<float, float> dropAt)
        {
            Vector2 foot = top + forward * length;
            ForCellsNear(Vector2.Min(top, foot), Vector2.Max(top, foot), halfWidth, c =>
            {
                Vector2 d = CentreOf(c) - top;
                float along = Vector2.Dot(d, forward);
                if (along < 0f || along > length || Mathf.Abs(d.x * forward.y - d.y * forward.x) > halfWidth) return;
                float height = topY - dropAt(along);
                LockCell(c, Mathf.RoundToInt((height - baseY) / step), plot: false, height);
            });
        }

        /// <summary>Locks every free cell within <paramref name="radius"/> of <paramref name="center"/> to level <paramref name="k"/>: a plaza.</summary>
        public void LockDisk(Vector2 center, float radius, int k)
        {
            ForCellsNear(center, center, radius, c =>
            {
                if (Vector2.Distance(CentreOf(c), center) <= radius) LockCell(c, k, plot: false);
            });
        }

        private void LockCell(int c, int k, bool plot, float height = float.NaN)
        {
            if (locked[c]) return;
            level[c] = k;
            locked[c] = true;
            plotOwned[c] = plot;
            lockedHeight[c] = height;
        }

        /// <summary>
        /// Grades the free ground to the locked terraces. Every free cell takes the level of the nearest
        /// locked cell; within <paramref name="wallReach"/> of it the ground is that level exactly, then
        /// the correction fades out over <paramref name="gradeDistance"/> and is smoothed where two
        /// terraces of different levels meet. The exception is a step with a plot on each side within
        /// <paramref name="wallReach"/>: a retaining wall stands there, so the ground steps for real and
        /// the upper side is held at the lower level for <paramref name="wallBand"/> metres behind the
        /// contour -- the heightmap's ramp then lands inside the wall block, not in front of its face.
        /// </summary>
        public void Grade(float gradeDistance, float wallReach, float wallBand)
        {
            AssignNearest();

            correction = new float[level.Length];
            for (int i = 0; i < level.Length; i++)
            {
                int s = source[i];
                if (s < 0) continue;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, (distance[i] - wallReach) / gradeDistance);
                correction[i] = (LockedHeight(s) - rawHeight(CentreOf(s))) * fade;
            }

            held = new float[level.Length];
            Array.Fill(held, float.NaN);
            var pinned = (bool[])locked.Clone();
            for (int a = 0; a < level.Length; a++)
            {
                int x = a % size, z = a / size;
                for (int n = 0; n < 4; n++)
                {
                    int nx = x + Neighbours[n].x, nz = z + Neighbours[n].y;
                    if (nx < 0 || nz < 0 || nx >= size || nz >= size) continue;
                    int b = nz * size + nx;
                    if (IsWallStep(a, b, wallReach)) HoldBehind(b, wallBand, pinned);
                }
            }

            SmoothCorrection(pinned, Mathf.Max(1, Mathf.RoundToInt(gradeDistance / cell)));
        }

        /// <summary>Whether a retaining wall stands between the cell at <paramref name="upper"/> and the one at <paramref name="lower"/>.</summary>
        public bool WallBetween(Vector2 upper, Vector2 lower, float wallReach) =>
            IsWallStep(CellOf(upper), CellOf(lower), wallReach);

        private bool IsWallStep(int upper, int lower, float wallReach) =>
            level[upper] > level[lower] && plotOwned[upper] && plotOwned[lower]
            && distance[upper] <= wallReach && distance[lower] <= wallReach;

        // Multi-source Dijkstra from the locked cells over the 8-connected grid.
        private void AssignNearest()
        {
            source = new int[level.Length];
            distance = new float[level.Length];
            var heap = new MinHeap(level.Length);
            for (int i = 0; i < level.Length; i++)
            {
                source[i] = locked[i] ? i : -1;
                distance[i] = locked[i] ? 0f : float.PositiveInfinity;
                if (locked[i]) heap.Push(i, 0f);
            }
            while (heap.TryPop(out int c, out float key))
            {
                if (key > distance[c]) continue;
                int x = c % size, z = c / size;
                foreach (var n in Neighbours)
                {
                    int nx = x + n.x, nz = z + n.y;
                    if (nx < 0 || nz < 0 || nx >= size || nz >= size) continue;
                    int j = nz * size + nx;
                    float next = distance[c] + cell * (n.x != 0 && n.y != 0 ? 1.41421356f : 1f);
                    if (next >= distance[j]) continue;
                    distance[j] = next;
                    source[j] = source[c];
                    heap.Push(j, next);
                }
            }
            for (int i = 0; i < level.Length; i++)
            {
                if (locked[i] || source[i] < 0) continue;
                level[i] = level[source[i]];
                plotOwned[i] = plotOwned[source[i]];
            }
        }

        // Holds the free cells above `lower` at its level, for `band` metres behind the step.
        private void HoldBehind(int lower, float band, bool[] pinned)
        {
            int reach = Mathf.CeilToInt(band / cell) + 1;
            int lx = lower % size, lz = lower / size;
            for (int z = Mathf.Max(0, lz - reach); z <= Mathf.Min(size - 1, lz + reach); z++)
            for (int x = Mathf.Max(0, lx - reach); x <= Mathf.Min(size - 1, lx + reach); x++)
            {
                int c = z * size + x;
                if (locked[c] || level[c] <= level[lower]) continue;
                if (Vector2.Distance(CentreOf(c), CentreOf(lower)) - cell * 0.5f >= band) continue;
                float height = LockedHeight(source[lower]);
                held[c] = float.IsNaN(held[c]) ? height : Mathf.Min(held[c], height);
                pinned[c] = true;
            }
            pinned[lower] = true;
        }

        // Jacobi passes: pinned cells keep their value, the rest drift toward their neighbours'.
        private void SmoothCorrection(bool[] pinned, int passes)
        {
            var next = new float[correction.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int c = 0; c < correction.Length; c++)
                {
                    if (pinned[c]) { next[c] = correction[c]; continue; }
                    int x = c % size, z = c / size;
                    float sum = correction[x > 0 ? c - 1 : c] + correction[x < size - 1 ? c + 1 : c]
                              + correction[z > 0 ? c - size : c] + correction[z < size - 1 ? c + size : c];
                    next[c] = 0.5f * correction[c] + 0.125f * sum;
                }
                (correction, next) = (next, correction);
            }
        }

        /// <summary>
        /// Ground height to sculpt at <paramref name="xz"/>, whose natural height is <paramref name="naturalY"/>:
        /// a street's or plot's level exactly, the held-down height behind a retaining wall, else the
        /// natural ground plus its correction.
        /// </summary>
        public float TerrainHeight(Vector2 xz, float naturalY)
        {
            int c = CellOf(xz);
            if (locked[c]) return LockedHeight(c);
            return float.IsNaN(held[c]) ? naturalY + correction[c] : held[c];
        }

        /// <summary>Metres <see cref="TerrainHeight"/> adds to the natural ground at <paramref name="xz"/>, for blending out past the field.</summary>
        public float CorrectionAt(Vector2 xz) => correction[CellOf(xz)];

        /// <summary>Every line between a level and the one below it, from marching squares over the cell centres.</summary>
        public List<Contour> Contours()
        {
            var contours = new List<Contour>();
            GetLevelRange(out int minLevel, out int maxLevel);
            for (int k = minLevel + 1; k <= maxLevel; k++)
            {
                foreach (var line in MarchingSquares(i => level[i] >= k)) contours.Add(new Contour(k, line));
            }
            return contours;
        }

        private List<List<Vector2>> MarchingSquares(Func<int, bool> inside)
        {
            // Edge pairs per case; corners b0 (x,z), b1 (x+1,z), b2 (x+1,z+1), b3 (x,z+1);
            // edges e0 bottom, e1 right, e2 top, e3 left.
            int[][] table =
            {
                new int[0], new[] { 3, 0 }, new[] { 0, 1 }, new[] { 3, 1 }, new[] { 1, 2 }, new[] { 3, 0, 1, 2 },
                new[] { 0, 2 }, new[] { 2, 3 }, new[] { 2, 3 }, new[] { 0, 2 }, new[] { 0, 1, 2, 3 }, new[] { 1, 2 },
                new[] { 1, 3 }, new[] { 0, 1 }, new[] { 3, 0 }, new int[0],
            };
            var segments = new List<(int a, int b)>();
            var byEdge = new Dictionary<int, List<int>>();
            for (int z = 0; z + 1 < size; z++)
            for (int x = 0; x + 1 < size; x++)
            {
                int i = z * size + x;
                int mask = (inside(i) ? 1 : 0) | (inside(i + 1) ? 2 : 0) | (inside(i + size + 1) ? 4 : 0) | (inside(i + size) ? 8 : 0);
                int[] pairs = table[mask];
                for (int p = 0; p < pairs.Length; p += 2)
                {
                    int ea = EdgeId(x, z, pairs[p]), eb = EdgeId(x, z, pairs[p + 1]);
                    segments.Add((ea, eb));
                    AddEdge(byEdge, ea, segments.Count - 1);
                    AddEdge(byEdge, eb, segments.Count - 1);
                }
            }

            var lines = new List<List<Vector2>>();
            var used = new bool[segments.Count];
            for (int s = 0; s < segments.Count; s++)
            {
                if (used[s]) continue;
                used[s] = true;
                var chain = new LinkedList<int>();
                chain.AddLast(segments[s].a);
                chain.AddLast(segments[s].b);
                Extend(chain, forward: true, segments, byEdge, used);
                Extend(chain, forward: false, segments, byEdge, used);
                var line = new List<Vector2>(chain.Count);
                foreach (int edge in chain) line.Add(EdgeMidpoint(edge));
                lines.Add(line);
            }
            return lines;
        }

        private static void Extend(LinkedList<int> chain, bool forward, List<(int a, int b)> segments, Dictionary<int, List<int>> byEdge, bool[] used)
        {
            while (true)
            {
                int end = forward ? chain.Last.Value : chain.First.Value;
                int next = -1;
                foreach (int s in byEdge[end]) if (!used[s]) { next = s; break; }
                if (next < 0) return;
                used[next] = true;
                int other = segments[next].a == end ? segments[next].b : segments[next].a;
                if (forward) chain.AddLast(other);
                else chain.AddFirst(other);
            }
        }

        private static void AddEdge(Dictionary<int, List<int>> byEdge, int edge, int segment)
        {
            if (!byEdge.TryGetValue(edge, out var list)) byEdge[edge] = list = new List<int>(2);
            list.Add(segment);
        }

        // Horizontal edge (x,z)-(x+1,z) = 2 * index, vertical edge (x,z)-(x,z+1) = 2 * index + 1.
        private int EdgeId(int x, int z, int edge) => edge switch
        {
            0 => 2 * (z * size + x),
            1 => 2 * (z * size + x + 1) + 1,
            2 => 2 * ((z + 1) * size + x),
            _ => 2 * (z * size + x) + 1,
        };

        private Vector2 EdgeMidpoint(int edge)
        {
            int index = edge / 2;
            Vector2 a = CentreOf(index);
            return (edge & 1) == 0 ? a + new Vector2(cell * 0.5f, 0f) : a + new Vector2(0f, cell * 0.5f);
        }

        private void GetLevelRange(out int minLevel, out int maxLevel)
        {
            minLevel = int.MaxValue;
            maxLevel = int.MinValue;
            foreach (int k in level)
            {
                minLevel = Mathf.Min(minLevel, k);
                maxLevel = Mathf.Max(maxLevel, k);
            }
        }

        private void ForCellsNear(Vector2 min, Vector2 max, float margin, Action<int> visit)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((min.x - margin - origin.x) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((min.y - margin - origin.y) / cell));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt((max.x + margin - origin.x) / cell));
            int z1 = Mathf.Min(size - 1, Mathf.CeilToInt((max.y + margin - origin.y) / cell));
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                visit(z * size + x);
        }

        private int CellOf(Vector2 xz)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((xz.x - origin.x) / cell), 0, size - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((xz.y - origin.y) / cell), 0, size - 1);
            return z * size + x;
        }

        private Vector2 CentreOf(int i) => origin + new Vector2((i % size + 0.5f) * cell, (i / size + 0.5f) * cell);

        /// <summary>Binary min-heap of (cell, key) with lazy deletion.</summary>
        private sealed class MinHeap
        {
            private readonly List<(int cell, float key)> items;

            public MinHeap(int capacity) => items = new List<(int, float)>(capacity);

            public void Push(int c, float key)
            {
                items.Add((c, key));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (items[parent].key <= items[i].key) break;
                    (items[parent], items[i]) = (items[i], items[parent]);
                    i = parent;
                }
            }

            public bool TryPop(out int c, out float key)
            {
                if (items.Count == 0) { c = -1; key = 0f; return false; }
                (c, key) = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, smallest = i;
                    if (l < items.Count && items[l].key < items[smallest].key) smallest = l;
                    if (r < items.Count && items[r].key < items[smallest].key) smallest = r;
                    if (smallest == i) break;
                    (items[smallest], items[i]) = (items[i], items[smallest]);
                    i = smallest;
                }
                return true;
            }
        }
    }
}
