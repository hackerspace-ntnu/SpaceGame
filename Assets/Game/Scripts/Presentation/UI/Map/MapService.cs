using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Holds the live list of map markers and the set of chunks the local player
    /// has revealed. Polls the player this peer is driving and reveals chunks
    /// within the streaming load radius.
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

        // POIs a previous session had already found, by id.
        //
        // Kept apart from poisById because the two are populated at different times and the gap
        // between them is the whole problem: a saved world knows the player found the wreck long
        // before the wreck's chunk streams in and registers a marker for it. Without somewhere to
        // put the fact in the meantime, every POI outside the chunks loaded at spawn would come back
        // as fog no matter what the record said.
        private readonly HashSet<string> discoveredPois = new();

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

            // Not a tag search: every player in the session is tagged "Player", and revealing the
            // map around an arbitrary one means a client uncovers the terrain the host is walking
            // through instead of its own. This service lives in the persistent scene rather than
            // under a player, so there is no parent chain to read — it asks the session who this
            // peer is driving. Re-asked while null, never cached as null: the player object arrives
            // asynchronously, well after this component's Start.
            if (localPlayer == null)
                localPlayer = GameplayMenuScope.LocalPlayerTransform;

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

            // A POI the player had already found stays found. Applied here rather than only at
            // restore time because the marker for a distant ruin is created whenever its chunk
            // finally streams in, which can be an hour after the world was loaded.
            if (discoveredPois.Contains(id)) m.discovered = true;

            poisById[id] = m;
            return m;
        }

        public bool HasPOI(string id) => !string.IsNullOrEmpty(id) && poisById.ContainsKey(id);

        /// <summary>Every POI registered under a stable id, so a save can write down what was found.</summary>
        public IReadOnlyDictionary<string, Marker> POIs => poisById;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Replaces the revealed set outright, because "revealed" is cumulative and a restore that
        /// only added would leave a previous world's explored chunks lit up on this one's map.
        /// Every restored chunk is announced, which is what makes the hologram redraw them.
        /// </summary>
        public void RestoreRevealedChunks(IEnumerable<Vector2Int> coords)
        {
            revealed.Clear();

            if (coords == null) return;

            foreach (Vector2Int c in coords)
                if (revealed.Add(c)) OnChunkRevealed?.Invoke(c);
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Re-creates a POI the record knows about, or marks the live one found. Both halves matter:
        /// a POI whose chunk is loaded is already registered by its <c>MapPOI</c> and only needs the
        /// discovery flag, while one whose chunk is nowhere near the player has no marker at all and
        /// would otherwise vanish from a map it was on when the game was saved.
        /// </summary>
        public void RestorePOI(string id, Vector3 worldPos, MapMarkerType type, string label,
            bool requiresRevealedChunk, float discoveryRadius, bool discovered)
        {
            if (string.IsNullOrEmpty(id)) return;

            if (discovered) discoveredPois.Add(id);
            else discoveredPois.Remove(id);

            Marker m = RegisterPOI(id, worldPos, type, label, requiresRevealedChunk, discoveryRadius);
            if (m != null) m.discovered = discovered;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Forgets which POIs were found, for a load whose record says none were. Live markers are
        /// left in place — they belong to the chunks currently in memory — but drop back to fog.
        /// </summary>
        public void RestoreNothingDiscovered()
        {
            discoveredPois.Clear();

            foreach (Marker m in poisById.Values)
                if (m != null) m.discovered = false;
        }

        public void RemoveMarker(Marker marker)
        {
            if (marker == null) return;
            if (markers.Remove(marker))
                OnMarkerRemoved?.Invoke(marker);
        }
    }
}
