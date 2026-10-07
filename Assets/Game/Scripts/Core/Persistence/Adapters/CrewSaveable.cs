using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists that a vehicle's NPC seats were stood down (<see cref="ICrewedSeats"/>): a war party's
    /// monowheel a player drove off with comes back from a load empty and theirs, instead of crewed
    /// by fresh NPCs its prefab spawns on start.
    ///
    /// <b>Restored after the crew may already exist.</b> A load network-spawns the record before
    /// restoring it, and a double's gunners are seated in <c>OnNetworkSpawn</c>; the saddle's rider
    /// waits for <c>Start</c>. So a restore takes away whoever was seated and stops the rest coming —
    /// <c>StandDown(restoring: true)</c> — rather than trying to be earlier than the spawn.
    ///
    /// Holds no state of its own: the seats know whether they were stood down.
    /// </summary>
    public class CrewSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "crew";     // written into save files — NEVER rename

        public string SaveKey => Key;

        public struct State
        {
            public bool stoodDown;
        }

        public object CaptureState()
        {
            // A crewed vehicle is at its prefab's default; only a stood-down one has anything to say.
            foreach (ICrewedSeats seats in GetComponents<ICrewedSeats>())
                if (seats.IsStoodDown) return new State { stoodDown = true };

            return null;
        }

        public void RestoreState(JObject state)
        {
            if (state == null || !state.ToObject<State>(SaveSerializer.Serializer).stoodDown) return;

            foreach (ICrewedSeats seats in GetComponents<ICrewedSeats>())
                seats.StandDown(restoring: true);
        }
    }
}
