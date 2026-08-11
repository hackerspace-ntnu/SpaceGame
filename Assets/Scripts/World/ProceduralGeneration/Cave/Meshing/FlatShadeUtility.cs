using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marching cubes produces a per-vertex-shared mesh whose smoothed normals look organic but blob-like.
/// For the low-poly look we want every triangle to have its own three vertices so each face renders
/// with a single flat normal — no special shader needed.
/// </summary>
public static class FlatShadeUtility
{
    public static void ApplyFlatShade(List<Vector3> verts, List<int> tris, Mesh mesh)
    {
        int triCount = tris.Count;
        var newVerts   = new Vector3[triCount];
        var newNormals = new Vector3[triCount];
        var newTris    = new int[triCount];

        for (int i = 0; i < triCount; i += 3)
        {
            Vector3 v0 = verts[tris[i + 0]];
            Vector3 v1 = verts[tris[i + 1]];
            Vector3 v2 = verts[tris[i + 2]];
            Vector3 n  = Vector3.Cross(v1 - v0, v2 - v0).normalized;

            newVerts[i + 0] = v0;
            newVerts[i + 1] = v1;
            newVerts[i + 2] = v2;
            newNormals[i + 0] = n;
            newNormals[i + 1] = n;
            newNormals[i + 2] = n;
            newTris[i + 0] = i + 0;
            newTris[i + 1] = i + 1;
            newTris[i + 2] = i + 2;
        }

        mesh.SetVertices(newVerts);
        mesh.SetNormals(newNormals);
        mesh.SetTriangles(newTris, 0);
    }
}
