using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marching-cubes iso-surface extractor for terrain features. Adapted from the cave system's
/// <c>MarchingCubesMesher</c>: same Paul-Bourke algorithm, same <see cref="MarchingCubesTables"/>
/// lookup tables, but with two terrain-specific changes:
///
///   1. WINDING — caves want normals facing inward (player sees the inside of the rock). Terrain
///      is a solid lump seen from outside, so triangle winding is NOT reversed: normals face out.
///   2. SURFACE-BAND WALK — for an <see cref="ITerrainDensity.IsHeightfield"/> density the mesher
///      voxelises only a thin slab straddling the surface instead of the whole volume. This makes
///      a heightfield feature cost scale with footprint AREA, not VOLUME (the performance mandate).
///      For a voxel SDF density it walks the full bounds, exactly like the cave mesher.
///
/// Output is a feature-local-space <see cref="Mesh"/>, smoothed via the shared
/// <see cref="MeshSmoothingUtility"/> and given gradient or flat normals — terrain must not look
/// flat-faceted, so smoothing defaults on.
/// </summary>
public static class TerrainMarchingCubesMesher
{
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

        var verts = new List<Vector3>(2048);
        var tris = new List<int>(4096);

        Vector3[] cornerPos = new Vector3[8];
        float[] cornerVal = new float[8];
        Vector3[] edgeVerts = new Vector3[12];

        // Vertical band half-thickness used to skip columns far from a heightfield surface.
        float bandHalf = settings.surfaceBandVoxels * voxel;

        for (int z = 0; z < nz - 1; z++)
        for (int x = 0; x < nx - 1; x++)
        {
            // Heightfield fast-path: only march the voxels whose Y range straddles the surface.
            //
            // A marching cube at (x,y,z) reads 8 corners spanning the columns x..x+1, z..z+1, so
            // its triangles can sit anywhere between THOSE columns' surface heights — not just this
            // column's. On a steep feature (a butte wall, a cliff face, a narrow overlap band) the
            // surface can jump tens of metres between adjacent columns; a band sized from this
            // column alone would miss the connecting wall voxels and leave vertical holes.
            //
            // Fix: size the band from the min/max surface height over the 3x3 block of columns
            // centred here. The band then auto-expands to exactly bridge the local steepness —
            // cheap on flat ground, fully sealed on cliffs — without needing a large fixed
            // surfaceBandVoxels.
            int yLo = 0;
            int yHi = ny - 1;
            if (density.IsHeightfield)
            {
                float loSurf = float.MaxValue;
                float hiSurf = float.MinValue;
                for (int sz = -1; sz <= 2; sz++)
                for (int sx = -1; sx <= 2; sx++)
                {
                    float wx = origin.x + (x + sx) * voxel;
                    float wz = origin.z + (z + sz) * voxel;
                    float s = density.SurfaceHeight(wx, wz);
                    if (s < loSurf) loSurf = s;
                    if (s > hiSurf) hiSurf = s;
                }
                yLo = Mathf.Clamp(Mathf.FloorToInt((loSurf - bandHalf - origin.y) / voxel), 0, ny - 2);
                yHi = Mathf.Clamp(Mathf.CeilToInt((hiSurf + bandHalf - origin.y) / voxel), 0, ny - 2);
            }

            for (int y = yLo; y <= yHi; y++)
            {
                int cubeIndex = 0;
                for (int i = 0; i < 8; i++)
                {
                    var off = MarchingCubesTables.CornerOffsets[i];
                    Vector3 cp = origin + new Vector3(
                        (x + off[0]) * voxel, (y + off[1]) * voxel, (z + off[2]) * voxel);
                    cornerPos[i] = cp;
                    float v = density.Sample(cp);
                    cornerVal[i] = v;
                    if (v < 0f) cubeIndex |= (1 << i);
                }

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

        return Finalise(verts, tris, density, settings);
    }

    /// <summary>
    /// Builds the Unity mesh from the raw MC vertex/triangle lists and runs the shared smoothing /
    /// normal passes. Smoothing is the visual workhorse — terrain MC output is blocky and must be
    /// welded + Laplacian-smoothed (Taubin volume-preserving) so it does not read as faceted.
    /// </summary>
    static Mesh Finalise(List<Vector3> verts, List<int> tris, ITerrainDensity density, TerrainMeshSettings settings)
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

        // Analytic gradient normals — same trick the cave mesher uses, but NOT negated: terrain
        // normals point away from the solid (the density gradient already points solid→air).
        if (settings.useGradientNormals)
            ApplyGradientNormals(mesh, density, Mathf.Max(0.01f, settings.gradientEpsilon));

        return mesh;
    }

    /// <summary>
    /// Per-vertex normals from the density gradient. Identical maths to
    /// <c>MeshSmoothingUtility.ApplyGradientNormals</c> but for the terrain (outward) convention,
    /// so the gradient is used as-is rather than negated.
    /// </summary>
    static void ApplyGradientNormals(Mesh mesh, ITerrainDensity density, float epsilon)
    {
        var verts = mesh.vertices;
        var normals = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = verts[i];
            float dx = density.Sample(p + new Vector3(epsilon, 0, 0)) - density.Sample(p - new Vector3(epsilon, 0, 0));
            float dy = density.Sample(p + new Vector3(0, epsilon, 0)) - density.Sample(p - new Vector3(0, epsilon, 0));
            float dz = density.Sample(p + new Vector3(0, 0, epsilon)) - density.Sample(p - new Vector3(0, 0, epsilon));
            Vector3 grad = new Vector3(dx, dy, dz);
            float m = grad.magnitude;
            normals[i] = m > 1e-6f ? grad / m : Vector3.up;
        }
        mesh.SetNormals(normals);
    }

    static Vector3 Interpolate(Vector3 a, Vector3 b, float va, float vb)
    {
        float denom = vb - va;
        if (Mathf.Abs(denom) < 1e-6f) return (a + b) * 0.5f;
        float t = Mathf.Clamp01(-va / denom);
        return a + (b - a) * t;
    }
}
