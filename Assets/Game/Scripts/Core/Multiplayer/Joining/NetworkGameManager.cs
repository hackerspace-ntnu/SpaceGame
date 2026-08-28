using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.Core
{
    public class NetworkGameManager : NetworkBehaviour
    {
        public static NetworkGameManager Instance;

        [Header("World Streaming (optional)")]
        [SerializeField] private WorldStreamer worldStreamer;

        [Tooltip("Seconds to keep asking for a ground-backed spawn position after the chunks around " +
                 "the spawn point have loaded, before giving up and using the spawn point's own " +
                 "position (clamped above the terrain).")]
        [SerializeField] private float spawnResolveTimeout = 10f;

        [Tooltip("How long the server waits for a joining client to report which save profile it is " +
                 "playing, before streaming the world around the spawn point instead of around that " +
                 "client's saved position. Only costs this long for a client whose report is lost; " +
                 "the report is normally already in by the time the world is ready to preload.")]
        [SerializeField] private float profileReportTimeout = 5f;

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
        /// Which save profile each connected client is playing, as reported by that client the
        /// moment it has this object.
        ///
        /// <para>
        /// The server has no other way to know before a player object exists. Connection approval —
        /// the usual carrier for a payload like this — is deliberately off in this project so the
        /// lobby and Relay flows stay as they are, so the id arrives on this scene object's own
        /// channel instead: every client has it as soon as it has persistentScene, which is well
        /// before its body is spawned. <see cref="PlayerSaveSync"/> still does the binding and still
        /// validates the claim independently; this copy only decides which chunks to stream.
        /// </para>
        /// </summary>
        private readonly Dictionary<ulong, string> profileByClient = new();

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
            Debug.Log($"[NGM DEBUG] OnNetworkSpawn called on instance {GetInstanceID()}, IsServer={IsServer}, PendingSceneNameToWaitFor='{PendingSceneNameToWaitFor}'");

            // Sent by everyone, host included. The host's own answer is redundant — it reads
            // PlayerProfile.LocalId directly — but a special case here would be one more branch
            // that only the rarely-tested path exercises.
            ReportProfileServerRpc(PlayerProfile.LocalId);

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
            if (NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[NGM DEBUG] OnClientConnected({clientId}) called, already handled={handledClients.Contains(clientId)}");
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
        }

        /// <summary>
        /// A client telling the server which save profile it is playing, before it has a body.
        ///
        /// Trusted only as far as it goes: the answer picks which chunks are streamed for this
        /// client and nothing else. Actually binding the profile — and therefore handing over a
        /// saved inventory — happens in <see cref="PlayerSaveSync"/>, which checks the claim against
        /// the live bindings on its own.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportProfileServerRpc(FixedString64Bytes profileId, RpcParams rpcParams = default)
        {
            string profile = profileId.ToString();
            if (!PlayerSaveSync.IsWellFormed(profile)) return;

            profileByClient[rpcParams.Receive.SenderClientId] = profile;
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

                SpawnManager.Instance.SpawnPlayerForClient(clientId, spawnPos);
                yield break;
            }

            SpawnManager.Instance.SpawnPlayerForClient(clientId);
        }
    
        /// <summary>
        /// Waits until <paramref name="clientId"/> has told us its profile, or we give up.
        ///
        /// Bounded, and the bound is the point: a client that never reports — an old build, a lost
        /// packet, a peer that dropped between connecting and loading the scene — must not hold its
        /// own spawn open forever. Giving up simply means the world is streamed around the spawn
        /// point, which is where it went before any of this existed.
        /// </summary>
        private IEnumerator WaitForProfile(ulong clientId)
        {
            if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
                yield break;                        // the host reads its own id locally

            float deadline = Time.time + Mathf.Max(0f, profileReportTimeout);

            while (!profileByClient.ContainsKey(clientId))
            {
                // A client that left while we waited has nothing left to wait for.
                if (NetworkManager.Singleton == null ||
                    !NetworkManager.Singleton.ConnectedClientsIds.Contains(clientId))
                    yield break;

                if (Time.time >= deadline)
                {
                    Debug.LogWarning($"[NGM] Client {clientId} never reported a save profile, so the " +
                                     "world is being streamed around the spawn point instead of around " +
                                     "wherever they left off. If they had a saved position they will be " +
                                     "moved there after spawning, across terrain that may still be loading.");
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// The saved position for a client, when one is known before it spawns.
        ///
        /// <para>
        /// This answers for remote clients too, and it has to. It decides which chunks are streamed,
        /// and a client restored to the far side of the map after the world was prepared around the
        /// spawn point is teleported onto terrain that was never loaded — the exact fall the host
        /// path at the call site exists to prevent. The host was only ever the easy case because its
        /// profile is a local read; a remote client's arrives over
        /// <see cref="ReportProfileServerRpc"/> instead, which is what makes the same answer
        /// available here rather than only after <see cref="PlayerSaveSync"/> has bound the body.
        /// </para>
        /// <para>
        /// Connection approval stays off. It is the mechanism this would normally use and turning it
        /// on would put a required handshake in front of every lobby and Relay join in the project;
        /// a report on an already-replicated scene object costs nothing and breaks nothing.
        /// </para>
        /// </summary>
        private bool TryGetSavedSpawn(ulong clientId, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            PlayerSaveService players = SaveManager.Instance?.Players;
            if (players == null) return false;

            bool isLocal = NetworkManager.Singleton != null &&
                           clientId == NetworkManager.Singleton.LocalClientId;

            string profileId = isLocal
                ? PlayerProfile.LocalId
                : profileByClient.GetValueOrDefault(clientId);

            if (string.IsNullOrEmpty(profileId)) return false;

            // A profile someone else is already playing is not this client's to be restored into.
            // PlayerSaveSync refuses the binding in that case, so honouring it here would stream the
            // world around a position this client is never going to be put at.
            if (!isLocal && players.TryGetBoundPlayer(profileId, out GameObject live) && live != null)
            {
                Debug.LogWarning($"[NGM] Client {clientId} reported profile '{profileId}', which " +
                                 $"'{live.name}' is already playing. Spawning them at the spawn point.");
                return false;
            }

            return players.TryGetSpawnPosition(profileId, out position, out rotation);
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
