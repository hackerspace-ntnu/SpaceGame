using System.Collections.Generic;
using UnityEngine;
using NavMesh = UnityEngine.AI.NavMesh;

namespace SpaceGame.World.NavMeshTools
{
    /// <summary>
    /// A NavMesh triangulation analysed as geometry: vertices welded, triangles grouped into islands
    /// (pieces joined across shared edges), the edges only one triangle uses (the boundary), and an
    /// XZ lookup from a point to the surfaces under it.
    ///
    /// Pure data in, pure data out, so a synthetic mesh can stand in for the baked world in a test.
    /// <see cref="FromActiveNavMesh"/> is the one place that reads the live NavMesh.
    ///
    /// <para>
    /// Islands join over shared <b>edges</b>, not shared vertices: two triangles that meet at a corner
    /// are not walkable into each other. A neighbouring tile that splits a seam differently from this
    /// one still reads as a separate island here, which is why <see cref="NavMeshAutoLinker"/> insists a
    /// gap is empty before it links across it.
    /// </para>
    /// </summary>
    public sealed class NavMeshGraph
    {
        /// <summary>An edge exactly one triangle uses.</summary>
        public readonly struct BoundaryEdge
        {
            public readonly Vector3 a;
            public readonly Vector3 b;
            /// <summary>The triangle's third corner: the side the mesh is on.</summary>
            public readonly Vector3 apex;
            public readonly int triangle;

            public BoundaryEdge(Vector3 a, Vector3 b, Vector3 apex, int triangle)
            {
                this.a = a;
                this.b = b;
                this.apex = apex;
                this.triangle = triangle;
            }
        }

        /// <summary>A walkable surface found under an XZ point.</summary>
        public readonly struct Surface
        {
            public readonly float y;
            public readonly int triangle;

            public Surface(float y, int triangle)
            {
                this.y = y;
                this.triangle = triangle;
            }
        }

        // Metres per lookup cell. Polygons here run from a stair tread to a whole terrain tile.
        private const float LookupCell = 4f;
        // Barycentric slack so a point exactly on a shared edge belongs to both triangles.
        private const float InsideSlack = 1e-4f;

        private readonly Vector3[] vertices;
        private readonly int[] indices;
        private readonly int[] triangleIsland;
        private readonly List<float> islandAreas = new();
        private readonly List<Vector3> islandCentres = new();
        private readonly List<BoundaryEdge> boundary = new();
        // XZ lookup, as arrays: the world mesh is millions of triangles and a per-cell list or per-edge
        // dictionary entry each costs minutes and gigabytes on Mono. Cell -> its run in cellTriangles.
        private readonly Dictionary<long, (int start, int count)> cells = new();
        private int[] cellTriangles;

        public int TriangleCount => indices.Length / 3;
        public int IslandCount => islandAreas.Count;
        public IReadOnlyList<BoundaryEdge> BoundaryEdges => boundary;

        /// <summary>Island of a triangle, 0 being the largest; -1 for a triangle welded away to nothing.</summary>
        public int IslandOf(int triangle) => triangleIsland[triangle];

        /// <summary>Surface area of an island in square metres.</summary>
        public float IslandArea(int island) => islandAreas[island];

        /// <summary>Area-weighted centre of an island, for telling islands apart in a log.</summary>
        public Vector3 IslandCentre(int island) => islandCentres[island];

        public NavMeshGraph(Vector3[] rawVertices, int[] rawIndices, float weldTolerance)
        {
            vertices = Weld(rawVertices, rawIndices, weldTolerance, out indices);
            triangleIsland = new int[indices.Length / 3];
            LabelIslandsAndBoundary();
            BuildLookup();
        }

        /// <summary>The triangulation of every NavMesh currently live, analysed.</summary>
        public static NavMeshGraph FromActiveNavMesh(float weldTolerance)
        {
            var t = NavMesh.CalculateTriangulation();
            return new NavMeshGraph(t.vertices, t.indices, weldTolerance);
        }

        /// <summary>Whether any NavMesh is live: a triangulation with a triangle in it.</summary>
        public static bool AnyNavMeshLive() => NavMesh.CalculateTriangulation().indices.Length > 0;

