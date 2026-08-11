using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.Core
{
    public class NetworkGameManager : NetworkBehaviour
    {
        public static NetworkGameManager Instance;

        [Header("World Streaming (optional)")]
        [SerializeField] private WorldStreamer worldStreamer;

        /// <summary>
        /// Set by a launcher (e.g. MainMenuUI.StartMinigame) that additively loads a second scene
        /// with its own SpawnPoint on top of persistentScene right after starting the host. Without
        /// this, the auto-spawn coroutine below sees persistentScene's own SpawnPoint immediately
        /// and spawns the player there before the second scene has finished loading.
        /// </summary>
        public static string PendingSceneNameToWaitFor;

        /// <summary>
        /// Clients already handed to SpawnWhenReady. OnNetworkSpawn calls OnClientConnected directly
        /// for the host's own OwnerClientId AND subscribes to OnClientConnectedCallback, which also
        /// fires for the host locally — without this guard the host's spawn flow runs twice, and the
        /// second run silently loses one-shot state like PendingSceneNameToWaitFor (already consumed
        /// by the first run), spawning the player at the wrong location.
        /// </summary>
        private readonly HashSet<ulong> handledClients = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[NGM DEBUG] OnNetworkSpawn called on instance {GetInstanceID()}, IsServer={IsServer}, PendingSceneNameToWaitFor='{PendingSceneNameToWaitFor}'");
            if (!IsServer) return;

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            OnClientConnected(OwnerClientId);
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[NGM DEBUG] OnClientConnected({clientId}) called, already handled={handledClients.Contains(clientId)}");
            if (!handledClients.Add(clientId)) return;
            StartCoroutine(SpawnWhenReady(clientId));
        }

        private IEnumerator SpawnWhenReady(ulong clientId)
        {
            // OnClientConnected (and this coroutine's start) can run synchronously inside Netcode's
            // own scene-load completion handling (NetworkSceneManager.OnSceneLoaded -> OnNetworkSpawn).
            // At that point NetworkSceneManager hasn't finished unwinding its current scene event yet,
            // so calling NetworkManager.Singleton.SceneManager.LoadScene (e.g. via WorldStreamer below)
            // from here would be re-entrant and fail with SceneEventInProgress. Yielding once first
            // guarantees everything below runs on a later frame, after Netcode's own callback stack
            // has fully unwound.
            yield return null;

            string pendingScene = PendingSceneNameToWaitFor;
            PendingSceneNameToWaitFor = null;
            Debug.Log($"[NGM DEBUG] SpawnWhenReady({clientId}) started, pendingScene='{pendingScene}'");

            if (!string.IsNullOrEmpty(pendingScene))
            {
                Debug.Log($"[NGM DEBUG] Waiting for scene '{pendingScene}'...");

                // Wait for Netcode's own OnLoadEventCompleted rather than polling the raw
                // UnityEngine scene state. NetworkSceneManager's internal "scene event active"
                // flag — which gates every subsequent NetworkManager.SceneManager.LoadScene call,
                // including the chunk loads WorldStreamer issues below — is only cleared as part
                // of Netcode's own completion handling, which runs strictly after the scene object
                // reports isLoaded (it also does post-load spawn/placed-object bookkeeping first).
                // Proceeding as soon as the raw scene is loaded races that internal flag and makes
                // every chunk load fail immediately with SceneEventInProgress.
                //
                // Subscribe-then-check-if-already-loaded (rather than the reverse) to close the
                // window where the scene's own LoadEventCompleted fires between the check and the
                // subscription — a single-frame race that's easy to hit since Unity scene loads
                // typically complete within a frame or two of being requested.
                bool sceneEventCompleted = false;

                void OnLoaded(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
                {
                    if (sceneName != pendingScene) return;
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoaded;
                    sceneEventCompleted = true;
                }

                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoaded;

                var alreadyLoadedScene = SceneManager.GetSceneByName(pendingScene);
                if (alreadyLoadedScene.IsValid() && alreadyLoadedScene.isLoaded)
                {
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoaded;
                    sceneEventCompleted = true;
                }

                while (!sceneEventCompleted)
                {
                    yield return null;
                }

                Debug.Log($"[NGM DEBUG] Scene '{pendingScene}' ready, setting active.");
                var scene = SceneManager.GetSceneByName(pendingScene);
                SceneManager.SetActiveScene(scene);
            }
            else
            {
                Debug.Log("[NGM DEBUG] No pending scene to wait for, proceeding immediately.");
            }

            if (worldStreamer)
            {
                // NetworkBehaviour.OnNetworkSpawn order across objects in the same scene load is not
                // guaranteed by Netcode, so WorldStreamer.OnNetworkSpawn (which sets IsReady) can run
                // after this coroutine starts. Without this wait, PreloadChunksAroundPositions below
                // can be called before WorldStreamer is ready and bail out immediately with an error,
                // silently skipping the initial chunk preload.
                while (!worldStreamer.IsReady)
                {
                    yield return null;
                }

                while (!SpawnManager.Instance.SpawnPointsAvailable())
                {
                    yield return new WaitForSeconds(1f);
                }
                var pos = SpawnManager.Instance.GetSpawnPoint();
                yield return WaitForWorldReady(new[] { pos });
            }

            SpawnManager.Instance.SpawnPlayerForClient(clientId);
        }
    
        IEnumerator WaitForWorldReady(IEnumerable<Vector3> positions)
        {
            bool done = false;

            worldStreamer.PreloadChunksAroundPositions(positions, () =>
            {
                done = true;
            });

            while (!done)
                yield return null;
        }
    }
}
