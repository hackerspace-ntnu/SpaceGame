using System;
using System.Collections.Generic;

namespace SpaceGame.Gameplay
{
    // Picks the largest set of spawn points that can all actually reach each other.
    //
    // The arena's baked NavMesh is not one connected surface — steep procedural
    // terrain breaks it into islands, and spawn points scattered across the arena
    // land on several of them. Entities spawned on a minority island can path a few
    // metres and then stop forever: they never find anyone, and a last-standing match
    // never ends. Restricting the match to one connected component costs a few spawn
    // points and makes every entity reachable by every other.
    //
    // The reachability test is injected so this stays free of NavMesh (and testable);
    // MatchManager passes a NavMesh.CalculatePath probe. The relation is assumed
    // symmetric, which NavMesh reachability is, so components are built with a plain
    // union-find over all pairs rather than a representative check that could split a
    // component whose chosen representative happens to fail.
    public static class SpawnReachability
    {
        public static List<int> LargestConnectedGroup(int count, Func<int, int, bool> canReach)
        {
            var result = new List<int>();
            if (count <= 0) return result;
            if (canReach == null || count == 1)
            {
                for (int i = 0; i < count; i++) result.Add(i);
                return result;
            }

            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (Find(parent, i) == Find(parent, j)) continue;
                    if (canReach(i, j))
                        parent[Find(parent, i)] = Find(parent, j);
                }
            }

            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < count; i++)
            {
                int root = Find(parent, i);
                if (!groups.TryGetValue(root, out List<int> members))
                {
                    members = new List<int>();
                    groups[root] = members;
                }
                members.Add(i);
            }

            foreach (var group in groups.Values)
            {
                // Ties broken by lowest first index so the choice is deterministic for
                // a given input order rather than dependent on dictionary iteration.
                if (group.Count > result.Count || (group.Count == result.Count && result.Count > 0 && group[0] < result[0]))
                    result = group;
            }

            return result;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }
    }
}
