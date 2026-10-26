using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>Sizing shared by the jittered-grid scatters (grass tufts, rocks).</summary>
    public static class ScatterGrid
    {
        /// <summary>
        /// The most instances a chunk of <paramref name="cells"/> grid cells can hold when each
        /// cell is kept independently with <paramref name="keepChance"/>, short of a six-sigma fluke.
        /// </summary>
        public static int CapacityFor(int cells, float keepChance)
        {
            float expected = cells * keepChance;
            return Mathf.Min(cells, Mathf.CeilToInt(expected + 6f * Mathf.Sqrt(expected) + 16f));
        }
    }
}
