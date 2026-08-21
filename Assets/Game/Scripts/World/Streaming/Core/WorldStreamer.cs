using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Core;

namespace SpaceGame.World
{
    /// <summary>
    /// Server-authoritative world chunk streamer.
    /// Lives in the persistent/base game scene. Loads and unloads chunk scenes
    /// additively based on all connected players' positions.
    /// Netcode only allows one scene operation at a time, so all loads/unloads are queued.
    /// </summary>
    public class WorldStreamer : NetworkBehaviour
    {
        [SerializeField] private WorldStreamingConfig config;

        /// <summary>The world this streamer is running, for code that needs the world's identity.</summary>
        public WorldStreamingConfig Config => config;

        [Header("NavMesh")]
        [Tooltip("How far to search when snapping an agent onto the pre-baked world NavMesh as its " +
                 "chunk loads. The NavMesh is always present (see WorldNavMeshProvider), so this " +
                 "only covers agents authored slightly off it.")]
        [SerializeField] private float agentSnapDistance = 32f;

        [Header("Chunk activation")]
        [Tooltip("Per-frame budget in milliseconds for building a freshly loaded chunk's deferred " +
                 "content (terrain features and their mesh colliders). Lower spreads the work over " +
                 "more frames; the chunk simply finishes filling in slightly later.")]
        [SerializeField] private float chunkActivationBudgetMs = 2f;

        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;

        /// <summary>
        /// Fired on the server once all initial chunks (around spawn points) are loaded.
        /// Subscribe to this before spawning players.
        /// </summary>
        public event Action OnInitialChunksReady;

        /// <summary>
        /// A chunk scene has finished loading and its contents are addressable.
        ///
        /// Static, matching the SceneTracked registry below, so a listener does not need a
        /// reference to a WorldStreamer that may not exist yet — the save system subscribes long
        /// before the world scene is loaded.
        /// </summary>
        public static event Action<Vector2Int, Scene> OnChunkLoaded;

        /// <summary>
        /// A chunk scene is about to be unloaded and everything in it destroyed.
        ///
        /// Fired BEFORE the unload is issued, which is the whole point: this is the last moment
        /// anything can read the state of the objects in that chunk. A listener that waited for the
        /// unload to complete would find nothing left to capture.
        /// </summary>
        public static event Action<Vector2Int, Scene> OnChunkWillUnload;

        /// <summary>A chunk scene is gone.</summary>
        public static event Action<Vector2Int> OnChunkUnloaded;

        public bool InitialChunksLoaded => initialChunksLoaded;
        public bool IsReady => isReady;

        public void RegisterTrackedTransform(Transform t)
        {
            if (t != null && !trackedTransforms.Contains(t))
                trackedTransforms.Add(t);
        }

        public void UnregisterTrackedTransform(Transform t) => trackedTransforms.Remove(t);

        /// <summary>
        /// Whether <paramref name="worldPos"/> is somewhere this streamer is responsible for —
        /// inside the chunk grid, as opposed to the minigame arena 16.5 km east or anywhere else
        /// deliberately parked off the map.
        ///
        /// Asked by callers that need to tell "there is no terrain here" apart from "there is no
        /// terrain here YET". Inside the grid every position has a heightmap owed to it, so an
        /// unsampleable one means the chunk has not arrived rather than that the place is
        /// terrainless. Deliberately not <see cref="IsChunkLoadedAt"/>: chunk state is only tracked
        /// on the machine that issues the loads, so on a client it reads NotLoaded forever.
        /// </summary>
        public bool IsInsideWorldGrid(Vector3 worldPos) => config != null && config.IsWithinGrid(worldPos);

        /// <summary>True if the chunk under <paramref name="worldPos"/> is fully loaded (state == Loaded).</summary>
        public bool IsChunkLoadedAt(Vector3 worldPos)
        {
            if (config == null) return false;
            var coord = config.WorldToChunkCoord(worldPos);
            return GetChunkState(coord) == ChunkState.Loaded;
        }

        /// <summary>
        /// Sample the ground height at <paramref name="worldPos"/> using a downward raycast first,
        /// then the chunk's terrain as fallback. Returns false if neither is available.
        /// </summary>
        public bool TrySampleGroundHeight(Vector3 worldPos, out float groundY)
        {
            // Raycast against any collider above ground level — works for terrain and structures.
            var rayOrigin = new Vector3(worldPos.x, worldPos.y + 200f, worldPos.z);
            if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return true;
            }

            // Fallback: sample the chunk's terrain directly.
            if (config != null)
            {
                var coord = config.WorldToChunkCoord(worldPos);
                if (loadedTerrains.TryGetValue(coord, out var terrain) && terrain != null)
                {
                    groundY = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
                    return true;
                }
            }

            groundY = 0f;
            return false;
        }