        /// <summary>Every walkable surface directly above or below <paramref name="x"/>, <paramref name="z"/>.</summary>
        public void SurfacesAt(float x, float z, List<Surface> into)
        {
            into.Clear();
            if (!cells.TryGetValue(CellKey(x, z), out var run)) return;

            for (int i = run.start; i < run.start + run.count; i++)
            {
                int tri = cellTriangles[i];
                if (TryHeightAt(tri, x, z, out float y)) into.Add(new Surface(y, tri));
            }
        }

        /// <summary>The island whose surface lies nearest in height to <paramref name="p"/> at its XZ, or -1.</summary>
        public int IslandNear(Vector3 p, float maxVerticalDistance)
        {
            var hits = new List<Surface>();
            SurfacesAt(p.x, p.z, hits);

            int best = -1;
            float bestDistance = maxVerticalDistance;
            foreach (var hit in hits)
            {
                float d = Mathf.Abs(hit.y - p.y);
                if (d > bestDistance) continue;
                bestDistance = d;
                best = triangleIsland[hit.triangle];
            }
            return best;
        }

        // Quantised coordinates are packed 21 bits apiece into one sortable key.
        private const int PackBits = 21;
        private const int PackOffset = 1 << (PackBits - 1);

        private static Vector3[] Weld(Vector3[] raw, int[] rawIndices, float tolerance, out int[] welded)
        {
            var keys = new long[raw.Length];
            var order = new int[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                keys[i] = PackKey(raw[i], tolerance);
                order[i] = i;
            }
            System.Array.Sort(keys, order);

            var remap = new int[raw.Length];
            var unique = new List<Vector3>();
            for (int i = 0; i < order.Length; i++)
            {
                if (i == 0 || keys[i] != keys[i - 1]) unique.Add(raw[order[i]]);
                remap[order[i]] = unique.Count - 1;
            }

            welded = new int[rawIndices.Length];
            for (int i = 0; i < rawIndices.Length; i++) welded[i] = remap[rawIndices[i]];
            return unique.ToArray();
        }

        private static long PackKey(Vector3 v, float tolerance)
        {
            long x = Mathf.RoundToInt(v.x / tolerance) + PackOffset;
            long y = Mathf.RoundToInt(v.y / tolerance) + PackOffset;
            long z = Mathf.RoundToInt(v.z / tolerance) + PackOffset;
            const long limit = 1L << PackBits;
            if (x < 0 || y < 0 || z < 0 || x >= limit || y >= limit || z >= limit)
                throw new System.ArgumentOutOfRangeException(nameof(v),
                    $"{v} is outside the +-{PackOffset * tolerance:0} m the welder can key at tolerance {tolerance}.");
            return (x << (2 * PackBits)) | (y << PackBits) | z;
        }

        private void LabelIslandsAndBoundary()
        {
            int triangles = TriangleCount;
            var parent = new int[triangles];
            for (int i = 0; i < triangles; i++) parent[i] = i;

            // Every edge of every live triangle, keyed by its two vertices; sorting brings the users
            // of one edge together. A run of one is a boundary edge, a run of two is a shared one.
            int live = 0;
            for (int t = 0; t < triangles; t++) if (!IsDegenerate(t)) live++;

            var keys = new long[live * 3];
            var uses = new int[live * 3];
            int n = 0;
            for (int t = 0; t < triangles; t++)
            {
                if (IsDegenerate(t)) continue;
                for (int slot = 0; slot < 3; slot++)
                {
                    keys[n] = EdgeKey(indices[t * 3 + slot], indices[t * 3 + (slot + 1) % 3]);
                    uses[n++] = t * 3 + slot;
                }
            }
            System.Array.Sort(keys, uses);

            for (int i = 0; i < n;)
            {
                int end = i + 1;
                while (end < n && keys[end] == keys[i]) end++;

                int first = uses[i] / 3;
                for (int j = i + 1; j < end; j++) Union(parent, first, uses[j] / 3);

                if (end - i == 1)
                {
                    int t = first, slot = uses[i] % 3;
                    boundary.Add(new BoundaryEdge(vertices[indices[t * 3 + slot]],
                                                  vertices[indices[t * 3 + (slot + 1) % 3]],
                                                  vertices[indices[t * 3 + (slot + 2) % 3]], t));
                }
                i = end;
            }

            // Sorted by edge key this is already deterministic for a given mesh.
            var areaByRoot = new Dictionary<int, float>();
            var momentByRoot = new Dictionary<int, Vector3>();
            for (int t = 0; t < triangles; t++)
            {
                if (IsDegenerate(t)) { triangleIsland[t] = -1; continue; }
                int root = Find(parent, t);
                float triangleArea = TriangleArea(t);
                areaByRoot.TryGetValue(root, out float area);
                momentByRoot.TryGetValue(root, out Vector3 moment);
                areaByRoot[root] = area + triangleArea;
                momentByRoot[root] = moment + TriangleCentroid(t) * triangleArea;
            }

            var roots = new List<int>(areaByRoot.Keys);
            roots.Sort((x, y) =>
            {
                int byArea = areaByRoot[y].CompareTo(areaByRoot[x]);
                return byArea != 0 ? byArea : x.CompareTo(y);
            });

            var islandOfRoot = new Dictionary<int, int>();
            foreach (int root in roots)
            {
                islandOfRoot[root] = islandAreas.Count;
                islandAreas.Add(areaByRoot[root]);
                islandCentres.Add(areaByRoot[root] > 0f ? momentByRoot[root] / areaByRoot[root] : Vector3.zero);
            }

            for (int t = 0; t < triangles; t++)
                if (triangleIsland[t] != -1) triangleIsland[t] = islandOfRoot[Find(parent, t)];
        }

