// A flying war party's escort: the members with their own wings (RoleCount.ownWings) who fly beside the
// vessel instead of taking a seat. They are spawned seated in mid-air on a ring round the hull and take off
// at once on the world sim's order (NpcFlightModule.TakeOffInAir — no launch delay, no flight distance, no
// fight check: the spawn is an order), keep station on the hull (NpcAviator.Escort), and are sent down beside
// the drop when the vessel goes down to unload (or loses them: wrecked, gone, or nobody left aboard). From
// then on they are ordinary party members on foot, and any later flight keeps the usual rules (D2).
//
// Server-only, like NpcWorldSim, which owns the group and calls in here. Nothing is saved: a war party's
// members never are, and a party folded with its escort in the air comes back with it in the air.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Agents
{
    public static class WarPartyEscorts
    {
        /// <summary>Where escort flier <paramref name="index"/> of <paramref name="count"/> is made round a hull at <paramref name="hull"/>.</summary>
        public static Vector3 SpawnPoint(Transform hull, NpcGroupTransport transport, int index, int count) =>
            hull.position + Quaternion.LookRotation(EscortSteering.YawForward(hull.forward), Vector3.up) *
            NpcGroupTransport.EscortOffset(index, count, transport.escortRadius, transport.escortHeight);

        /// <summary>
        /// Give each of <paramref name="fliers"/> (already spawned seated at its <see cref="SpawnPoint"/>) its
        /// craft and its station on <paramref name="hull"/>. One that cannot fly is taken away (GroupFlight).
        /// </summary>
        public static void TakeOff(NpcGroup group, Transform hull, NpcGroupTransport transport,
                                   IReadOnlyList<GameObject> fliers, Vector3 groundBelow)
        {
            for (int i = 0; i < fliers.Count; i++)
            {
                GameObject flier = fliers[i];
                if (!GroupFlight.TakeOffOrDrop(group, flier, hull.forward, groundBelow, out NpcAviator aviator)) continue;

                Vector3 offset = NpcGroupTransport.EscortOffset(i, fliers.Count, transport.escortRadius, transport.escortHeight);
                aviator.Escort(FlightStation.Fixed(hull, offset, flier.GetInstanceID()));
                group.Escorts.Add(flier);
            }
        }

        /// <summary>
        /// Send the escort down once its vessel goes down to unload, is wrecked, is gone, or has delivered —
        /// each beside the drop at its own spread, clear of the hull. Polled every sim tick.
        /// </summary>
        public static void Steer(NpcGroup group, NpcGroupTransport transport)
        {
            if (group.Escorts.Count == 0) return;

            GameObject vessel = group.Transport;
            VesselPilot pilot = vessel != null ? vessel.GetComponent<VesselPilot>() : null;
            if (!ShouldLand(pilot != null, pilot != null && pilot.IsWrecked, group.Delivered,
                            pilot != null ? pilot.State : VesselMissionState.Done))
                return;

            int count = group.Escorts.Count;
            for (int i = 0; i < count; i++)
            {
                GameObject flier = group.Escorts[i];
                if (flier == null || !flier.TryGetComponent(out NpcFlightModule flight) || !flight.InFlight) continue;

                Vector3 centre = pilot != null ? vessel.transform.position : flier.transform.position;
                flight.Aviator.LandAt(NpcGroupTransport.EscortLanding(centre, i, count, transport.escortLandingSpread));
            }

            group.Escorts.Clear();
        }

        /// <summary>
        /// The escort goes down when there is nothing left to escort — no vessel, a wreck, a party already off it —
        /// or when the vessel itself goes down to unload. A run that turns for home still loaded keeps its escort.
        /// </summary>
        public static bool ShouldLand(bool vesselExists, bool wrecked, bool delivered, VesselMissionState state) =>
            !vesselExists || wrecked || delivered ||
            state == VesselMissionState.Descend || state == VesselMissionState.Unload;
    }
}