        /// <summary>
        /// Sample the chunk terrain's own surface height at <paramref name="worldPos"/> — terrain
        /// only, never a raycast. Returns false when the chunk under the position is not loaded or
        /// carries no Terrain.
        ///
        /// Deliberately not <see cref="TrySampleGroundHeight"/>: that one raycasts from 200 m up and
        /// takes the first hit, so standing inside a building it reports the roof, and standing on a
        /// vehicle it reports the vehicle. Both are correct answers to "what is the ground here" and
        /// both are wrong answers to "am I under the terrain", which is a question strictly about
        /// the terrain surface.
        /// </summary>
        public bool TryGetTerrainHeight(Vector3 worldPos, out float terrainY)
        {
            // IsWithinGrid, not WorldToChunkCoord alone: that clamps anything outside the grid to
            // the nearest edge chunk, so without this a position in the minigame arena 16.5 km east
            // of the world would be answered with the height of the world's corner terrain.
            if (config != null && config.IsWithinGrid(worldPos))
            {
                var coord = config.WorldToChunkCoord(worldPos);
                if (GetChunkState(coord) == ChunkState.Loaded
                    && loadedTerrains.TryGetValue(coord, out var terrain) && terrain != null)
                {
                    terrainY = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
                    return true;
                }
            }

            terrainY = 0f;
            return false;
        }

        // ─────────────────────────────────────────────
        //  SceneTracked registry (static so components can self-register from OnEnable
        //  without a FindFirstObjectByType call, and survives WorldStreamer respawn).
        // ─────────────────────────────────────────────

        private static readonly HashSet<SceneTracked> s_trackedEntities = new();

        public static void RegisterTracked(SceneTracked entity)
        {
            if (entity != null)
                s_trackedEntities.Add(entity);
        }

        public static void UnregisterTracked(SceneTracked entity)
        {
            if (entity != null)
                s_trackedEntities.Remove(entity);
        }

        private enum ChunkState { NotLoaded, Loading, Loaded, Unloading }

        private readonly Dictionary<Vector2Int, ChunkState> chunkStates = new();
        private readonly Dictionary<Vector2Int, Scene> loadedScenes = new();
        private readonly Dictionary<Vector2Int, Terrain> loadedTerrains = new();
        private readonly Dictionary<Vector2Int, float> unloadTimers = new();
        private readonly List<Transform> trackedTransforms = new();

        // Preload callers waiting for chunk CONTENT, not just the scene load — see WhenChunkContentBuilt.
        private readonly List<Action> pendingContentCallbacks = new();
        private readonly List<Action> contentCallbackBuffer = new();

