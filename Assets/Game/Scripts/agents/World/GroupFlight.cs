// Putting a group member that was spawned seated in mid-air into its own craft — shared by a flying war
// party's escort (WarPartyEscorts) and an air patrol (NpcAirPatrol). A member that cannot fly is taken
// away, loudly, rather than left hanging in the sky with its NavMeshAgent off.
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Agents
{
    public static class GroupFlight
    {
        /// <summary>
        /// <paramref name="flier"/>'s craft, made around it where it hangs, flying, boarded and with no order yet;
        /// false (and the flier removed from <paramref name="group"/> and the world) when it cannot fly.
        /// <paramref name="cruise"/> is the group's cruise height, metres; null flies the flier's own.
        /// </summary>
        public static bool TakeOffOrDrop(NpcGroup group, GameObject flier, Vector3 heading, Vector3 groundBelow,
                                         out NpcAviator aviator, float? cruise = null)
        {
            aviator = null;
            if (flier.TryGetComponent(out NpcFlightModule flight) && flight.TakeOffInAir(heading, groundBelow, out aviator, cruise))
                return true;

            Debug.LogError($"[GroupFlight] '{group.Id}': '{flier.name}' was spawned in the air but could not take off " +
                           "(no wing pack worn, or no craft prefab on its NpcFlightModule); it is taken away.", flier);
            group.Live.Remove(flier);
            if (group.Fighters.Remove(flier)) group.FightersSpawned--;
            NpcSpawn.Remove(flier);
            return false;
        }
    }
}
