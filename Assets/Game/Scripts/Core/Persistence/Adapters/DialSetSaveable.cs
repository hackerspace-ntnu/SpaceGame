using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay.Puzzles;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps a dial puzzle — the weathervane ring, the sun shrine's mirrors — where the players left it.
    ///
    /// <para>
    /// Both halves matter. A solved ring that re-scrambles on load shuts the gust behind whoever
    /// saved on the plateau; an unsolved puzzle that re-rolls hands the players a different one from
    /// the one they were halfway through. Anything derived from the arrangement (the gust, the beam)
    /// is not saved — restoring the arrangement restores it.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(DialSet))]
    public class DialSetSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "dials";   // written into save files — NEVER rename

        private DialSet set;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake.
        private DialSet Set => set != null ? set : set = GetComponent<DialSet>();

        public string SaveKey => Key;

        public struct State
        {
            public int arrangement;
        }

        public object CaptureState()
        {
            if (Set == null || !Set.IsKnown) return null;
            return new State { arrangement = Set.Packed };
        }

        public void RestoreState(JObject state)
        {
            // Nothing saved means nothing known when the world was written: leave the fresh roll
            // this session made rather than inventing an arrangement.
            if (Set == null || state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Set.Restore(restored.arrangement);
        }
    }
}
