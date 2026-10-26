using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// A copy of the ground under one scatter chunk on one terrain: the heights and one paint
    /// channel's weights for just that area, plus a margin. It is captured on the main thread and
    /// never changes, so a worker thread can scatter from it while the terrain is repainted or
    /// unloaded. Copying only the chunk's area keeps a 4097² terrain from costing a 64 MB copy.
    ///
    /// The paint is read the way the terrain draws it — blended between cell centres — and the
    /// read position is warped by noise up to <c>edgeWarp</c> cells, so a painted area ends in
    /// an organic outline. A big terrain's paint cells are many metres wide (8 km at 512² is
    /// 15.6 m), and read as-is they grow grass and rocks in squares.
    /// </summary>
    public sealed class PaintedGroundPatch
    {
        /// <summary>
        /// Noise cycles per paint cell for the edge warp: the outline wanders within a cell or
        /// two rather than wobbling at the grid's own rhythm. GrassGroundTint warps the ground
        /// colour's outline at the same rate, so the colour ends where the grass does.
        /// </summary>
        public const float WarpFrequency = 0.55f;
        private const float WarpDetailFrequency = 1.9f;
        private const float WarpDetailShare = 0.35f;

        private readonly Vector3 origin;
        private readonly Vector3 size;
        private readonly int heightLast;
        private readonly int heightBaseX;
        private readonly int heightBaseZ;
        private readonly float[,] heights;
        private readonly int paintResolution;
        private readonly int paintBaseX;
        private readonly int paintBaseZ;
        private readonly float[,] weights;
        private readonly float edgeWarp;

        /// <param name="paintIndex">The channel's index on the terrain (<see cref="GroundPaint.IndexOn"/>); -1 grows everywhere.</param>
        /// <param name="edgeWarp">How far, in paint cells, the paint edge wanders.</param>
        public PaintedGroundPatch(Terrain terrain, GroundPaint paint, int paintIndex, Rect area, float edgeWarp)
        {
            TerrainData data = terrain.terrainData;
            origin = terrain.transform.position;
            size = data.size;
            this.edgeWarp = edgeWarp;

            // Heightmap samples sit ON the grid lines: sample i is at u = i / (resolution - 1).
            int heightResolution = data.heightmapResolution;
            heightLast = heightResolution - 1;
            RectInt heightTexels = TexelsUnder(area, heightLast, 0f, heightResolution, 1);
            heightBaseX = heightTexels.x;
            heightBaseZ = heightTexels.y;
            heights = data.GetHeights(heightTexels.x, heightTexels.y, heightTexels.width, heightTexels.height);

            if (paintIndex < 0)
                return;
            // Paint cells are CELLS: cell i's centre is at u = (i + 0.5) / resolution.
            paintResolution = paint.Resolution(data);
            int margin = 1 + Mathf.CeilToInt(edgeWarp);
            RectInt cells = TexelsUnder(area, paintResolution, -0.5f, paintResolution, margin);
            paintBaseX = cells.x;
            paintBaseZ = cells.y;
            weights = paint.Read(data, paintIndex, cells);
        }

        /// <summary>False when the point is off the terrain. Safe on any thread.</summary>
        /// <param name="normal">The ground's unit normal; its y is the cosine of the slope.</param>
        public bool TrySample(float worldX, float worldZ, out float height, out Vector3 normal, out float paintWeight)
        {
            float u = (worldX - origin.x) / size.x;
            float v = (worldZ - origin.z) / size.z;
            height = paintWeight = 0f;
            normal = Vector3.up;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return false;

            float gridX = u * heightLast;
            float gridZ = v * heightLast;
            height = origin.y + HeightAt(gridX, gridZ);
            float slopeX = (HeightAt(gridX + 1f, gridZ) - HeightAt(gridX - 1f, gridZ)) / (2f * size.x / heightLast);
            float slopeZ = (HeightAt(gridX, gridZ + 1f) - HeightAt(gridX, gridZ - 1f)) / (2f * size.z / heightLast);
            normal = new Vector3(-slopeX, 1f, -slopeZ).normalized;
            paintWeight = weights == null ? 1f : WeightAt(u, v);
            return true;
        }

        private float HeightAt(float gridX, float gridZ) =>
            Bilinear(heights, gridX - heightBaseX, gridZ - heightBaseZ) * size.y;

        /// <summary>The paint, blended between cell centres, read at a noise-warped position.</summary>
        private float WeightAt(float u, float v)
        {
            float cellX = u * paintResolution;
            float cellZ = v * paintResolution;
            float warpX = WarpNoise(cellX, cellZ, 17.3f) * edgeWarp;
            float warpZ = WarpNoise(cellX, cellZ, 41.9f) * edgeWarp;
            return Bilinear(weights, cellX - 0.5f + warpX - paintBaseX, cellZ - 0.5f + warpZ - paintBaseZ);
        }

        /// <summary>-1..1 noise in paint-cell space, two octaves so the outline is not a smooth wave.</summary>
        private static float WarpNoise(float cellX, float cellZ, float offset)
        {
            float broad = Mathf.PerlinNoise(cellX * WarpFrequency + offset, cellZ * WarpFrequency + offset);
            float detail = Mathf.PerlinNoise(cellX * WarpDetailFrequency + offset * 2f, cellZ * WarpDetailFrequency + offset);
            return (Mathf.Lerp(broad, detail, WarpDetailShare) - 0.5f) * 2f;
        }

        /// <summary>Bilinear read of <paramref name="grid"/> [z, x] at local grid coordinates, clamped to its edges.</summary>
        private static float Bilinear(float[,] grid, float x, float z)
        {
            int lastX = grid.GetLength(1) - 1;
            int lastZ = grid.GetLength(0) - 1;
            x = Mathf.Clamp(x, 0f, lastX);
            z = Mathf.Clamp(z, 0f, lastZ);
            int x0 = Mathf.Min((int)x, Mathf.Max(0, lastX - 1));
            int z0 = Mathf.Min((int)z, Mathf.Max(0, lastZ - 1));
            int x1 = Mathf.Min(x0 + 1, lastX);
            int z1 = Mathf.Min(z0 + 1, lastZ);
            float fx = x - x0;
            float fz = z - z0;
            float bottom = Mathf.Lerp(grid[z0, x0], grid[z0, x1], fx);
            float top = Mathf.Lerp(grid[z1, x0], grid[z1, x1], fx);
            return Mathf.Lerp(bottom, top, fz);
        }

        /// <summary>
        /// The grid samples covering <paramref name="area"/>, where sample i sits at grid
        /// coordinate u * <paramref name="scale"/> + <paramref name="offset"/>, plus a margin.
        /// </summary>
        private RectInt TexelsUnder(Rect area, float scale, float offset, int resolution, int margin)
        {
            int last = resolution - 1;
            int xMin = Mathf.Clamp(Mathf.FloorToInt((area.xMin - origin.x) / size.x * scale + offset) - margin, 0, last);
            int zMin = Mathf.Clamp(Mathf.FloorToInt((area.yMin - origin.z) / size.z * scale + offset) - margin, 0, last);
            int xMax = Mathf.Clamp(Mathf.CeilToInt((area.xMax - origin.x) / size.x * scale + offset) + margin, 0, last);
            int zMax = Mathf.Clamp(Mathf.CeilToInt((area.yMax - origin.z) / size.z * scale + offset) + margin, 0, last);
            return new RectInt(xMin, zMin, xMax - xMin + 1, zMax - zMin + 1);
        }
    }
}
