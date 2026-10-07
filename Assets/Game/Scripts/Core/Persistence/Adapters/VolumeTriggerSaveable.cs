using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what a trigger volume currently refuses, and to whom.
    ///
    /// <para>
    /// The state this keeps is a REFUSAL, which is why losing it is worse than it sounds. A
    /// transition volume — a cave mouth, a doorway into an interior — arms a per-player cooldown
    /// when the player comes back out through it, so stepping back into the exterior does not
    /// immediately send them in again. Loading a save puts the player back exactly where they stood,
    /// which for somebody who has just walked out of a cave is INSIDE that volume, and a re-armed
    /// volume fires on the first <c>OnTriggerStay</c> — a load that walks you straight back into the
    /// cave you just left, and one that plays a one-time cutscene at you a second time.
    /// </para>
    /// <para>
    /// The map it reads is static and shared by every volume, which is deliberate: a volume streams
    /// out while the player is inside the interior it leads to, and a cooldown that lived on the
    /// component would be destroyed by the very journey it exists to measure. This saver takes only
    /// the rows belonging to its own volume, keyed by the identity the record uses.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(VolumeTrigger))]
    public class VolumeTriggerSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "volume";       // written into save files — NEVER rename

        private VolumeTrigger volume;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private VolumeTrigger Volume =>
            volume != null ? volume : volume = GetComponent<VolumeTrigger>();

        public string SaveKey => Key;

        public struct State
        {
            public List<VolumeTrigger.ReentryRecord> reentry;
        }

        public object CaptureState()
        {
            if (Volume == null) return null;

            List<VolumeTrigger.ReentryRecord> records = Volume.CaptureReentry();

            // Nothing pending and nothing refused is the state every volume in the world is in most
            // of the time, and it is the prefab's own — so it stores nothing.
            return records == null || records.Count == 0 ? null : new State { reentry = records };
        }

        public void RestoreState(JObject state)
        {
            if (Volume == null) return;

            // No record means this volume owed nobody anything, which has to be applied: the map is
            // static and shared, so leaving it alone would keep whatever a previous load put there.
            if (state == null) { Volume.RestoreReentry(null); return; }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Volume.RestoreReentry(restored.reentry);
        }
    }
}
