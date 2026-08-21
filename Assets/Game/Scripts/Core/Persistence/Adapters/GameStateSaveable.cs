using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists session-wide state that belongs to no object in particular — the game timer, and
    /// whether the run is still going.
    ///
    /// This one is registered with <see cref="SaveManager"/> as a global saver rather than hanging
    /// off a <see cref="SaveableEntity"/>: it describes the session, so it must be written even
    /// when no chunk is loaded and read before any world exists.
    ///
    /// <para>
    /// The state enum used to be deliberately left out, on the grounds that a run is always
    /// resumed as a run. It is not: <c>GameManager.WinGame</c> sets <c>Won</c>, and a world saved
    /// after the ship left came back <c>Playing</c> — with the timer counting again, because
    /// <c>Update</c> only advances it while playing. Restoring the enum is also what makes
    /// <c>ShipSaveable</c> safe to keep dumb: the ship restores its scrap count without re-deciding
    /// the win, because the win is recorded here.
    /// </para>
    /// <para>
    /// Restoring it needs no migration. A save written before this field existed simply has no
    /// <c>state</c> entry, and <c>ToObject</c> leaves the struct's default — <c>Playing</c> — which
    /// is exactly what those saves meant.
    /// </para>
    /// </summary>
    public class GameStateSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "gameState";

        public string SaveKey => Key;

        public struct State
        {
            public float gameTimer;

            /// <summary>
            /// <c>GameManager.GameState</c>. Stored as the enum, which Newtonsoft writes as its
            /// integer value — a shape that survives a new member being added to the end.
            /// </summary>
            public GameManager.GameState state;
        }

        public object CaptureState()
        {
            GameManager manager = GameManager.Instance;

            return manager == null
                ? null
                : new State { gameTimer = manager.GameTimer, state = manager.CurrentState };
        }

        public void RestoreState(JObject state)
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);

            manager.RestoreTimer(restored.gameTimer);
            manager.RestoreState(restored.state);
        }

        private void OnEnable() => SaveManager.RegisterGlobalSaver(this);

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
}
