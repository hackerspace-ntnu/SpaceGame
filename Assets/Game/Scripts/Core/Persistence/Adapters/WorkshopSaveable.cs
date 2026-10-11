using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a workshop: how far every room has turned.
    ///
    /// <para>
    /// A room has no stops, so where its one entrance points is nowhere but in its angle: a workshop that came back
    /// with its rooms as modelled would have shut whoever saved inside a tunnel behind a wall. The hoist is not
    /// saved: it is wherever the clock says.
    /// </para>
    /// <para>
    /// Server-authoritative on the way back in: each <c>WorkshopWheel</c> lands its room and answers whoever asks,
    /// so clients follow by message rather than by each running a second copy of this saver.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Workshop))]
    public class WorkshopSaveable : MonoBehaviour, ISaveable
    {
        /// <summary>Written into save files. NEVER rename.</summary>
        public const string Key = "workshop";

        private Workshop workshop;

        // Lazy, not cached in Awake: EditMode tests never run Awake.
        private Workshop Workshop => workshop != null ? workshop : workshop = GetComponent<Workshop>();

        public string SaveKey => Key;

        /// <summary>
        /// A save older than a field reads it as empty, which is the modelled pose; fields it holds that are gone
        /// (the cranes' stops, from when they were worked by hand) are ignored.
        /// </summary>
        public struct State
        {
            /// <summary>Degrees each room has turned, by its wheel's id. A room not named stands as modelled.</summary>
            public Dictionary<string, float> rooms;
        }

        public object CaptureState()
        {
            if (Workshop == null) return null;

            var rooms = new Dictionary<string, float>();
            foreach (WorkshopWheel wheel in GetComponentsInChildren<WorkshopWheel>(true))
                if (!Mathf.Approximately(wheel.Angle, 0f)) rooms[wheel.Id] = wheel.Angle;

            return rooms.Count == 0 ? null : new State { rooms = rooms };   // the authored start: nothing to say
        }

        public void RestoreState(JObject state)
        {
            if (Workshop == null) return;

            State restored = state != null ? state.ToObject<State>(SaveSerializer.Serializer) : default;
            foreach (WorkshopWheel wheel in GetComponentsInChildren<WorkshopWheel>(true))
                wheel.Restore(restored.rooms != null && restored.rooms.TryGetValue(wheel.Id, out float degrees) ? degrees : 0f);
        }
    }
}
