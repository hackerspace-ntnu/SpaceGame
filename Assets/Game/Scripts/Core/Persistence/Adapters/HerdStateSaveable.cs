using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists every herd's migration — which phase it is in and where it is heading — as a handful
    /// of records keyed by herd id.
    ///
    /// <b>Global, because a herd is not an object.</b> The phase and destination live in a static
    /// table on <see cref="HerdModule"/> keyed by a string authored into prefabs. There is no
    /// GameObject that owns them, and there may be no member loaded at the moment a save is written
    /// — the herd's chunk can be streamed out while the herd is still, as far as the world is
    /// concerned, on its way somewhere. So it registers with <c>SaveManager</c> like the day/night
    /// cycle does, rather than hanging off a <c>SaveableEntity</c>.
    ///
    /// <b>It also plugs a leak.</b> Because that table is process-wide and keyed by a string, "cattle"
    /// in the world you just left and "cattle" in the world you are about to load are the same key.
    /// Restoring replaces the table wholesale rather than merging, so a herd the record does not
    /// mention comes back Idle instead of setting off toward a destination in a world that no longer
    /// exists. <see cref="OnEnable"/> clears it too, for the load that has no record at all.
    ///
    /// <b>Placement.</b> One instance, in the persistent scene, beside the other global savers.
    /// </summary>
    public class HerdStateSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "herds";     // written into save files — NEVER rename

        public string SaveKey => Key;

        public struct State
        {
            public HerdModule.HerdRecord[] herds;
        }

        public object CaptureState()
        {
            HerdModule.HerdRecord[] records = HerdModule.CaptureSharedState();

            // Every herd idle is the ordinary case, and dropping the key beats writing an empty
            // array into every save file.
            return records == null || records.Length == 0 ? null : new State { herds = records };
        }

        public void RestoreState(JObject state)
        {
            if (state == null)
            {
                // No record means no herd was mid-move. Said explicitly rather than left alone,
                // because "left alone" is what carries the previous world's destinations in.
                HerdModule.RestoreSharedState(null);
                return;
            }

            // Through the shared serializer: the records carry Vector3s, and reading one without the
            // registered converters recurses through its own properties into a stack overflow.
            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            HerdModule.RestoreSharedState(restored.herds);
        }

        private void OnEnable()
        {
            // Before registering, so a session that starts without ever restoring a herd record
            // still starts from a clean table. Membership is untouched — members register
            // themselves and the ones already loaded must stay registered.
            HerdModule.ClearSharedState();

            SaveManager.RegisterGlobalSaver(this);
        }

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
}
