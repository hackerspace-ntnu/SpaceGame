// The one rule for taking away something the world left lying around: its time is up, and no player
// is close enough to watch it blink out. Shared by a vehicle its group lost (AbandonedVehicle) and by
// what a dead NPC leaves behind (Remains), so the two can never disagree about what "unseen" means.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public static class UnseenRemoval
    {
        /// <summary>Its lifetime is up and nobody is close enough to see it go.</summary>
        public static bool ShouldTakeAway(float remaining, bool playerNear) =>
            remaining <= 0f && !playerNear;

        /// <summary>
        /// <see cref="ShouldTakeAway"/> for something at <paramref name="here"/>, asking where the players
        /// are only once its time is up -- a countdown runs every frame on every body in the world, and
        /// the player positions are not needed until the last of them.
        /// </summary>
        public static bool IsDue(float remaining, Vector3 here, float unseenDistance, List<Vector3> scratch) =>
            ShouldTakeAway(remaining, remaining <= 0f && PlayerWithin(here, unseenDistance, scratch));

        /// <summary>Whether any player stands within <paramref name="distance"/> metres, measured flat.</summary>
        public static bool PlayerWithin(Vector3 here, float distance, List<Vector3> scratch)
        {
            NpcWorldSim sim = NpcWorldSim.Instance;
            if (sim == null) return false;

            sim.CollectPlayerPositions(scratch);

            foreach (Vector3 player in scratch)
            {
                Vector3 delta = player - here;
                delta.y = 0f;
                if (delta.sqrMagnitude <= distance * distance) return true;
            }

            return false;
        }
    }
}
