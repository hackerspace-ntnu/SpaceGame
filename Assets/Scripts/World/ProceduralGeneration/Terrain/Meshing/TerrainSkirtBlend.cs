using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared terrain-skirt blend. A feature produces a standalone marching-cubes mesh; on its own
/// that mesh can punch THROUGH the underlying Unity Terrain wherever the feature surface dips below
/// the ground. This helper closes that seam.
///
/// IMPORTANT — this is NOT the feature's overlap/edge-falloff control. A feature shapes its own
/// soft edge with <see cref="TerrainNoiseHelper.OverlapWeight"/> driven by the <c>overlap</c>
/// tuning; that is the designer-facing blend. The skirt here only fixes the literal geometric
/// seam, with a SMALL FIXED band that does NOT depend on <c>overlap</c> — so raising the overlap
/// slider widens the feature's soft edge without the skirt eating into the feature.
///
/// What <see cref="Apply"/> does: a vertex that has sunk BELOW the terrain is lifted back up to
/// sit just under the ground (clears the punch-through); a vertex within a thin fixed band ABOVE
/// the ground is gently nudged down so the contact reads as a buried skirt rather than a floating
/// crease. Vertices clearly above the ground — the whole body of the feature — are never touched,
/// so low/flat features (dune fields, mesa bases, canyon floors) keep their full height.
/// </summary>
public static class TerrainSkirtBlend
{
    /// <summary>Fixed skirt band (metres above ground) over which the gentle contact-nudge fades.
    /// Deliberately small and constant — the feature's own OverlapWeight owns the wide soft edge.</summary>
    const float ContactBand = 1.5f;

    /// <summary>
    /// Closes the geometric seam between <paramref name="mesh"/> and the underlying terrain.
    /// Mutates the mesh in place (vertices + bounds + normals).
    ///
    /// Only two kinds of vertex are moved:
    ///   • BELOW ground — lifted up to <c>groundY - embed</c> so the feature stops poking through.
    ///   • Within <see cref="ContactBand"/> ABOVE ground — nudged down by a fraction that fades to
    ///     zero at the top of the band, so the base buries cleanly with no hard crease.
    /// Everything higher is left exactly as the feature authored it.
    /// </summary>
    /// <param name="mesh">Feature-local-space mesh produced by the MC mesher.</param>
    /// <param name="context">Carries the ground sampler and the local→world transform.</param>
    /// <param name="embed">Metres the contact vertices sit BELOW the ground, so the skirt buries
    /// into the terrain instead of resting on a visible crease. 0.5–1 is typical.</param>
    public static void Apply(Mesh mesh, FeatureContext context, float embed)
    {
        if (mesh == null || mesh.vertexCount == 0 || context == null) return;

        var verts = mesh.vertices;

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 local = verts[i];
            float groundLocalY = context.LocalGroundHeight(local.x, local.z);
            float target = groundLocalY - embed;        // where a contact vertex should sit
            float above = local.y - groundLocalY;       // signed height over the ground

            if (above <= 0f)
            {
                // Punching through the terrain — lift it back up to the buried-contact line.
                local.y = Mathf.Max(local.y, target);
            }
            else if (above < ContactBand)
            {
                // Thin contact band — gently nudge down toward the buried line, fading to zero
                // at the top of the band. Never raises the vertex, never touches the body above.
                float t = 1f - above / ContactBand;     // 1 at ground, 0 at band top
                local.y = Mathf.Lerp(local.y, Mathf.Min(local.y, target), t);
            }

            verts[i] = local;
        }

        mesh.SetVertices(verts);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
    }

    /// <summary>
    /// Welds the feature mesh's open bottom edge into a closed skirt down to the ground. Where the
    /// MC mesher left an open boundary at the bottom band, this finds those boundary edges and
    /// fans a vertical quad strip from each down to a ground-snapped vertex, so the feature reads
    /// as solid even when viewed from a low angle. Optional — call after <see cref="Apply"/> when
    /// a feature's footprint edge would otherwise show a hollow underside.
    /// </summary>
    /// <returns>The number of skirt triangles appended.</returns>
    public static int SealOpenBottom(Mesh mesh, FeatureContext context, float embed)
    {
        if (mesh == null || mesh.vertexCount == 0 || context == null) return 0;

        var verts = new List<Vector3>(mesh.vertices);
        var tris = new List<int>(mesh.triangles);

        // Count edge usage: an edge used by exactly one triangle is a boundary (open) edge.
        var edgeCount = new Dictionary<long, int>(tris.Count);
        for (int i = 0; i < tris.Count; i += 3)
        {
            Accumulate(edgeCount, tris[i + 0], tris[i + 1]);
            Accumulate(edgeCount, tris[i + 1], tris[i + 2]);
            Accumulate(edgeCount, tris[i + 2], tris[i + 0]);
        }

        int added = 0;
        foreach (var kv in edgeCount)
        {
            if (kv.Value != 1) continue;             // not a boundary edge
            int a = (int)(kv.Key >> 32);
            int b = (int)(kv.Key & 0xFFFFFFFF);

            Vector3 va = verts[a];
            Vector3 vb = verts[b];
            // Two ground-snapped vertices directly below the edge endpoints.
            Vector3 ga = new Vector3(va.x, context.LocalGroundHeight(va.x, va.z) - embed, va.z);
            Vector3 gb = new Vector3(vb.x, context.LocalGroundHeight(vb.x, vb.z) - embed, vb.z);

            int ia = verts.Count; verts.Add(ga);
            int ib = verts.Count; verts.Add(gb);

            // Quad (va, vb, gb, ga) as two triangles.
            tris.Add(a); tris.Add(b); tris.Add(ib);
            tris.Add(a); tris.Add(ib); tris.Add(ia);
            added += 2;
        }

        if (added == 0) return 0;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return added;
    }

    static void Accumulate(Dictionary<long, int> map, int i0, int i1)
    {
        // Order-independent key so the same edge from two triangles hashes identically.
        int lo = Mathf.Min(i0, i1);
        int hi = Mathf.Max(i0, i1);
        long key = ((long)lo << 32) | (uint)hi;
        map.TryGetValue(key, out int c);
        map[key] = c + 1;
    }
}
