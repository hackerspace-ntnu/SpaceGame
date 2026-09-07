// Every coat in the world, and the one question movers ask about them.
//
// The world-facing twin of StatusReceiver: a status hangs on a body and has a receiver per body, a
// coat hangs on a SURFACE and there is no surface to hang a component on — so the coats live in one
// field, once per session, and a patch is a record in it rather than a flag written onto terrain.
//
// The server sprays and expires; every machine draws. A patch is small enough to send as a point, a
// radius, a kind and a lifetime, so after the one message every machine runs the same clock over the
// same numbers and nothing is sent per frame. Ordinary expiry sends nothing at all: every machine
// already knows when the patch runs out.
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// The coats currently on the world's surfaces, and the grip answer that follows from them.
    ///
    /// <para>
    /// <b>Put this on the <c>NetworkGameManager</c> prefab</b>, beside <c>ChatNetwork</c> and
    /// <c>SandstormManager</c>. That object already carries the <see cref="NetworkObject"/> and
    /// <c>NetRelay</c> a coat message needs, it lives in the persistent scene beneath every
    /// gameplay scene, and it spawns on every peer before the first player does — which is what
    /// makes a client's handler exist before the first <c>CoatSprayed</c> arrives. A message whose
    /// entity has nothing subscribed is dropped without a word, so a field that only the host had
    /// would be a world slicked for the host and dry for everybody else.
    /// </para>
    /// <para>
    /// <b>It answers <see cref="IGripSource"/>; it never pushes.</b> A mover reads the multiplier on
    /// the machine that owns it, on the frame it reads it — which is what keeps a slicked player's
    /// own movement owner-authoritative (GDC-L1-MP-0004) and puts the skid on the frame they are
    /// looking at (GDC-L1-FEEL-0002).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SurfaceCoatSaveable))]
    public sealed class SurfaceCoatField : MonoBehaviour, IGripSource
    {
        // One concrete field per kind rather than a polymorphic list, for StatusReceiver's reasons:
        // every number below gets an Inspector row on the object that carries it, and no type name
        // ends up in a prefab where a rename would break it.
        [Header("Coats")]
        [SerializeField] private SlickCoat slick = new SlickCoat();
        [SerializeField] private IceCoat ice = new IceCoat();
        [SerializeField] private WetCoat wet = new WetCoat();

        [Header("Field")]
        [Tooltip("How close to an existing patch's centre a new dab of the same kind has to land " +
                 "to grow that patch instead of laying another one, as a share of its radius.\n\n" +
                 "This is what stops a held spray carpeting a chunk in patches: at 0.5 a can " +
                 "swept across the ground lays a chain of overlapping puddles rather than one " +
                 "colliderless patch per dab, fifteen times a second.")]
        [SerializeField, Range(0.05f, 1f)] private float mergeShare = 0.5f;

        [Tooltip("Ceiling on how wide merging may grow one patch, in metres. Without it a player " +
                 "walking backwards while spraying grows a single patch across a chunk.")]
        [SerializeField] private float maxPatchRadius = 3f;

        [Tooltip("Hard ceiling on patches alive at once, anywhere in the world. Each one costs a " +
                 "decal draw and a term in every grip query; past this the oldest coat that is " +
                 "going to expire anyway is dropped to make room.")]
        [SerializeField, Range(8, 512)] private int maxPatches = 192;

        /// <summary>
        /// Live patches, in the order the server minted them — so the last match walking backwards
        /// is the newest, which is the one that wins where two of them cross.
        /// </summary>
        private readonly List<SurfaceCoatPatch> patches = new List<SurfaceCoatPatch>();

        /// <summary>
        /// Saved coats whose chunk is not loaded. Server-side: the store's invariant is that a
        /// thing is either live in a loaded scene or in a record, never neither and never both.
        /// </summary>
        private readonly List<SurfaceCoatRecord> dormant = new List<SurfaceCoatRecord>();

        private SurfaceCoatBehaviour[] behaviours;
        private Transform patchRoot;
        private WorldStreamer streamer;
        private int nextId = 1;
        private bool subscribed;
        private bool answeringGrip;
        private bool watchingForJoiners;
        private bool warnedMissingFilm;

        /// <summary>
        /// The session's coat field, or null when there is none. Everything outside this system
        /// goes through <see cref="SurfaceCoats"/> rather than reaching for this.
        /// </summary>
        public static SurfaceCoatField Instance { get; private set; }

        /// <summary>
        /// Does this machine decide what the world's surfaces are made of? Spraying, breaking and
        /// expiring are its alone; drawing the film is everybody's.
        ///
        /// True offline and in an interior nobody has networked, which is what keeps a coat working
        /// exactly as it would have before any of this existed.
        /// </summary>
        public bool Decides => Network.Simulates(this);

        /// <summary>Every live patch, newest last. Read-only to everything outside this file.</summary>
        public IReadOnlyList<SurfaceCoatPatch> Patches => patches;

        // Statics outlive the world, the session and play mode, and enter-play-mode options are on
        // in this project — so a stale Instance would answer for a field in a world that is gone.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => Instance = null;

        // ─────────── life cycle ───────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Switched off rather than merely ignored: left enabled it would subscribe to the
                // coat messages as well and lay a second, invisible copy of every patch in the
                // world. Awake runs before OnEnable, so this is early enough to stop that.
                enabled = false;

                Debug.LogWarning($"[Coats] A second SurfaceCoatField on '{name}' — the session has " +
                                 "one field, and this one has been switched off.", this);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            EnsureSubscribed();

            WorldStreamer.OnChunkLoaded += OnChunkLoaded;
            WorldStreamer.OnChunkWillUnload += OnChunkWillUnload;
        }

        private void OnDisable()
        {
            WorldStreamer.OnChunkLoaded -= OnChunkLoaded;
            WorldStreamer.OnChunkWillUnload -= OnChunkWillUnload;

            if (subscribed)
            {
                this.NetOff(NetMsg.CoatSprayed, OnCoatSprayed);
                this.NetOff(NetMsg.CoatBroken, OnCoatBroken);
                subscribed = false;
            }

            StopWatchingForJoiners();

            // Everything the field took gets given back. A field torn down mid-session must not
            // leave an answer in a static list for a world that no longer exists.
            for (int i = patches.Count - 1; i >= 0; i--)
                if (patches[i] != null) Destroy(patches[i].gameObject);

            patches.Clear();
            StopAnsweringIfIdle();

            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            for (int i = patches.Count - 1; i >= 0; i--)
            {
                SurfaceCoatPatch patch = patches[i];

                // A patch whose GameObject went with a scene load. Dropping it here rather than
                // guarding every read is what keeps the grip query free of null checks.
                if (patch == null)
                {
                    patches.RemoveAt(i);
                    continue;
                }

                patch.Tick(deltaTime);

                // Expiry is NOT announced, and that is the one asymmetry with the status system:
                // every machine was told the lifetime when the patch was sprayed and reaches the
                // same moment on its own, so a message per expiring patch would be traffic for a
                // fact nobody is missing.
                if (patch.Expired) Remove(i);
            }

            // A loaded world stages its global payload before any chunk has hydrated, and the
            // machine reading it may not be the server yet either — so a restored coat can arrive
            // before there is anywhere to put it or anyone allowed to put it there. Retried here
            // rather than left to the chunk events alone, because a world with no streaming grid at
            // all (an interior, the arena) never raises one, and the cost is a dictionary lookup
            // per waiting record in the only worlds that have any.
            if (dormant.Count > 0) ResolveDormant();

            // A field holding restored ice from before the session started has patches but has
            // never seen a spray, so nothing has asked it to watch for joiners yet — and a client
            // arriving after that would never be told about a bridge everyone else is walking on.
            if (patches.Count > 0) BeginWatchingForJoiners();
        }

        // ─────────── what the world asks ───────────

        /// <summary>
        /// How much grip a body standing at <paramref name="groundPoint"/> has, as far as the
        /// coats are concerned.
        ///
        /// <para>
        /// <b>The NEWEST patch wins where two of them cross</b>, rather than the slipperiest —
        /// freezing a wet patch makes it ice, and rain falling on a slick makes it wet, so the last
        /// thing done to a piece of ground is what that ground is now. Resolved by patch ID, which
        /// the server mints in order, so every machine reaches the same answer without a clock.
        /// </para>
        /// <para>
        /// That is a different question from the one <see cref="GroundGrip"/> answers ACROSS
        /// sources, which is the smallest of them: a coat and a body's own Slick status are two
        /// unrelated reasons to have no purchase, and neither overrules the other. Newest-wins
        /// applies only within this field, where the patches describe one surface.
        /// </para>
        /// </summary>
        public float GripFor(GameObject body, Vector3 groundPoint) =>
            body != null ? GripAt(groundPoint) : GroundGrip.Full;

        /// <summary>
        /// The coats' grip at a point, with no body in the question. What
        /// <see cref="GripFor"/> answers, for anything that wants to know about the SURFACE rather
        /// than about a particular mover on it.
        /// </summary>
        public float GripAt(Vector3 groundPoint)
        {
            SurfaceCoatPatch patch = Newest(groundPoint);
            return patch != null ? patch.Coat.Grip : GroundGrip.Full;
        }

        /// <summary>The coat on the surface at <paramref name="point"/>, or null for bare ground.</summary>
        public SurfaceCoatKind? KindAt(Vector3 point)
        {
            SurfaceCoatPatch patch = Newest(point);
            return patch != null ? patch.Kind : (SurfaceCoatKind?)null;
        }

        /// <summary>
        /// Is there a patch of <paramref name="kind"/> under <paramref name="point"/>?
        ///
        /// Deliberately not "is the newest coat this kind": the Cryo Sprayer asks whether the
        /// ground has been rained on, and a slick film sprayed over the puddle afterwards does not
        /// make it dry.
        /// </summary>
        public bool HasCoat(SurfaceCoatKind kind, Vector3 point)
        {
            for (int i = patches.Count - 1; i >= 0; i--)
                if (patches[i] != null && patches[i].Kind == kind && patches[i].Covers(point))
                    return true;

            return false;
        }

        /// <summary>
        /// Would a coat of <paramref name="kind"/> stick at <paramref name="point"/>?
        ///
        /// Asked separately from <see cref="Spray"/> by anything that wants to show the player what
        /// its spray is about to do before they commit to it. It costs the kind's own surface test
        /// — for ice, one short ray — so ask it at a frame rate somebody chose, not per pixel.
        /// </summary>
        public bool CanCoat(SurfaceCoatKind kind, Vector3 point)
        {
            SurfaceCoatBehaviour coat = BehaviourFor(kind);
            return coat != null && coat.CanCoat(point, this, out _);
        }

        // ─────────── what the artifacts do ───────────

        /// <summary>
        /// Lay a coat at <paramref name="point"/>. The deciding machine's call; false when this
        /// machine does not decide, when the kind refuses the surface, or when the world is full.
        ///
        /// <para>
        /// It does not apply anything itself: it announces, and every machine — this one included,
        /// through the same handler — lays what it hears. One code path for the host, the peers and
        /// the late joiner is what stops the three drifting apart, and it is what makes the
        /// announcement idempotent by construction.
        /// </para>
        /// </summary>
        /// <param name="radius">Footprint in metres, or 0 for the kind's own dab size.</param>
        /// <param name="seconds">Lifetime, or 0 for the kind's own — which for ice is forever.</param>
        public bool Spray(SurfaceCoatKind kind, Vector3 point, float radius = 0f, float seconds = 0f)
        {
            if (!Decides) return false;

            SurfaceCoatBehaviour coat = BehaviourFor(kind);
            if (coat == null) return false;

            if (!coat.CanCoat(point, this, out int colliderLayer)) return false;

            return Lay(coat, point, radius, seconds, colliderLayer);
        }

        /// <summary>
        /// Lay a coat that has already been decided on, merging it into a patch of its own kind if
        /// there is one under it. The half of <see cref="Spray"/> that does NOT ask the kind whether
        /// the surface will take it.
        ///
        /// <para>
        /// Split out for the restore, and the split is load-bearing. A save says there WAS ice here;
        /// re-asking would put that fact at the mercy of a raycast fired at a chunk that has loaded
        /// but not finished building its colliders — and the answer would be no, and the bridge
        /// somebody built would be gone from the world with nothing on the console. A restore
        /// restates; only a spray asks.
        /// </para>
        /// </summary>
        private bool Lay(SurfaceCoatBehaviour coat, Vector3 point, float radius, float seconds,
                         int colliderLayer)
        {
            float dab = radius > 0f ? radius : coat.DefaultRadius;
            float life = seconds > 0f ? seconds : coat.DefaultSeconds;

            // A dab landing on a patch of its own kind grows that patch instead of laying another.
            // The centre stays put — a refresh that moved it would drag a puddle around under the
            // player's feet — and the radius only reaches out far enough to cover the new dab.
            SurfaceCoatPatch merged = MergeTargetFor(coat.Kind, point, dab);
            if (merged != null)
            {
                float reach = Vector3.Distance(HorizontalOf(merged.Center), HorizontalOf(point)) + dab;
                Announce(merged.Id, coat.Kind, merged.Center,
                         Mathf.Min(Mathf.Max(merged.Radius, reach), maxPatchRadius), life,
                         merged.ColliderLayer);
                return true;
            }

            if (!MakeRoom()) return false;

            Announce(nextId++, coat.Kind, point, Mathf.Min(dab, maxPatchRadius), life, colliderLayer);
            return true;
        }

        /// <summary>
        /// End every patch whose footprint <paramref name="point"/> is inside — ice smashed, a fire
        /// drying the ground out. The deciding machine's call; returns how many it ended.
        ///
        /// <paramref name="kind"/> narrows it to one coat, which is usually what a caller means: a
        /// hammer breaks the ice it was swung at and has nothing to say about the puddle under it.
        /// </summary>
        public int Break(Vector3 point, float radius, SurfaceCoatKind? kind = null)
        {
            if (!Decides) return 0;

            int broken = 0;

            for (int i = patches.Count - 1; i >= 0; i--)
            {
                SurfaceCoatPatch patch = patches[i];
                if (patch == null) continue;
                if (kind.HasValue && patch.Kind != kind.Value) continue;

                Vector3 delta = HorizontalOf(patch.Center) - HorizontalOf(point);
                float reach = patch.Radius + Mathf.Max(0f, radius);
                if (delta.sqrMagnitude > reach * reach) continue;
                if (Mathf.Abs(patch.Center.y - point.y) > patch.Coat.VerticalReach) continue;

                BreakPatch(patch.Id);
                broken++;
            }

            return broken;
        }

        /// <summary>
        /// End one patch by id, before its clock said so. The deciding machine's call, and
        /// idempotent — an id nothing holds any more is a no-op.
        /// </summary>
        public void BreakPatch(int id)
        {
            if (!Decides) return;

            EnsureSubscribed();
            this.NetToAll(NetMsg.CoatBroken, new NetArg { A = id });
        }

        // ─────────── the wire ───────────

        /// <summary>
        /// <c>NetMsg.CoatSprayed</c>: A is the patch id, B the kind, P the sprayed point, and R
        /// carries the floats that are left — x the radius in metres, y the lifetime in seconds
        /// with 0 meaning permanent, z the physics layer a collider-carrying kind builds its
        /// geometry on.
        ///
        /// <para>
        /// R is four floats on the wire and is being used as such, the way <c>StatusSet</c> uses
        /// <c>P.x</c> for a magnitude. It is not an orientation and nothing reads it as one.
        /// </para>
        /// <para>
        /// One id covers laying a patch, growing it and refreshing its clock, because all three are
        /// the same fact — "this is what that patch is now" — seen at different times. That is also
        /// what makes a late joiner's copy correct: the server restates each live patch as it
        /// stands, and the machines that already had it read the restatement as a refresh.
        /// </para>
        /// </summary>
        private void Announce(int id, SurfaceCoatKind kind, Vector3 centre, float radius,
                              float seconds, int colliderLayer = 0)
        {
            EnsureSubscribed();

            // Carried in the message rather than re-derived on arrival: the layer comes from a
            // probe of the surface being frozen, and a client whose chunk arrived a moment later
            // would probe different geometry — or nothing at all — and build a sheet of ice its
            // ground probes cannot see.
            this.NetToAll(NetMsg.CoatSprayed, new NetArg
            {
                A = id,
                B = (int)kind,
                P = centre,
                R = new Quaternion(radius, Mathf.Max(0f, seconds),
                                   Mathf.Clamp(colliderLayer, 0, 31), 0f),
            });
        }

        private void OnCoatSprayed(in NetArg arg, ulong sender)
        {
            // A kind this build has no behaviour for. Not an error: the ids are append-only and a
            // peer on a newer build may legitimately know a coat this one does not.
            var kind = (SurfaceCoatKind)arg.B;
            SurfaceCoatBehaviour coat = BehaviourFor(kind);
            if (coat == null) return;

            float radius = arg.R.x;
            float seconds = arg.R.y;

            SurfaceCoatPatch existing = ById(arg.A);
            if (existing != null)
            {
                existing.Refresh(radius, seconds);
                return;
            }

            Material film = coat.Film;
            if (film == null && !warnedMissingFilm)
            {
                warnedMissingFilm = true;
                Debug.LogWarning($"[Coats] No film material for {kind} and no shader by that " +
                                 "kind's name — the coat is there and works, and nobody can see " +
                                 "it. Wire a material on the field, or check the shader shipped.", this);
            }

            SurfaceCoatPatch patch = SurfaceCoatPatch.Create(coat, arg.A, arg.P, radius, seconds,
                                                             film, Mathf.RoundToInt(arg.R.z), PatchRoot);
            if (patch == null) return;

            // Which chunk owns it, decided from the CENTRE and only where the streaming grid
            // reaches — see SurfaceCoatPatch.OwningChunk. Server-side, because chunk state is only
            // ever tracked on the machine that issues the loads.
            if (Decides && TryOwningChunk(patch.Center, out Vector2Int coord))
                patch.SetOwningChunk(coord, true);

            // Ids arrive from the server, so a machine that is not the server has to keep its own
            // counter ahead of them — otherwise a host handing over, or an offline field that has
            // heard ids from a session, would start minting ones already in use.
            if (arg.A >= nextId) nextId = arg.A + 1;

            patches.Add(patch);
            StartAnswering();
            BeginWatchingForJoiners();
        }

        private void OnCoatBroken(in NetArg arg, ulong sender)
        {
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                if (patches[i] == null || patches[i].Id != arg.A) continue;

                Remove(i);
                return;
            }
        }

        private void EnsureSubscribed()
        {
            // Guarded rather than left to OnEnable alone, because this component can be added at
            // runtime and Unity raises no OnEnable for an AddComponent outside play mode — and
            // because registering the same handler twice would lay every patch twice.
            if (subscribed) return;

            subscribed = true;
            this.NetOn(NetMsg.CoatSprayed, OnCoatSprayed);
            this.NetOn(NetMsg.CoatBroken, OnCoatBroken);
        }

        /// <summary>
        /// Say it all again when somebody new arrives.
        ///
        /// A joining client has none of the patches that were laid before it connected, and no
        /// message it missed will be replayed to it by the transport — which for ice, the coat that
        /// outlives a session, means a bridge everyone else is standing on and it cannot see. So
        /// the server restates every live patch as one ordinary announcement, and the joiner walks
        /// exactly the code path every other machine already walked.
        /// </summary>
        private void BeginWatchingForJoiners()
        {
            if (watchingForJoiners || !Network.Server) return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            manager.OnClientConnectedCallback += OnClientJoined;
            watchingForJoiners = true;
        }

        private void StopWatchingForJoiners()
        {
            if (!watchingForJoiners) return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null) manager.OnClientConnectedCallback -= OnClientJoined;

            watchingForJoiners = false;
        }

        private void OnClientJoined(ulong clientId)
        {
            for (int i = 0; i < patches.Count; i++)
            {
                SurfaceCoatPatch patch = patches[i];
                if (patch == null) continue;

                // Restated as it stands NOW — the time LEFT, not the time it was given — so the
                // joiner's copy runs out with everyone else's instead of starting over.
                // Floored above zero, because zero is how this message spells "permanent" and a
                // patch caught on the frame its clock ran out would arrive as an eternal one.
                Announce(patch.Id, patch.Kind, patch.Center, patch.Radius,
                         patch.IsPermanent ? 0f : Mathf.Max(0.01f, patch.SecondsLeft),
                         patch.ColliderLayer);
            }
        }

        // ─────────── streaming ───────────

        /// <summary>
        /// The chunk under this patch is going away, and with it every reason to keep the patch:
        /// nothing can stand on ground that is not loaded, and a coat left behind would be a disc
        /// of ice hanging in an empty chunk.
        ///
        /// A saved kind is folded back into a record first, which is the store's own invariant —
        /// state is either live in a loaded scene or in a record, never neither.
        /// </summary>
        private void OnChunkWillUnload(Vector2Int coord, Scene scene)
        {
            if (!Decides) return;

            for (int i = patches.Count - 1; i >= 0; i--)
            {
                SurfaceCoatPatch patch = patches[i];
                if (patch == null || !patch.HasOwningChunk || patch.OwningChunk != coord) continue;

                if (patch.Coat.Saved) dormant.Add(SurfaceCoatRecord.Of(patch));

                BreakPatch(patch.Id);
            }
        }

        private void OnChunkLoaded(Vector2Int coord, Scene scene) => ResolveDormant();

        /// <summary>
        /// Lay every dormant record whose ground has arrived. Records outside the streaming grid —
        /// an interior, the arena — have no chunk to wait for and are laid at once.
        ///
        /// Each one comes back with a FRESH id. Ids are session handles for "break that patch", not
        /// identity, and nothing that outlives a session refers to one.
        /// </summary>
        private void ResolveDormant()
        {
            if (!Decides || dormant.Count == 0) return;

            for (int i = dormant.Count - 1; i >= 0; i--)
            {
                SurfaceCoatRecord record = dormant[i];
                Vector3 centre = record.Center;

                if (TryOwningChunk(centre, out Vector2Int coord) && !IsChunkLoaded(coord)) continue;

                SurfaceCoatBehaviour coat = BehaviourFor(record.Kind);
                if (coat == null)
                {
                    // A kind this build no longer has. The record is kept where it is rather than
                    // dropped — a downgrade should not delete somebody's world.
                    continue;
                }

                dormant.RemoveAt(i);
                Lay(coat, centre, record.radius, coat.DefaultSeconds, record.layer);
            }
        }

        private bool TryOwningChunk(Vector3 point, out Vector2Int coord)
        {
            coord = default;

            WorldStreamer world = Streamer;
            return world != null && world.Config != null &&
                   world.Config.TryGetStreamingCoord(point, out coord);
        }

        private bool IsChunkLoaded(Vector2Int coord)
        {
            WorldStreamer world = Streamer;
            if (world == null || world.Config == null) return false;

            return world.IsChunkLoadedAt(world.Config.ChunkToWorldPosition(coord));
        }

        private WorldStreamer Streamer =>
            streamer != null ? streamer : streamer = FindFirstObjectByType<WorldStreamer>();

        // ─────────── persistence ───────────

        /// <summary>
        /// Every coat worth saving, live and dormant alike, as flat records.
        ///
        /// Dormant ones are included and have to be: a chunk that unloaded an hour ago still holds
        /// the pool somebody froze, and a capture that only walked the live list would delete it
        /// from the world by writing a file that never mentioned it.
        /// </summary>
        public void CollectSaved(List<SurfaceCoatRecord> into)
        {
            if (into == null) return;

            for (int i = 0; i < patches.Count; i++)
                if (patches[i] != null && patches[i].Coat.Saved)
                    into.Add(SurfaceCoatRecord.Of(patches[i]));

            into.AddRange(dormant);
        }

        /// <summary>
        /// Replace every saved coat with the loaded set. Restore-only; called by the save system.
        ///
        /// Everything is parked dormant rather than laid immediately, because a global payload is
        /// restored before any chunk has hydrated — <see cref="ResolveDormant"/> then lays each one
        /// as its ground arrives, and lays anything with no chunk to wait for straight away.
        /// </summary>
        public void RestoreSaved(IReadOnlyList<SurfaceCoatRecord> records)
        {
            dormant.Clear();

            for (int i = patches.Count - 1; i >= 0; i--)
                if (patches[i] != null && patches[i].Coat.Saved)
                    BreakPatch(patches[i].Id);

            if (records != null) dormant.AddRange(records);

            ResolveDormant();
        }

        // ─────────── plumbing ───────────

        /// <summary>
        /// The newest patch covering <paramref name="point"/>, or null. Walked backwards because
        /// the list is in mint order, so the first match from the end is the highest id.
        /// </summary>
        private SurfaceCoatPatch Newest(Vector3 point)
        {
            for (int i = patches.Count - 1; i >= 0; i--)
                if (patches[i] != null && patches[i].Covers(point))
                    return patches[i];

            return null;
        }

        private SurfaceCoatPatch ById(int id)
        {
            for (int i = patches.Count - 1; i >= 0; i--)
                if (patches[i] != null && patches[i].Id == id)
                    return patches[i];

            return null;
        }

        /// <summary>The newest patch of <paramref name="kind"/> a dab here should grow instead.</summary>
        private SurfaceCoatPatch MergeTargetFor(SurfaceCoatKind kind, Vector3 point, float dab)
        {
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                SurfaceCoatPatch patch = patches[i];
                if (patch == null || patch.Kind != kind) continue;
                if (Mathf.Abs(patch.Center.y - point.y) > patch.Coat.VerticalReach) continue;

                float merge = patch.Radius * mergeShare;
                Vector3 delta = HorizontalOf(patch.Center) - HorizontalOf(point);
                if (delta.sqrMagnitude <= merge * merge) return patch;
            }

            return null;
        }

        /// <summary>
        /// Make room for one more patch, dropping the oldest coat that was going to expire anyway.
        /// False when every patch there is permanent, which is a world somebody has covered in ice.
        /// </summary>
        private bool MakeRoom()
        {
            if (patches.Count < maxPatches) return true;

            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i] == null || patches[i].IsPermanent) continue;

                BreakPatch(patches[i].Id);
                return true;
            }

            Debug.LogWarning($"[Coats] {maxPatches} permanent coats already in the world — this " +
                             "one is refused. Raise the cap, or break some of the ice.", this);
            return false;
        }

        private void Remove(int index)
        {
            SurfaceCoatPatch patch = patches[index];
            patches.RemoveAt(index);

            // Destroyed, never merely switched off: a disabled collider is out of every physics
            // query but still standing in the scene, so a sheet of ice hidden that way is one a
            // raycast finds and nobody can see.
            if (patch != null) Destroy(patch.gameObject);

            StopAnsweringIfIdle();
        }

        /// <summary>
        /// Start answering grip questions. Registered only while there is something to say, so a
        /// world with no coats in it costs every mover a walk over a list of length zero.
        /// </summary>
        private void StartAnswering()
        {
            if (answeringGrip) return;

            answeringGrip = true;
            GroundGrip.Add(this);
        }

        /// <summary>
        /// The last coat is gone: stop answering grip questions and stop waiting for joiners to
        /// restate them to. Both are only worth holding while there is something to say.
        /// </summary>
        private void StopAnsweringIfIdle()
        {
            if (patches.Count > 0) return;

            if (answeringGrip)
            {
                answeringGrip = false;
                GroundGrip.Remove(this);
            }

            StopWatchingForJoiners();
        }

        private SurfaceCoatBehaviour BehaviourFor(SurfaceCoatKind kind) =>
            SurfaceCoatKinds.IsKind(kind) ? Behaviours[(int)kind] : null;

        /// <summary>
        /// The behaviours, slotted by kind so a lookup is an index rather than a switch — which is
        /// what keeps this class from growing one branch per coat as coats are added. Built on
        /// demand, because this component is often added at runtime and Unity raises no Awake for
        /// an AddComponent outside play mode.
        /// </summary>
        private SurfaceCoatBehaviour[] Behaviours
        {
            get
            {
                if (behaviours != null) return behaviours;

                behaviours = new SurfaceCoatBehaviour[SurfaceCoatKinds.Count];
                Slot(slick);
                Slot(ice);
                Slot(wet);
                return behaviours;
            }
        }

        private void Slot(SurfaceCoatBehaviour coat)
        {
            if (coat == null) return;

            int index = (int)coat.Kind;
            if (index < 0 || index >= behaviours.Length) return;

            behaviours[index] = coat;
        }

        /// <summary>
        /// Where patches are parented: one object of this field's own, not the chunk scene under
        /// the coat. A patch's lifetime is this field's business — see
        /// <see cref="OnChunkWillUnload"/> — and a coat left in a chunk scene would be destroyed by
        /// the unload with the field still holding the reference.
        /// </summary>
        private Transform PatchRoot
        {
            get
            {
                if (patchRoot != null) return patchRoot;

                var root = new GameObject("Surface Coats");
                root.transform.SetParent(transform, false);
                return patchRoot = root.transform;
            }
        }

        private static Vector3 HorizontalOf(Vector3 point) => new Vector3(point.x, 0f, point.z);
    }
}
