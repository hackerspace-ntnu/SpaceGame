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

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;

    /// <summary>
    /// Fired on the server once all initial chunks (around spawn points) are loaded.
    /// Subscribe to this before spawning players.
    /// </summary>
    public event Action OnInitialChunksReady;

    public bool InitialChunksLoaded => initialChunksLoaded;

    private enum ChunkState { NotLoaded, Loading, Loaded, Unloading }

    private readonly Dictionary<Vector2Int, ChunkState> chunkStates = new();
    private readonly Dictionary<Vector2Int, Scene> loadedScenes = new();
    private readonly Dictionary<Vector2Int, Terrain> loadedTerrains = new();
    private readonly Dictionary<Vector2Int, float> unloadTimers = new();

    // Queue for sequential scene operations (Netcode only allows one at a time)
    private readonly Queue<SceneOperation> operationQueue = new();
    private bool operationInProgress;

    private bool initialChunksLoaded;
    private float updateInterval = 0.5f;
    private float nextUpdateTime;

    private bool navMeshDirty;
    private float navMeshRebuildTime;
    private NavMeshDataInstance navMeshDataInstance;
    private AsyncOperation navMeshBuildOperation;

    private struct SceneOperation
    {
        public enum Type { Load, Unload }
        public Type OperationType;
        public Vector2Int Coord;
        public Action OnComplete;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (config == null)
        {
            Debug.LogError("[WorldStreamer] Missing WorldStreamingConfig reference.");
            return;
        }

        NetworkManager.Singleton.SceneManager.OnSceneEvent += HandleSceneEvent;
        InitializeChunkStates();
        LoadInitialChunks();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleSceneEvent;

        if (navMeshDataInstance.valid)
            navMeshDataInstance.Remove();

        chunkStates.Clear();
        loadedScenes.Clear();
        loadedTerrains.Clear();
        unloadTimers.Clear();
        operationQueue.Clear();
        operationInProgress = false;
    }

    private void Update()
    {
        if (!IsServer || !initialChunksLoaded) return;

        if (navMeshDirty && Time.time >= navMeshRebuildTime)
        {
            navMeshDirty = false;
            RebuildNavMesh();
        }

        if (Time.time < nextUpdateTime) return;
        nextUpdateTime = Time.time + updateInterval;

        UpdateChunkLoading();
    }

    private void InitializeChunkStates()
    {
        if (config == null || config.chunks == null) return;

        foreach (var chunk in config.chunks)
        {
            chunkStates[chunk.gridCoord] = ChunkState.NotLoaded;
        }
    }

    private void LoadInitialChunks()
    {
        var centerCoord = new Vector2Int(config.gridDimensions.x / 2, config.gridDimensions.y / 2);
        var chunksToLoad = GetChunksInRadius(centerCoord, config.loadRadius);

        if (chunksToLoad.Count == 0)
        {
            Debug.LogWarning("[WorldStreamer] No chunks to load. Is the config set up?");
            MarkInitialChunksLoaded();
            return;
        }

        int remaining = chunksToLoad.Count;

        foreach (var coord in chunksToLoad)
        {
            EnqueueLoad(coord, () =>
            {
                remaining--;
                if (remaining <= 0)
                {
                    Debug.Log("[WorldStreamer] Initial chunks loaded. Ready for player spawn.");
                    MarkInitialChunksLoaded();
                }
            });
        }
    }

    public void PreloadChunksAroundPosition(Vector3 worldPos, Action onComplete = null)
    {
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
        if (config == null)
        {
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

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var playerPos = client.PlayerObject.transform.position;
            var playerChunk = config.WorldToChunkCoord(playerPos);
            var nearby = GetChunksInRadius(playerChunk, config.loadRadius);

            foreach (var coord in nearby)
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
        Debug.Log($"[WorldStreamer] Loading chunk {op.Coord} ({sceneName})");

        var status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[WorldStreamer] Failed to load {sceneName}: {status}");
            chunkStates[op.Coord] = ChunkState.NotLoaded;
            FinishOperation(op.OnComplete);
            return;
        }

        // Completion is handled in HandleSceneEvent
        pendingCallback = op.OnComplete;
        pendingCoord = op.Coord;
        pendingSceneName = sceneName;
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

        var status = NetworkManager.Singleton.SceneManager.UnloadScene(scene);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[WorldStreamer] Failed to unload chunk {op.Coord}: {status}");
            chunkStates[op.Coord] = ChunkState.Loaded;
            FinishOperation(op.OnComplete);
            return;
        }

        pendingCallback = op.OnComplete;
        pendingCoord = op.Coord;
        pendingSceneName = null; // unload doesn't match by name
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
            RefreshTerrainNeighborsAround(pendingCoord);
            ScheduleNavMeshRebuild();
            Debug.Log($"[WorldStreamer] Chunk {pendingCoord} loaded");
            FinishOperation(pendingCallback);
        }
        else if (sceneEvent.SceneEventType == SceneEventType.UnloadEventCompleted
                 && pendingSceneName == null)
        {
            loadedTerrains.Remove(pendingCoord);
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
                // Chunk scenes can carry stale baked positions from generation time.
                terrain.transform.position = expectedPosition;

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
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || config == null || config.chunks == null) return;

        foreach (var chunk in config.chunks)
        {
            bool isLoaded = chunkStates.TryGetValue(chunk.gridCoord, out var state)
                         && state == ChunkState.Loaded;

            Gizmos.color = isLoaded
                ? new Color(0f, 1f, 0f, 0.15f)
                : new Color(1f, 0f, 0f, 0.08f);

            Gizmos.DrawCube(chunk.worldBounds.center, chunk.worldBounds.size);

            Gizmos.color = isLoaded
                ? new Color(0f, 1f, 0f, 0.6f)
                : new Color(1f, 1f, 1f, 0.2f);

            Gizmos.DrawWireCube(chunk.worldBounds.center, chunk.worldBounds.size);
        }
    }
#endif
}
