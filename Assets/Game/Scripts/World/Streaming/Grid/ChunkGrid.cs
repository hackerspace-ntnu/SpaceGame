// The chunk grid as pure geometry: no scenes, no components, no Unity lifecycle.
//
// This is the part of streaming that decides WHICH chunks a tracker needs, split out from
// WorldStreamer because that decision is what strands a fast vehicle in an unloaded world and
// it was previously inlined in Update() where nothing could test it.
//
// The rule that matters lives in TryGetStreamingCoord. Streaming used to key off strict
// containment in the grid rectangle — outside it, a tracker required no chunks at all. That
// conflates two different things:
//
//   * a player who sailed off the edge of the world, who is still very much in the world and
//     must keep the ground they came from loaded (and must be able to sail back onto it), and
//   * a player teleported somewhere that is deliberately not the world at all — the minigame
//     arena sits ~16.5 km east of the grid — who must not hold a world chunk open.
//
// Distance from the grid's edge separates them. Strict containment cannot: it puts a metre
// past the beach in the same bucket as another map.
using UnityEngine;

namespace SpaceGame.World.Streaming
{
    public readonly struct ChunkGrid
    {
        public readonly Vector3 Origin;
        public readonly Vector2 ChunkSize;
        public readonly Vector2Int Dimensions;

        public ChunkGrid(Vector3 origin, Vector2 chunkSize, Vector2Int dimensions)
        {
            Origin = origin;
            ChunkSize = chunkSize;
            Dimensions = dimensions;
        }

        public float Width => ChunkSize.x * Dimensions.x;
        public float Depth => ChunkSize.y * Dimensions.y;

        public bool IsUsable => ChunkSize.x > 0f && ChunkSize.y > 0f
                                && Dimensions.x > 0 && Dimensions.y > 0;

        /// <summary>True when the position is strictly inside the grid rectangle.</summary>
        public bool Contains(Vector3 worldPos)
        {
            float relX = worldPos.x - Origin.x;
            float relZ = worldPos.z - Origin.z;

            return relX >= 0f && relX < Width
                && relZ >= 0f && relZ < Depth;
        }

        /// <summary>
        /// The grid coordinate containing the position, clamped to the nearest edge chunk for
        /// anything outside. Callers that need to tell "outside the world" from "inside it"
        /// must ask <see cref="Contains"/> or <see cref="TryGetStreamingCoord"/> first.
        /// </summary>
        public Vector2Int ToCoord(Vector3 worldPos)
        {
            if (!IsUsable) return Vector2Int.zero;

            float relX = worldPos.x - Origin.x;
            float relZ = worldPos.z - Origin.z;

            int cx = Mathf.FloorToInt(relX / ChunkSize.x);
            int cy = Mathf.FloorToInt(relZ / ChunkSize.y);

            return new Vector2Int(Mathf.Clamp(cx, 0, Dimensions.x - 1),
                                  Mathf.Clamp(cy, 0, Dimensions.y - 1));
        }

        /// <summary>
        /// Every chunk a view <paramref name="size"/> metres across and centred on
        /// <paramref name="worldPos"/> touches. <paramref name="min"/> is inclusive,
        /// <paramref name="max"/> exclusive, and neither is clamped to the grid — a view near the
        /// edge of the world overhangs it, and the caller decides what that means.
        ///
        /// This is not <see cref="ToCoord"/> ± a radius. That window is symmetric about the
        /// CHUNK, so it reaches up to half a chunk further on one side of the position than the
        /// other and swaps which side as the position crosses a boundary — invisible when the
        /// window is used to decide what to LOAD, and plainly visible when it decides what to
        /// DRAW: it hangs the map hologram's terrain off the centre it is supposed to be
        /// centred on.
        /// </summary>
        public void WindowAround(Vector3 worldPos, Vector2 size, out Vector2Int min, out Vector2Int max)
        {
            if (!IsUsable)
            {
                min = Vector2Int.zero;
                max = Vector2Int.zero;
                return;
            }

            float relX = worldPos.x - Origin.x;
            float relZ = worldPos.z - Origin.z;
            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;

            min = new Vector2Int(Mathf.FloorToInt((relX - halfX) / ChunkSize.x),
                                 Mathf.FloorToInt((relZ - halfZ) / ChunkSize.y));
            max = new Vector2Int(Mathf.CeilToInt((relX + halfX) / ChunkSize.x),
                                 Mathf.CeilToInt((relZ + halfZ) / ChunkSize.y));
        }

        public bool IsValidCoord(Vector2Int coord)
        {
            return coord.x >= 0 && coord.x < Dimensions.x
                && coord.y >= 0 && coord.y < Dimensions.y;
        }

        public Vector3 ChunkToWorldPosition(Vector2Int coord)
        {
            return new Vector3(Origin.x + coord.x * ChunkSize.x,
                               Origin.y,
                               Origin.z + coord.y * ChunkSize.y);
        }

        /// <summary>
        /// Shortest horizontal distance from the position to the grid rectangle. Zero inside it.
        /// </summary>
        public float DistanceOutside(Vector3 worldPos)
        {
            float dx = Mathf.Max(Origin.x - worldPos.x, worldPos.x - (Origin.x + Width), 0f);
            float dz = Mathf.Max(Origin.z - worldPos.z, worldPos.z - (Origin.z + Depth), 0f);

            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// The chunk this position should stream around, or false when it is far enough outside
        /// the grid to count as another place entirely.
        ///
        /// <paramref name="offWorldDistance"/> is the whole judgement call: everything within it
        /// keeps the world loaded around the nearest edge chunk, everything past it streams
        /// nothing. It has to sit above how far anything can plausibly travel off the edge of the
        /// map and below the distance to any deliberately off-grid scene.
        /// </summary>
        public bool TryGetStreamingCoord(Vector3 worldPos, float offWorldDistance, out Vector2Int coord)
        {
            coord = ToCoord(worldPos);
            if (!IsUsable) return false;

            return DistanceOutside(worldPos) <= Mathf.Max(0f, offWorldDistance);
        }

        /// <summary>
        /// Where a tracker moving at <paramref name="velocity"/> will be in
        /// <paramref name="seconds"/>, so the loader can start on the ground it is about to
        /// arrive on rather than the ground already under it.
        ///
        /// Anything whose predicted step exceeds a whole chunk is not travelling, it has jumped —
        /// a teleport (respawn, interior exit, arena hand-off) reads as an enormous single-tick
        /// velocity. Predicting from it would queue a load along a heading nobody is actually
        /// moving on, so it predicts nothing and the tracker's own chunk carries it. Vertical
        /// speed is ignored too: falling is not travel across the grid.
        /// </summary>
        public Vector3 PredictAhead(Vector3 worldPos, Vector3 velocity, float seconds)
        {
            Vector3 step = new Vector3(velocity.x, 0f, velocity.z) * Mathf.Max(0f, seconds);

            float maxStep = Mathf.Min(ChunkSize.x, ChunkSize.y);
            if (maxStep > 0f && step.sqrMagnitude > maxStep * maxStep)
                return worldPos;

            return worldPos + step;
        }
    }
}
