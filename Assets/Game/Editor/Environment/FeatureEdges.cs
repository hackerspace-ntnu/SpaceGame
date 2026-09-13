// Pulls the edges worth drawing out of a triangle mesh: its silhouette and its hard creases, and
// none of the triangulation.
//
// A wireframe of every triangle is not a wireframe of a ship. The lander is 46,000 triangles; drawn
// edge for edge at the size the terminal's glass gives it, that is a hairball with a ship somewhere
// inside it. What reads as a technical drawing is the set of edges a draughtsman would ink: the
// boundary of each shell, and the folds where two faces meet at an angle. On hard-surface geometry
// that is a few thousand lines, and it is the same set however dense the mesh under it gets.
//
// Vertices are WELDED BY POSITION first. An FBX splits vertices at every normal and UV seam, so the
// two triangles either side of a perfectly flat quad usually do not share vertex indices at all —
// and without welding every one of those seams reads as a boundary, which puts a line on every
// triangle and defeats the whole exercise.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Accumulates the feature edges of one or more meshes into a single line mesh. Meshes handed
    /// to the same instance are welded together, so a hull cut into panels draws as one shell
    /// rather than as a cage of panel outlines.
    /// </summary>
    public sealed class FeatureEdges
    {
        /// <summary>
        /// Positions are rounded to this many units per metre before they are compared. The
        /// miniature is about a unit long, so 1e-5 is far below anything the modeller authored and
        /// far above the error of carrying a vertex through two transforms.
        /// </summary>
        private const float Quantum = 100000f;

        private readonly Dictionary<Vector3Int, int> lookup = new();
        private readonly List<Vector3> points = new();

        /// <summary>How many faces use each edge. Anything but two is a boundary.</summary>
        private readonly Dictionary<long, int> uses = new();

        /// <summary>The first face normal seen for an edge, and the sharpest fold found since.</summary>
        private readonly Dictionary<long, Vector3> firstNormal = new();
        private readonly Dictionary<long, float> sharpest = new();

        public int EdgeCount => uses.Count;

        /// <summary>
        /// Add a mesh, carried into <paramref name="space"/> — the frame the finished line mesh is
        /// expressed in, which is the one its renderer will sit at with an identity transform.
        /// </summary>
        public void Add(MeshFilter filter, Transform space)
        {
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return;

            Vector3[] source = mesh.vertices;
            int[] triangles = mesh.triangles;

            var placed = new Vector3[source.Length];
            var ids = new int[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                placed[i] = space.InverseTransformPoint(filter.transform.TransformPoint(source[i]));
                ids[i] = Weld(placed[i]);
            }

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];

                Vector3 normal = Vector3.Cross(placed[b] - placed[a], placed[c] - placed[a]);
                if (normal.sqrMagnitude < 1e-16f) continue; // A degenerate sliver has no fold to measure.
                normal.Normalize();

                Fold(ids[a], ids[b], normal);
                Fold(ids[b], ids[c], normal);
                Fold(ids[c], ids[a], normal);
            }
        }

        /// <summary>
        /// The line mesh: every boundary edge, plus every edge whose two faces meet at more than
        /// <paramref name="creaseDegrees"/>. Null when nothing survived, which is the caller's cue
        /// that this group has no wireframe worth a renderer.
        /// </summary>
        public Mesh ToMesh(string name, float creaseDegrees)
        {
            float sharpestFlat = Mathf.Cos(creaseDegrees * Mathf.Deg2Rad);

            // Only the vertices the kept edges actually touch reach the asset; the welded set holds
            // every corner of every face and most of them are in the middle of flat panels.
            var remap = new Dictionary<int, int>();
            var vertices = new List<Vector3>();
            var indices = new List<int>();

            foreach (KeyValuePair<long, int> edge in uses)
            {
                bool boundary = edge.Value != 2;
                bool crease = sharpest.TryGetValue(edge.Key, out float dot) && dot < sharpestFlat;
                if (!boundary && !crease) continue;

                indices.Add(Keep((int)(edge.Key >> 32), remap, vertices));
                indices.Add(Keep((int)(edge.Key & 0xFFFFFFFFL), remap, vertices));
            }

            if (indices.Count == 0) return null;

            var mesh = new Mesh { name = name };

            // A hull's worth of welded corners passes 65535 easily, and an overflowing 16-bit index
            // buffer wraps in silence.
            mesh.indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private int Keep(int welded, Dictionary<int, int> remap, List<Vector3> vertices)
        {
            if (remap.TryGetValue(welded, out int kept)) return kept;

            kept = vertices.Count;
            vertices.Add(points[welded]);
            remap[welded] = kept;
            return kept;
        }

        private int Weld(Vector3 point)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(point.x * Quantum),
                Mathf.RoundToInt(point.y * Quantum),
                Mathf.RoundToInt(point.z * Quantum));

            if (lookup.TryGetValue(key, out int id)) return id;

            id = points.Count;
            points.Add(point);
            lookup[key] = id;
            return id;
        }

        /// <summary>Record one face's use of one edge, and how far it folds away from the first.</summary>
        private void Fold(int a, int b, Vector3 normal)
        {
            if (a == b) return; // A welded degenerate edge is not an edge.

            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            if (!uses.TryGetValue(key, out int count))
            {
                uses[key] = 1;
                firstNormal[key] = normal;
                return;
            }

            uses[key] = count + 1;

            float dot = Vector3.Dot(firstNormal[key], normal);
            if (!sharpest.TryGetValue(key, out float worst) || dot < worst) sharpest[key] = dot;
        }
    }
}
