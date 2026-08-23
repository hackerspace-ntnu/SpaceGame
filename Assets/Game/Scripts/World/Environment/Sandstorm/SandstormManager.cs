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

        /// <summary>
        /// The weather clock's anchor, as the server states it.
        ///
        /// <para>
        /// The storm records replicate themselves through the list above, and their StartTimes are
        /// readings of a clock the clients have to be able to evaluate. A fresh session's anchor is
        /// derivable everywhere — it is zero against a clock whose origin is the session — but a
        /// LOADED one is not: the server re-states the anchor from a file no client has, and no
        /// amount of shared-clock arithmetic bridges that. Two numbers, sent when they move. Same
        /// shape and same reason as <c>SkyNetwork</c> carrying the sun's anchor.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<StormClockAnchor> clockAnchor =
            new NetworkVariable<StormClockAnchor>();

        public SandstormCatalog Catalog => catalog;

        /// <summary>Every live storm as a record. What the save system writes out.</summary>
        public IReadOnlyList<StormInstance> Records => records;

        /// <summary>The id the next storm would be given.</summary>
        public int NextId => nextId;

        /// <summary>
        /// Raised on the machine whose records were just replaced by a restore.
        ///
        /// <see cref="SandstormZone"/> listens: a zone that had already registered its storm holds an
        /// id from before the load, and the restored list either does not contain it or contains the
        /// SAVED copy of the same storm under a different id. Either way the zone has to look again.
        /// </summary>
        public static event System.Action RecordsRestored;

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
            clockAnchor.OnValueChanged += OnClockAnchorChanged;

            if (IsServer)
            {
                // Anything that moves the weather clock from here on gets published. Subscribed
                // before the promotion below, because reading Sandstorms.WeatherTime during it can itself
                // move the anchor — the clock source has just changed identity from game time to
                // this session's server time.
                StormClock.AnchorMoved += PublishClockAnchor;

                // Read once before publishing. The clock is lazy — the first read is what settles
                // its anchor against this session's server time — so publishing without it would
                // send the unset zeroes it holds until something asks the time.
                _ = Sandstorms.WeatherTime;
                PublishClockAnchor();

                // Storms that started before the session came up — scene zones, which register as
                // soon as they are enabled — live only in the local list. Promote them, or the
                // server is the only machine that has them.
                PromoteLocalRecords();
            }
            else
            {
                // A late joiner's anchor arrives filled and OnValueChanged only fires on later
                // edits, so adopt it once here. Before mirroring the records, because every record
                // is timed against it.
                AdoptClockAnchor(clockAnchor.Value);
            }

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
                // No longer restamped. These were timed against the weather clock, and the weather
                // clock is anchored — StormClock re-states its anchor across the change of clock
                // source, so the reading these StartTimes were taken against still means what it
                // meant. Restamping them here was only ever safe because promotion happened at
                // session start with every storm seconds old, and it is precisely what made a
                // storm restored from a save start over.
                replicated.Add(pending[i]);
            }
        }

        public override void OnNetworkDespawn()
        {
            replicated.OnListChanged -= OnReplicatedChanged;
            clockAnchor.OnValueChanged -= OnClockAnchorChanged;
            StormClock.AnchorMoved -= PublishClockAnchor;
        }

        // ── The weather clock, across the wire ───────────────────────────────────

        private void PublishClockAnchor()
        {
            if (!IsSpawned || !IsServer || !StormClock.HasAnchor) return;

            StormClock.ReadAnchor(out double weather, out double clock);

            var anchor = new StormClockAnchor { Set = true, Weather = weather, Clock = clock };
            if (clockAnchor.Value.Equals(anchor)) return;

            clockAnchor.Value = anchor;
        }

        private void OnClockAnchorChanged(StormClockAnchor previous, StormClockAnchor current) =>
            AdoptClockAnchor(current);

        /// <summary>
        /// Takes the server's statement of what time the weather is.
        ///
        /// Clients only. The server is the machine the value came from, and applying its own echo
        /// would re-state the anchor, which raises <c>AnchorMoved</c>, which publishes again.
        /// </summary>
        private void AdoptClockAnchor(StormClockAnchor anchor)
        {
            if (IsServer || !anchor.Set) return;

            StormClock.AnchorTo(anchor.Weather, anchor.Clock);
            InvalidateResolved();
        }

        public override void OnDestroy()
        {
            // Belt and braces beside OnNetworkDespawn: StormClock is static and would otherwise hold
            // a delegate onto a destroyed manager for the rest of the process.
            StormClock.AnchorMoved -= PublishClockAnchor;

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
                StartTime = Sandstorms.WeatherTime,
                Duration = duration < 0f ? profile.duration : duration,
            };

            id = storm.Id;
            Add(storm);
            return true;
        }

        /// <summary>
        /// Adopts a storm that is already running and matches what the caller would have spawned.
        ///
        /// <para>
        /// For <see cref="SandstormZone"/> after a load. A zone re-registers its storm on every
        /// startup, and a world restored with that zone's storm already in it would end up with two
        /// of them — the saved one and the freshly rolled one — sitting on top of each other for the
        /// rest of the session. A zone storm is fully determined by its profile, its fixed seed and
        /// its position, so the saved record can be recognised and taken back over instead.
        /// </para>
        /// <para>
        /// Only for a non-zero seed. A zero seed means "pick one", so two spawns from it are
        /// genuinely different storms and there is nothing to match on.
        /// </para>
        /// </summary>
        public bool TryAdopt(SandstormProfile profile, Vector3 origin, uint seed, out int id)
        {
            id = 0;

            if (profile == null || catalog == null || seed == 0u) return false;

            int profileIndex = catalog.IndexOf(profile);
            if (profileIndex < 0) return false;

            var wanted = new Vector2(origin.x, origin.z);

            for (int i = 0; i < records.Count; i++)
            {
                StormInstance record = records[i];

                if (record.ProfileIndex != (byte)profileIndex || record.Seed != seed) continue;
                if ((record.Origin - wanted).sqrMagnitude > 0.01f) continue;

                id = record.Id;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Restore-only. Replaces every live storm with a saved set. Called by the save system; do
        /// not call from gameplay.
        ///
        /// <para>
        /// <paramref name="nextId"/> matters as much as the storms do. Ids are handed out from a
        /// counter that restarts at 1 every session, so a restored storm 3 and a later-rolled storm
        /// 3 would be the same storm to every visual layer, to the director's "is my storm still
        /// running" test and to <see cref="Despawn"/>.
        /// </para>
        /// </summary>
        public bool RestoreRecords(IReadOnlyList<StormInstance> storms, int nextId)
        {
            if (!HasAuthority) return false;

            if (IsSpawned)
            {
                replicated.Clear();
                if (storms != null)
                {
                    for (int i = 0; i < storms.Count; i++)
                        replicated.Add(storms[i]);
                }
            }
            else
            {
                records.Clear();
                if (storms != null)
                {
                    for (int i = 0; i < storms.Count; i++)
                        records.Add(storms[i]);
                }

                InvalidateResolved();
            }

            // Never below where it already is: a zone that registered before the restore landed has
            // already consumed ids, and reusing them would collide with storms still in the list.
            this.nextId = Mathf.Max(nextId, this.nextId);

            RecordsRestored?.Invoke();
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
            double now = Sandstorms.WeatherTime;
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

            double now = Sandstorms.WeatherTime;
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
