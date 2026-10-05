// Where each planned member of a group stands in its column: the leader at the origin, every other
// member that spawns in the next follower slot, with the slot's own fixed jitter (FormationMath.SlotPosition,
// seeded by the slot, drift at time 0). One rule for the live spawn (NpcWorldSim.Spawn) and the distant
// city's silhouette (DistantGroupSilhouette), so the silhouette hands over to the live city in the same
// places. The column is rigid: turning the heading turns every place about the origin. Pure: tested
// without a scene.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>One planned member's place: its index in the plan, whether it leads, and where it stands.</summary>
    public readonly struct ColumnPlace
    {
        public readonly int PlanIndex;
        public readonly bool Leads;
        public readonly Vector3 Position;

        public ColumnPlace(int planIndex, bool leads, Vector3 position)
        {
            PlanIndex = planIndex;
            Leads = leads;
            Position = position;
        }
    }

    public static class GroupColumnLayout
    {
        /// <summary>Seeds each follower slot's fixed jitter and drift phase (FormationMath.SlotPosition's memberSeed).</summary>
        private const int SlotSeedStride = 7919;

        /// <summary>
        /// A place for every planned member with a prefab, in plan order: the first that leads at
        /// <paramref name="origin"/>, the rest in follower slots behind it along <paramref name="heading"/>.
        /// Crew take a slot too (NpcWorldSim seats them on a carrier instead), so every later member keeps
        /// the slot ColumnDeal dealt it (NpcGroupComposition.FollowerSlots counts the same way).
        /// </summary>
        public static List<ColumnPlace> Places(IReadOnlyList<PlannedMember> plan, Vector3 origin, Vector3 heading, in FormationShape shape)
        {
            var places = new List<ColumnPlace>(plan.Count);
            int follower = 0;
            bool leaderTaken = false;
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Prefab == null) continue;

                bool leads = plan[i].Leads && !leaderTaken;
                leaderTaken |= leads;
                Vector3 at = leads
                    ? origin
                    : FormationMath.SlotPosition(follower, origin, heading, shape, follower * SlotSeedStride, 0f);
                if (!leads) follower++;
                places.Add(new ColumnPlace(i, leads, at));
            }
            return places;
        }
    }
}
