using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marching-cubes iso-surface extractor for terrain features. Adapted from the cave system's
/// <c>MarchingCubesMesher</c>: same Paul-Bourke algorithm, same <see cref="MarchingCubesTables"/>
/// lookup tables.
///
/// PERFORMANCE — the density field is sampled ONCE per grid point into a cached array, exactly
/// like the cave mesher. A marching cube reads 8 corners, and every interior grid point is shared
/// by up to 8 neighbouring cubes; sampling per-corner would evaluate the field ~8× more than
/// necessary. For the voxel (overhang) path the density sample is expensive — multi-octave noise
/// plus undercut maths — so caching is the difference between a fast bake and a slow one.
///
/// Two density kinds:
///   • HEIGHTFIELD — the cache is filled only for the thin Y band straddling the surface in each
///     column (cost scales with footprint AREA). Rows outside the band are left "known solid /
///     known air" and skipped.
///   • VOXEL — the whole grid is cached and walked (real overhangs need the full volume).
///
/// Gradient normals also read the cached grid (trilinear) instead of re-sampling the field.
/// </summary>
public static class TerrainMarchingCubesMesher
{
    // Sentinel marking a grid cell that was never sampled (heightfield rows outside the band).
    const float Unsampled = float.MaxValue;

    public static Mesh Build(ITerrainDensity density, TerrainMeshSettings settings)
    {
        if (settings == null) settings = new TerrainMeshSettings();
        float voxel = Mathf.Max(0.1f, settings.voxelSize);

        Bounds b = density.Bounds;
        Vector3 origin = b.min - Vector3.one * voxel;
        Vector3 size = b.size + Vector3.one * voxel * 2f;

        int nx = Mathf.CeilToInt(size.x / voxel) + 1;
        int ny = Mathf.CeilToInt(size.y / voxel) + 1;
        int nz = Mathf.CeilToInt(size.z / voxel) + 1;

        // -------------------------------------------------------------------------
        // 1) Sample the density field ONCE per grid point into a cached array.
        //    Heightfield: only the band rows per column. Voxel: the whole grid.
        // -------------------------------------------------------------------------
        float[] grid = new float[nx * ny * nz];
        float bandHalf = settings.surfaceBandVoxels * voxel;
        bool heightfield = density.IsHeightfield;

        // Per-column band limits (heightfield only) — reused by the march pass so a cube never
        // touches an Unsampled corner.
        int[] colLo = heightfield ? new int[nx * nz] : null;
        int[] colHi = heightfield ? new int[nx * nz] : null;

        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int yLo = 0, yHi = ny - 1;
            if (heightfield)
            {
                // Band straddling the surface, sized from the 4×4 neighbour columns this point's
                // cubes can read — so a steep wall between columns is always bridged.
                // Columns the feature does not cover report HeightfieldDensity.NoColumn; they are
                // skipped here so an empty column produces no geometry and never drags a covered
                // neighbour's band down to the sentinel.
                float loSurf = float.MaxValue, hiSurf = float.MinValue;
                bool anyCovered = false;
                for (int sz = -1; sz <= 2; sz++)
                for (int sx = -1; sx <= 2; sx++)
                {
                    float wx = origin.x + (x + sx) * voxel;
                    float wz = origin.z + (z + sz) * voxel;
                    float s = density.SurfaceHeight(wx, wz);
                    if (s <= HeightfieldDensity.NoColumn) continue;   // uncovered — ignore
                    anyCovered = true;
                    if (s < loSurf) loSurf = s;
                    if (s > hiSurf) hiSurf = s;
                }

                if (!anyCovered)
                {
                    // Whole 4×4 neighbourhood is empty ground — mesh nothing in this column.
                    colLo[x + z * nx] = 1;
                    colHi[x + z * nx] = 0;          // lo > hi ⇒ no cube rows walked
                    for (int y = 0; y < ny; y++)
                        grid[Idx(x, y, z, nx, ny)] = Unsampled;
                }
                else
                {
                    yLo = Mathf.Clamp(Mathf.FloorToInt((loSurf - bandHalf - origin.y) / voxel), 0, ny - 1);
                    yHi = Mathf.Clamp(Mathf.CeilToInt((hiSurf + bandHalf - origin.y) / voxel), 0, ny - 1);
                    colLo[x + z * nx] = yLo;
                    colHi[x + z * nx] = yHi;

                    // Rows outside the band: mark as Unsampled and skip the field call entirely.
                    for (int y = 0; y < ny; y++)
                        if (y < yLo || y > yHi)
                            grid[Idx(x, y, z, nx, ny)] = Unsampled;
                }
            }

            for (int y = yLo; y <= yHi; y++)
            {
                Vector3 wp = origin + new Vector3(x * voxel, y * voxel, z * voxel);
                grid[Idx(x, y, z, nx, ny)] = density.Sample(wp);
            }
        }

