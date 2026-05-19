using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Discovers liquid pools inside a cave by flood-filling the voxel grid produced by the SDF.
///
/// Algorithm:
///   1. Sample the same density field on a (typically coarser) voxel grid.
///   2. Determine the waterline Y from settings.fillFraction × cave vertical extent.
///   3. Mark every voxel that is BOTH inside the cave (sdf &lt; 0) AND below the waterline as
///      candidate liquid.
///   4. Run a 3D connected-component flood fill over those candidate voxels. Each component
///      becomes a <see cref="LiquidPool"/>.
///   5. Drop pools whose volume is below settings.minPoolVolume; sort largest first; trim to
///      maxPoolCount.
///
/// The flood fill uses 6-connectivity (face neighbours). This is what guarantees a pool only
/// fills regions actually reachable underwater — disconnected sub-floors won't merge.
/// </summary>
public static class LiquidPoolFinder
{
    /// <summary>Voxel size used for pool detection (independent of the mesher's voxel size).</summary>
    public const float DetectionVoxelSize = 1.5f;

    public static List<LiquidPool> Find(ICaveDensityField field, CaveLiquidSettings settings, Transform worldParent)
    {
        var result = new List<LiquidPool>();
        if (settings == null || !settings.enabled) return result;

        Bounds b = field.Bounds;
        float voxel = DetectionVoxelSize;

        int nx = Mathf.CeilToInt(b.size.x / voxel) + 1;
        int ny = Mathf.CeilToInt(b.size.y / voxel) + 1;
        int nz = Mathf.CeilToInt(b.size.z / voxel) + 1;
        if (nx <= 0 || ny <= 0 || nz <= 0) return result;

        float waterlineLocalY = b.min.y + b.size.y * settings.fillFraction;

        // -------------------------------------------------------------------------
        // 1) Mark candidate cells: inside the cave AND below the waterline.
        // -------------------------------------------------------------------------
        var candidate = new bool[nx * ny * nz];
        for (int z = 0; z < nz; z++)
        for (int y = 0; y < ny; y++)
        for (int x = 0; x < nx; x++)
        {
            Vector3 wp = b.min + new Vector3(x * voxel, y * voxel, z * voxel);
            if (wp.y > waterlineLocalY) continue;
            if (field.Sample(wp) < 0f) candidate[Idx(x, y, z, nx, ny)] = true;
        }

        // -------------------------------------------------------------------------
        // 2) Flood-fill to find connected components. Each cell visited at most once.
        // -------------------------------------------------------------------------
        var componentId = new int[nx * ny * nz];
        for (int i = 0; i < componentId.Length; i++) componentId[i] = -1;
        var stack = new Stack<(int x, int y, int z)>();
        int nextId = 0;
        var componentCells = new List<List<Vector3Int>>();

        for (int z = 0; z < nz; z++)
        for (int y = 0; y < ny; y++)
        for (int x = 0; x < nx; x++)
        {
            int seedIdx = Idx(x, y, z, nx, ny);
            if (!candidate[seedIdx] || componentId[seedIdx] != -1) continue;

            var cells = new List<Vector3Int>();
            stack.Clear();
            stack.Push((x, y, z));
            componentId[seedIdx] = nextId;

            while (stack.Count > 0)
            {
                var (cx, cy, cz) = stack.Pop();
                cells.Add(new Vector3Int(cx, cy, cz));
                TryPush(cx + 1, cy, cz);
                TryPush(cx - 1, cy, cz);
                TryPush(cx, cy + 1, cz);
                TryPush(cx, cy - 1, cz);
                TryPush(cx, cy, cz + 1);
                TryPush(cx, cy, cz - 1);
            }

            componentCells.Add(cells);
            nextId++;

            void TryPush(int ax, int ay, int az)
            {
                if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz) return;
                int aIdx = Idx(ax, ay, az, nx, ny);
                if (!candidate[aIdx] || componentId[aIdx] != -1) return;
                componentId[aIdx] = nextId; // mark BEFORE pushing to avoid double-enqueue
                stack.Push((ax, ay, az));
            }
        }

        // -------------------------------------------------------------------------
        // 3) Build pool descriptors, filter, sort, trim.
        // -------------------------------------------------------------------------
        float voxelVolume = voxel * voxel * voxel;

        for (int id = 0; id < componentCells.Count; id++)
        {
            var cells = componentCells[id];
            if (cells.Count == 0) continue;

            float volume = cells.Count * voxelVolume;
            if (volume < settings.minPoolVolume) continue;

            // Horizontal bounds = XZ extent of the component's cells.
            float xmin = float.MaxValue, xmax = float.MinValue;
            float zmin = float.MaxValue, zmax = float.MinValue;
            float ymin = float.MaxValue;
            foreach (var c in cells)
            {
                Vector3 wp = b.min + new Vector3(c.x * voxel, c.y * voxel, c.z * voxel);
                if (wp.x < xmin) xmin = wp.x;
                if (wp.x > xmax) xmax = wp.x;
                if (wp.z < zmin) zmin = wp.z;
                if (wp.z > zmax) zmax = wp.z;
                if (wp.y < ymin) ymin = wp.y;
            }

            Bounds hb = new Bounds();
            hb.SetMinMax(new Vector3(xmin, 0f, zmin), new Vector3(xmax + voxel, 0f, zmax + voxel));

            result.Add(new LiquidPool
            {
                Id = id,
                WaterlineY = waterlineLocalY,
                FloorY = ymin,
                HorizontalBounds = hb,
                VoxelCount = cells.Count,
                VolumeM3 = volume,
            });
        }

        result.Sort((a, b2) => b2.VolumeM3.CompareTo(a.VolumeM3));
        if (settings.maxPoolCount > 0 && result.Count > settings.maxPoolCount)
            result.RemoveRange(settings.maxPoolCount, result.Count - settings.maxPoolCount);

        return result;
    }

    static int Idx(int x, int y, int z, int nx, int ny) => x + y * nx + z * nx * ny;
}
