using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Server-authoritative world chunk streamer.
/// Lives in the persistent/base game scene. Loads and unloads chunk scenes
/// additively based on all connected players' positions.
/// Netcode only allows one scene operation at a time, so all loads/unloads are queued.
/// </summary>
public class WorldStreamer : NetworkBehaviour
{
    [SerializeField] private WorldStreamingConfig config;

    [Header("NavMesh")]
    [Tooltip("Single NavMeshSurface in the persistent scene. Rebuilt at runtime when chunks load/unload so NPCs can navigate across chunk boundaries.")]
    [SerializeField] private NavMeshSurface navMeshSurface;
    [Tooltip("Delay in seconds after the last chunk load/unload before rebuilding the NavMesh. Prevents multiple rebuilds when loading a batch of chunks.")]
    [SerializeField] private float navMeshRebuildDelay = 0.5f;
    [Tooltip("How far from a parked agent to search when reattaching it to the rebuilt NavMesh.")]
    [SerializeField] private float parkedAgentActivationDistance = 32f;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;

    /// <summary>
    /// Fired on the server once all initial chunks (around spawn points) are loaded.
    /// Subscribe to this before spawning players.
    /// </summary>
    public event Action OnInitialChunksReady;

    public bool InitialChunksLoaded => initialChunksLoaded;
    public bool IsReady => isReady;

    public void RegisterTrackedTransform(Transform t)
    {
        if (t != null && !trackedTransforms.Contains(t))
            trackedTransforms.Add(t);
    }

    public void UnregisterTrackedTransform(Transform t) => trackedTransforms.Remove(t);

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
    private readonly Dictionary<Vector2Int, List<NavMeshAgent>> parkedAgentsByChunk = new();
    private readonly List<NavMeshAgent> globallyParkedAgents = new();
    private readonly Dictionary<NavMeshAgent, Vector3> parkedAgentPositions = new();
    private readonly Dictionary<Vector2Int, float> unloadTimers = new();
    private readonly List<Transform> trackedTransforms = new();

    // Queue for sequential scene operations (Netcode only allows one at a time)
    private readonly Queue<SceneOperation> operationQueue = new();
    private bool operationInProgress;

    private bool isReady;
    private bool initialChunksLoaded;
    private float updateInterval = 0.5f;
    private float nextUpdateTime;

    // Scene this WorldStreamer lives in — used as the migration target for Pin'd entities.
    private Scene persistentScene;

    private bool navMeshDirty;
    private float navMeshRebuildTime;
    private float nextParkedAgentRetryTime;
    private NavMeshDataInstance navMeshDataInstance;
    private AsyncOperation navMeshBuildOperation;

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

        if (navMeshDataInstance.valid)
            navMeshDataInstance.Remove();

