// An air patrol (NpcGroupTemplate.airPatrol): a group that lives in the sky. The airborne twin of a caravan —
// a seeded record that moves unseen, real only near a player, gone for good once shot down.
//
//   Folded: the record flies its loop at travelSpeed, waypoint to waypoint, circling each for a rolled dwell.
//           The leg is never saved on its own: the next waypoint is the one after the waypoint nearest the
//           record (NextLeg), so position, goal and dwell — already in the record — are all a reload needs.
//   Spawned: every member is made seated in mid-air at the patrol's cruiseHeight (the leader at the record,
//           the rest on its V, WingOffset) and takes off at once in its own craft (GroupFlight), cruising at
//           that height: no take-off, no launch delay. The leader cruises to the record's waypoint (NpcAviator.CruiseTo) at leaderSpeed;
//           each wingman keeps its chevron station on the leader's craft (NpcAviator.Escort). Steer runs every
//           sim tick: a leader shot down hands the lead to the next pilot and the chevron re-forms on it.
//   It never lands: neither order ever does, so the only ways down are dying (a wreck) and a fold (beyond
//   NpcWorldSim.airborneFoldRadius, since a member is always aloft).
//
// Server-only, like NpcWorldSim, which owns the group and calls in here. Nothing new is saved.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Agents
{
    public static class NpcAirPatrol
    {
        /// <summary>
        /// Wingman <paramref name="wingIndex"/>'s chevron station, in the leader's yaw frame (x right, y up, z ahead):
        /// even indices to the left, odd to the right, each pair one rank further back and higher.
        /// </summary>
        public static Vector3 WingOffset(int wingIndex, NpcGroupAirPatrol patrol)
        {
            int index = Mathf.Max(0, wingIndex);
            float side = index % 2 == 0 ? -1f : 1f;
            int rank = index / 2 + 1;
            return new Vector3(side * rank * patrol.wingLateral, rank * patrol.wingStepUp, -rank * patrol.wingBehind);
        }

        /// <summary>The waypoint after the one nearest <paramref name="position"/> (flat), wrapping round the loop.</summary>
        public static int NextLeg(IReadOnlyList<Vector3> route, Vector3 position)
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < route.Count; i++)
            {
                Vector3 offset = route[i] - position;
                offset.y = 0f;
                if (offset.sqrMagnitude >= best) continue;
                best = offset.sqrMagnitude;
                nearest = i;
            }
            return (nearest + 1) % route.Count;
        }

        /// <summary>Where a patrol member is made: the leader at <paramref name="origin"/>, a wingman on its chevron.</summary>
        public static Vector3 SpawnPoint(Vector3 origin, Vector3 heading, bool leads, int wingIndex, NpcGroupAirPatrol patrol) =>
            leads ? origin : origin + Quaternion.LookRotation(EscortSteering.YawForward(heading), Vector3.up) * WingOffset(wingIndex, patrol);

        /// <summary>The folded record: fly the loop, circling each waypoint for a rolled dwell.</summary>
        public static void TickVirtual(NpcGroup group, NpcGroupAirPatrol patrol, float travelSpeed, float delta)
        {
            if (group.DwellRemaining > 0f)
            {
                group.DwellRemaining -= delta;
                return;
            }

            if (!group.HasGoal)
            {
                SetNextWaypoint(group, patrol);
                return;
            }

            if (!group.AdvanceToward(travelSpeed, delta)) return;

            group.HasGoal = false;
            group.DwellRemaining = RollDwell(patrol);
        }

        /// <summary>
        /// Put the freshly spawned, seated members into their craft and give them their orders. Those that cannot
        /// fly are taken away (GroupFlight).
        /// </summary>
        public static void TakeOff(NpcGroup group, NpcGroupAirPatrol patrol, Vector3 groundBelow)
        {
            Vector3 heading = group.Heading;
            foreach (GameObject member in new List<GameObject>(group.Live))
                GroupFlight.TakeOffOrDrop(group, member, heading, groundBelow, out _, patrol.cruiseHeight);

            group.AirLeader = null;
            if (!group.HasGoal && group.DwellRemaining <= 0f) SetNextWaypoint(group, patrol);
            Steer(group, patrol, 0f);
        }

        /// <summary>
        /// Every sim tick while spawned: keep a living leader on the record's waypoint and the chevron on its
        /// craft; move on to the next waypoint once the leader is circling this one and the dwell is out.
        /// </summary>
        public static void Steer(NpcGroup group, NpcGroupAirPatrol patrol, float delta)
        {
            NpcAviator leader = AviatorOf(group.AirLeader);
            if (leader == null) leader = TakeLead(group, patrol);
            if (leader == null) return;

            if (group.HasGoal)
            {
                if (!leader.ReachedCruisePoint) return;
                group.HasGoal = false;
                group.DwellRemaining = RollDwell(patrol);
                return;
            }

            group.DwellRemaining -= delta;
            if (group.DwellRemaining > 0f) return;

            SetNextWaypoint(group, patrol);
            leader.CruiseTo(group.GoalPosition, patrol.leaderSpeed);
        }

        /// <summary>
        /// The first living pilot (in spawn order, so the template's leader while it lives) takes the lead and the
        /// waypoint; every other pilot is given the next chevron station on its craft.
        /// </summary>
        private static NpcAviator TakeLead(NpcGroup group, NpcGroupAirPatrol patrol)
        {
            NpcAviator leader = null;
            int wing = 0;
            foreach (GameObject member in group.Live)
            {
                NpcAviator aviator = AviatorOf(member);
                if (aviator == null) continue;

                if (leader == null)
                {
                    leader = aviator;
                    group.AirLeader = member;
                    leader.CruiseTo(group.HasGoal ? group.GoalPosition : leader.transform.position, patrol.leaderSpeed);
                    continue;
                }

                aviator.Escort(FlightStation.Fixed(leader.transform, WingOffset(wing++, patrol), member.GetInstanceID()));
            }

            if (leader == null) group.AirLeader = null;
            return leader;
        }

        /// <summary>The craft a member is flying with a living pilot, or null.</summary>
        private static NpcAviator AviatorOf(GameObject member) =>
            member != null && member.TryGetComponent(out NpcFlightModule flight) && flight.InFlight && flight.Aviator.Aloft
                ? flight.Aviator
                : null;

        private static void SetNextWaypoint(NpcGroup group, NpcGroupAirPatrol patrol)
        {
            group.GoalPosition = patrol.route[NextLeg(patrol.route, group.Position)];
            group.ArriveRadius = patrol.arriveRadius;
            group.HasGoal = true;
        }

        private static float RollDwell(NpcGroupAirPatrol patrol) =>
            Random.Range(Mathf.Max(0f, patrol.waypointDwell.x), Mathf.Max(patrol.waypointDwell.x, patrol.waypointDwell.y));
    }
}
