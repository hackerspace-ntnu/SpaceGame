using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// One detected liquid pool. Produced by <see cref="LiquidPoolFinder"/>, consumed by
    /// <see cref="LiquidPoolSpawner"/> (to build a water-plane mesh) and by
    /// <see cref="DecorationScatterer"/> (for "vegetation hugs the waterline" rules).
    ///
    /// Geometry: the pool surface is a flat plane at <see cref="WaterlineY"/>, bounded laterally by
    /// <see cref="HorizontalBounds"/> (XZ Bounds, with Y zero). The full 3D water volume sits between
    /// <see cref="WaterlineY"/> and <see cref="FloorY"/> — useful for trigger volumes.
    /// </summary>
    [System.Serializable]
    public struct LiquidPool
    {
        public int Id;
        public float WaterlineY;
        public float FloorY;
        public Bounds HorizontalBounds; // y == 0
        public int VoxelCount;          // raw flood-fill cell count (proportional to volume)
        public float VolumeM3;

        /// <summary>
        /// Cheapest 3D distance estimate from <paramref name="worldPos"/> to the pool surface.
        /// Uses XZ-distance to the bounding rectangle plus vertical clearance to the waterline.
        /// Good enough for vegetation proximity boosts — not exact for arbitrarily shaped pools.
        /// </summary>
        public float DistanceTo(Vector3 worldPos)
        {
            Vector2 p = new Vector2(worldPos.x, worldPos.z);
            Vector2 mn = new Vector2(HorizontalBounds.min.x, HorizontalBounds.min.z);
            Vector2 mx = new Vector2(HorizontalBounds.max.x, HorizontalBounds.max.z);
            Vector2 closest = new Vector2(
                Mathf.Clamp(p.x, mn.x, mx.x),
                Mathf.Clamp(p.y, mn.y, mx.y));
            float horiz = (p - closest).magnitude;
            float vert = Mathf.Abs(worldPos.y - WaterlineY);
            return Mathf.Sqrt(horiz * horiz + vert * vert);
        }
    }
}
