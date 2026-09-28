// Sculpts the ground under and around a settlement: pins the terrain flat under each building
// (with a small padded margin), blends smoothly back to the natural surface, and fills everything
// else with gentle seeded rises/dips so a settlement doesn't read as a flat disc stamped onto the
// world. Edit-time only, like every other generator in ProceduralGeneration — heights are baked
// into the TerrainData asset and committed, not recomputed at runtime.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementTerrainSculptor
    {
        public struct BuildingFootprint
        {
            public Vector3 worldPos;
            public float radius;
            public float baseY;
        }

        /// <summary>Snapshot of one terrain's affected height patch, taken before shaping, so a later Clear/regenerate can restore it exactly.</summary>
        [Serializable]
        public struct TerrainPatchBackup
        {
            public Terrain terrain;
            public int x, y, width, height;
            public float[] heights;
        }

        /// <summary>
        /// Shapes every loaded terrain tile overlapping the settlement. Returns a backup per
        /// affected tile; the caller (a <see cref="Settlement"/>) is responsible for storing it and
        /// calling <see cref="Restore"/> before the next Generate/Clear.
        /// </summary>
        public static List<TerrainPatchBackup> Shape(
            Vector3 center, float radius, float blendDistance, float flattenPadding,
            float noiseAmplitude, float noiseScale, int seed,
            List<BuildingFootprint> buildings)
        {
            var backups = new List<TerrainPatchBackup>();

            float maxFootprint = 0f;
            foreach (var b in buildings) if (b.radius > maxFootprint) maxFootprint = b.radius;
            float outerRadius = radius + blendDistance + maxFootprint;

            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                if (!TryGetHeightmapRect(terrain, center, outerRadius, out RectInt rect)) continue;

                TerrainData data = terrain.terrainData;
                float[,] heights = data.GetHeights(rect.x, rect.y, rect.width, rect.height);

                backups.Add(new TerrainPatchBackup
                {
                    terrain = terrain, x = rect.x, y = rect.y, width = rect.width, height = rect.height,
                    heights = Flatten(heights),
                });

                ShapeHeights(terrain, rect, heights, center, radius, blendDistance, flattenPadding, noiseAmplitude, noiseScale, seed, buildings);
                data.SetHeights(rect.x, rect.y, heights);
            }

            return backups;
        }

        /// <summary>Restores every backed-up patch, undoing a previous <see cref="Shape"/> call exactly.</summary>
        public static void Restore(List<TerrainPatchBackup> backups)
        {
            if (backups == null) return;

            foreach (var backup in backups)
            {
                if (backup.terrain == null || backup.terrain.terrainData == null || backup.heights == null) continue;
                backup.terrain.terrainData.SetHeights(backup.x, backup.y, Unflatten(backup.heights, backup.width, backup.height));
            }
        }

        private static void ShapeHeights(
            Terrain terrain, RectInt rect, float[,] heights, Vector3 center,
            float radius, float blendDistance, float flattenPadding,
            float noiseAmplitude, float noiseScale, int seed, List<BuildingFootprint> buildings)
        {
            TerrainData data = terrain.terrainData;
            Vector3 terrainPos = terrain.transform.position;
            Vector3 size = data.size;
            int res = data.heightmapResolution;
            Vector2 centerXZ = new Vector2(center.x, center.z);

            for (int row = 0; row < rect.height; row++)
            {
                for (int col = 0; col < rect.width; col++)
                {
                    float worldX = terrainPos.x + ((rect.x + col) / (float)(res - 1)) * size.x;
                    float worldZ = terrainPos.z + ((rect.y + row) / (float)(res - 1)) * size.z;
                    Vector2 pointXZ = new Vector2(worldX, worldZ);

                    float distToCenter = Vector2.Distance(pointXZ, centerXZ);
                    if (distToCenter > radius + blendDistance) continue;

                    float originalWorldY = terrainPos.y + heights[row, col] * size.y;

                    float nearestSignedDist = float.PositiveInfinity;
                    float targetY = originalWorldY;
                    foreach (var b in buildings)
                    {
                        float d = Vector2.Distance(pointXZ, new Vector2(b.worldPos.x, b.worldPos.z)) - (b.radius + flattenPadding);
                        if (d < nearestSignedDist) { nearestSignedDist = d; targetY = b.baseY; }
                    }

                    float flattenWeight = buildings.Count > 0
                        ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nearestSignedDist / blendDistance))
                        : 0f;

                    // Fades the ambient noise out toward the settlement's outer edge so it blends
                    // into the pristine terrain instead of stopping in a visible ring.
                    float edgeFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distToCenter - radius) / blendDistance));
                    float ambient = TerrainNoiseHelper.Fbm(new Vector3(worldX * noiseScale, 0f, worldZ * noiseScale), 1f, seed, 3)
                                     * noiseAmplitude * edgeFade;

                    float finalWorldY = Mathf.Lerp(originalWorldY + ambient, targetY, flattenWeight);
                    heights[row, col] = Mathf.Clamp01((finalWorldY - terrainPos.y) / size.y);
                }
            }
        }

        private static bool TryGetHeightmapRect(Terrain terrain, Vector3 center, float outerRadius, out RectInt rect)
        {
            Vector3 terrainPos = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            int res = terrain.terrainData.heightmapResolution;

            float minX = center.x - outerRadius, maxX = center.x + outerRadius;
            float minZ = center.z - outerRadius, maxZ = center.z + outerRadius;

            if (maxX < terrainPos.x || minX > terrainPos.x + size.x || maxZ < terrainPos.z || minZ > terrainPos.z + size.z)
            {
                rect = default;
                return false;
            }

            int xStart = Mathf.Clamp(Mathf.FloorToInt((minX - terrainPos.x) / size.x * (res - 1)) - 1, 0, res - 1);
            int xEnd   = Mathf.Clamp(Mathf.CeilToInt((maxX - terrainPos.x) / size.x * (res - 1)) + 1, 0, res - 1);
            int zStart = Mathf.Clamp(Mathf.FloorToInt((minZ - terrainPos.z) / size.z * (res - 1)) - 1, 0, res - 1);
            int zEnd   = Mathf.Clamp(Mathf.CeilToInt((maxZ - terrainPos.z) / size.z * (res - 1)) + 1, 0, res - 1);

            int width = xEnd - xStart + 1;
            int height = zEnd - zStart + 1;
            if (width <= 0 || height <= 0) { rect = default; return false; }

            rect = new RectInt(xStart, zStart, width, height);
            return true;
        }

        private static float[] Flatten(float[,] grid)
        {
            int h = grid.GetLength(0), w = grid.GetLength(1);
            var flat = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    flat[y * w + x] = grid[y, x];
            return flat;
        }

        private static float[,] Unflatten(float[] flat, int width, int height)
        {
            var grid = new float[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    grid[y, x] = flat[y * width + x];
            return grid;
        }
    }
}