        // -------------------------------------------------------------------------
        // 2) March the voxels, reading corner densities from the cache.
        // -------------------------------------------------------------------------
        var verts = new List<Vector3>(2048);
        var tris = new List<int>(4096);

        Vector3[] cornerPos = new Vector3[8];
        float[] cornerVal = new float[8];
        Vector3[] edgeVerts = new Vector3[12];

        for (int z = 0; z < nz - 1; z++)
        for (int x = 0; x < nx - 1; x++)
        {
            // Heightfield: derive this cube column's Y range from the per-column band of all four
            // columns the cube touches, so no cube ever reads an Unsampled corner.
            int yLo = 0, yHi = ny - 2;
            if (heightfield)
            {
                int lo = ny, hi = -1;
                for (int sz = 0; sz <= 1; sz++)
                for (int sx = 0; sx <= 1; sx++)
                {
                    int c = (x + sx) + (z + sz) * nx;
                    if (colLo[c] < lo) lo = colLo[c];
                    if (colHi[c] > hi) hi = colHi[c];
                }
                yLo = Mathf.Clamp(lo, 0, ny - 2);
                yHi = Mathf.Clamp(hi - 1, 0, ny - 2);
            }

            for (int y = yLo; y <= yHi; y++)
            {
                int cubeIndex = 0;
                bool incomplete = false;
                for (int i = 0; i < 8; i++)
                {
                    var off = MarchingCubesTables.CornerOffsets[i];
                    int cx = x + off[0], cy = y + off[1], cz = z + off[2];
                    cornerPos[i] = origin + new Vector3(cx * voxel, cy * voxel, cz * voxel);
                    float v = grid[Idx(cx, cy, cz, nx, ny)];
                    if (v == Unsampled) { incomplete = true; break; }
                    cornerVal[i] = v;
                    if (v < 0f) cubeIndex |= (1 << i);
                }
                if (incomplete) continue;   // cube straddles the band edge — skip cleanly

                int edges = MarchingCubesTables.EdgeTable[cubeIndex];
                if (edges == 0) continue;

                for (int e = 0; e < 12; e++)
                {
                    if ((edges & (1 << e)) == 0) continue;
                    int c0 = MarchingCubesTables.EdgeCorners[e][0];
                    int c1 = MarchingCubesTables.EdgeCorners[e][1];
                    edgeVerts[e] = Interpolate(cornerPos[c0], cornerPos[c1], cornerVal[c0], cornerVal[c1]);
                }

                var triList = MarchingCubesTables.TriTable[cubeIndex];
                for (int i = 0; triList[i] != -1; i += 3)
                {
                    int baseIdx = verts.Count;
                    // Terrain: keep the natural winding so triangle normals face OUT of the solid.
                    verts.Add(edgeVerts[triList[i + 0]]);
                    verts.Add(edgeVerts[triList[i + 1]]);
                    verts.Add(edgeVerts[triList[i + 2]]);
                    tris.Add(baseIdx + 0);
                    tris.Add(baseIdx + 1);
                    tris.Add(baseIdx + 2);
                }
            }
        }

