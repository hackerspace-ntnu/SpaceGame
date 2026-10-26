using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// A small generator seeded from one grid cell of a scatter, so every cell draws the same
    /// numbers whichever other cells a build keeps or skips (xorshift32 over a murmur-mixed seed).
    /// That is what makes every machine and every reload scatter the same grass and rocks, and a
    /// thinned grass chunk an exact subset of a full one.
    /// </summary>
    public struct ScatterCellRandom
    {
        private uint state;

        public ScatterCellRandom(Vector2Int chunk, int i, int j, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)chunk.x * 0x85EBCA77u;
                h ^= (uint)chunk.y * 0xC2B2AE3Du;
                h ^= (uint)i * 0x27D4EB2Fu;
                h ^= (uint)j * 0x165667B1u;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                state = h == 0 ? 1u : h;
            }
        }

        /// <summary>0 (inclusive) to 1 (exclusive).</summary>
        public float Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state >> 8) * (1f / 16777216f);
        }
    }
}
