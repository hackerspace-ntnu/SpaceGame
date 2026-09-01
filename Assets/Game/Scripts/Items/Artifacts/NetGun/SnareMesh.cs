using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Lattice nodes in, one mesh of cord out.
    ///
    /// <para>
    /// A ribbon quad per strand segment, turned to face the viewer — which is what a LineRenderer
    /// does internally, done here for four hundred-odd lines at once so the whole net is a single
    /// draw call instead of thirty renderers each rebuilding their own geometry.
    /// </para>
    /// <para>
    /// <b>Why not a textured sheet.</b> One quad per cell with an alpha-cut net texture is far
    /// cheaper and reads perfectly well at twenty metres. It also stops being a net the moment the
    /// player walks up to the animal they just caught, which is the exact moment the item is asking
    /// to be looked at. The cost here is small enough not to buy the compromise.
    /// </para>
    /// <para>
    /// Buffers are allocated once and rewritten in place. A <see cref="Mesh"/> per frame is a
    /// collection spike that lands precisely while the net is on screen. Only the VERTICES are
    /// re-uploaded per rebuild: winding, UVs and index count are fixed by the node count, so they
    /// are written when the buffers are sized and never touched again.
    /// </para>
    /// </summary>
    public class SnareMesh : IDisposable
    {
        private Mesh mesh;
        private Vector3[] vertices;
        private Vector3[] normals;
        private Vector2[] uv;
        private int[] triangles;
        private int segmentCapacity;
        private bool topologyDirty;

        /// <summary>
        /// Rebuild for the lattice's current shape. Returns the same Mesh every call.
        ///
        /// <para>
        /// <paramref name="toViewer"/> points from the net TOWARD the camera, and the direction
        /// matters rather than just the axis. Unity treats <c>cross(v1-v0, v2-v0)</c> as the front
        /// face, and the ribbon is built so that works out to <paramref name="toViewer"/> — hand it
        /// the camera's forward vector instead and every quad in the net is wound backwards, so
        /// with ordinary back-face culling the entire net renders as nothing at all.
        /// </para>
        /// <para>
        /// <paramref name="origin"/> is the world position of the renderer this mesh is about to be
        /// hung on, and it is not optional. Lattice nodes are WORLD space — the drape clamps them
        /// against world ground heights and pushes them out of world-space capsules — while Unity
        /// draws vertices THROUGH the renderer's transform. Handing the raw node positions to a
        /// renderer sitting anywhere but the origin therefore draws the net at its own position
        /// plus the net's, which for a player five hundred metres out is a net drawn five hundred
        /// metres past them: no error, no warning, a gun that fires and produces nothing.
        /// </para>
        /// <para>
        /// Subtracting it here rather than pinning the renderer at the world origin is also what
        /// keeps the cord sharp. A 0.028 m ribbon written in absolute coordinates four kilometres
        /// across this game's world is a handful of float mantissa bits from the width it was
        /// authored at; written about its own centre it is exact.
        /// </para>
        /// </summary>
        public Mesh Build(SnareLattice lattice, Vector3 toViewer, float cordWidth, Vector3 origin)
        {
            int side = lattice.Resolution;
            int segments = 2 * side * (side - 1);

            Allocate(segments);

            Vector3 view = toViewer.sqrMagnitude < 1e-4f ? Vector3.forward : toViewer.normalized;
            int segment = 0;

            // Node positions are moved into the renderer's space here, so WriteSegment stays
            // indifferent to which space it is working in — every quantity it computes is either a
            // difference between its two endpoints or a direction, and translation changes neither.
            for (int row = 0; row < side; row++)
            for (int col = 0; col < side - 1; col++)
                WriteSegment(segment++, lattice.NodeAt(row, col) - origin,
                             lattice.NodeAt(row, col + 1) - origin, view, cordWidth);

            for (int col = 0; col < side; col++)
            for (int row = 0; row < side - 1; row++)
                WriteSegment(segment++, lattice.NodeAt(row, col) - origin,
                             lattice.NodeAt(row + 1, col) - origin, view, cordWidth);

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);

            // Indices last, and only when the buffers were resized. They cannot be uploaded before
            // the vertices they point at exist, and re-uploading an unchanged index buffer every
            // frame pays for a full index validation to write back exactly what was already there.
            if (topologyDirty)
            {
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(triangles, 0, calculateBounds: false);
                topologyDirty = false;
            }

            mesh.RecalculateBounds();

            return mesh;
        }

        private void Allocate(int segments)
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "SnareNet" };
                mesh.MarkDynamic();
            }

            if (segmentCapacity == segments) return;

            segmentCapacity = segments;
            vertices = new Vector3[segments * 4];
            normals = new Vector3[segments * 4];
            uv = new Vector2[segments * 4];
            triangles = new int[segments * 6];

            // Winding never changes, so it is written once rather than every frame.
            for (int s = 0; s < segments; s++)
            {
                int v = s * 4;
                int t = s * 6;

                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;

                uv[v + 0] = new Vector2(0f, 0f);
                uv[v + 1] = new Vector2(0f, 1f);
                uv[v + 2] = new Vector2(1f, 0f);
                uv[v + 3] = new Vector2(1f, 1f);
            }

            mesh.Clear();
            topologyDirty = true;
        }

        /// <summary>
        /// One ribbon from <paramref name="a"/> to <paramref name="b"/>, broadened across the view.
        ///
        /// <para>
        /// The width axis is the strand crossed with the view direction, so a cord seen end-on
        /// stays visible instead of collapsing to nothing — the degenerate case a fixed world-space
        /// width would hit every time the player looks along a strand.
        /// </para>
        /// <para>
        /// The normal is written here rather than left to <c>RecalculateNormals</c>. It is not an
        /// approximation of it: for a quad built this way the two are the same vector, because
        /// <c>cross(across, along)</c> is exactly the face normal the winding produces. Computing
        /// it costs one cross product per segment, where RecalculateNormals walks every triangle
        /// and every vertex again, every frame, to arrive at the answer already in hand.
        /// </para>
        /// </summary>
        private void WriteSegment(int segment, Vector3 a, Vector3 b, Vector3 view, float width)
        {
            Vector3 along = b - a;

            Vector3 across = Vector3.Cross(along, view);
            across = across.sqrMagnitude < 1e-8f
                ? Vector3.Cross(along, Vector3.up).normalized
                : across.normalized;

            Vector3 offset = across * (width * 0.5f);
            Vector3 normal = Vector3.Cross(across, along).normalized;
            int v = segment * 4;

            vertices[v + 0] = a - offset;
            vertices[v + 1] = a + offset;
            vertices[v + 2] = b - offset;
            vertices[v + 3] = b + offset;

            normals[v + 0] = normal;
            normals[v + 1] = normal;
            normals[v + 2] = normal;
            normals[v + 3] = normal;
        }

        public void Dispose()
        {
            if (mesh == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);

            mesh = null;
        }
    }
}
