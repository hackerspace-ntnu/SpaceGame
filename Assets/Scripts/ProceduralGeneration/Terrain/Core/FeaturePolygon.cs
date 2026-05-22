using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A serializable closed polygon in the feature's LOCAL XZ plane (Y is ignored). It is pure
/// geometry — a list of vertices plus the queries a feature and the Scene-view editor need:
/// signed distance to the boundary, containment, bounds, and the edge operations that make
/// hand-editing reliable (insert a vertex on the nearest edge, find the closest edge).
///
/// This is the raw outline storage for BOTH footprint modes:
///   • Polygon mode — the designer hand-edits these vertices directly.
///   • Noise mode   — <see cref="FootprintNoise"/> regenerates these vertices from the box + knobs.
/// Either way a feature only ever asks <see cref="SignedDistanceInside"/>; it never cares which
/// mode filled the list. Noise GENERATION lives in <see cref="FootprintNoise"/>, not here, so this
/// class stays a single-responsibility geometry primitive.
///
/// Sign convention of <see cref="SignedDistanceInside"/>: POSITIVE inside the polygon (metres in
/// from the nearest edge), NEGATIVE outside — the exact value every area feature feeds to
/// <see cref="TerrainNoiseHelper.OverlapWeight"/>.
/// </summary>
[System.Serializable]
public class FeaturePolygon
{
    [Tooltip("Ordered vertices of the closed footprint, in feature-local space. Y is ignored — " +
             "only X and Z matter. At least 3 are required to define an area.")]
    public List<Vector3> vertices = new List<Vector3>();

    /// <summary>True when the polygon has enough vertices to define an area.</summary>
    public bool IsValid => vertices != null && vertices.Count >= 3;

    /// <summary>Vertex count (0 when unused).</summary>
    public int Count => vertices != null ? vertices.Count : 0;

    // -------------------------------------------------------------------------
    // Seeding
    // -------------------------------------------------------------------------

    /// <summary>
    /// Replaces the polygon with a rectangle matching the given local-space box half-extents
    /// (Width on X, Breadth on Z), so a freshly created area feature starts from a clean shape
    /// the designer can reshape from.
    /// </summary>
    public void ResetToBox(Vector3 localBoxHalfExtents)
    {
        float hx = Mathf.Max(1f, localBoxHalfExtents.x);
        float hz = Mathf.Max(1f, localBoxHalfExtents.z);
        vertices = new List<Vector3>
        {
            new Vector3(-hx, 0f, -hz),
            new Vector3( hx, 0f, -hz),
            new Vector3( hx, 0f,  hz),
            new Vector3(-hx, 0f,  hz),
        };
    }

    /// <summary>
    /// Replaces the polygon with a smooth N-gon ellipse filling the box half-extents — a friendlier
    /// hand-editing starting point than a hard rectangle.
    /// </summary>
    public void ResetToEllipse(Vector3 localBoxHalfExtents, int segments = 12)
    {
        float hx = Mathf.Max(1f, localBoxHalfExtents.x);
        float hz = Mathf.Max(1f, localBoxHalfExtents.z);
        int n = Mathf.Max(3, segments);
        var pts = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
        {
            float a = (i / (float)n) * Mathf.PI * 2f;
            pts.Add(new Vector3(Mathf.Cos(a) * hx, 0f, Mathf.Sin(a) * hz));
        }
        vertices = pts;
    }

