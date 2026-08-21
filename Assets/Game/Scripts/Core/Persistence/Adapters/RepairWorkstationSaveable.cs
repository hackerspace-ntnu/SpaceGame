using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists how far a repair has got.
    ///
    /// <para>
    /// This is the only quest progress in the project stored as a count: five pieces of scrap, fed
    /// in one at a time, each one a trip out into the desert. Losing it is not a cosmetic reset —
    /// four of those trips are simply undone, and because <c>onRepaired</c> fires when the count
    /// crosses the line, a machine repaired before the save could fire its completion event a
    /// second time in the loaded session.
    /// </para>
    /// <para>
    /// Server-authoritative on the way back in. <see cref="RepairWorkstation.RestoreProgress"/>
    /// writes through the workstation's <c>NetworkVariable</c>, so clients get the restored gauge
    /// by replication rather than by a second copy of this saver running on each of them — which
    /// is right, because clients do not load worlds.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RepairWorkstation))]
    public class RepairWorkstationSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "repair";       // written into save files — NEVER rename

        private RepairWorkstation station;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private RepairWorkstation Station =>
            station != null ? station : station = GetComponent<RepairWorkstation>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>Pieces of the required item accepted so far.</summary>
            public int progress;
        }

        public object CaptureState()
        {
            if (Station == null) return null;

            // An untouched machine is at zero, which is what the prefab already says.
            return Station.CurrentAmount > 0 ? new State { progress = Station.CurrentAmount } : (object)null;
        }

        public void RestoreState(JObject state)
        {
            if (Station == null) return;

            if (state == null) { Station.RestoreProgress(0); return; }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Station.RestoreProgress(restored.progress);
        }
    }
}
