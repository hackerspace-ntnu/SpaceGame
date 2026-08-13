// "How high is the terrain surface here?" — answered without a raycast, in any scene.
//
// Every consumer of this asks it for the same reason: to find out whether something has ended up
// underneath the world. That makes a raycast the wrong instrument. A downward ray takes the first
// collider it meets, so a player standing inside a building is told the roof is the ground and a
// player standing on a vehicle is told the vehicle is; both readings are useless for deciding
// whether the terrain is above them. Terrain.SampleHeight asks the heightmap directly and cannot
// be shadowed by anything.
//
// The two sources exist because the world scene and every other scene answer differently. In the
// streamed world only the loaded chunk knows its terrain, and asking a chunk that has not loaded
// must fail rather than guess. In MinigameArena, CaveTest and the personal test scenes there is no
// streamer at all, but there may still be a plain Terrain in the scene worth measuring against.
//
// Failing is a real answer, not an error. No terrain under a position means there is no surface to
// be beneath — which is exactly the situation inside an interior scene, and is what lets the guard
// switch itself off there without knowing interiors exist.
using UnityEngine;

namespace SpaceGame.World.Safety
{
    public static class TerrainProbe
    {
        private static WorldStreamer s_streamer;

        /// <summary>
        /// Terrain surface height at <paramref name="worldPos"/>'s X/Z, or false when no terrain
        /// covers that position.
        /// </summary>
        public static bool TryGetTerrainHeight(Vector3 worldPos, out float terrainY)
        {
            var streamer = ResolveStreamer();
            if (streamer != null && streamer.TryGetTerrainHeight(worldPos, out terrainY))
                return true;

            return TryGetActiveTerrainHeight(worldPos, out terrainY);
        }

        /// <summary>
        /// Cached because this runs on a timer for every guarded body; re-resolved whenever the
        /// cache is stale, since WorldStreamer is destroyed and recreated across scene reloads and
        /// a destroyed reference compares equal to null.
        /// </summary>
        private static WorldStreamer ResolveStreamer()
        {
            if (s_streamer == null)
                s_streamer = Object.FindFirstObjectByType<WorldStreamer>();

            return s_streamer;
        }

        /// <summary>
        /// Any plain Terrain in the loaded scenes whose footprint covers the position. Bounds are
        /// checked explicitly because SampleHeight clamps out-of-range coordinates to the edge of
        /// the heightmap instead of reporting that it has nothing — so without this, standing well
        /// off the side of a terrain would be answered with the height of its border.
        /// </summary>
        private static bool TryGetActiveTerrainHeight(Vector3 worldPos, out float terrainY)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;

                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;

                if (worldPos.x < origin.x || worldPos.x > origin.x + size.x) continue;
                if (worldPos.z < origin.z || worldPos.z > origin.z + size.z) continue;

                terrainY = terrain.SampleHeight(worldPos) + origin.y;
                return true;
            }

            terrainY = 0f;
            return false;
        }
    }
}