    // -------------------------------------------------------------------------
    // Editing — used by the Scene-view handles
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the polygon edge nearest the local-space (x, z) point and returns the index of the
    /// edge's FIRST vertex (the new vertex would be inserted right after it). <paramref name="closest"/>
    /// receives the closest point on that edge. Returns -1 if the polygon has no edges.
    /// </summary>
    public int ClosestEdge(float x, float z, out Vector3 closest)
    {
        closest = Vector3.zero;
        if (vertices == null || vertices.Count < 2) return -1;

        Vector2 p = new Vector2(x, z);
        int best = -1;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < vertices.Count; i++)
        {
            int j = (i + 1) % vertices.Count;
            Vector2 a = new Vector2(vertices[i].x, vertices[i].z);
            Vector2 b = new Vector2(vertices[j].x, vertices[j].z);
            Vector2 ab = b - a;
            float lenSqr = Vector2.Dot(ab, ab);
            float t = lenSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSqr) : 0f;
            Vector2 onEdge = a + ab * t;
            float dSqr = (p - onEdge).sqrMagnitude;
            if (dSqr < bestSqr)
            {
                bestSqr = dSqr;
                best = i;
                closest = new Vector3(onEdge.x, 0f, onEdge.y);
            }
        }
        return best;
    }

    /// <summary>
    /// Inserts a vertex at <paramml="point"/> on the edge nearest that point — the click-on-edge
    /// authoring action. Returns the index the new vertex landed at, or -1 if it could not insert.
    /// </summary>
    public int InsertOnNearestEdge(Vector3 point)
    {
        int edge = ClosestEdge(point.x, point.z, out _);
        if (edge < 0) return -1;
        int at = edge + 1;
        vertices.Insert(at, new Vector3(point.x, 0f, point.z));
        return at;
    }

    /// <summary>
    /// Inserts a vertex at the midpoint of the edge that starts at <paramref name="vertexIndex"/>
    /// — the per-vertex "+" button action. Returns the new vertex index, or -1 on a bad index.
    /// </summary>
    public int InsertAfter(int vertexIndex)
    {
        if (vertices == null || vertexIndex < 0 || vertexIndex >= vertices.Count) return -1;
        int next = (vertexIndex + 1) % vertices.Count;
        Vector3 mid = (vertices[vertexIndex] + vertices[next]) * 0.5f;
        int at = vertexIndex + 1;
        vertices.Insert(at, new Vector3(mid.x, 0f, mid.z));
        return at;
    }

    /// <summary>
    /// Removes the vertex at <paramref name="vertexIndex"/>, refusing to drop below the 3 vertices
    /// a polygon needs. Returns true if a vertex was actually removed.
    /// </summary>
    public bool RemoveVertex(int vertexIndex)
    {
        if (vertices == null || vertices.Count <= 3) return false;
        if (vertexIndex < 0 || vertexIndex >= vertices.Count) return false;
        vertices.RemoveAt(vertexIndex);
        return true;
    }

    /// <summary>Sets a vertex, snapping it onto the local XZ plane (Y forced to 0).</summary>
    public void SetVertex(int vertexIndex, Vector3 localPoint)
    {
        if (vertices == null || vertexIndex < 0 || vertexIndex >= vertices.Count) return;
        vertices[vertexIndex] = new Vector3(localPoint.x, 0f, localPoint.z);
    }

    // -------------------------------------------------------------------------
    // Queries — what features and the bounds pipeline read
    // -------------------------------------------------------------------------

    /// <summary>
    /// Signed distance from the local-space (x, z) point to the polygon boundary. POSITIVE inside
    /// (metres in from the nearest edge), NEGATIVE outside. The single value an area feature feeds
    /// to <see cref="TerrainNoiseHelper.OverlapWeight"/>, so the silhouette is exactly this polygon.
    /// </summary>
    public float SignedDistanceInside(float x, float z)
    {
        if (!IsValid) return 0f;

        Vector2 p = new Vector2(x, z);
        float minSqr = float.MaxValue;
        bool inside = false;

        // Distance to the nearest edge + even-odd point-in-polygon test, in one pass.
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            Vector2 a = new Vector2(vertices[i].x, vertices[i].z);
            Vector2 b = new Vector2(vertices[j].x, vertices[j].z);

            // Distance to segment a-b.
            Vector2 ab = b - a;
            float lenSqr = Vector2.Dot(ab, ab);
            float t = lenSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSqr) : 0f;
            float dSqr = (p - (a + ab * t)).sqrMagnitude;
            if (dSqr < minSqr) minSqr = dSqr;

            // Ray-cast parity test for containment.
            if (((a.y > p.y) != (b.y > p.y)) &&
                (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x))
                inside = !inside;
        }

        float dist = Mathf.Sqrt(minSqr);
        return inside ? dist : -dist;
    }

    /// <summary>True if the local-space (x, z) point lies inside the polygon.</summary>
    public bool Contains(float x, float z) => SignedDistanceInside(x, z) > 0f;

    /// <summary>Axis-aligned local-space bounds enclosing every vertex (zero-size if empty). Y is
    /// always zero — area features derive their vertical band from the height function.</summary>
    public Bounds ComputeLocalBounds(float padding = 0f)
    {
        if (Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
        Bounds b = new Bounds(new Vector3(vertices[0].x, 0f, vertices[0].z), Vector3.zero);
        for (int i = 1; i < vertices.Count; i++)
            b.Encapsulate(new Vector3(vertices[i].x, 0f, vertices[i].z));
        if (padding != 0f) b.Expand(padding * 2f);
        return b;
    }

    /// <summary>Replaces this polygon's vertices with a deep copy of <paramref name="source"/>.</summary>
    public void CopyFrom(FeaturePolygon source)
    {
        vertices = new List<Vector3>(source != null ? source.Count : 0);
        if (source?.vertices == null) return;
        foreach (var v in source.vertices) vertices.Add(v);
    }
}
