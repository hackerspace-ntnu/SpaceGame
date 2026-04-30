using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds the live list of map markers and the set of chunks the local player
/// has revealed. Polls the local player by tag and reveals chunks within the
/// streaming load radius.
///
/// Lives in the persistent scene. Singleton-style access via Instance.
/// </summary>
public class MapService : MonoBehaviour
{
    public static MapService Instance { get; private set; }

    [SerializeField] private WorldStreamingConfig config;
    [Tooltip("How often to check the player's position for chunk reveal.")]
    [SerializeField] private float revealPollInterval = 0.5f;
    [Tooltip("Reveal radius in chunks around the player. 1 = 3x3.")]
    [SerializeField] private int revealRadius = 1;
    [Tooltip("If true, every chunk in the grid is considered revealed at start (debug).")]
    [SerializeField] private bool revealAll;

    public WorldStreamingConfig Config => config;

    public sealed class Marker
    {
        public Transform follow;       // optional; if set, position tracks this each frame
        public Vector3 worldPosition;  // used when follow is null
        public MapMarkerType type;
        public string label;
        public bool requiresRevealedChunk;

        // Discovery state for "Hide" markers (requiresRevealedChunk == true).
        // While !discovered the hologram renders a fog cloud instead of the
        // marker itself. Negative discoveryRadius means "use the hologram's
        // global default".
        public bool discovered;
        public float discoveryRadius = -1f;

        public Vector3 GetWorldPosition() =>
            follow != null ? follow.position : worldPosition;
    }

    private readonly List<Marker> markers = new();
    private readonly Dictionary<string, Marker> poisById = new();
    private readonly HashSet<Vector2Int> revealed = new();
    private Transform localPlayer;
    private float nextPollTime;

    public event Action<Marker> OnMarkerAdded;
    public event Action<Marker> OnMarkerRemoved;
    public event Action<Vector2Int> OnChunkRevealed;

    public IReadOnlyList<Marker> Markers => markers;
    public IReadOnlyCollection<Vector2Int> RevealedChunks => revealed;
    public Transform LocalPlayer => localPlayer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (revealAll && config != null && config.chunks != null)
        {
            foreach (var c in config.chunks)
            {
                if (revealed.Add(c.gridCoord))
                    OnChunkRevealed?.Invoke(c.gridCoord);
            }
        }
    }

    private void Update()
    {
        if (config == null) return;
        if (Time.time < nextPollTime) return;
        nextPollTime = Time.time + revealPollInterval;

        if (localPlayer == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) localPlayer = go.transform;
        }
        if (localPlayer == null) return;

        var center = config.WorldToChunkCoord(localPlayer.position);
        for (int dx = -revealRadius; dx <= revealRadius; dx++)
        {
            for (int dy = -revealRadius; dy <= revealRadius; dy++)
            {
                var c = new Vector2Int(center.x + dx, center.y + dy);
                if (!config.IsValidCoord(c)) continue;
                if (revealed.Add(c)) OnChunkRevealed?.Invoke(c);
            }
        }
    }

    public bool IsChunkRevealed(Vector2Int coord) => revealed.Contains(coord);

    public Marker RegisterMarker(Transform follow, MapMarkerType type, string label = null,
        bool requiresRevealedChunk = true, float discoveryRadius = -1f)
    {
        if (follow == null) return null;
        var m = new Marker
        {
            follow = follow,
            type = type,
            label = label,
            requiresRevealedChunk = requiresRevealedChunk,
            discoveryRadius = discoveryRadius,
        };
        markers.Add(m);
        OnMarkerAdded?.Invoke(m);
        return m;
    }

    public Marker AddStaticMarker(Vector3 worldPos, MapMarkerType type, string label = null,
        bool requiresRevealedChunk = true, float discoveryRadius = -1f)
    {
        var m = new Marker
        {
            worldPosition = worldPos,
            type = type,
            label = label,
            requiresRevealedChunk = requiresRevealedChunk,
            discoveryRadius = discoveryRadius,
        };
        markers.Add(m);
        OnMarkerAdded?.Invoke(m);
        return m;
    }

    /// <summary>
    /// Registers a static POI with a unique ID — used by `MapPOI` components so
    /// that re-enabling on chunk reload doesn't create duplicates. The marker
    /// persists for the rest of the session even if the GameObject is destroyed.
    /// </summary>
    public Marker RegisterPOI(string id, Vector3 worldPos, MapMarkerType type, string label = null,
        bool requiresRevealedChunk = false, float discoveryRadius = -1f)
    {
        if (string.IsNullOrEmpty(id)) return AddStaticMarker(worldPos, type, label, requiresRevealedChunk, discoveryRadius);
        if (poisById.TryGetValue(id, out var existing)) return existing;
        var m = AddStaticMarker(worldPos, type, label, requiresRevealedChunk, discoveryRadius);
        poisById[id] = m;
        return m;
    }

    public bool HasPOI(string id) => !string.IsNullOrEmpty(id) && poisById.ContainsKey(id);

    public void RemoveMarker(Marker marker)
    {
        if (marker == null) return;
        if (markers.Remove(marker))
            OnMarkerRemoved?.Invoke(marker);
    }
}
