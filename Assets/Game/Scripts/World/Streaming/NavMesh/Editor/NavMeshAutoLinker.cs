using System.Collections.Generic;
using UnityEngine;
using NavMesh = UnityEngine.AI.NavMesh;

namespace SpaceGame.World.NavMeshTools
{
    /// <summary>
    /// Finds the jumps an agent could make between separate pieces of NavMesh: across a narrow gap, or
    /// down a ledge of 0.8 to 2 m. The bake has no ledge-drop or jump generation of its own (that is a
    /// <c>NavMeshSurface</c> feature, and the world is baked from raw sources), so a one-slab terrace
    /// whose stair did not bake, or a roof a metre off the next, is an island the agent can never leave.
    ///
    /// <para>
    /// For each boundary edge of the mesh, at intervals along it, the finder looks straight out from
    /// the edge. It insists the first <see cref="WorldNavMeshLinkSettings.voidProbeDistance"/> is empty
    /// (a neighbouring tile's mesh begins right at the edge and is not a gap), then walks outward until
    /// it meets mesh on a <i>different</i> island within the settings' reach and height window. Links
    /// only join different islands: a drop onto the same piece is not what is missing.
    /// </para>
    /// </summary>
    public static class NavMeshAutoLinker
    {
        public sealed class Result
        {
            public readonly List<WorldNavMeshAsset.AutoLink> links = new();
            public int drops;
            public int gaps;
            /// <summary>True when <see cref="WorldNavMeshLinkSettings.maxLinks"/> stopped the search early.</summary>
            public bool truncated;
        }

        private readonly struct Placed
        {
            public readonly Vector2 start;
            public readonly Vector2 end;

            public Placed(Vector3 start, Vector3 end)
            {
                this.start = new Vector2(start.x, start.z);
                this.end = new Vector2(end.x, end.z);
            }
        }

        /// <summary>Whether something solid stands between a link's two ends: a wall or a fence, not a gap.</summary>
        public delegate bool LinkBlocked(Vector3 from, Vector3 to);

        /// <summary>
        /// Every link the settings allow on <paramref name="graph"/>. A candidate that <paramref name="blocked"/> says is
        /// walled off is dropped: the mesh edges either side of a thin fence look exactly like the edges of a gap.
        /// </summary>
        public static Result Find(NavMeshGraph graph, WorldNavMeshLinkSettings settings, LinkBlocked blocked = null)
        {
            var result = new Result();
            var placed = new Dictionary<(int, int, bool), List<Placed>>();
            var surfaces = new List<NavMeshGraph.Surface>();

            foreach (var edge in graph.BoundaryEdges)
            {
                int startIsland = graph.IslandOf(edge.triangle);
                if (startIsland < 0 || graph.IslandArea(startIsland) < settings.minIslandArea) continue;
                if (!TryOutward(edge, out Vector3 outward, out float length)) continue;

                int samples = Mathf.Max(1, Mathf.CeilToInt(length / settings.edgeSampleSpacing));
                for (int i = 0; i < samples; i++)
                {
                    Vector3 start = Vector3.Lerp(edge.a, edge.b, (i + 0.5f) / samples);
                    if (!TryLink(graph, settings, surfaces, start, outward, startIsland,
                                 out Vector3 end, out int endIsland, out bool drop))
                        continue;

                    if (blocked != null && blocked(start, end)) continue;
                    if (TooClose(placed, settings, startIsland, endIsland, drop, start, end)) continue;

                    result.links.Add(new WorldNavMeshAsset.AutoLink
                    {
                        start = start,
                        end = end,
                        bidirectional = !drop,
                    });
                    if (drop) result.drops++; else result.gaps++;

                    if (result.links.Count >= settings.maxLinks)
                    {
                        result.truncated = true;
                        return result;
                    }
                }
            }

            return result;
        }

        /// <summary>Unit XZ direction away from the mesh, perpendicular to the edge; false for an edge that is a point in plan.</summary>
        private static bool TryOutward(NavMeshGraph.BoundaryEdge edge, out Vector3 outward, out float length)
        {
            var along = new Vector2(edge.b.x - edge.a.x, edge.b.z - edge.a.z);
            length = along.magnitude;
            outward = default;
            if (length < 0.05f) return false;

            var normal = new Vector2(-along.y, along.x) / length;
            var toApex = new Vector2(edge.apex.x - edge.a.x, edge.apex.z - edge.a.z);
            if (Vector2.Dot(normal, toApex) > 0f) normal = -normal;

            outward = new Vector3(normal.x, 0f, normal.y);
            return true;
        }

