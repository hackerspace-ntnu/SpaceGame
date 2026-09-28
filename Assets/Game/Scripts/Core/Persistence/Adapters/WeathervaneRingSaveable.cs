using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay.Puzzles;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps a weathervane ring where the players left it.
    ///
    /// <para>
    /// Both halves matter. A solved ring that re-scrambles on load shuts the gust behind whoever
    /// saved on the plateau; an unsolved one that re-rolls hands the players a different puzzle
    /// from the one they were halfway through. The gust itself is not saved — it is derived from
    /// the arrangement, so restoring the arrangement opens it.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(WeathervaneRing))]
    public class WeathervaneRingSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "weathervanes";   // written into save files — NEVER rename

        private WeathervaneRing ring;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake.
        private WeathervaneRing Ring => ring != null ? ring : ring = GetComponent<WeathervaneRing>();

        public string SaveKey => Key;

        public struct State
        {
            public int positions;
        }

        public object CaptureState()
        {
            if (Ring == null || !Ring.IsKnown) return null;
            return new State { positions = Ring.Positions.Packed };
        }

        public void RestoreState(JObject state)
        {
            // Nothing saved means nothing known when the world was written: leave the fresh
            // scramble this session rolled rather than inventing an arrangement.
            if (Ring == null || state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Ring.RestorePositions(restored.positions);
        }
    }
}