        private void BuildLookup()
        {
            // (cell, triangle) pairs for every cell a triangle's XZ bounds touch, sorted by cell.
            var pairKeys = new List<long>();
            var pairTriangles = new List<int>();
            for (int t = 0; t < TriangleCount; t++)
            {
                if (triangleIsland[t] < 0) continue;

                Vector3 a = vertices[indices[t * 3]], b = vertices[indices[t * 3 + 1]], c = vertices[indices[t * 3 + 2]];
                int x0 = CellOf(Mathf.Min(a.x, b.x, c.x)), x1 = CellOf(Mathf.Max(a.x, b.x, c.x));
                int z0 = CellOf(Mathf.Min(a.z, b.z, c.z)), z1 = CellOf(Mathf.Max(a.z, b.z, c.z));

                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int cz = z0; cz <= z1; cz++)
                    {
                        pairKeys.Add(CellKey(cx, cz));
                        pairTriangles.Add(t);
                    }
                }
            }

            var keys = pairKeys.ToArray();
            cellTriangles = pairTriangles.ToArray();
            System.Array.Sort(keys, cellTriangles);

            for (int i = 0; i < keys.Length;)
            {
                int end = i + 1;
                while (end < keys.Length && keys[end] == keys[i]) end++;
                cells[keys[i]] = (i, end - i);
                i = end;
            }
        }

        private bool TryHeightAt(int triangle, float x, float z, out float y)
        {
            Vector3 a = vertices[indices[triangle * 3]], b = vertices[indices[triangle * 3 + 1]],
                    c = vertices[indices[triangle * 3 + 2]];

            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < 1e-9f) { y = 0f; return false; }

            float wa = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
            float wb = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
            float wc = 1f - wa - wb;

            y = wa * a.y + wb * b.y + wc * c.y;
            return wa >= -InsideSlack && wb >= -InsideSlack && wc >= -InsideSlack;
        }

        private bool IsDegenerate(int t)
        {
            int a = indices[t * 3], b = indices[t * 3 + 1], c = indices[t * 3 + 2];
            return a == b || b == c || a == c;
        }

        private float TriangleArea(int t) =>
            0.5f * Vector3.Cross(vertices[indices[t * 3 + 1]] - vertices[indices[t * 3]],
                                 vertices[indices[t * 3 + 2]] - vertices[indices[t * 3]]).magnitude;

        private Vector3 TriangleCentroid(int t) =>
            (vertices[indices[t * 3]] + vertices[indices[t * 3 + 1]] + vertices[indices[t * 3 + 2]]) / 3f;

        private static long EdgeKey(int a, int b) =>
            a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static int CellOf(float v) => Mathf.FloorToInt(v / LookupCell);
        private static long CellKey(int cx, int cz) => ((long)cx << 32) | (uint)cz;
        private static long CellKey(float x, float z) => CellKey(CellOf(x), CellOf(z));

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[Mathf.Max(ra, rb)] = Mathf.Min(ra, rb);
        }
    }
}