        // Per-tracker history, so the streamer knows how fast each anchor is travelling and can
        // load ahead of it instead of behind it.
        private struct AnchorSample
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public bool OffWorld;
            public int Pass;
        }

        private readonly Dictionary<Transform, AnchorSample> anchorHistory = new();
        private readonly List<Transform> anchorPruneBuffer = new();
        private int anchorPass;

        // Queue for sequential scene operations (Netcode only allows one at a time)
        private readonly Queue<SceneOperation> operationQueue = new();
        private bool operationInProgress;

        // Set when NetworkManager.SceneManager.LoadScene/UnloadScene rejects our call with
        // SceneEventInProgress. Netcode's "a scene event is active" flag is global to the whole
        // NetworkSceneManager, not per-scene, so this can happen even for the very first operation
        // in our own queue — e.g. another, unrelated scene load (the minigame scene itself, or one
        // kicked off by other game code) is still finishing on Netcode's side even though it may
        // already look complete from a plain UnityEngine.SceneManager perspective. Failing the
        // operation outright would silently drop chunk loads; instead we retry the SAME operation
        // after a short cooldown rather than moving on to the next queued op (which would just hit
        // the same busy flag and cascade-fail every other queued operation in the same frame).
        private SceneOperation? retryOperation;
        private float retryTime;
        private const float SceneEventRetryDelay = 0.2f;

        private bool isReady;
        private bool initialChunksLoaded;
        private float updateInterval = 0.5f;
        private float nextUpdateTime;

        // Scene this WorldStreamer lives in — used as the migration target for Pin'd entities.
        private Scene persistentScene;

        private struct SceneOperation
        {
            public enum Type { Load, Unload }
            public Type OperationType;
            public Vector2Int Coord;
            public Action OnComplete;
        }

        private void Start()
        {
            persistentScene = gameObject.scene;

            // Let Unity time-slice the integration phase of additive scene loads instead of
            // finishing a whole chunk in one frame. Without this a content-heavy chunk lands its
            // entire object graph — colliders cooked and all — inside a single Update.
            Application.backgroundLoadingPriority = ThreadPriority.Low;

            // Offline: initialize immediately without waiting for Netcode
            if (!Network.IsNetworked)
                InitializeOffline();
        }

        private void InitializeOffline()
        {
            if (config == null)
            {
                Debug.LogError("[WorldStreamer] Missing WorldStreamingConfig reference.");
                return;
            }

            InitializeChunkStates();
            isReady = true;
            Debug.Log("[WorldStreamer] Initialized in offline mode.");
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            if (config == null)
            {
                Debug.LogError("[WorldStreamer] Missing WorldStreamingConfig reference.");
                return;
            }

            if (!persistentScene.IsValid())
                persistentScene = gameObject.scene;

            NetworkManager.Singleton.SceneManager.OnSceneEvent += HandleSceneEvent;
            InitializeChunkStates();
            isReady = true;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleSceneEvent;

            isReady = false;
            ChunkActivationQueue.Shared.Clear();
            chunkStates.Clear();
            loadedScenes.Clear();
            loadedTerrains.Clear();
            unloadTimers.Clear();
            anchorHistory.Clear();
            operationQueue.Clear();
            operationInProgress = false;
            retryOperation = null;
        }

        private void Update()
        {
            // Run if we're the server (online) or in offline mode
            if (!isReady) return;
            if (Network.IsNetworked && !IsServer) return;

            // Retry an operation Netcode previously rejected with SceneEventInProgress. Nothing
            // else pokes ProcessNextOperation() once the retry cooldown elapses, since the retry
            // isn't triggered by an Enqueue call or a FinishOperation completion.
            if (retryOperation.HasValue && !operationInProgress && Time.time >= retryTime)
            {
                ProcessNextOperation();
            }

            // The queue drains itself (see ChunkActivationRunner); the streamer only owns the rate.
            ChunkActivationQueue.Shared.budgetMs = chunkActivationBudgetMs;

            // Never overlap a chunk's construction with the next chunk's load — that is how two
            // costly phases end up in one frame. The queue is drained every frame regardless, so
            // this defers the next operation rather than blocking it.
            if (!operationInProgress && operationQueue.Count > 0
                && ChunkActivationQueue.Shared.PendingCount == 0)
            {
                ProcessNextOperation();
            }

            FlushContentCallbacks();

            if (Time.time < nextUpdateTime) return;
            nextUpdateTime = Time.time + updateInterval;

            UpdateChunkLoading();
            UpdateSceneMembership();
        }

        private void InitializeChunkStates()
        {
            if (config == null || config.chunks == null) return;

            foreach (var chunk in config.chunks)
            {
                // A chunk scene may already be open when we enter Play mode — this happens
                // whenever someone keeps the chunk scenes loaded additively in the editor for
                // editing. Marking it NotLoaded would leave the streamer believing it can load a
                // scene that is already there, and its terrain would never be aligned to the grid
                // or registered for height queries. Adopt the already-open scene instead.
                var existing = SceneManager.GetSceneByName(chunk.sceneName);
                if (existing.IsValid() && existing.isLoaded)
                {
                    AdoptLoadedChunk(chunk.gridCoord, existing);
                }
                else
                {
                    chunkStates[chunk.gridCoord] = ChunkState.NotLoaded;
                }
            }
        }

        // Register a chunk scene that was already loaded before the streamer took over
        // (e.g. left open additively in the editor). Mirrors the work the load callbacks do.
        private void AdoptLoadedChunk(Vector2Int coord, Scene scene)
        {
            chunkStates[coord] = ChunkState.Loaded;
            loadedScenes[coord] = scene;
            CacheTerrainForChunk(coord);
            RefreshTerrainNeighborsAround(coord);
            SnapAgentsToNavMesh(coord);
            Debug.Log($"[WorldStreamer] Adopted pre-loaded chunk {coord} ({scene.name})");

            // An adopted chunk is as loaded as one the streamer opened itself, and its saved
            // contents have to be restored the same way — otherwise leaving chunk scenes open in
            // the editor silently disables persistence for exactly those chunks.
            RaiseChunkLoaded(coord);
        }

        public void PreloadChunksAroundPosition(Vector3 worldPos, Action onComplete = null)
        {
            if (!isReady)
            {
                Debug.LogError("[WorldStreamer] PreloadChunksAroundPosition called before OnNetworkSpawn. Ensure WorldStreamer spawns before callers.");
                onComplete?.Invoke();
                return;
            }

            var coord = config.WorldToChunkCoord(worldPos);
            var chunks = GetChunksInRadius(coord, config.loadRadius);
            var toLoad = chunks.Where(c => GetChunkState(c) == ChunkState.NotLoaded).ToList();

            if (toLoad.Count == 0)
            {
                MarkInitialChunksLoaded();
                onComplete?.Invoke();
                return;
            }

            int remaining = toLoad.Count;
            foreach (var c in toLoad)
            {
                EnqueueLoad(c, () =>
                {
                    remaining--;
                    if (remaining <= 0) WhenChunkContentBuilt(onComplete);
                });
            }
        }

        public void PreloadChunksAroundPositions(IEnumerable<Vector3> worldPositions, Action onComplete = null)
        {
            if (!isReady || config == null)
            {
                if (!isReady)
                    Debug.LogError("[WorldStreamer] PreloadChunksAroundPositions called before OnNetworkSpawn. Ensure WorldStreamer spawns before callers.");
                onComplete?.Invoke();
                return;
            }

            var chunksToLoad = new HashSet<Vector2Int>();

            foreach (var worldPos in worldPositions)
            {
                // Positions that belong to a deliberately off-grid scene (the minigame arena)
                // must not be clamped into a real edge chunk here — WorldToChunkCoord clamps, so
                // the off-world test has to happen before calling it.
                if (!config.TryGetStreamingCoord(worldPos, out var coord)) continue;

                foreach (var chunk in GetChunksInRadius(coord, config.loadRadius))
                {
                    if (GetChunkState(chunk) == ChunkState.NotLoaded)
                        chunksToLoad.Add(chunk);
                }
            }

            if (chunksToLoad.Count == 0)
            {
                MarkInitialChunksLoaded();
                onComplete?.Invoke();
                return;
            }

            int remaining = chunksToLoad.Count;
            foreach (var chunk in chunksToLoad)
            {
                EnqueueLoad(chunk, () =>
                {
                    remaining--;
                    if (remaining <= 0) WhenChunkContentBuilt(onComplete);
                });
            }
        }

        /// <summary>
        /// Run <paramref name="onComplete"/> once the loaded chunks have also finished BUILDING —
        /// not merely finished loading.
        ///
        /// Terrain features spawn through <see cref="ChunkActivationQueue"/> now, several frames
        /// after the scene load reports done, and a feature's MeshCollider is what a player stands
        /// on. Callers of the preload API use it to decide when it is safe to place a player:
        /// NetworkGameManager waits on it before spawning, and the loading screen waits on
        /// InitialChunksLoaded before handing over control. Firing on the load alone drops players
        /// through cliffs and mesas, or spawns them inside one.
        /// </summary>
        private void WhenChunkContentBuilt(Action onComplete)
        {
            if (ChunkActivationQueue.Shared.PendingCount == 0)
            {
                MarkInitialChunksLoaded();
                onComplete?.Invoke();
                return;
            }

            pendingContentCallbacks.Add(onComplete);
        }

        /// <summary>
        /// Fire any preload callbacks that were waiting on chunk content to finish building.
        /// Called every frame from <c>Update</c>; cheap while the list is empty.
        /// </summary>
        private void FlushContentCallbacks()
        {
            if (pendingContentCallbacks.Count == 0) return;
            if (ChunkActivationQueue.Shared.PendingCount > 0) return;
            if (operationInProgress || operationQueue.Count > 0) return;

            // Copy first: a callback may spawn a player, which can register a tracker and enqueue
            // more loads, and mutating the list we are iterating would throw.
            contentCallbackBuffer.Clear();
            contentCallbackBuffer.AddRange(pendingContentCallbacks);
            pendingContentCallbacks.Clear();

            MarkInitialChunksLoaded();

            foreach (var callback in contentCallbackBuffer)
                callback?.Invoke();
        }

        private void UpdateChunkLoading()
        {
            var requiredChunks = new HashSet<Vector2Int>();
            anchorPass++;

            if (Network.IsNetworked)
            {
                foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    if (client.PlayerObject == null) continue;
                    AddAnchor(requiredChunks, client.PlayerObject.transform, config.loadRadius);
                }
            }

            foreach (var t in trackedTransforms)
                AddAnchor(requiredChunks, t, config.loadRadius);

            // SceneTracked entities with keepChunksLoaded=true also pull chunks in around them.
            // Pin'd entities (mounts) need their surroundings loaded so the world doesn't vanish
            // beneath them when the player drives further than the player's own load radius.
            s_trackedEntities.RemoveWhere(e => e == null);
            foreach (var entity in s_trackedEntities)
            {
                if (!entity.KeepChunksLoaded) continue;
                AddAnchor(requiredChunks, entity.TrackedTransform, config.loadRadius);
            }

            // Chunks that contain a tracker which would be destroyed by the unload (Pin/Migrate)
            // get pinned even if they're outside the load radius. Despawn-policy trackers don't pin.
            // Without this guard a Migrate'd vehicle could still get yanked out from under itself
            // if it idled at the very edge of a chunk for the grace period.
            foreach (var entity in s_trackedEntities)
            {
                if (entity.Policy == SceneTracked.UnloadPolicy.Despawn) continue;
                AddAnchor(requiredChunks, entity.TrackedTransform, 0);
            }

            PruneAnchorHistory();

            // Load required chunks that aren't loaded
            foreach (var coord in requiredChunks)
            {
                var state = GetChunkState(coord);
                if (state == ChunkState.NotLoaded)
                {
                    EnqueueLoad(coord);
                }
                unloadTimers.Remove(coord);
            }

            // Start unload timers for chunks no longer needed
            foreach (var kvp in chunkStates.ToList())
            {
                if (kvp.Value != ChunkState.Loaded) continue;
                if (requiredChunks.Contains(kvp.Key)) continue;

                if (!unloadTimers.ContainsKey(kvp.Key))
                {
                    unloadTimers[kvp.Key] = Time.time + config.unloadGracePeriod;
                }
                else if (Time.time >= unloadTimers[kvp.Key])
                {
                    EnqueueUnload(kvp.Key);
                    unloadTimers.Remove(kvp.Key);
                }
            }
        }

        // ─────────────────────────────────────────────
        //  Anchors — every tracker that pulls chunks in around itself
        // ─────────────────────────────────────────────

        /// <summary>
        /// Require the chunks around <paramref name="t"/>, and around where it is heading.
        ///
        /// Every streaming decision goes through here so there is exactly one answer to "does
        /// this thing keep the world loaded". A tracker only stops requiring chunks when it is
        /// genuinely off-world (see <see cref="WorldStreamingConfig.offWorldDistance"/>); being
        /// past the edge of the grid is not enough, because sailing off the map is something a
        /// player does at speed and they still need the ground behind them to exist.
        /// </summary>
        private void AddAnchor(HashSet<Vector2Int> required, Transform t, int radius)
        {
            if (t == null) return;

            Vector3 position = t.position;
            Vector3 velocity = SampleVelocity(t, position);

            bool anchored = AddAnchorAt(required, position, radius);
            ReportOffWorld(t, !anchored);
            if (!anchored) return;

            // Start on the ground the tracker is about to arrive on, not just the ground already
            // under it. At walking pace the predicted position is metres away and lands in the
            // same chunk; on something fast enough to cross a chunk between scene loads it pulls
            // the next one in a tick early.
            if (velocity.sqrMagnitude > 1f)
                AddAnchorAt(required, config.PredictAhead(position, velocity), radius);
        }

        private bool AddAnchorAt(HashSet<Vector2Int> required, Vector3 position, int radius)
        {
            if (!config.TryGetStreamingCoord(position, out var coord)) return false;

            AddChunksInRadius(required, coord, radius);
            return true;
        }

        /// <summary>
        /// Speed of a tracker in world units per second, measured between chunk-loading passes.
        /// Sampled from the transform rather than a Rigidbody because a tracker can be anything —
        /// a player, a vehicle, a plain marker object like the interior return pin — and a player
        /// riding a deck is moved by the platform writing their pose, not by their own velocity.
        /// </summary>
        private Vector3 SampleVelocity(Transform t, Vector3 position)
        {
            if (!anchorHistory.TryGetValue(t, out var sample))
            {
                anchorHistory[t] = new AnchorSample { Position = position, Pass = anchorPass };
                return Vector3.zero;
            }

            // Several anchor rules can ask about the same transform in one pass; only the first
            // advances the sample, or the rest would all measure a zero delta.
            if (sample.Pass == anchorPass) return sample.Velocity;

            sample.Velocity = (position - sample.Position) / Mathf.Max(updateInterval, 1e-3f);
            sample.Position = position;
            sample.Pass = anchorPass;
            anchorHistory[t] = sample;
            return sample.Velocity;
        }

        /// <summary>
        /// Say so once when a tracker leaves the streamed world and once when it returns. Silence
        /// here is what made this hard to see from the outside: chunks simply stopped arriving.
        /// </summary>
        private void ReportOffWorld(Transform t, bool offWorld)
        {
            if (!anchorHistory.TryGetValue(t, out var sample) || sample.OffWorld == offWorld) return;

            sample.OffWorld = offWorld;
            anchorHistory[t] = sample;

            if (offWorld)
                Debug.LogWarning($"[WorldStreamer] '{t.name}' is more than {config.offWorldDistance:0} m " +
                                 $"outside the chunk grid at {t.position} — it no longer keeps any chunk loaded.");
            else
                Debug.Log($"[WorldStreamer] '{t.name}' is back inside the streamed world at {t.position}.");
        }

        private void PruneAnchorHistory()
        {
            anchorPruneBuffer.Clear();

            foreach (var kvp in anchorHistory)
            {
                // Stale by several passes means the tracker was unregistered or destroyed.
                if (kvp.Key == null || anchorPass - kvp.Value.Pass > 4)
                    anchorPruneBuffer.Add(kvp.Key);
            }

            foreach (var t in anchorPruneBuffer)
                anchorHistory.Remove(t);
        }

        // ─────────────────────────────────────────────
        //  Scene membership pass — keep SceneTracked entities in the right scene
        // ─────────────────────────────────────────────

        private void UpdateSceneMembership()
        {
            if (!persistentScene.IsValid() || !persistentScene.isLoaded)
                return;

            s_trackedEntities.RemoveWhere(e => e == null);

            foreach (var entity in s_trackedEntities)
            {
                Scene desired = ResolveDesiredScene(entity);
                if (!desired.IsValid() || !desired.isLoaded) continue;

                var go = entity.gameObject;
                if (go.scene == desired) continue;

                // Only move root objects — Unity rejects MoveGameObjectToScene on a child.
                // Anything parented elsewhere (e.g. rider parented to mount) follows automatically.
                if (go.transform.parent != null) continue;

                // Hand off through Netcode when this is a NetworkObject so all clients agree on
                // the new scene assignment. Non-networked or offline path falls through to the
                // direct SceneManager call.
                // TODO: when NetworkObject support lands on vehicles, route this through
                // NetworkManager.Singleton.SceneManager.MoveObjectToScene (Netcode-for-GameObjects
                // adds this in 1.x via NetworkObject.SceneMigrationSynchronization).
                SceneManager.MoveGameObjectToScene(go, desired);
            }
        }

        private Scene ResolveDesiredScene(SceneTracked entity)
        {
            switch (entity.Policy)
            {
                case SceneTracked.UnloadPolicy.Pin:
                    return persistentScene;

                case SceneTracked.UnloadPolicy.Migrate:
                {
                    var coord = config.WorldToChunkCoord(entity.TrackedTransform.position);
                    if (loadedScenes.TryGetValue(coord, out var scene) && scene.IsValid() && scene.isLoaded)
                        return scene;
                    // Chunk under the entity isn't loaded yet — leave the entity where it is.
                    // The unload guard in UpdateChunkLoading prevents its current scene from being
                    // ripped out, and the next tick will re-evaluate once the chunk loads.
                    return entity.gameObject.scene;
                }

                case SceneTracked.UnloadPolicy.Despawn:
                default:
                    return entity.gameObject.scene;
            }
        }

        // ─────────────────────────────────────────────
        //  Sequential operation queue
        // ─────────────────────────────────────────────

        private void EnqueueLoad(Vector2Int coord, Action onComplete = null)
        {
            if (GetChunkState(coord) != ChunkState.NotLoaded)
            {
                onComplete?.Invoke();
                return;
            }

            chunkStates[coord] = ChunkState.Loading;

            operationQueue.Enqueue(new SceneOperation
            {
                OperationType = SceneOperation.Type.Load,
                Coord = coord,
                OnComplete = onComplete
            });

            ProcessNextOperation();
        }

        private void EnqueueUnload(Vector2Int coord, Action onComplete = null)
        {
            if (GetChunkState(coord) != ChunkState.Loaded)
            {
                onComplete?.Invoke();
                return;
            }

            chunkStates[coord] = ChunkState.Unloading;

            operationQueue.Enqueue(new SceneOperation
            {
                OperationType = SceneOperation.Type.Unload,
                Coord = coord,
                OnComplete = onComplete
            });

            ProcessNextOperation();
        }

        private void ProcessNextOperation()
        {
            if (operationInProgress) return;

            // Wait for the last chunk to finish building itself before pulling in another. Update()
            // re-pokes this the frame the queue empties, so nothing is dropped — it is only paced.
            if (ChunkActivationQueue.Shared.PendingCount > 0) return;

            SceneOperation op;
            if (retryOperation.HasValue)
            {
                if (Time.time < retryTime) return;
                op = retryOperation.Value;
                retryOperation = null;
            }
            else if (operationQueue.Count > 0)
            {
                op = operationQueue.Dequeue();
            }
            else
            {
                return;
            }

            operationInProgress = true;

            if (op.OperationType == SceneOperation.Type.Load)
            {
                ExecuteLoad(op);
            }
            else
            {
                ExecuteUnload(op);
            }
        }

        private void ExecuteLoad(SceneOperation op)
        {
            var chunkInfo = config.GetChunk(op.Coord);
            if (chunkInfo == null)
            {
                Debug.LogWarning($"[WorldStreamer] No chunk data for {op.Coord}");
                chunkStates[op.Coord] = ChunkState.NotLoaded;
                FinishOperation(op.OnComplete);
                return;
            }

            string sceneName = chunkInfo.Value.sceneName;
            string scenePath = chunkInfo.Value.scenePath;
            Debug.Log($"[WorldStreamer] Loading chunk {op.Coord} ({sceneName}){(string.IsNullOrEmpty(scenePath) ? "" : $" from {scenePath}")}");

            pendingCallback = op.OnComplete;
            pendingCoord = op.Coord;
            pendingSceneName = sceneName;

            if (Network.IsNetworked)
            {
                var status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
                if (status == SceneEventProgressStatus.SceneEventInProgress)
                {
                    RetryOperation(op);
                }
                else if (status != SceneEventProgressStatus.Started)
                {
                    Debug.LogError($"[WorldStreamer] Failed to load {sceneName}: {status}");
                    chunkStates[op.Coord] = ChunkState.NotLoaded;
                    FinishOperation(op.OnComplete);
                }
                // Completion handled in HandleSceneEvent
            }
            else
            {
                var asyncOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (asyncOp == null)
                {
                    Debug.LogError($"[WorldStreamer] Failed to load {sceneName} (offline).");
                    chunkStates[op.Coord] = ChunkState.NotLoaded;
                    FinishOperation(op.OnComplete);
                    return;
                }
                asyncOp.completed += _ => OnOfflineSceneLoaded(op.Coord, sceneName, op.OnComplete);
            }
        }

        // Netcode rejected the scene event because ITS OWN internal "event active" flag — which is
        // global to the whole NetworkSceneManager, not per-scene — is still set from an unrelated
        // scene event elsewhere in the game. Re-attempt the exact same operation shortly instead of
        // dropping it; leaves chunkStates/pendingXxx untouched since nothing actually started.
        private void RetryOperation(SceneOperation op)
        {
            retryOperation = op;
            retryTime = Time.time + SceneEventRetryDelay;
            pendingCallback = null;
            pendingSceneName = null;
            operationInProgress = false;
        }

        private void OnOfflineSceneLoaded(Vector2Int coord, string sceneName, Action onComplete)
        {
            chunkStates[coord] = ChunkState.Loaded;
            loadedScenes[coord] = SceneManager.GetSceneByName(sceneName);
            CacheTerrainForChunk(coord);
            RefreshTerrainNeighborsAround(coord);
            SnapAgentsToNavMesh(coord);
            Debug.Log($"[WorldStreamer] Chunk {coord} loaded (offline)");
            RaiseChunkLoaded(coord);
            FinishOperation(onComplete);
        }

        private void ExecuteUnload(SceneOperation op)
        {
            if (!loadedScenes.TryGetValue(op.Coord, out var scene))
            {
                Debug.LogWarning($"[WorldStreamer] No loaded scene for {op.Coord}");
                chunkStates[op.Coord] = ChunkState.NotLoaded;
                FinishOperation(op.OnComplete);
                return;
            }

            Debug.Log($"[WorldStreamer] Unloading chunk {op.Coord}");

            // Before the unload is issued, not after it completes: this is the last frame on which
            // anything in that scene can still be read. The save system captures the chunk here.
            RaiseChunkWillUnload(op.Coord, scene);

            pendingCallback = op.OnComplete;
            pendingCoord = op.Coord;
            pendingSceneName = null;

            if (Network.IsNetworked)
            {
                var status = NetworkManager.Singleton.SceneManager.UnloadScene(scene);
                if (status == SceneEventProgressStatus.SceneEventInProgress)
                {
                    RetryOperation(op);
                }
                else if (status != SceneEventProgressStatus.Started)
                {
                    Debug.LogError($"[WorldStreamer] Failed to unload chunk {op.Coord}: {status}");
                    chunkStates[op.Coord] = ChunkState.Loaded;
                    FinishOperation(op.OnComplete);
                }
                // Completion handled in HandleSceneEvent
            }
            else
            {
                var asyncOp = SceneManager.UnloadSceneAsync(scene);
                if (asyncOp == null)
                {
                    Debug.LogError($"[WorldStreamer] Failed to unload chunk {op.Coord} (offline).");
                    chunkStates[op.Coord] = ChunkState.Loaded;
                    FinishOperation(op.OnComplete);
                    return;
                }
                asyncOp.completed += _ => OnOfflineSceneUnloaded(op.Coord, op.OnComplete);
            }
        }

        private void OnOfflineSceneUnloaded(Vector2Int coord, Action onComplete)
        {
            loadedTerrains.Remove(coord);
            chunkStates[coord] = ChunkState.NotLoaded;
            loadedScenes.Remove(coord);
            RefreshTerrainNeighborsAround(coord);
            Debug.Log($"[WorldStreamer] Chunk {coord} unloaded (offline)");
            RaiseChunkUnloaded(coord);
            FinishOperation(onComplete);
        }

        // Pending operation tracking
        private Action pendingCallback;
        private Vector2Int pendingCoord;
        private string pendingSceneName;

        public string DebugDumpState()
        {
            // trackedTransforms/anchors are in here because "no chunk ever loads" and "nothing is
            // anchoring the world" look identical from the outside, and only one of them is a
            // streaming fault.
            return $"operationInProgress={operationInProgress} queueCount={operationQueue.Count} " +
                   $"pendingCoord={pendingCoord} pendingSceneName={pendingSceneName} " +
                   $"trackedTransforms={trackedTransforms.Count} sceneTracked={s_trackedEntities.Count} " +
                   $"anchors=[{string.Join(", ", anchorHistory.Where(kv => kv.Key != null).Select(kv => $"{kv.Key.name}@{kv.Value.Position}{(kv.Value.OffWorld ? " OFF-WORLD" : "")}"))}] " +
                   $"chunkStates=[{string.Join(", ", chunkStates.Select(kv => $"{kv.Key}:{kv.Value}"))}]";
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            if (!operationInProgress) return;

            if (sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted
                && pendingSceneName != null
                && sceneEvent.SceneName == pendingSceneName)
            {
                chunkStates[pendingCoord] = ChunkState.Loaded;
                loadedScenes[pendingCoord] = SceneManager.GetSceneByName(pendingSceneName);
                CacheTerrainForChunk(pendingCoord);
                RefreshTerrainNeighborsAround(pendingCoord);
                SnapAgentsToNavMesh(pendingCoord);
                Debug.Log($"[WorldStreamer] Chunk {pendingCoord} loaded");
                RaiseChunkLoaded(pendingCoord);
                FinishOperation(pendingCallback);
            }
            else if (sceneEvent.SceneEventType == SceneEventType.UnloadEventCompleted
                     && pendingSceneName == null)
            {
                loadedTerrains.Remove(pendingCoord);
                chunkStates[pendingCoord] = ChunkState.NotLoaded;
                loadedScenes.Remove(pendingCoord);
                RefreshTerrainNeighborsAround(pendingCoord);
                Debug.Log($"[WorldStreamer] Chunk {pendingCoord} unloaded");
                RaiseChunkUnloaded(pendingCoord);
                FinishOperation(pendingCallback);
            }
        }

        // Raised inside a try/catch because these are static events with subscribers outside the
        // streamer's control. A listener that throws would otherwise leave operationInProgress set
        // and stall the chunk queue permanently — a save-system bug would look like a world that
        // simply stops streaming.
        private void RaiseChunkLoaded(Vector2Int coord)
        {
            if (!loadedScenes.TryGetValue(coord, out var scene)) return;

            try { OnChunkLoaded?.Invoke(coord, scene); }
            catch (Exception e) { Debug.LogError($"[WorldStreamer] OnChunkLoaded listener threw for {coord}: {e}"); }
        }

        private void RaiseChunkWillUnload(Vector2Int coord, Scene scene)
        {
            try { OnChunkWillUnload?.Invoke(coord, scene); }
            catch (Exception e) { Debug.LogError($"[WorldStreamer] OnChunkWillUnload listener threw for {coord}: {e}"); }
        }

        private void RaiseChunkUnloaded(Vector2Int coord)
        {
            try { OnChunkUnloaded?.Invoke(coord); }
            catch (Exception e) { Debug.LogError($"[WorldStreamer] OnChunkUnloaded listener threw for {coord}: {e}"); }
        }

        private void FinishOperation(Action callback)
        {
            operationInProgress = false;
            pendingCallback = null;
            pendingSceneName = null;
            callback?.Invoke();
            ProcessNextOperation();
        }

        // ─────────────────────────────────────────────
        //  NavMesh
        // ─────────────────────────────────────────────

        /// <summary>
        /// Attach a freshly loaded chunk's agents to the world NavMesh.
        ///
        /// This replaces a park-and-release system that disabled every agent on load and retried
        /// every half second until a runtime bake finished. With the NavMesh pre-baked and
        /// permanently present (see <see cref="WorldNavMeshProvider"/>) an agent is normally already
        /// standing on it the moment its scene loads, so no parking is needed.
        ///
        /// Enabling disabled agents is NOT optional cleanup. <c>NavMeshAgentMotor.Awake</c> disables
        /// its own agent when it finds no NavMesh under itself, on the documented promise that the
        /// streamer will switch it back on once the chunk's mesh exists. Nothing else in the project
        /// keeps that promise. Skipping disabled agents here would leave any agent that happened to
        /// wake before its mesh was reachable dead for the rest of the session, silently.
        /// </summary>
        private void SnapAgentsToNavMesh(Vector2Int coord)
        {
            if (!loadedScenes.TryGetValue(coord, out var scene) || !scene.IsValid() || !scene.isLoaded)
                return;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var agent in root.GetComponentsInChildren<NavMeshAgent>(true))
                {
                    if (agent == null) continue;
                    if (agent.enabled && agent.isOnNavMesh) continue;

                    float distance = Mathf.Max(agent.radius * 4f, agent.height * 2f, agentSnapDistance);
                    if (!NavMesh.SamplePosition(agent.transform.position, out var hit,
                                                distance, NavMesh.AllAreas))
                    {
                        Debug.LogWarning($"[WorldStreamer] '{agent.name}' in chunk {coord} is over " +
                                         $"{distance:0} m from any NavMesh, so it cannot be activated. " +
                                         "Is the world NavMesh baked and does it cover this chunk?", agent);
                        continue;
                    }

                    // Order matters: Warp only takes effect on an enabled agent, and an agent
                    // enabled this frame has not registered onto the mesh yet, so isOnNavMesh is
                    // not a usable gate here — Warp unconditionally.
                    agent.transform.position = hit.position;
                    agent.enabled = true;
                    agent.Warp(hit.position);
                }
            }
        }

        // ─────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────

        private HashSet<Vector2Int> GetChunksInRadius(Vector2Int center, int radius)
        {
            var result = new HashSet<Vector2Int>();
            AddChunksInRadius(result, center, radius);
            return result;
        }

        private void AddChunksInRadius(HashSet<Vector2Int> into, Vector2Int center, int radius)
        {
            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                for (int y = center.y - radius; y <= center.y + radius; y++)
                {
                    var coord = new Vector2Int(x, y);
                    if (config.IsValidCoord(coord))
                        into.Add(coord);
                }
            }
        }

        private ChunkState GetChunkState(Vector2Int coord)
        {
            return chunkStates.TryGetValue(coord, out var state) ? state : ChunkState.NotLoaded;
        }

        private void CacheTerrainForChunk(Vector2Int coord)
        {
            loadedTerrains.Remove(coord);

            if (!loadedScenes.TryGetValue(coord, out var scene) || !scene.IsValid() || !scene.isLoaded)
                return;

            Vector3 expectedPosition = config.ChunkToWorldPosition(coord);
            Terrain primaryTerrain = null;

            foreach (var root in scene.GetRootGameObjects())
            {
                var terrains = root.GetComponentsInChildren<Terrain>(true);
                foreach (var terrain in terrains)
                {
                    // Preserve baked terrain elevation while aligning chunk X/Z to the grid.
                    Vector3 terrainPosition = terrain.transform.position;
                    terrain.transform.position = new Vector3(expectedPosition.x, terrainPosition.y, expectedPosition.z);

                    if (primaryTerrain == null)
                        primaryTerrain = terrain;
                }
            }

            if (primaryTerrain != null)
            {
                loadedTerrains[coord] = primaryTerrain;
                Physics.SyncTransforms();
            }
        }

        private void RefreshTerrainNeighborsAround(Vector2Int centerCoord)
        {
            for (int x = centerCoord.x - 1; x <= centerCoord.x + 1; x++)
            {
                for (int y = centerCoord.y - 1; y <= centerCoord.y + 1; y++)
                {
                    var coord = new Vector2Int(x, y);
                    if (!config.IsValidCoord(coord))
                        continue;

                    RefreshTerrainNeighbors(coord);
                }
            }
        }

        private void RefreshTerrainNeighbors(Vector2Int coord)
        {
            if (!loadedTerrains.TryGetValue(coord, out var terrain) || terrain == null)
                return;

            loadedTerrains.TryGetValue(new Vector2Int(coord.x - 1, coord.y), out var left);
            loadedTerrains.TryGetValue(new Vector2Int(coord.x, coord.y + 1), out var top);
            loadedTerrains.TryGetValue(new Vector2Int(coord.x + 1, coord.y), out var right);
            loadedTerrains.TryGetValue(new Vector2Int(coord.x, coord.y - 1), out var bottom);

            terrain.SetNeighbors(left, top, right, bottom);
        }

        private void MarkInitialChunksLoaded()
        {
            if (initialChunksLoaded) return;

            initialChunksLoaded = true;
            OnInitialChunksReady?.Invoke();
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            agentSnapDistance = Mathf.Max(1f, agentSnapDistance);
        }

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos || config == null || config.chunks == null) return;
        }
    #endif

    }
}
