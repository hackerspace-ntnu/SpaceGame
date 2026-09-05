using UnityEngine;
using System;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using SpaceGame.Core;

namespace SpaceGame.Gameplay
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public enum GameState { Playing, Paused, Won }
        public GameState CurrentState { get; private set; } = GameState.Playing;

        public float GameTimer { get; private set; } = 0f;

        public event Action<GameState> OnStateChanged;
    
        [SerializeField] private SceneReference onWinScene;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            if (CurrentState == GameState.Playing)
                GameTimer += Time.deltaTime;
        }

        /// <summary>Re-seeds the timer from a save so a loaded session continues counting where it stopped.</summary>
        public void RestoreTimer(float seconds)
        {
            GameTimer = Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Sets the state and announces it, but never acts on it: <see cref="WinGame"/> is what
        /// LOADS the win scene, and a restore that went through it would send a player who saved
        /// after winning straight back out of the world they just loaded.
        /// </summary>
        public void RestoreState(GameState state) => SetState(state);

        public void SetState(GameState state)
        {
            CurrentState = state;
            OnStateChanged?.Invoke(state);
        }

        /// <summary>
        /// Ends the run for everyone in the session.
        ///
        /// <para>
        /// The scene load used to go through UnityEngine's SceneManager, which moves ONLY the
        /// machine that called it: the host left for the win scene and everyone else stayed behind
        /// in a world whose server had gone. Every other transition in the project routes through
        /// Netcode for exactly this reason — see SaveHotkeys' quickload, whose comment says the same
        /// thing about the same mistake.
        /// </para>
        ///
        /// <para>
        /// And winning is not this machine's to declare, which is why the guard below is here for
        /// whatever calls this next. NOTHING CALLS IT TODAY: the only caller was the scrap-fed
        /// <c>Ship</c>, removed with the rest of the scrap system, so the run currently has no way
        /// to end in a win. Whatever replaces it must route the decision to the server the way that
        /// deposit did — a client that reached this method would load the win scene on its own and
        /// abandon a session that is still running.
        /// </para>
        ///
        /// <para>
        /// Un-networked counts as the authority. Singleplayer runs as a host, so the only way to be
        /// here with no NetworkManager listening is a scene opened straight from the editor, and
        /// refusing there would make the win condition impossible to try out.
        /// </para>
        ///
        /// <para>
        /// Written out rather than using <c>Network.Simulates</c>, which is the project's usual
        /// guard and would be wrong here. Simulates answers "true" for anything without a
        /// NetworkObject over it, on the reasoning that such a thing has no remote truth to defer
        /// to — and this manager is a plain MonoBehaviour, so every client would read as its own
        /// authority and load the win scene alone. What is being guarded is not an entity's
        /// simulation; it is a decision about the whole session.
        /// </para>
        ///
        /// <para>
        /// <see cref="OnStateChanged"/> therefore only fires on the authority. Nothing subscribes to
        /// it today, and the thing it would have announced — the run being over — reaches every
        /// client as the scene load itself.
        /// </para>
        /// </summary>
        public void WinGame()
        {
            if (Network.IsNetworked && !Network.Server)
            {
                Debug.LogWarning("[GameManager] WinGame ignored on a client. The server decides the " +
                                 "win and moves everyone, including whoever asked.");
                return;
            }

            if (onWinScene == null || string.IsNullOrEmpty(onWinScene.SceneName))
            {
                Debug.LogError("[GameManager] Nothing is assigned to onWinScene, so winning has " +
                               "nowhere to go. The run stays open.", this);
                return;
            }

            SetState(GameState.Won);

            // Through Netcode's scene manager when hosted, so clients follow. The plain load below
            // is the un-networked editor case and nothing else.
            if (Network.Server)
                NetworkManager.Singleton.SceneManager.LoadScene(onWinScene.SceneName, LoadSceneMode.Single);
            else
                SceneManager.LoadScene(onWinScene.SceneName, LoadSceneMode.Single);
        }
    }
}
