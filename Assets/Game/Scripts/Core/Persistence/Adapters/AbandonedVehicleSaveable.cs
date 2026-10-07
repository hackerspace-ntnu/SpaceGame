using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists how long a vehicle its group lost (<see cref="AbandonedVehicle"/>) has left before it is
    /// taken away, so a save/quit/load -- or its chunk unloading -- pauses the countdown instead of
    /// resetting it or losing the vehicle.
    ///
    /// Only an abandoned vehicle has anything to say. One still serving its group is never in the world
    /// store (its group's record respawns it), and one a player claimed, or that never had a group,
    /// loads with no membership and keeps itself.
    /// </summary>
    public class AbandonedVehicleSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "abandoned";     // written into save files — NEVER rename

        public string SaveKey => Key;

        public struct State
        {
            public float remaining;
        }

        public object CaptureState()
        {
            AbandonedVehicle vehicle = GetComponent<AbandonedVehicle>();
            if (vehicle == null || vehicle.Current != AbandonedVehicle.Stage.Abandoned) return null;

            return new State { remaining = vehicle.Remaining };
        }

        public void RestoreState(JObject state)
        {
            if (state == null || !TryGetComponent(out AbandonedVehicle vehicle)) return;

            vehicle.Abandon(state.ToObject<State>(SaveSerializer.Serializer).remaining);
        }
    }
}
