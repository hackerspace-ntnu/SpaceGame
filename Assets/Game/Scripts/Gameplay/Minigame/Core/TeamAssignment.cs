using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    // Splits a shuffled list of spawn positions into `teamCount` roughly-even blocks.
    // Caller is responsible for shuffling — this only divides, so results are
    // deterministic given the same input order (kept separate from randomness for
    // testability).
    public static class TeamAssignment
    {
        public static List<List<Vector3>> SplitEvenly(IReadOnlyList<Vector3> positions, int teamCount)
        {
            var teams = new List<List<Vector3>>(teamCount);
            for (int i = 0; i < teamCount; i++)
                teams.Add(new List<Vector3>());

            for (int i = 0; i < positions.Count; i++)
                teams[i % teamCount].Add(positions[i]);

            return teams;
        }
    }
}
