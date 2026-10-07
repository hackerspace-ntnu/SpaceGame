using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the spaceship's state machine — idle, in flight, or crashed — and the speed it hit
    /// the ground at.
    ///
    /// <para>
    /// The bug this closes is not a missing field, it is a reset. <c>SpaceshipManager.Initialize</c>
    /// runs from <c>OnEnable</c> and ends in "start in idle state", unconditionally, so a ship that
    /// had crashed came back on the pad with its boosters off and its wreck forgotten — and there
    /// was no saver to disagree, because there was nothing to disagree with. The manager now carries
    /// a restored latch that makes a restore outrank that default exactly once.
    /// </para>
    /// <para>
    /// Only the state and the crash velocity are stored. The booster flag and the booster lights are
    /// what each state's <c>Enter</c> SETS, so they come back for free by re-entering the state —
    /// and storing them would introduce the one thing this could get wrong, a ship in flight with
    /// its engines dark.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(SpaceshipManager))]
    public class SpaceshipSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "spaceship";       // written into save files — NEVER rename

        private SpaceshipManager ship;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private SpaceshipManager Ship =>
            ship != null ? ship : ship = GetComponent<SpaceshipManager>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary><c>SpaceshipManager.StateKind</c>, written as its integer value.</summary>
            public SpaceshipManager.StateKind state;

            public Vector3 crashVelocity;
        }

        public object CaptureState()
        {
            if (Ship == null) return null;

            // Idle with nothing to remember is the prefab's own state, so it stores nothing.
            if (Ship.CurrentKind == SpaceshipManager.StateKind.Idle) return null;

            return new State { state = Ship.CurrentKind, crashVelocity = Ship.CrashVelocity };
        }

        public void RestoreState(JObject state)
        {
            if (Ship == null) return;

            if (state == null)
            {
                Ship.RestoreState(SpaceshipManager.StateKind.Idle, Vector3.zero);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Ship.RestoreState(restored.state, restored.crashVelocity);
        }
    }
}
