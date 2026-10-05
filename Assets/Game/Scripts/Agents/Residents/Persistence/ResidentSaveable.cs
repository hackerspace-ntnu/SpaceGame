using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Agents.Residents
{
    /// <summary>
    /// Persists what a settlement resident remembers about players: who it has met, how well it
    /// knows them, how it favors them and the deeds it knows of.
    ///
    /// <b>Only the memory.</b> A resident's day is a pure function of the settlement seed and the
    /// day counter, so plans, activity, overrides and being indoors are re-derived on load rather
    /// than saved — a stored plan could only ever disagree with the one the planner builds. What
    /// the planner cannot rebuild is history, and that is all this writes.
    ///
    /// Keyed by player PROFILE inside the state (see <see cref="ResidentMemory"/>), so a grudge
    /// outlives the player's NetworkObject and the session it was earned in. Runs where
    /// <c>SaveManager</c> does — the server, or offline — which is also the only place memory is
    /// written.
    /// </summary>
    [RequireComponent(typeof(Resident))]
    public class ResidentSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "resident";     // written into save files — NEVER rename

        private Resident resident;

        private Resident Resident => resident != null ? resident : resident = GetComponent<Resident>();

        public string SaveKey => Key;

        public object CaptureState()
        {
            ResidentMemory memory = Resident != null ? Resident.Memory : null;
            if (memory == null) return null;

            // A resident that has never met anyone is at its default, and the key is dropped rather
            // than storing three empty lists on every villager in the world.
            ResidentMemory.MemoryState state = memory.Capture();
            bool blank = state.acquaintances.Count == 0 && state.favor.Count == 0 && state.deeds.Count == 0;
            return blank ? null : state;
        }

        public void RestoreState(JObject state)
        {
            ResidentMemory memory = Resident != null ? Resident.Memory : null;
            if (memory == null) return;

            memory.Restore(state?.ToObject<ResidentMemory.MemoryState>(SaveSerializer.Serializer));
        }
    }
}
