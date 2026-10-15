// The order a chore visits its targets in. A round takes a few consecutive targets from a fixed walking
// chain (nearest neighbour from the source), then the next round carries on where the last stopped — so a
// gardener waters every plant in turn, in an order that looks like a walk, instead of hopping at random.
// Pure; the errand runner owns the cursor.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public static class ChoreRounds
    {
        /// <summary>Indices of <paramref name="points"/> in nearest-neighbour order, starting from the one closest to <paramref name="from"/>.</summary>
        public static int[] Chain(Vector3 from, IReadOnlyList<Vector3> points)
        {
            var order = new int[points.Count];
            var left = new List<int>(points.Count);
            for (int i = 0; i < points.Count; i++) left.Add(i);

            Vector3 at = from;
            for (int n = 0; n < order.Length; n++)
            {
                int best = 0;
                for (int i = 1; i < left.Count; i++)
                    if (FlatSqr(points[left[i]] - at) < FlatSqr(points[left[best]] - at)) best = i;
                order[n] = left[best];
                at = points[left[best]];
                left.RemoveAt(best);
            }
            return order;
        }

        /// <summary>The next <paramref name="count"/> entries of <paramref name="order"/> from <paramref name="cursor"/>, wrapping; advances the cursor.</summary>
        public static List<int> Take(int[] order, ref int cursor, int count)
        {
            var round = new List<int>(count);
            for (int n = 0; n < count && order.Length > 0; n++)
            {
                round.Add(order[cursor % order.Length]);
                cursor = (cursor + 1) % order.Length;
            }
            return round;
        }

        private static float FlatSqr(Vector3 v) => v.x * v.x + v.z * v.z;
    }
}
