using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Single-threaded marching cubes mesh extractor.
    ///
    /// Algorithm (textbook, Paul Bourke 1994):
    ///   1. Sample the density field at every grid corner inside a padded version of the field's bounds.
    ///   2. For each voxel (cube of 8 corners), build an 8-bit mask of which corners are inside the cave
    ///      (sdf &lt; 0).
    ///   3. Use that mask to index <see cref="MarchingCubesTables.EdgeTable"/> → which of the 12 edges
    ///      cross the iso-surface. Linearly interpolate to find the exact crossing point per edge.
    ///   4. Use the mask to index <see cref="MarchingCubesTables.TriTable"/> → which edge triples form
    ///      output triangles.
    ///
    /// Output orientation: we treat <see cref="ICaveDensityField.Sample"/> as "negative = air". Marching
    /// cubes naturally orients the surface so the normal points from inside toward outside. With the
    /// convention we use that means triangle normals point *into the cave*, which is what we want —
    /// the player sees the inside of the walls.
    /// </summary>
    public static class MarchingCubesMesher
    {
        public static Mesh Build(ICaveDensityField field, CaveGenerationSettings settings)
        {
            Bounds b = field.Bounds;
            float voxel = Mathf.Max(0.1f, settings.voxelSize);

            // Pad outward so that anywhere the cave touches the bounds, we still get a sealed wall.
            Vector3 origin = b.min - Vector3.one * voxel;
            Vector3 size   = b.size + Vector3.one * voxel * 2f;

            int nx = Mathf.CeilToInt(size.x / voxel) + 1;
            int ny = Mathf.CeilToInt(size.y / voxel) + 1;
            int nz = Mathf.CeilToInt(size.z / voxel) + 1;

            // -------------------------------------------------------------------------
            // 1) Sample the density field on the corner grid.
            // -------------------------------------------------------------------------

            float[] density = new float[nx * ny * nz];
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                Vector3 wp = origin + new Vector3(x * voxel, y * voxel, z * voxel);
                density[Idx(x, y, z, nx, ny)] = field.Sample(wp);
            }

            // -------------------------------------------------------------------------
            // 2) March every voxel and emit triangles
            // -------------------------------------------------------------------------

            var verts = new List<Vector3>(1024);
            var tris  = new List<int>(2048);

            // Reused per-voxel buffers.
            Vector3[] cornerPos = new Vector3[8];
            float[]   cornerVal = new float[8];
            Vector3[] edgeVerts = new Vector3[12];

            for (int z = 0; z < nz - 1; z++)
            for (int y = 0; y < ny - 1; y++)
            for (int x = 0; x < nx - 1; x++)
            {
                // Gather corner positions + density values.
                int cubeIndex = 0;
                for (int i = 0; i < 8; i++)
                {
                    var off = MarchingCubesTables.CornerOffsets[i];
                    int cx = x + off[0];
                    int cy = y + off[1];
                    int cz = z + off[2];
                    cornerPos[i] = origin + new Vector3(cx * voxel, cy * voxel, cz * voxel);
                    cornerVal[i] = density[Idx(cx, cy, cz, nx, ny)];
                    if (cornerVal[i] < 0f) cubeIndex |= (1 << i);
                }

                int edges = MarchingCubesTables.EdgeTable[cubeIndex];
                if (edges == 0) continue;

                // Compute crossing point per active edge.
                for (int e = 0; e < 12; e++)
                {
                    if ((edges & (1 << e)) == 0) continue;
                    int c0 = MarchingCubesTables.EdgeCorners[e][0];
                    int c1 = MarchingCubesTables.EdgeCorners[e][1];
                    edgeVerts[e] = InterpolateVertex(cornerPos[c0], cornerPos[c1], cornerVal[c0], cornerVal[c1]);
                }

                // Emit triangles for this cube.
                var triList = MarchingCubesTables.TriTable[cubeIndex];
                for (int i = 0; triList[i] != -1; i += 3)
                {
                    int baseIdx = verts.Count;
                    // Reverse winding: our SDF convention (negative inside) makes default winding point
                    // away from the cave. Flip so normals face into the cave.
                    verts.Add(edgeVerts[triList[i + 0]]);
                    verts.Add(edgeVerts[triList[i + 2]]);
                    verts.Add(edgeVerts[triList[i + 1]]);
                    tris.Add(baseIdx + 0);
                    tris.Add(baseIdx + 1);
                    tris.Add(baseIdx + 2);
                }
            }

            // -------------------------------------------------------------------------
            // 3) Build the Unity mesh
            // -------------------------------------------------------------------------

            Mesh mesh = new Mesh { name = "CaveMesh" };
            if (settings.use32BitIndices)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // Three output modes, in priority order:
            //
            //   1. smoothingIterations > 0 → weld + Laplacian smooth + smooth normals.
            //      Best visual quality, masks marching-cube blockiness, ~100 ms per million tris.
            //   2. flatShade                → per-triangle vertex split + flat normals.
            //      Stylised low-poly look (original behaviour).
            //   3. else                     → raw welded mesh with averaged normals.
            //      Cheapest "smooth" output; not as clean as #1 because MC voxel seams remain.
            //
            // The mutual-exclusion is intentional: applying flat-shade after smoothing would undo
            // the point of smoothing (it splits every vertex again).
            if (settings.smoothingIterations > 0)
            {
                MeshSmoothingUtility.WeldAndSmooth(
                    verts, tris, mesh,
                    settings.smoothingIterations,
                    settings.smoothingStrength,
                    settings.smoothingPreserveVolume,
                    settings.recalculateTangents);
            }
            else if (settings.flatShade)
            {
                FlatShadeUtility.ApplyFlatShade(verts, tris, mesh);
                mesh.RecalculateBounds();
                if (settings.recalculateTangents) mesh.RecalculateTangents();
            }
            else
            {
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                if (settings.recalculateTangents) mesh.RecalculateTangents();
            }

            // Optional analytic-gradient normals — replaces whatever normals were just computed with
            // the true SDF gradient at each vertex. Geometry unchanged, lighting noticeably smoother.
            if (settings.useGradientNormals)
            {
                MeshSmoothingUtility.ApplyGradientNormals(mesh, field, Mathf.Max(0.01f, settings.gradientEpsilon));
            }

            return mesh;
        }

        static int Idx(int x, int y, int z, int nx, int ny) => x + y * nx + z * nx * ny;

        static Vector3 InterpolateVertex(Vector3 a, Vector3 b, float va, float vb)
        {
            // Linear interpolation toward the zero crossing. Guard against (vb - va) ≈ 0 which would
            // happen when the iso-surface clips through a corner.
            float denom = vb - va;
            if (Mathf.Abs(denom) < 1e-6f) return (a + b) * 0.5f;
            float t = Mathf.Clamp01(-va / denom);
            return a + (b - a) * t;
        }
    }
}
