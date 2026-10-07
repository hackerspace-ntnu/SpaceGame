// How a road is cut into tiles: the stretches of a street left free between its nodes, stairs and
// ends, and how many tiles fill each one. A run gets a whole number of tiles, stretched evenly along
// the street to fill it exactly -- or none, when that would stretch them visibly. Pure rules;
// SettlementStreetPaver places the tiles.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementRoadTiles
    {
        /// <summary>
        /// Tiles of <paramref name="tileLength"/> that fill a run <paramref name="length"/> long, and how much
        /// each is stretched along the street to do it. False when the stretch would be more than
        /// <paramref name="fitRange"/> either way.
        /// </summary>
        public static bool FitRun(float length, float tileLength, float fitRange, out int count, out float scale)
        {
            count = Mathf.Max(1, Mathf.RoundToInt(length / tileLength));
            scale = length / (count * tileLength);
            return Mathf.Abs(scale - 1f) <= fitRange;
        }

        /// <summary>
        /// The stretches of [0, <paramref name="length"/>] outside every blocked interval (x = from, y = to),
        /// in order. Blocked intervals may overlap and reach past either end.
        /// </summary>
        public static List<Vector2> FreeRuns(float length, List<Vector2> blocked)
        {
            blocked.Sort((a, b) => a.x.CompareTo(b.x));
            var free = new List<Vector2>();
            float cursor = 0f;
            foreach (var interval in blocked)
            {
                if (interval.x > cursor) free.Add(new Vector2(cursor, Mathf.Min(interval.x, length)));
                cursor = Mathf.Max(cursor, interval.y);
                if (cursor >= length) break;
            }
            if (cursor < length) free.Add(new Vector2(cursor, length));
            free.RemoveAll(run => run.y - run.x <= 0f);
            return free;
        }
    }
}
