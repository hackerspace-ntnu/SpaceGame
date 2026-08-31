using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.World;

namespace SpaceGame.Core
{
    /// <summary>
    /// Spawns a player body for every client in the session, once the world under it is ready.
    ///
    /// This file holds the per-client spawn flow. Which save profile each client is playing — and
    /// therefore where the world is streamed for it — lives in NetworkGameManager.Profiles.cs.
    /// </summary>
    public partial class NetworkGameManager : NetworkBehaviour
    {
        public static NetworkGameManager Instance;

        [Header("World Streaming (optional)")]
        [SerializeField] private WorldStreamer worldStreamer;

        [Tooltip("Seconds to keep asking for a ground-backed spawn position after the chunks around " +
                 "the spawn point have loaded, before giving up and using the spawn point's own " +
                 "position (clamped above the terrain).")]
        [SerializeField] private float spawnResolveTimeout = 10f;

        [Tooltip("Seconds to wait for a SpawnPoint to appear before giving up. One that is merely " +
                 "late arrives with an additively loaded scene; one that is not authored at all " +
                 "never arrives, and this is what keeps that from being a silent hang.")]
        [SerializeField] private float spawnPointWaitTimeout = 15f;

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

        /// <summary>
        /// Server-side copy of <see cref="PendingSceneNameToWaitFor"/>, taken once when this object
        /// spawns. The static is a one-shot, and reading it per client meant the first client's
        /// coroutine consumed it and every later client proceeded as if no scene were pending —
        /// spawning them into persistentScene's own SpawnPoint instead of the additively loaded
        /// one. Every client in a session waits for the same scene, so it is captured once here.
        /// </summary>
        private string pendingSceneForSession;

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
            // Before anything else, on every peer: this is what decides whether the spawn flow
            // below takes the versus route at all, and it must be settled before that flow starts.
            AdoptVersusSessionFromLobby();

            // Sent by everyone, host included. The host's own answer is redundant — it reads
            // PlayerProfile.LocalId directly — but a special case here would be one more branch
            // that only the rarely-tested path exercises.
            ReportProfileServerRpc(PlayerProfile.LocalId);

            // Only in a match, and only from a peer that knows its own side. Outside versus there
            // is no team to report and the server has no roster to put it in.
            if (VersusSession.IsActive && VersusSession.LocalTeam >= 0)
                ReportVersusTeamServerRpc(VersusSession.LocalTeam);

            if (!IsServer) return;

