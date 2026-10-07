// Who a house takes in: of the residents free to visit, the people who live there first, then their friends and kin,
// then anyone. Pure, so the order is testable without a scene.
using System;
using System.Collections.Generic;

namespace SpaceGame.Agents.Residents
{
    public readonly struct GuestCandidate
    {
        public GuestCandidate(bool livesHere, bool kin)
        {
            LivesHere = livesHere;
            Kin = kin;
        }

        /// <summary>The resident's bed is in this house.</summary>
        public bool LivesHere { get; }

        /// <summary>Family or a friend of somebody whose bed is in this house.</summary>
        public bool Kin { get; }
    }

    public static class GuestPick
    {
        /// <summary>What belonging is worth to a house, in the units of the <c>jitter</c> a draw adds: a household beats a friend, a friend beats a stranger.</summary>
        public const float HouseholdWeight = 2f, KinWeight = 1f;

        /// <summary>The index of the candidate to invite, or -1 when there are none. <paramref name="rng"/> breaks ties and lets a stranger sometimes win.</summary>
        public static int Choose(IReadOnlyList<GuestCandidate> candidates, Random rng)
        {
            int best = -1;
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                double score = (candidates[i].LivesHere ? HouseholdWeight : 0f) + (candidates[i].Kin ? KinWeight : 0f) + rng.NextDouble() * KinWeight;
                if (score <= bestScore) continue;

                best = i;
                bestScore = score;
            }
            return best;
        }
    }
}
