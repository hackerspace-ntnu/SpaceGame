// Who owns the storms.
//
// The server decides when a storm is born and writes a thirty-byte record. Everything after that
// is arithmetic: every machine, server and client alike, recomputes where the storm is from the
// record plus the shared clock. No position updates, no intensity updates, and a client that
// joins mid-storm gets the record from the NetworkList and lands in exactly the same weather as
// everyone else.
//
// Put this on the same GameObject as NetworkGameManager: it needs the NetworkObject that is
// already there, and a second scene NetworkObject is a liability in this project.
//
// Offline — no session at all, an EditMode test, the main menu — the same code runs against a
// plain list instead of the NetworkList. That is the degradation contract NetMessaging set: a
// system nobody has networked yet must still work, locally, rather than throw.
using System.Collections.Generic;
using SpaceGame.Core;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    /// <summary>A storm record resolved to this frame. What every consumer actually reads.</summary>
    public readonly struct ResolvedStorm
    {
        public readonly int Id;
        public readonly SandstormProfile Profile;
        public readonly StormFootprint Footprint;
        public readonly float Intensity;

        public ResolvedStorm(int id, SandstormProfile profile, in StormFootprint footprint, float intensity)
        {
            Id = id;
            Profile = profile;
            Footprint = footprint;
            Intensity = intensity;
        }

        /// <summary>Density at a point, shape and lifecycle combined. 0 to 1.</summary>
        public float DensityAt(Vector3 worldPos) => StormShape.Density(Footprint, worldPos) * Intensity;

        public Vector3 Center => new Vector3(Footprint.Center.x, Footprint.BaseY, Footprint.Center.y);
    }

    [DefaultExecutionOrder(-90)] // resolve storms before anything reads them this frame
    [DisallowMultipleComponent]
    public class SandstormManager : NetworkBehaviour
    {
        public static SandstormManager Instance { get; private set; }

        [Tooltip("Every kind of storm this world can produce. Required — without it a storm has " +
                 "no way to cross the network.")]
        [SerializeField] private SandstormCatalog catalog;

        [Tooltip("Hard ceiling on storms alive at once. Each one costs a silhouette mesh and a " +
                 "term in every density query, and more than a handful is not readable anyway.")]
        [SerializeField, Range(1, 8)] private int maxConcurrent = 4;

        // The write model when there is a session: the server appends, everyone mirrors. Small,
        // and written only at birth and death, so it never shows up in a bandwidth profile.
        private readonly NetworkList<StormInstance> replicated = new NetworkList<StormInstance>();

        // The read model. Mirrors the NetworkList when spawned and holds the storms directly when
        // offline, so every reader below has exactly one list to care about.
        private readonly List<StormInstance> records = new List<StormInstance>();

        private readonly List<ResolvedStorm> resolved = new List<ResolvedStorm>();
        private readonly List<int> expired = new List<int>();
        private int resolvedOnFrame = -1;
        private int nextId = 1;

        public SandstormCatalog Catalog => catalog;

        /// <summary>Storms resolved to this frame, strongest first is NOT guaranteed — query by point.</summary>
        public IReadOnlyList<ResolvedStorm> Resolved
        {
            get
            {
                EnsureResolved();
                return resolved;
            }
        }

        /// <summary>True when this machine is allowed to start and end storms.</summary>
        public bool HasAuthority => !Network.IsNetworked || IsServer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[Sandstorm] {name} is a second SandstormManager; " +
                                 $"{Instance.name} already owns the weather. Disabling this one.", this);
                enabled = false;
                return;
            }

            Instance = this;

            if (catalog == null)
                Debug.LogError($"[Sandstorm] {name} has no catalog. No storm can be spawned.", this);
        }

        public override void OnNetworkSpawn()
        {
            replicated.OnListChanged += OnReplicatedChanged;

            // Storms that started before the session came up — scene zones, which register as soon
            // as they are enabled — live only in the local list. Promote them, or the server is
            // the only machine that has them.
            if (IsServer)
                PromoteLocalRecords();

            // A late joiner's list arrives filled, and the callback only fires on later edits — so
            // mirror once here or that player stands in clear air while everyone else is blinded.
            MirrorFromNetwork();
        }

        private void PromoteLocalRecords()
        {
            // Snapshotted first: every Add fires OnListChanged, which rebuilds `records` from the
            // network list underneath the loop. Iterating the live list promoted the first storm
            // and silently dropped every one after it.
            StormInstance[] pending = records.ToArray();
            for (int i = 0; i < pending.Length; i++)
            {
                StormInstance storm = pending[i];

                // Restamped, because the clock changed underneath it: these were timed against
                // local game time and everything from here on is timed against server time. Only
                // safe because promotion happens at session start, when every storm is seconds old.
                storm.StartTime = Sandstorms.Now;
                replicated.Add(storm);
            }
        }

        public override void OnNetworkDespawn()
        {
            replicated.OnListChanged -= OnReplicatedChanged;
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            base.OnDestroy();
        }

        private void OnReplicatedChanged(NetworkListEvent<StormInstance> _) => MirrorFromNetwork();

        private void MirrorFromNetwork()
        {
            records.Clear();
            foreach (StormInstance storm in replicated)
                records.Add(storm);

            InvalidateResolved();
        }

        /// <summary>
        /// Starts a storm. Server-only; a client calling this is a bug, not a request, so it says
        /// so rather than pretending to work.
        /// </summary>
        /// <param name="id">The new storm's id, for despawning it again.</param>
        /// <param name="duration">Seconds to live. Negative uses the profile's own duration; zero parks it forever.</param>
        /// <param name="seed">Zero picks one, which is the normal case.</param>
        public bool TrySpawn(SandstormProfile profile, Vector3 origin, float headingDegrees,
                             out int id, float duration = -1f, uint seed = 0u)
        {
            id = 0;

            if (!HasAuthority)
            {
                Debug.LogWarning("[Sandstorm] Only the server starts storms; this call was ignored.", this);
                return false;
            }

            if (profile == null || catalog == null)
                return false;

            // Capacity before the catalog lookup: SandstormZone retries every frame until it gets
            // in, and a full world should cost that retry a comparison rather than a list scan.
            if (records.Count >= maxConcurrent)
                return false;

            int profileIndex = catalog.IndexOf(profile);
            if (profileIndex < 0)
            {
                Debug.LogError($"[Sandstorm] '{profile.name}' is not in catalog '{catalog.name}', so it " +
                               "cannot be sent to clients. Add it to the catalog.", this);
                return false;
            }

            var storm = new StormInstance
            {
                Id = nextId++,
                ProfileIndex = (byte)profileIndex,
                Seed = seed != 0u ? seed : (uint)Random.Range(1, int.MaxValue),
                Origin = new Vector2(origin.x, origin.z),
                HeadingDegrees = Mathf.Repeat(headingDegrees, 360f),
                StartTime = Sandstorms.Now,
                Duration = duration < 0f ? profile.duration : duration,
            };

            id = storm.Id;
            Add(storm);
            return true;
        }

        /// <summary>Ends a storm early. Server-only.</summary>
        public bool Despawn(int id)
        {
            if (!HasAuthority)
                return false;

            if (IsSpawned)
            {
                for (int i = 0; i < replicated.Count; i++)
                {
                    if (replicated[i].Id != id)
                        continue;

                    replicated.RemoveAt(i);
                    return true;
                }

                return false;
            }

            int index = records.FindIndex(storm => storm.Id == id);
            if (index < 0)
                return false;

            records.RemoveAt(index);
            InvalidateResolved();
            return true;
        }

        private void Add(StormInstance storm)
        {
            if (IsSpawned)
            {
                // Mirroring happens through OnListChanged, which fires on the server too — one
                // path in, so the server can never end up holding a list the clients do not have.
                replicated.Add(storm);
                return;
            }

            records.Add(storm);
            InvalidateResolved();
        }

        private void Update()
        {
            if (!HasAuthority)
                return;

            // Collected before removing any: Despawn rebuilds `records` through the list callback,
            // so reading it by index across a removal is asking to skip or repeat an entry.
            double now = Sandstorms.Now;
            expired.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].IsExpired(now))
                    expired.Add(records[i].Id);
            }

            for (int i = 0; i < expired.Count; i++)
                Despawn(expired[i]);
        }

        private void InvalidateResolved() => resolvedOnFrame = -1;

        // Resolving is O(storms) and every damage tick, every agent and every render layer wants
        // the answer, so it happens once a frame and everyone reads the same snapshot. Sharing the
        // snapshot also means the fog you see and the damage you take are computed from one
        // position, not from two evaluations a few milliseconds apart.
        private void EnsureResolved()
        {
            if (resolvedOnFrame == Time.frameCount)
                return;

            resolvedOnFrame = Time.frameCount;
            resolved.Clear();

            if (catalog == null)
                return;

            double now = Sandstorms.Now;
            for (int i = 0; i < records.Count; i++)
            {
                StormInstance record = records[i];
                SandstormProfile profile = catalog.Get(record.ProfileIndex);
                if (profile == null)
                    continue;

                StormState state = record.Evaluate(profile, now);
                if (state.Intensity <= 0f)
                    continue;

                resolved.Add(new ResolvedStorm(record.Id, profile,
                                               profile.Footprint(state.Center, state.Heading),
                                               state.Intensity));
            }
        }
    }
}