            pendingSceneForSession = PendingSceneNameToWaitFor;
            PendingSceneNameToWaitFor = null;

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Every client ALREADY CONNECTED, not just the owner.
            //
            // OwnerClientId on a scene-placed NetworkObject is always the server, so spawning only
            // that spawned the host and nobody else. Everyone who joined through the lobby
            // connected while the menu scene was up — long before this object existed — so their
            // OnClientConnectedCallback fired with no listener attached and will never fire again.
            // They arrived in the world with no player object at all.
            //
            // Copied to a list first: OnClientConnected starts a coroutine, and a disconnect
            // landing mid-loop would otherwise mutate the collection being iterated.
            // handledClients keeps this idempotent against the callback subscribed above.
            foreach (ulong clientId in new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds))
                OnClientConnected(clientId);
        }

        public override void OnNetworkDespawn()
        {
            // Before the guard below, which returns early during a shutdown that has already torn
            // the singleton down — the one exit where a roster would otherwise be left standing.
            // Its lifetime is this networked session, and a match that inherited the last one's
            // team allocations would start its sides uneven.
            VersusTeamRoster.Clear();

            if (NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!handledClients.Add(clientId)) return;
            StartCoroutine(SpawnWhenReady(clientId));
        }

        /// <summary>
        /// Forgets a client so a later connection reusing its id is spawned rather than skipped.
        ///
        /// <para>
        /// <see cref="handledClients"/> exists to stop the host's spawn flow running twice in one
        /// connection, and left un-pruned it also stopped it running at all in the NEXT one: Netcode
        /// hands out the lowest free client id, so a peer that drops and reconnects routinely comes
        /// back as the same number. <c>Add</c> returned false, <c>SpawnWhenReady</c> never started,
        /// and the player was connected, streaming the world, with no body — which also means no
        /// profile claim, so nothing of theirs was ever saved either.
        /// </para>
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            handledClients.Remove(clientId);
            profileByClient.Remove(clientId);

            // Frees their place on a versus team, so the sides stay even for whoever joins next.
            // Harmless outside a match, where the roster is empty anyway.
            VersusTeamRoster.Release(clientId);

            // Frees their seat if they dropped mid-descent, so the hull is not left
            // carrying a body that no longer exists. Harmless once the arrival is over.
            if (ArrivalDirector.Instance != null) ArrivalDirector.Instance.ReleaseClient(clientId);
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

            // Read, never consumed: this coroutine runs once per client and they all wait for the
            // same scene. Nulling a shared one-shot here made every client after the first skip
            // the wait entirely.
            string pendingScene = pendingSceneForSession;

            if (!string.IsNullOrEmpty(pendingScene))
                yield return WaitForPendingScene(pendingScene);

            // A versus match starts everybody inside their own team's ship rather than on a
            // SpawnPoint, so it takes a different route to a position entirely. Checked after the
            // pending scene has loaded, because that load is what brings the spawner into being.
            if (VersusSession.IsActive && VersusShipSpawner.Instance != null)
            {
                yield return SpawnIntoTeamShip(clientId);
                yield break;
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

                // Bounded, and loudly. "Not yet" and "never" look identical from here: a point that
                // is merely late arrives with an additively loaded scene, while one that was never
                // authored never comes — and polling for it forever is indistinguishable from a
                // hang. Nothing below this line runs, so there is no player, no chunk preload and
                // no arrival, and the loading screen it all feeds deliberately never times out
                // either. The result is a world that streams and simulates behind a Loading caption
                // with an empty console. That is what this wait cost once already.
                float spawnPointDeadline = Time.time + spawnPointWaitTimeout;

                while (!SpawnManager.Instance.SpawnPointsAvailable())
                {
                    if (Time.time >= spawnPointDeadline)
                    {
                        Debug.LogError($"[NGM] No SpawnPoint appeared in {spawnPointWaitTimeout}s, so " +
                                       $"client {clientId} has nowhere to be put and the world has no " +
                                       "anchor to stream around. Is one authored in the scene?");
                        yield break;
                    }

                    yield return new WaitForSeconds(1f);
                }

                // Two different questions, deliberately asked in this order.
                //
                // First, WHICH CHUNKS to load. That only needs the spawn point's authored position,
                // and it must not need anything more: no ground has loaded yet, so nothing here can
                // be validated against terrain, and a call that insisted on a validated position
                // would wait forever for a world that this very preload is responsible for loading.
                // The result is checked, not discarded. On failure `anchor` is Vector3.zero, and
                // proceeding with it streams the world around the grid's corner and spawns the
                // player there — which is how a save came back recording a player at (0,0,0)
                // instead of where they were standing.
                if (!SpawnManager.Instance.TryGetSpawnAnchor(out Vector3 anchor))
                {
                    Debug.LogError("[NGM] No spawn anchor available — refusing to stream and spawn " +
                                   "around the world origin. Is a SpawnPoint present in the scene?");
                    yield break;
                }

                // A remote client's profile arrives on its own channel and may not have landed yet.
                // Waiting for it is what makes the branch below work for anyone but the host; the
                // wait is normally already satisfied, since the client has had persistentScene since
                // before the chunk preload above could finish.
                yield return WaitForProfile(clientId);

                // A loaded save overrides the spawn point — and it has to do so HERE, before the
                // preload, not later at the spawn call. The preload decides which chunks exist; a
                // player restored to the far side of the map after the world was prepared around
                // the spawn point would drop through ground that was never loaded.
                bool restoringPlayer = TryGetSavedSpawn(clientId, out Vector3 savedPosition, out Quaternion savedRotation);
                if (restoringPlayer) anchor = savedPosition;

                yield return WaitForWorldReady(new[] { anchor });

                if (restoringPlayer)
                {
                    Debug.Log($"[NGM] Restoring client {clientId} to its saved position {savedPosition}.");
                    SpawnManager.Instance.SpawnPlayerForClient(clientId, savedPosition, savedRotation);
                    yield break;
                }

                // Only now, with the terrain in, WHERE TO STAND. This is the position the player is
                // actually spawned at, resolved once and carried through to the spawn call.
                // Resolving twice is what put players underground: SpawnPoint scatters inside a
                // radius, so a second call returns a different position from the one the world was
                // prepared around.
                Vector3 spawnPos = anchor;
                float deadline = Time.time + spawnResolveTimeout;

                while (!SpawnManager.Instance.TryGetSpawnPoint(out spawnPos))
                {
                    if (Time.time >= deadline)
                    {
                        // Chunks are loaded and the spawn point still cannot find ground. Spawning
                        // at the anchor is not safe by itself, but SpawnPlayerForClient clamps it
                        // above the terrain, and stranding the client unspawned is worse.
                        Debug.LogError($"[NGM] No valid spawn position after {spawnResolveTimeout}s — " +
                                       "falling back to the spawn point's own position.");
                        spawnPos = anchor;
                        break;
                    }

                    yield return null;
                }

                // A brand new world starts everybody strapped into a ship on its way down, so it
                // takes a different route to a body entirely. Checked here, at the very end, and
                // deliberately AFTER the restoringPlayer branch above has already returned: a
                // loaded save has had its arrival, and replaying the crash on somebody standing in
                // the wreck is the one outcome worth engineering against.
                //
                // spawnPos is handed on rather than re-resolved, for the reason written out above:
                // a second resolve returns a different point from the one the terrain was streamed
                // around.
                if (ArrivalDirector.Instance != null && ArrivalDirector.Instance.IsPending)
                {
                    yield return ArrivalDirector.Instance.SpawnIntoArrival(clientId, spawnPos);
                    yield break;
                }

                SpawnManager.Instance.SpawnPlayerForClient(clientId, spawnPos);
                yield break;
            }

            SpawnManager.Instance.SpawnPlayerForClient(clientId);
        }

        /// <summary>
        /// Waits for an additively loaded scene the launcher named, then makes it active.
        ///
        /// Waits for Netcode's own OnLoadEventCompleted rather than polling the raw
        /// UnityEngine scene state. NetworkSceneManager's internal "scene event active"
        /// flag — which gates every subsequent NetworkManager.SceneManager.LoadScene call,
        /// including the chunk loads WorldStreamer issues afterwards — is only cleared as part
        /// of Netcode's own completion handling, which runs strictly after the scene object
        /// reports isLoaded (it also does post-load spawn/placed-object bookkeeping first).
        /// Proceeding as soon as the raw scene is loaded races that internal flag and makes
        /// every chunk load fail immediately with SceneEventInProgress.
        ///
        /// Subscribe-then-check-if-already-loaded (rather than the reverse) to close the
        /// window where the scene's own LoadEventCompleted fires between the check and the
        /// subscription — a single-frame race that's easy to hit since Unity scene loads
        /// typically complete within a frame or two of being requested.
        /// </summary>
        private static IEnumerator WaitForPendingScene(string pendingScene)
        {
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

            var scene = SceneManager.GetSceneByName(pendingScene);
            SceneManager.SetActiveScene(scene);
        }

        private IEnumerator WaitForWorldReady(IEnumerable<Vector3> positions)
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
