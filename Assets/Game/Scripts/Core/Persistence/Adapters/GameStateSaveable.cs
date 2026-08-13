using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists session-wide state that belongs to no object in particular — currently the game
    /// timer.
    ///
    /// This one is registered with <see cref="SaveManager"/> as a global saver rather than hanging
    /// off a <see cref="SaveableEntity"/>: it describes the session, so it must be written even
    /// when no chunk is loaded and read before any world exists.
    /// </summary>
    public class GameStateSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "gameState";

        public string SaveKey => Key;

        public struct State
        {
            public float gameTimer;
        }

        public object CaptureState()
        {
            GameManager manager = GameManager.Instance;
            return manager == null ? null : new State { gameTimer = manager.GameTimer };
        }

        public void RestoreState(JObject state)
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || state == null) return;

            if (state["gameTimer"] is { Type: JTokenType.Float or JTokenType.Integer } timer)
                manager.RestoreTimer(timer.Value<float>());
        }

        private void OnEnable() => SaveManager.RegisterGlobalSaver(this);

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
}