        private static bool TryLink(NavMeshGraph graph, WorldNavMeshLinkSettings s,
                                    List<NavMeshGraph.Surface> surfaces, Vector3 start, Vector3 outward,
                                    int startIsland, out Vector3 end, out int endIsland, out bool drop)
        {
            end = default;
            endIsland = -1;
            drop = false;

            float lowest = start.y - s.maxDrop;
            float highest = start.y + s.maxRise;
            float reach = Mathf.Max(s.maxGap, s.maxDropReach);

            // The edge must open onto empty space, or it is a seam, a ramp's side or a notch.
            if (TopSurface(graph, surfaces, start + outward * s.voidProbeDistance, lowest, highest, out _, out _))
                return false;

            for (float d = s.voidProbeDistance + s.probeStep; d <= reach; d += s.probeStep)
            {
                Vector3 probe = start + outward * d;
                if (!TopSurface(graph, surfaces, probe, lowest, highest, out float y, out int triangle)) continue;

                endIsland = graph.IslandOf(triangle);
                if (endIsland == startIsland || graph.IslandArea(endIsland) < s.minIslandArea) return false;

                drop = start.y - y >= s.minDrop;
                if (d > (drop ? s.maxDropReach : s.maxGap)) return false;
                if (drop && graph.IslandArea(endIsland) < s.minDropLandingArea) return false;

                end = new Vector3(probe.x, y, probe.z);
                return true;
            }

            return false;
        }

        /// <summary>The highest surface under <paramref name="p"/>'s XZ whose height is in the window.</summary>
        private static bool TopSurface(NavMeshGraph graph, List<NavMeshGraph.Surface> scratch, Vector3 p,
                                       float lowest, float highest, out float y, out int triangle)
        {
            graph.SurfacesAt(p.x, p.z, scratch);
            y = float.NegativeInfinity;
            triangle = -1;

            foreach (var surface in scratch)
            {
                if (surface.y < lowest || surface.y > highest || surface.y <= y) continue;
                y = surface.y;
                triangle = surface.triangle;
            }
            return triangle >= 0;
        }

        /// <summary>
        /// Whether a link between these two pieces already stands near either end. A gap is crossed
        /// both ways, so its two sides share one key and the second side finds the first's link.
        /// </summary>
        private static bool TooClose(Dictionary<(int, int, bool), List<Placed>> placed,
                                     WorldNavMeshLinkSettings s, int startIsland, int endIsland, bool drop,
                                     Vector3 start, Vector3 end)
        {
            var key = drop ? (startIsland, endIsland, true)
                           : (Mathf.Min(startIsland, endIsland), Mathf.Max(startIsland, endIsland), false);

            if (!placed.TryGetValue(key, out var list)) placed[key] = list = new List<Placed>();

            var candidate = new Placed(start, end);
            float spacing = s.linkSpacing;
            foreach (var other in list)
            {
                if (Vector2.Distance(candidate.start, other.start) < spacing
                    || Vector2.Distance(candidate.start, other.end) < spacing
                    || Vector2.Distance(candidate.end, other.start) < spacing
                    || Vector2.Distance(candidate.end, other.end) < spacing)
                    return true;
            }

            list.Add(candidate);
            return false;
        }

        /// <summary>
        /// Links for a baked mesh that is not live yet: puts it on the NavMesh just long enough to
        /// read its triangulation. Refuses when another NavMesh is already live, whose triangles would
        /// be mistaken for this one's.
        /// </summary>
        public static Result FindOnBakedData(UnityEngine.AI.NavMeshData data, WorldNavMeshLinkSettings settings, LinkBlocked blocked = null)
        {
            if (NavMeshGraph.AnyNavMeshLive())
                throw new System.InvalidOperationException(
                    "Another NavMesh is live in the editor; its triangles would be mixed into the " +
                    "links. Leave Play mode and remove any other NavMeshSurface data first.");

            var instance = NavMesh.AddNavMeshData(data);
            try
            {
                return Find(NavMeshGraph.FromActiveNavMesh(settings.weldTolerance), settings, blocked);
            }
            finally
            {
                instance.Remove();
            }
        }
    }
}
