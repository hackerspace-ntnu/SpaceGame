using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Random-walk graph generator. Lays down rooms one at a time, each placed at a random
    /// direction+distance from an existing room (so they form a connected cluster, not a Poisson cloud).
    /// Then connects nearby room pairs with corridors. Tags rooms (BigChamber/Junction/DeadEnd) based
    /// on size and connectivity so downstream systems can decorate appropriately.
    /// </summary>
    public static class CaveGraphGenerator
    {
        public static CaveGraph Generate(CaveGenerationSettings s, System.Random rng)
        {
            var graph = new CaveGraph();

            // -------------------------------------------------------------------------
            // 1) Place rooms via a guided random walk
            // -------------------------------------------------------------------------

            float yLimit = Mathf.Min(s.verticalRange, s.halfExtents.y);
            var startCenter = new Vector3(
                (float)((rng.NextDouble() - 0.5) * s.halfExtents.x * 0.4),
                (float)((rng.NextDouble() - 0.5) * yLimit * 0.5),
                (float)((rng.NextDouble() - 0.5) * s.halfExtents.z * 0.4));

            graph.Rooms.Add(new CaveRoom
            {
                Id = 0,
                Center = startCenter,
                Radius = SampleRoomRadius(s, rng),
                Kind = RoomKind.Normal,
            });

            int attempts = 0;
            int maxAttempts = s.roomCount * 25;

            while (graph.Rooms.Count < s.roomCount && attempts < maxAttempts)
            {
                attempts++;

                // Anchor on a random existing room — bias toward the most recent ones so we form
                // a slithering shape rather than a fat blob, which gives nicer corridor topology.
                int anchorIdx = graph.Rooms.Count - 1 - (int)(rng.NextDouble() * rng.NextDouble() * graph.Rooms.Count);
                anchorIdx = Mathf.Clamp(anchorIdx, 0, graph.Rooms.Count - 1);
                var anchor = graph.Rooms[anchorIdx];

                // Direction: bias the Y component up/down. verticalWalkBias scales the baseline
                // vertical component on every step; a ~20% chance "spike step" produces occasional
                // bigger elevation jumps. perRoomVerticalJitter then perturbs each room independently.
                // (The slope clamp below caps anything that would make a corridor too steep to walk.)
                bool spike = (float)rng.NextDouble() < 0.2f;
                float vertBias = spike ? 1.3f : s.verticalWalkBias;
                Vector3 dir = new Vector3(
                    (float)(rng.NextDouble() - 0.5),
                    (float)(rng.NextDouble() - 0.5) * vertBias,
                    (float)(rng.NextDouble() - 0.5)).normalized;

                // Distance so the corridor will be within maxCorridorLength.
                float dist = Mathf.Lerp(
                    anchor.Radius * 1.4f,
                    s.maxCorridorLength,
                    (float)rng.NextDouble());

                Vector3 candidate = anchor.Center + dir * dist;

                // Independent vertical jitter per room — each room gets its own elevation noise on
                // top of the walk, so heights aren't fully chained off the previous room.
                candidate.y += (float)(rng.NextDouble() - 0.5) * 2f * s.perRoomVerticalJitter;

                // Enforce max slope from the anchor: clamp Y so |dy| <= maxSlope * horizontalDistance.
                // This is what makes every corridor walkable — no near-vertical capsule jumps.
                Vector3 deltaFromAnchor = candidate - anchor.Center;
                float horiz = new Vector2(deltaFromAnchor.x, deltaFromAnchor.z).magnitude;
                float maxDy = horiz * s.maxCorridorSlope;
                if (Mathf.Abs(deltaFromAnchor.y) > maxDy)
                {
                    candidate.y = anchor.Center.y + Mathf.Sign(deltaFromAnchor.y) * maxDy;
                }

                float radius = SampleRoomRadius(s, rng);

                // Clamp inside footprint.
                if (!InsideFootprint(candidate, radius, s)) continue;

                // Reject if it would overlap an existing room too much (we want corridors to do that
                // job, not the rooms themselves).
                if (TooClose(graph, candidate, radius * 0.65f)) continue;

                graph.Rooms.Add(new CaveRoom
                {
                    Id = graph.Rooms.Count,
                    Center = candidate,
                    Radius = radius,
                    Kind = RoomKind.Normal,
                });
            }

            // -------------------------------------------------------------------------
            // 2) Connect rooms with corridors
            //    Two passes:
            //      a) every room → its N nearest neighbours (guaranteed reachability)
            //      b) sprinkle a few extra "shortcut" corridors so the graph isn't a pure tree
            // -------------------------------------------------------------------------

            var existingEdges = new HashSet<long>();

            // Pass a — k-nearest neighbours
            for (int i = 0; i < graph.Rooms.Count; i++)
            {
                var neighbours = FindKNearest(graph, i, s.minConnectionsPerRoom, s.maxCorridorLength);
                foreach (int j in neighbours)
                {
                    if (TryAddCorridor(graph, existingEdges, i, j, s, rng))
                    {
                        // ok
                    }
                }
            }

            // Pass b — extra shortcuts
            int extras = Mathf.RoundToInt(graph.Rooms.Count * s.extraConnectionRatio);
            int extraAttempts = 0;
            while (extras > 0 && extraAttempts < extras * 10)
            {
                extraAttempts++;
                int a = rng.Next(graph.Rooms.Count);
                int b = rng.Next(graph.Rooms.Count);
                if (a == b) continue;
                if (Vector3.Distance(graph.Rooms[a].Center, graph.Rooms[b].Center) > s.maxCorridorLength) continue;
                if (TryAddCorridor(graph, existingEdges, a, b, s, rng)) extras--;
            }

            // Pass c — guarantee the graph is one connected component (adds bridge corridors and
            // stepping-stone rooms as needed). This is what makes the cave traversable end-to-end.
            EnsureConnectivity(graph, existingEdges, s, rng);

            // Pass d — optional vertical-shaft corridors between vertically-stacked rooms.
            // Shafts skip the slope clamp, so they unlock connections that the normal corridor
            // pass refused. Only added when the profile opts in.
            if (s.enableVerticalShafts)
                AddVerticalShafts(graph, existingEdges, s, rng);

            // Pass e — upgrade some Tight corridors to slot canyons.
            if (s.enableSlotCanyons && s.slotCanyonHeightStretch > 1.01f)
                ApplySlotCanyonUpgrades(graph, s, rng);

            // Pass f — append the dedicated entrance: a thin dead-end passage rising from the
            // highest existing room to a small entrance chamber at the top of the footprint.
            // Runs after connectivity so the entrance is guaranteed to be a leaf (degree 1).
            if (s.generateEntrance)
                AppendEntrance(graph, existingEdges, s, rng);

            // -------------------------------------------------------------------------
            // 3) Tag rooms based on size + connectivity
            // -------------------------------------------------------------------------

            int[] degree = new int[graph.Rooms.Count];
            foreach (var c in graph.Corridors)
            {
                degree[c.FromRoomId]++;
                degree[c.ToRoomId]++;
            }

            float bigCutoff = Mathf.Lerp(s.roomRadius.x, s.roomRadius.y, 0.7f);
            for (int i = 0; i < graph.Rooms.Count; i++)
            {
                var r = graph.Rooms[i];

                // Every entrance-tube node keeps its dedicated Entrance kind — don't let the degree
                // pass re-tag the tube (mouth would become DeadEnd, mid-nodes Normal/BigChamber).
                if (r.Kind == RoomKind.Entrance)
                {
                    graph.Rooms[i] = r;
                    continue;
                }

                if (degree[i] == 1) r.Kind = RoomKind.DeadEnd;
                else if (degree[i] >= 4) r.Kind = RoomKind.Junction;
                else if (r.Radius >= bigCutoff) r.Kind = RoomKind.BigChamber;
                else r.Kind = RoomKind.Normal;

                // Cathedral lift: BigChambers get their ceilings stretched upward when the toggle is on.
                // We only apply this here (after kind tagging) so cathedrals = "extra-tall BigChambers."
                if (s.enableCathedralChambers && r.Kind == RoomKind.BigChamber && s.cathedralCeilingLift > 0.01f)
                {
                    // Per-room variation so they aren't all identical height.
                    float jitter = 0.6f + (float)rng.NextDouble() * 0.4f; // 60–100% of the configured lift
                    r.CeilingLift = s.cathedralCeilingLift * jitter;
                }

                graph.Rooms[i] = r;
            }

            return graph;
        }

        // -------------------------------------------------------------------------
        // Shape modifier passes
        // -------------------------------------------------------------------------

        /// <summary>
        /// For every pair of rooms whose vertical separation exceeds verticalShaftMinDrop AND whose
        /// horizontal distance is small (so they'd be roughly stacked), roll a chance to add a Shaft
        /// corridor. Shafts skip the slope clamp because they drop straight down.
        /// </summary>
        static void AddVerticalShafts(CaveGraph g, HashSet<long> edges, CaveGenerationSettings s, System.Random rng)
        {
            int n = g.Rooms.Count;
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                Vector3 d = g.Rooms[j].Center - g.Rooms[i].Center;
                float horiz = new Vector2(d.x, d.z).magnitude;
                float dy = Mathf.Abs(d.y);
                if (dy < s.verticalShaftMinDrop) continue;
                // "Stacked-ish": horizontal offset shouldn't exceed half the vertical drop.
                if (horiz > dy * 0.5f) continue;
                if (edges.Contains(EdgeKey(i, j))) continue;
                if (rng.NextDouble() > s.verticalShaftChance) continue;

                g.Corridors.Add(new CaveCorridor
                {
                    FromRoomId = i,
                    ToRoomId   = j,
                    Radius     = s.verticalShaftRadius,
                    Kind       = CorridorKind.Shaft,
                });
                edges.Add(EdgeKey(i, j));
            }
        }

        /// <summary>
        /// Re-roll a fraction of existing Tight corridors as Slot canyons (very narrow horizontal,
        /// vertically stretched). Implemented by mutating the corridor in-place rather than adding
        /// new edges — slot canyons replace what would otherwise be a tight passage.
        /// </summary>
        static void ApplySlotCanyonUpgrades(CaveGraph g, CaveGenerationSettings s, System.Random rng)
        {
            for (int i = 0; i < g.Corridors.Count; i++)
            {
                var c = g.Corridors[i];
                if (c.Kind != CorridorKind.Tight) continue;
                if (rng.NextDouble() > s.slotCanyonChance) continue;
                c.Kind = CorridorKind.Slot;
                c.Radius *= 0.7f; // narrower horizontally
                c.HeightStretch = s.slotCanyonHeightStretch;
                g.Corridors[i] = c;
            }
        }

        /// <summary>
        /// Appends the cave's entrance tunnel — a "thick spaghetti" of uniform-radius segments that
        /// winds gently upward from the highest cave room to a mouth node near the top of the
        /// footprint. Every node is a small same-radius room and every link is a same-radius
        /// <see cref="CorridorKind.Entrance"/> corridor, so the tube has a constant cross-section
        /// and a low, walkable slope (no big chamber anywhere).
        ///
        /// The walk starts at the cave end and climbs, so the slope/wander accumulate predictably;
        /// the final node (mouth) becomes <see cref="CaveGraph.EntranceRoomId"/> — a guaranteed
        /// dead end (single corridor) and the highest point of the cave.
        /// </summary>
        static void AppendEntrance(CaveGraph g, HashSet<long> edges, CaveGenerationSettings s, System.Random rng)
        {
            if (g.Rooms.Count == 0) return;

            // Highest existing room — the tunnel grows up out of it.
            int baseIdx = 0;
            for (int i = 1; i < g.Rooms.Count; i++)
                if (g.Rooms[i].Center.y > g.Rooms[baseIdx].Center.y) baseIdx = i;

            float radius = s.entranceTubeRadius;
            int segments = Mathf.Max(2, s.entranceSegmentCount);
            float segLen = Mathf.Max(2f, s.entranceSegmentLength);

            // Footprint limits the tube must stay inside (so marching cubes doesn't clip it).
            float maxY = Mathf.Min(s.halfExtents.y, s.verticalRange) - radius;
            float limX = s.halfExtents.x - radius;
            float limZ = s.halfExtents.z - radius;

            // Initial heading: random compass direction, walked one segment at a time. The vertical
            // component is fixed by entranceTubeSlope so every segment climbs the same gentle amount.
            float heading = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float vertPerStep = segLen * s.entranceTubeSlope; // metres of rise per segment

            Vector3 cursor = g.Rooms[baseIdx].Center;
            int prevId = baseIdx;
            int lastNodeId = baseIdx;

            for (int seg = 0; seg < segments; seg++)
            {
                // Wander the heading a little each step — this is what gives the tube its winding
                // spaghetti shape. Bounded so the tube never doubles back hard.
                heading += (float)((rng.NextDouble() - 0.5) * 2.0) * s.entranceTubeWander * Mathf.Deg2Rad;

                float horiz = Mathf.Sqrt(Mathf.Max(0f, segLen * segLen - vertPerStep * vertPerStep));
                Vector3 step = new Vector3(
                    Mathf.Cos(heading) * horiz,
                    vertPerStep,
                    Mathf.Sin(heading) * horiz);

                Vector3 next = cursor + step;
                next.y = Mathf.Min(next.y, maxY);
                next.x = Mathf.Clamp(next.x, -limX, limX);
                next.z = Mathf.Clamp(next.z, -limZ, limZ);

                // If a clamp shoved the node, nudge the cursor with it so the next segment continues
                // from the real position rather than drifting outside the footprint.
                cursor = next;

                int nodeId = g.Rooms.Count;
                g.Rooms.Add(new CaveRoom
                {
                    Id = nodeId,
                    Center = cursor,
                    Radius = radius,
                    Kind = RoomKind.Entrance,
                });

                g.Corridors.Add(new CaveCorridor
                {
                    FromRoomId = prevId,
                    ToRoomId   = nodeId,
                    Radius     = radius,            // uniform — same as the nodes, so a constant tube
                    Kind       = CorridorKind.Entrance,
                });
                edges.Add(EdgeKey(prevId, nodeId));

                prevId = nodeId;
                lastNodeId = nodeId;
            }

            // The last node is the mouth: highest point, single corridor → guaranteed dead end.
            g.EntranceRoomId = lastNodeId;
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        static float SampleRoomRadius(CaveGenerationSettings s, System.Random rng)
        {
            bool big = (float)rng.NextDouble() < s.bigRoomChance;
            if (big)
                return Mathf.Lerp((s.roomRadius.x + s.roomRadius.y) * 0.5f, s.roomRadius.y, (float)rng.NextDouble());
            return Mathf.Lerp(s.roomRadius.x, (s.roomRadius.x + s.roomRadius.y) * 0.5f, (float)rng.NextDouble());
        }

        static bool InsideFootprint(Vector3 p, float r, CaveGenerationSettings s)
        {
            if (Mathf.Abs(p.x) + r > s.halfExtents.x) return false;
            if (Mathf.Abs(p.z) + r > s.halfExtents.z) return false;
            if (p.y + r > s.halfExtents.y) return false;
            if (p.y - r < -s.halfExtents.y) return false;
            if (Mathf.Abs(p.y) > s.verticalRange) return false;
            return true;
        }

        static bool TooClose(CaveGraph g, Vector3 candidate, float minDist)
        {
            foreach (var r in g.Rooms)
                if (Vector3.Distance(r.Center, candidate) < minDist) return true;
            return false;
        }

        static List<int> FindKNearest(CaveGraph g, int self, int k, float maxDist)
        {
            var list = new List<(int idx, float d)>(g.Rooms.Count);
            var selfPos = g.Rooms[self].Center;
            for (int i = 0; i < g.Rooms.Count; i++)
            {
                if (i == self) continue;
                float d = Vector3.Distance(selfPos, g.Rooms[i].Center);
                if (d <= maxDist) list.Add((i, d));
            }
            list.Sort((a, b) => a.d.CompareTo(b.d));
            var result = new List<int>(k);
            for (int i = 0; i < Mathf.Min(k, list.Count); i++) result.Add(list[i].idx);
            return result;
        }

        static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        static bool TryAddCorridor(CaveGraph g, HashSet<long> edges, int a, int b, CaveGenerationSettings s, System.Random rng)
        {
            if (a == b) return false;
            long key = EdgeKey(a, b);
            if (edges.Contains(key)) return false;

            Vector3 from = g.Rooms[a].Center;
            Vector3 to   = g.Rooms[b].Center;
            Vector3 delta = to - from;
            float horiz = new Vector2(delta.x, delta.z).magnitude;

            // Slope gate: refuse near-vertical pairs. Two rooms stacked nearly atop each other can't be
            // joined by a walkable corridor — we'd rather drop the edge and let the connectivity pass
            // route around via a third room at intermediate elevation.
            if (horiz < 0.5f) return false;
            float slope = Mathf.Abs(delta.y) / horiz;
            if (slope > s.maxCorridorSlope * 1.2f) return false; // small tolerance for rooms placed pre-slope-clamp

            bool wide = (float)rng.NextDouble() < s.wideCorridorChance;
            bool tight = !wide && (float)rng.NextDouble() < 0.3f;

            float radius;
            CorridorKind kind;
            if (wide)
            {
                radius = Mathf.Lerp((s.corridorRadius.x + s.corridorRadius.y) * 0.5f, s.corridorRadius.y, (float)rng.NextDouble());
                kind = CorridorKind.Wide;
            }
            else if (tight)
            {
                radius = Mathf.Lerp(s.corridorRadius.x, (s.corridorRadius.x + s.corridorRadius.y) * 0.45f, (float)rng.NextDouble());
                kind = CorridorKind.Tight;
            }
            else
            {
                radius = Mathf.Lerp(s.corridorRadius.x, (s.corridorRadius.x + s.corridorRadius.y) * 0.5f, (float)rng.NextDouble());
                kind = CorridorKind.Normal;
            }

            // Steeper corridors get widened a bit so the NavMesh has enough lateral room around the
            // diagonal ramp for an agent to walk without clipping into the wall on either side.
            if (slope > 0.25f) radius *= Mathf.Lerp(1f, 1.4f, Mathf.InverseLerp(0.25f, s.maxCorridorSlope, slope));

            g.Corridors.Add(new CaveCorridor
            {
                FromRoomId = a,
                ToRoomId   = b,
                Radius     = radius,
                Kind       = kind,
            });
            edges.Add(key);
            return true;
        }

        // -------------------------------------------------------------------------
        // Connectivity pass — guarantees the whole cave is one component
        // -------------------------------------------------------------------------

        /// <summary>
        /// Builds a union-find over current corridors, finds disjoint components, and adds the cheapest
        /// inter-component edge (subject to slope) until everything is connected. Falls back to
        /// inserting a stepping-stone room mid-air if no walkable direct edge exists.
        /// </summary>
        static void EnsureConnectivity(CaveGraph g, HashSet<long> edges, CaveGenerationSettings s, System.Random rng)
        {
            int n = g.Rooms.Count;
            if (n <= 1) return;

            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            bool Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra == rb) return false; parent[ra] = rb; return true; }

            foreach (var c in g.Corridors) Union(c.FromRoomId, c.ToRoomId);

            // Repeatedly merge the two closest disjoint components.
            int safety = n * n;
            while (safety-- > 0)
            {
                // Group rooms by component root.
                var roots = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
                for (int i = 0; i < n; i++)
                {
                    int r = Find(i);
                    if (!roots.TryGetValue(r, out var list)) { list = new System.Collections.Generic.List<int>(); roots[r] = list; }
                    list.Add(i);
                }
                if (roots.Count <= 1) return; // fully connected

                // Find the cheapest pair (a in component X, b in component Y, X != Y) that satisfies slope.
                float bestCost = float.MaxValue;
                int bestA = -1, bestB = -1;
                bool bestNeedsBridge = false;

                var rootList = new System.Collections.Generic.List<int>(roots.Keys);
                for (int i = 0; i < rootList.Count; i++)
                {
                    var listA = roots[rootList[i]];
                    for (int j = i + 1; j < rootList.Count; j++)
                    {
                        var listB = roots[rootList[j]];
                        foreach (int ai in listA)
                        foreach (int bi in listB)
                        {
                            Vector3 d = g.Rooms[bi].Center - g.Rooms[ai].Center;
                            float horiz = new Vector2(d.x, d.z).magnitude;
                            float slope = horiz > 0.5f ? Mathf.Abs(d.y) / horiz : float.MaxValue;
                            float cost = d.magnitude;
                            bool walkable = slope <= s.maxCorridorSlope * 1.2f && horiz > 0.5f;
                            // Prefer walkable edges; penalise non-walkable ones so we only resort to them last.
                            if (!walkable) cost += 1000f;
                            if (cost < bestCost)
                            {
                                bestCost = cost;
                                bestA = ai;
                                bestB = bi;
                                bestNeedsBridge = !walkable;
                            }
                        }
                    }
                }

                if (bestA < 0) return; // can't happen given >1 components

                if (!bestNeedsBridge)
                {
                    // Just add the corridor.
                    AddBridgeCorridor(g, edges, bestA, bestB, s, rng);
                    Union(bestA, bestB);
                }
                else
                {
                    // Insert a stepping-stone room at a slope-friendly midpoint so the two near-vertical
                    // rooms can be reached by two walkable diagonal corridors.
                    Vector3 pa = g.Rooms[bestA].Center;
                    Vector3 pb = g.Rooms[bestB].Center;
                    Vector3 mid = (pa + pb) * 0.5f;
                    // Push mid horizontally away from the line so both legs hit allowable slope.
                    Vector3 horizDelta = new Vector3(pb.x - pa.x, 0f, pb.z - pa.z);
                    Vector3 perp = horizDelta.sqrMagnitude > 0.1f
                        ? Vector3.Cross(horizDelta.normalized, Vector3.up).normalized
                        : Vector3.right;
                    float dy = Mathf.Abs(pb.y - pa.y);
                    float pushDist = dy / s.maxCorridorSlope + 1f;
                    mid += perp * pushDist * ((float)rng.NextDouble() < 0.5f ? 1f : -1f);

                    int stoneId = g.Rooms.Count;
                    g.Rooms.Add(new CaveRoom
                    {
                        Id = stoneId,
                        Center = mid,
                        Radius = Mathf.Lerp(s.roomRadius.x, (s.roomRadius.x + s.roomRadius.y) * 0.5f, (float)rng.NextDouble()),
                        Kind = RoomKind.Junction,
                    });
                    System.Array.Resize(ref parent, g.Rooms.Count);
                    parent[stoneId] = stoneId;

                    AddBridgeCorridor(g, edges, bestA, stoneId, s, rng);
                    AddBridgeCorridor(g, edges, stoneId, bestB, s, rng);
                    Union(bestA, stoneId);
                    Union(stoneId, bestB);
                }
            }
        }

        static void AddBridgeCorridor(CaveGraph g, HashSet<long> edges, int a, int b, CaveGenerationSettings s, System.Random rng)
        {
            long key = EdgeKey(a, b);
            if (edges.Contains(key)) return;
            float radius = Mathf.Lerp((s.corridorRadius.x + s.corridorRadius.y) * 0.5f, s.corridorRadius.y, (float)rng.NextDouble());
            g.Corridors.Add(new CaveCorridor
            {
                FromRoomId = a,
                ToRoomId   = b,
                Radius     = radius,
                Kind       = CorridorKind.Bridge,
            });
            edges.Add(key);
        }
    }
}