        isReady = false;
        chunkStates.Clear();
        loadedScenes.Clear();
        loadedTerrains.Clear();
        parkedAgentsByChunk.Clear();
        globallyParkedAgents.Clear();
        parkedAgentPositions.Clear();
        unloadTimers.Clear();
        operationQueue.Clear();
        operationInProgress = false;
    }

    private void Update()
    {
        // Run if we're the server (online) or in offline mode
        if (!isReady) return;
        if (Network.IsNetworked && !IsServer) return;

        if (navMeshDirty && Time.time >= navMeshRebuildTime)
        {
            navMeshDirty = false;
            RebuildNavMesh();
        }

        if ((parkedAgentsByChunk.Count > 0 || globallyParkedAgents.Count > 0)
            && (navMeshBuildOperation == null || navMeshBuildOperation.isDone)
            && Time.time >= nextParkedAgentRetryTime)
        {
            nextParkedAgentRetryTime = Time.time + 0.5f;
            ReleaseParkedAgents();
        }

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
            chunkStates[chunk.gridCoord] = ChunkState.NotLoaded;
        }
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
                if (remaining <= 0)
                {
                    MarkInitialChunksLoaded();
                    onComplete?.Invoke();
                }
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
            var coord = config.WorldToChunkCoord(worldPos);
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
                if (remaining <= 0)
                {
                    MarkInitialChunksLoaded();
                    onComplete?.Invoke();
                }
            });
        }
    }

    private void UpdateChunkLoading()
    {
        var requiredChunks = new HashSet<Vector2Int>();

        if (Network.IsNetworked)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;

                var playerPos = client.PlayerObject.transform.position;
                var playerChunk = config.WorldToChunkCoord(playerPos);
                foreach (var coord in GetChunksInRadius(playerChunk, config.loadRadius))
                    requiredChunks.Add(coord);
            }
        }

        foreach (var t in trackedTransforms)
        {
            if (t == null) continue;
            var playerChunk = config.WorldToChunkCoord(t.position);
            foreach (var coord in GetChunksInRadius(playerChunk, config.loadRadius))
                requiredChunks.Add(coord);
        }

        // SceneTracked entities with keepChunksLoaded=true also pull chunks in around them.
        // Pin'd entities (mounts) need their surroundings loaded so the world doesn't vanish
        // beneath them when the player drives further than the player's own load radius.
        s_trackedEntities.RemoveWhere(e => e == null);
        foreach (var entity in s_trackedEntities)
        {
            if (!entity.KeepChunksLoaded) continue;
            var entityChunk = config.WorldToChunkCoord(entity.TrackedTransform.position);
            foreach (var coord in GetChunksInRadius(entityChunk, config.loadRadius))
                requiredChunks.Add(coord);
        }

        // Chunks that contain a tracker which would be destroyed by the unload (Pin/Migrate)
        // get pinned even if they're outside the load radius. Despawn-policy trackers don't pin.
        // Without this guard a Migrate'd vehicle could still get yanked out from under itself
        // if it idled at the very edge of a chunk for the grace period.
        foreach (var entity in s_trackedEntities)
        {
            if (entity.Policy == SceneTracked.UnloadPolicy.Despawn) continue;
            var coord = config.WorldToChunkCoord(entity.TrackedTransform.position);
            if (config.IsValidCoord(coord))
                requiredChunks.Add(coord);
        }

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
        if (operationQueue.Count == 0) return;

        operationInProgress = true;
        var op = operationQueue.Dequeue();

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
            if (status != SceneEventProgressStatus.Started)
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

    private void OnOfflineSceneLoaded(Vector2Int coord, string sceneName, Action onComplete)
    {
        chunkStates[coord] = ChunkState.Loaded;
        loadedScenes[coord] = SceneManager.GetSceneByName(sceneName);
        CacheTerrainForChunk(coord);
        ParkAgentsForChunk(coord);
        RefreshTerrainNeighborsAround(coord);
        ScheduleNavMeshRebuild();
        Debug.Log($"[WorldStreamer] Chunk {coord} loaded (offline)");
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

        pendingCallback = op.OnComplete;
        pendingCoord = op.Coord;
        pendingSceneName = null;

        if (Network.IsNetworked)
        {
            var status = NetworkManager.Singleton.SceneManager.UnloadScene(scene);
            if (status != SceneEventProgressStatus.Started)
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
        parkedAgentsByChunk.Remove(coord);
        chunkStates[coord] = ChunkState.NotLoaded;
        loadedScenes.Remove(coord);
        RefreshTerrainNeighborsAround(coord);
        ScheduleNavMeshRebuild();
        Debug.Log($"[WorldStreamer] Chunk {coord} unloaded (offline)");
        FinishOperation(onComplete);
    }

    // Pending operation tracking
    private Action pendingCallback;
    private Vector2Int pendingCoord;
    private string pendingSceneName;

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
            ParkAgentsForChunk(pendingCoord);
            RefreshTerrainNeighborsAround(pendingCoord);
            ScheduleNavMeshRebuild();
            Debug.Log($"[WorldStreamer] Chunk {pendingCoord} loaded");
            FinishOperation(pendingCallback);
        }
        else if (sceneEvent.SceneEventType == SceneEventType.UnloadEventCompleted
                 && pendingSceneName == null)
        {
            loadedTerrains.Remove(pendingCoord);
            parkedAgentsByChunk.Remove(pendingCoord);
            chunkStates[pendingCoord] = ChunkState.NotLoaded;
            loadedScenes.Remove(pendingCoord);
            RefreshTerrainNeighborsAround(pendingCoord);
            ScheduleNavMeshRebuild();
            Debug.Log($"[WorldStreamer] Chunk {pendingCoord} unloaded");
            FinishOperation(pendingCallback);
        }
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

    private void ScheduleNavMeshRebuild()
    {
        navMeshDirty = true;
        navMeshRebuildTime = Time.time + navMeshRebuildDelay;
        nextParkedAgentRetryTime = navMeshRebuildTime;
    }

    private void RebuildNavMesh()
    {
        if (navMeshSurface == null)
        {
            Debug.LogWarning("[WorldStreamer] NavMeshSurface not assigned — NPCs will not be able to navigate.");
            return;
        }

        // Skip if a previous async build is still running — it will be replaced next cycle
        if (navMeshBuildOperation != null && !navMeshBuildOperation.isDone)
        {
            navMeshDirty = true;
            navMeshRebuildTime = Time.time + navMeshRebuildDelay;
            return;
        }

        // Any streamed agents that are still enabled while NavMesh data is swapped in
        // can throw "not close enough to the NavMesh" and get stranded. Park them all
        // before the rebuild, then reattach after the async build completes.
        ParkAgentsForAllLoadedChunks();
        ParkAgentsGlobally();

        var settings = navMeshSurface.GetBuildSettings();
        // Increase slope and step height so agents handle gentle terrain undulation
        settings.agentSlope = 60f;
        settings.agentClimb = 0.8f;

        var sources = new List<NavMeshBuildSource>();
        var markups = new List<NavMeshBuildMarkup>();

        NavMeshBuilder.CollectSources(
            navMeshSurface.collectObjects == CollectObjects.Children ? navMeshSurface.transform : null,
            navMeshSurface.layerMask,
            navMeshSurface.useGeometry,
            navMeshSurface.defaultArea,
            markups,
            sources
        );

        // Skip any non-readable meshes that slipped through (shouldn't happen
        // after MeshReadablePostprocessor reimports, but just in case).
        sources.RemoveAll(s => s.sourceObject is Mesh mesh && !mesh.isReadable);

        var bounds = new Bounds(navMeshSurface.center, navMeshSurface.size);
        if (navMeshSurface.collectObjects != CollectObjects.Volume)
        {
            bounds = new Bounds(Vector3.zero, new Vector3(10000f, 500f, 10000f));
        }

        // Ensure we have NavMeshData to update into
        if (navMeshSurface.navMeshData == null)
            navMeshSurface.navMeshData = new NavMeshData(settings.agentTypeID);

        if (!navMeshDataInstance.valid)
            navMeshDataInstance = NavMesh.AddNavMeshData(navMeshSurface.navMeshData);

        // Async build — spreads work across frames instead of freezing the game
        navMeshBuildOperation = NavMeshBuilder.UpdateNavMeshDataAsync(
            navMeshSurface.navMeshData,
            settings,
            sources,
            bounds
        );

        navMeshBuildOperation.completed += _ =>
        {
            if (!this || !isReady)
                return;

            ReleaseParkedAgents();
        };

        Debug.Log($"[WorldStreamer] NavMesh async rebuild started ({sources.Count} sources)");
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    private HashSet<Vector2Int> GetChunksInRadius(Vector2Int center, int radius)
    {
        var result = new HashSet<Vector2Int>();

        for (int x = center.x - radius; x <= center.x + radius; x++)
        {
            for (int y = center.y - radius; y <= center.y + radius; y++)
            {
                var coord = new Vector2Int(x, y);
                if (config.IsValidCoord(coord))
                    result.Add(coord);
            }
        }

        return result;
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

    private void ParkAgentsForChunk(Vector2Int coord)
    {
        if (!loadedScenes.TryGetValue(coord, out var scene) || !scene.IsValid() || !scene.isLoaded)
            return;

        if (!parkedAgentsByChunk.TryGetValue(coord, out var agents))
        {
            agents = new List<NavMeshAgent>();
            parkedAgentsByChunk[coord] = agents;
        }

        agents.RemoveAll(agent => agent == null);

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var agent in root.GetComponentsInChildren<NavMeshAgent>(true))
            {
                if (agent == null)
                    continue;

                if (!agents.Contains(agent))
                    agents.Add(agent);

                ParkAgent(agent);
            }
        }
    }

    private void ParkAgent(NavMeshAgent agent)
    {
        if (!parkedAgentPositions.ContainsKey(agent))
            parkedAgentPositions[agent] = agent.transform.position;

        agent.enabled = false;

        if (agent.TryGetComponent<Rigidbody>(out var rb))
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.Sleep();
        }
    }

    private void ParkAgentsForAllLoadedChunks()
    {
        foreach (var coord in loadedScenes.Keys.ToList())
            ParkAgentsForChunk(coord);
    }

    private void ParkAgentsGlobally()
    {
        globallyParkedAgents.RemoveAll(agent => agent == null);

#if UNITY_2023_1_OR_NEWER
        var agents = FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var agents = FindObjectsOfType<NavMeshAgent>(true);
#endif

        var alreadyTracked = new HashSet<NavMeshAgent>(parkedAgentsByChunk.Values.SelectMany(l => l));

        foreach (var agent in agents)
        {
            if (agent == null)
                continue;

            // Skip agents already managed per-chunk to avoid double-activation.
            if (alreadyTracked.Contains(agent))
                continue;

            if (!globallyParkedAgents.Contains(agent))
                globallyParkedAgents.Add(agent);

            ParkAgent(agent);
        }
    }

    private void ReleaseParkedAgents()
    {
        foreach (var kvp in parkedAgentsByChunk.ToList())
        {
            var remaining = new List<NavMeshAgent>();

            foreach (var agent in kvp.Value)
            {
                if (agent == null)
                    continue;

                if (!TryActivateAgent(agent))
                    remaining.Add(agent);
            }

            if (remaining.Count > 0)
                parkedAgentsByChunk[kvp.Key] = remaining;
            else
                parkedAgentsByChunk.Remove(kvp.Key);
        }

        if (globallyParkedAgents.Count == 0)
            return;

        var remainingGlobalAgents = new List<NavMeshAgent>();
        foreach (var agent in globallyParkedAgents)
        {
            if (agent == null)
                continue;

            if (!TryActivateAgent(agent))
                remainingGlobalAgents.Add(agent);
        }

        globallyParkedAgents.Clear();
        globallyParkedAgents.AddRange(remainingGlobalAgents);
    }

    private bool TryActivateAgent(NavMeshAgent agent)
    {
        Vector3 sampleOrigin = parkedAgentPositions.TryGetValue(agent, out var cached)
            ? cached
            : agent.transform.position;
        float sampleDistance = Mathf.Max(agent.radius * 4f, agent.height * 2f, parkedAgentActivationDistance);

        if (!NavMesh.SamplePosition(sampleOrigin, out var hit, sampleDistance, NavMesh.AllAreas))
        {
            Debug.LogWarning($"[WorldStreamer] Failed to activate NavMeshAgent '{agent.name}' near {sampleOrigin} (distance {sampleDistance:0.##}).");
            return false;
        }

        agent.transform.position = hit.position;
        agent.enabled = true;

        // Warp snaps the agent's internal state to the navmesh position.
        // Must be called after enabled=true or it is a no-op.
        if (agent.isOnNavMesh)
            agent.Warp(hit.position);

        if (agent.TryGetComponent<Rigidbody>(out var rb))
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        parkedAgentPositions.Remove(agent);

        return true;
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
        navMeshRebuildDelay = Mathf.Max(0.05f, navMeshRebuildDelay);
        parkedAgentActivationDistance = Mathf.Max(1f, parkedAgentActivationDistance);
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || config == null || config.chunks == null) return;
    }
#endif

}
