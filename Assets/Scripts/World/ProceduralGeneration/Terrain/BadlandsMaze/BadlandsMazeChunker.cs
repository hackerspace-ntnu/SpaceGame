using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ====================================================================================
/// STAGE 3b — INTERNAL CHUNKING: seamless sub-meshes from one global SDF.
/// ====================================================================================
///
/// The BadlandsMaze site is large. Meshing it as ONE giant voxel volume would be slow and produce
/// an oversized mesh. So this splits the site footprint into a GRID of sub-tiles and meshes each
/// tile's portion of the field separately — the feature thus produces MULTIPLE sub-meshes, each
/// from a modest voxel volume. This is a direct copy of <see cref="ArchingCaveChunker"/>'s strategy
/// — the seamlessness guarantee is identical and proven.
///
/// SEAMLESSNESS — the critical guarantee:
///   1. Every sub-tile samples the SAME global <see cref="BadlandsMazeSdf"/>. The field value at
///      any world point is identical no matter which tile asks for it.
///   2. Tile bounds are SNAPPED to a common voxel lattice (origin + integer multiples of the voxel
///      size), so two lattice-aligned neighbouring tiles place grid points at exactly the same
///      world coordinates along the shared border.
///   3. Each tile's bounds is PADDED outward by one voxel so adjacent tiles' marching cells overlap
///      — every border surface triangle is generated identically by both tiles, no crack.
///
/// Empty tiles (no solid rock anywhere inside) are skipped, so a sparse maze costs only the tiles
/// that actually contain geometry.
/// </summary>
public static class BadlandsMazeChunker
{
    /// <summary>
    /// Meshes the global SDF as a set of seamless sub-meshes. Returns one <see cref="Mesh"/> per
    /// non-empty sub-tile, each in feature-local space, ready for a MeshFilter+MeshCollider.
    /// </summary>
    public static List<Mesh> BuildSubMeshes(
        BadlandsMazeSdf sdf, BadlandsMazeSettings settings, TerrainMeshSettings meshSettings)
    {
        var meshes = new List<Mesh>();
        if (sdf == null) return meshes;
        if (meshSettings == null) meshSettings = new TerrainMeshSettings();

        float voxel = Mathf.Max(0.5f, meshSettings.voxelSize);
        Bounds site = sdf.Bounds;

        // The common voxel lattice origin. Every tile's bounds.min is snapped to this.
        Vector3 latticeOrigin = site.min;

        // Sub-tile XZ size, snapped UP to a whole number of voxels.
        float rawTile = Mathf.Max(voxel * 4f, settings.subTileSize);
        int tileVoxels = Mathf.Max(4, Mathf.CeilToInt(rawTile / voxel));
        float tileSize = tileVoxels * voxel;

        int tilesX = Mathf.Max(1, Mathf.CeilToInt(site.size.x / tileSize));
        int tilesZ = Mathf.Max(1, Mathf.CeilToInt(site.size.z / tileSize));

        // One voxel of overlap padding on every side — that is what seals the seam.
        float pad = voxel;

        for (int tz = 0; tz < tilesZ; tz++)
        for (int tx = 0; tx < tilesX; tx++)
        {
            float minX = latticeOrigin.x + tx * tileSize;
            float minZ = latticeOrigin.z + tz * tileSize;
            float maxX = minX + tileSize;
            float maxZ = minZ + tileSize;

            Vector3 tileMin = new Vector3(minX - pad, site.min.y - pad, minZ - pad);
            Vector3 tileMax = new Vector3(maxX + pad, site.max.y + pad, maxZ + pad);
            Bounds tileBounds = new Bounds((tileMin + tileMax) * 0.5f, tileMax - tileMin);

            // Skip wholly-empty tiles (no solid rock) so a sparse site is cheap.
            if (TileIsEmpty(sdf, tileBounds, voxel)) continue;

            // Wrap the SHARED global SDF in a per-tile density whose Bounds is just this tile.
            var tileDensity = new VoxelSdfDensity(sdf.Sample, tileBounds);
            Mesh mesh = TerrainMarchingCubesMesher.Build(tileDensity, meshSettings);
            if (mesh != null && mesh.vertexCount > 0)
            {
                mesh.name = $"BadlandsMazeTile_{tx}_{tz}";
                meshes.Add(mesh);
            }
        }

        return meshes;
    }

    /// <summary>
    /// Coarse emptiness test: probes the tile on a low-resolution lattice and reports whether the
    /// field is positive (air) everywhere — such a tile contributes no geometry and is skipped.
    /// </summary>
    static bool TileIsEmpty(BadlandsMazeSdf sdf, Bounds tile, float voxel)
    {
        float step = voxel * 3f;
        int nx = Mathf.Max(2, Mathf.CeilToInt(tile.size.x / step));
        int ny = Mathf.Max(2, Mathf.CeilToInt(tile.size.y / step));
        int nz = Mathf.Max(2, Mathf.CeilToInt(tile.size.z / step));

        for (int z = 0; z <= nz; z++)
        for (int y = 0; y <= ny; y++)
        for (int x = 0; x <= nx; x++)
        {
            Vector3 p = new Vector3(
                Mathf.Lerp(tile.min.x, tile.max.x, x / (float)nx),
                Mathf.Lerp(tile.min.y, tile.max.y, y / (float)ny),
                Mathf.Lerp(tile.min.z, tile.max.z, z / (float)nz));
            if (sdf.Sample(p) < step) return false;
        }
        return true;
    }
}
