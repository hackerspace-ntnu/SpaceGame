using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared terrain-skirt blend. A feature produces a standalone marching-cubes mesh; on its own
/// that mesh would float above, or punch through, the underlying Unity Terrain wherever the two
/// disagree. This helper closes that gap: it pulls the mesh's LOWER BAND of vertices down so they
/// land exactly on the ground, leaving no floating edge and no hard seam.
///
/// Every one of the nine features calls <see cref="Apply"/> as the last step of its build, so the
/// blend behaviour is identical and tunable in one place. The maths is a vertical snap weighted
/// by how close a vertex is to the feature's base — vertices at or below the base height are
/// snapped fully onto the ground, vertices high up are untouched, and a smooth band between the
/// two is interpolated so the transition reads as a natural skirt of sand/rock.
/// </summary>
public static class TerrainSkirtBlend
{
    /// <summary>
    /// Snaps the lower band of <paramref name="mesh"/> down onto the ground sampled through the
    /// <see cref="FeatureContext"/>. Mutates the mesh in place (vertices + bounds + normals).
    /// </summary>
    /// <param name="mesh">Feature-local-space mesh produced by the MC mesher.</param>
    /// <param name="context">Carries the ground sampler and the local→world transform.</param>
    /// <param name="blendBand">Height (metres) above the ground over which the snap fades from
    /// full (at/below ground) to none. Larger = a longer, gentler skirt. Drive this from the
    /// feature's <c>overlap</c> tuning for consistency.</param>
    /// <param name="embed">Metres the snapped vertices are pushed BELOW the ground, so the skirt
    /// visibly buries into the terrain instead of resting on a visible crease. 0.5–1 is typical.</param>
    public static void Apply(Mesh mesh, FeatureContext context, float blendBand, float embed)
    {
        if (mesh == null || mesh.vertexCount == 0 || context == null) return;

        var verts = mesh.vertices;
        float band = Mathf.Max(0.0001f, blendBand);

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 local = verts[i];
            // Ground height expressed in feature-local Y under this vertex's XZ column.
            float groundLocalY = context.LocalGroundHeight(local.x, local.z);

            // How far this vertex sits ABOVE the ground. Vertices below the ground (negative)
            // get fully snapped; vertices within the band get a smooth partial snap.
            float above = local.y - groundLocalY;
            float snap = 1f - Mathf.Clamp01(above / band);
            if (snap <= 0f) continue;

            float target = groundLocalY - embed;
            local.y = Mathf.Lerp(local.y, Mathf.Min(local.y, target), snap);
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