        return Finalise(verts, tris, grid, origin, voxel, nx, ny, nz, settings);
    }

    /// <summary>
    /// Builds the Unity mesh from the raw MC lists and runs smoothing / normals. Smoothing welds
    /// and Laplacian-smooths the blocky MC output so it does not read as faceted.
    /// </summary>
    static Mesh Finalise(List<Vector3> verts, List<int> tris, float[] grid,
        Vector3 origin, float voxel, int nx, int ny, int nz, TerrainMeshSettings settings)
    {
        Mesh mesh = new Mesh { name = "TerrainFeatureMesh" };
        if (settings.use32BitIndices)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        if (verts.Count == 0)
        {
            mesh.Clear();
            return mesh;
        }

        if (settings.smoothingIterations > 0)
        {
            MeshSmoothingUtility.WeldAndSmooth(
                verts, tris, mesh,
                settings.smoothingIterations,
                settings.smoothingStrength,
                settings.smoothingPreserveVolume,
                settings.recalculateTangents);
        }
        else
        {
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (settings.recalculateTangents) mesh.RecalculateTangents();
        }

        // Gradient normals — derived from the CACHED grid (trilinear finite difference), so this
        // costs zero extra density-field evaluations.
        if (settings.useGradientNormals)
            ApplyGradientNormals(mesh, grid, origin, voxel, nx, ny, nz);

        return mesh;
    }

    /// <summary>
    /// Per-vertex normals from the density gradient, sampled from the cached grid by trilinear
    /// interpolation. The gradient points solid→air, which is the outward terrain normal.
    /// </summary>
    static void ApplyGradientNormals(Mesh mesh, float[] grid,
        Vector3 origin, float voxel, int nx, int ny, int nz)
    {
        var verts = mesh.vertices;
        var normals = new Vector3[verts.Length];
        float h = voxel * 0.5f;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = verts[i];
            float dx = SampleGrid(grid, p + new Vector3(h, 0, 0), origin, voxel, nx, ny, nz)
                     - SampleGrid(grid, p - new Vector3(h, 0, 0), origin, voxel, nx, ny, nz);
            float dy = SampleGrid(grid, p + new Vector3(0, h, 0), origin, voxel, nx, ny, nz)
                     - SampleGrid(grid, p - new Vector3(0, h, 0), origin, voxel, nx, ny, nz);
            float dz = SampleGrid(grid, p + new Vector3(0, 0, h), origin, voxel, nx, ny, nz)
                     - SampleGrid(grid, p - new Vector3(0, 0, h), origin, voxel, nx, ny, nz);
            Vector3 grad = new Vector3(dx, dy, dz);
            float m = grad.magnitude;
            normals[i] = m > 1e-6f ? grad / m : Vector3.up;
        }
        mesh.SetNormals(normals);
    }

    /// <summary>Trilinear sample of the cached density grid at a world-space point. Unsampled
    /// corners (heightfield band edges) fall back to their nearest sampled neighbour's sign.</summary>
    static float SampleGrid(float[] grid, Vector3 p, Vector3 origin, float voxel,
        int nx, int ny, int nz)
    {
        float fx = (p.x - origin.x) / voxel;
        float fy = (p.y - origin.y) / voxel;
        float fz = (p.z - origin.z) / voxel;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, nx - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, ny - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, nz - 1);
        int x1 = Mathf.Min(x0 + 1, nx - 1);
        int y1 = Mathf.Min(y0 + 1, ny - 1);
        int z1 = Mathf.Min(z0 + 1, nz - 1);
        float tx = Mathf.Clamp01(fx - x0);
        float ty = Mathf.Clamp01(fy - y0);
        float tz = Mathf.Clamp01(fz - z0);

        float c000 = G(grid, x0, y0, z0, nx, ny), c100 = G(grid, x1, y0, z0, nx, ny);
        float c010 = G(grid, x0, y1, z0, nx, ny), c110 = G(grid, x1, y1, z0, nx, ny);
        float c001 = G(grid, x0, y0, z1, nx, ny), c101 = G(grid, x1, y0, z1, nx, ny);
        float c011 = G(grid, x0, y1, z1, nx, ny), c111 = G(grid, x1, y1, z1, nx, ny);

        float x00 = Mathf.Lerp(c000, c100, tx), x10 = Mathf.Lerp(c010, c110, tx);
        float x01 = Mathf.Lerp(c001, c101, tx), x11 = Mathf.Lerp(c011, c111, tx);
        float y0v = Mathf.Lerp(x00, x10, ty), y1v = Mathf.Lerp(x01, x11, ty);
        return Mathf.Lerp(y0v, y1v, tz);
    }

    /// <summary>Reads a grid cell, treating an Unsampled cell as a large positive (air) value so
    /// gradients at the heightfield band edge still point sanely outward.</summary>
    static float G(float[] grid, int x, int y, int z, int nx, int ny)
    {
        float v = grid[Idx(x, y, z, nx, ny)];
        return v == Unsampled ? 1f : v;
    }

    static int Idx(int x, int y, int z, int nx, int ny) => x + y * nx + z * nx * ny;

    static Vector3 Interpolate(Vector3 a, Vector3 b, float va, float vb)
    {
        float denom = vb - va;
        if (Mathf.Abs(denom) < 1e-6f) return (a + b) * 0.5f;
        float t = Mathf.Clamp01(-va / denom);
        return a + (b - a) * t;
    }
}
