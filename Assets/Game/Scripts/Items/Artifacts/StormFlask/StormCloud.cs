// The storm a Storm Flask uncorks: one dark disc of vapour that rains for half a minute and throws
// bolts at whatever is tallest underneath it.
//
// IT IS A SPAWNED NETWORK OBJECT rather than a Present() visual, and that is what it costs a prefab
// registration for. Unlike a projectile it is not a drawing of an event: it stands in the world for
// thirty seconds, it picks targets, and it bills damage — everybody has to be looking at the same
// one, and exactly one machine may decide what it does (GDC-L1-MP-0004).
//
// EVERY MACHINE RUNS THE SAME CLOCK OFF THE SAME TWO NUMBERS. The server stamps when the storm broke
// and how long it has, both on the shared server clock, and each machine derives the gather and the
// dispersal from them. A player who joins twenty seconds in picks the storm up already fading,
// exactly where everyone else has it — which a locally started clock could not do, because it would
// gather a storm that is nearly over.
//
// WHAT TRAVELS AND WHAT DOES NOT. The rain and the veil are drawn from the replicated cloud, so
// there is no message per raindrop and none per rain tick. The one thing a client cannot work out
// for itself is WHERE a bolt landed — the server chose that from a physics sweep — so a strike is
// one replicated point with an ordinal beside it, and every machine draws the shipped Lightning VFX
// from it. The ordinal is what makes two bolts onto the same spot two changes rather than one.
//
// IT IS NOT SAVED, and that is load-bearing rather than an omission. A storm is thirty seconds long,
// so a save taken mid-storm loads a world with clear sky — the honest outcome, since restoring one
// would hand the player back a hazard they had already walked out of. The prefab therefore must NOT
// carry a non-kinematic Rigidbody, a HealthComponent, a PickupableItem, a NavMeshAgent or a
// SceneTracked: SaveablePolicy.NeedsSaving reads exactly those, and any one of them would give this
// object a SaveableEntity with no stamped prefab id — captured faithfully into every save file and
// dropped with a warning on every load.
//
// WHICH IS ALSO WHY IT WATCHES THE STREAMING GRID ITSELF. SceneTracked is how a moving entity
// follows the chunk under it, but SceneTracked is IPersistentEntity, so wearing one would be the
// save opt-in the paragraph above rules out. The rule it stands for still applies — a coat, a prop
// or a storm belongs to the chunk under it — so the cloud resolves its own chunk on the server and
// ends when that chunk goes, which is what SurfaceCoatField does with a patch for the same reason.
// A storm left standing over unloaded ground is rain nobody can walk in.
using System;
using System.Collections.Generic;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Status;
using SpaceGame.Gameplay.Surface;
using SpaceGame.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.Items
{
    /// <summary>
    /// One bolt, as it reaches the machines that have to draw it.
    ///
    /// <para>
    /// <b><see cref="Ordinal"/> is what makes this a message rather than a value.</b> A
    /// NetworkVariable announces a change, and two bolts that happen to earth into the same spot
    /// are the same Vector3 — so without a counter beside it the second one would be silent. Zero
    /// means the server has struck nothing yet, which is also what a late joiner uses to tell "this
    /// bolt is new" from "this bolt is the last one, and it landed before I arrived".
    /// </para>
    /// </summary>
    public struct StormStrike : INetworkSerializable, IEquatable<StormStrike>
    {
        /// <summary>Where the bolt earthed — the body it was billed against, in world space.</summary>
        public Vector3 Point;

        /// <summary>How many bolts this storm has thrown, counting this one.</summary>
        public int Ordinal;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Point);
            serializer.SerializeValue(ref Ordinal);
        }

        public bool Equals(StormStrike other) => Ordinal == other.Ordinal && Point.Equals(other.Point);

        public override bool Equals(object obj) => obj is StormStrike other && Equals(other);

        public override int GetHashCode() => Ordinal.GetHashCode() ^ Point.GetHashCode();
    }

    /// <summary>
    /// A small, flat, angry disc of vapour with rain under it. Gathers, rains, strikes, disperses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StormCloud : NetworkBehaviour
    {
        [Header("Shape")]
        [Tooltip("How wide the storm reaches, in metres, measured horizontally from its axis. It " +
                 "is the radius of the wet ground, of the body sweep and of the cloud body's own " +
                 "scale — the prefab scales the mesh to match, because the mesh is a unit cloud.")]
        [SerializeField, Min(1f)] private float radius = 12f;

        [Tooltip("How far above the aimed point the cloud hangs, in metres. The flask reads this " +
                 "off the prefab to decide where to put the cloud, and the rain veil is scaled to " +
                 "exactly this so the streaks end on the ground they are wetting.")]
        [SerializeField, Min(1f)] private float height = 15f;

        [Tooltip("How far BELOW the aimed point a body still counts as under the storm, in metres. " +
                 "Not zero: the aimed point is one raycast hit, and somebody standing in the dip " +
                 "beside it is plainly still in the rain.")]
        [SerializeField, Min(0f)] private float reachBelow = 15f;

        [Header("Life")]
        [Tooltip("Seconds the storm stands. The design's thirty: long enough to deny a place, " +
                 "short enough that walking away from it is an answer.")]
        [SerializeField, Min(1f)] private float stormSeconds = 30f;

        [Tooltip("Seconds the storm takes to gather, and to disperse again at the end. It fades in " +
                 "and out through the shader's _Form rather than appearing, so nobody is caught " +
                 "under one that was not there a frame ago.")]
        [SerializeField, Min(0f)] private float formSeconds = 1.5f;

        [Header("Rain")]
        [Tooltip("Seconds between rain ticks. Each tick refreshes the Wet coat under the cloud and " +
                 "puts out any fires in it. Faster costs a sweep and a coat refresh; slower leaves " +
                 "a body burning for that long after walking into the rain.")]
        [SerializeField, Min(0.1f)] private float rainInterval = 1f;

        [Header("Lightning")]
        [Tooltip("Seconds between bolts. The design's two and a half — slow enough to be read as " +
                 "individual strikes and to be walked out from under.")]
        [SerializeField, Min(0.25f)] private float boltInterval = 2.5f;

        [Tooltip("Damage one bolt deals. The Lightning Spell's own figure: the design says the " +
                 "storm throws the same bolt, and this is a second copy of that number only " +
                 "because the spell keeps its own on its own prefab.")]
        [SerializeField, Min(0)] private int boltDamage = 120;

        [Tooltip("How wide a bolt bites, in metres, measured from the body it earthed into. The " +
                 "Lightning Spell's figure, for the same reason as the damage.")]
        [SerializeField, Min(0f)] private float boltRadius = 3.5f;

        [Tooltip("What a bolt can hurt. Triggers are always ignored.")]
        [SerializeField] private LayerMask damageMask = ~0;

        [Tooltip("What the storm sweeps for when it looks for something to hit. Narrow this to the " +
                 "layers bodies live on: the sweep is a twelve-metre column and terrain in it is " +
                 "work for nothing.")]
        [SerializeField] private LayerMask bodyMask = ~0;

        [Header("Bolt presentation")]
        [Tooltip("The Lightning Spell's own strike prefab, instantiated locally on every machine. " +
                 "A bolt is a drawing of an event, not shared world state, so this must NOT be a " +
                 "registered network prefab.")]
        [SerializeField] private GameObject boltVfxPrefab;

        [Tooltip("How far above the body it hits the bolt is drawn from, in metres. The prefab is " +
                 "authored to fall through this much sky, which is why the Lightning Spell offsets " +
                 "its own strike by the same amount rather than drawing at the point it bills.")]
        [SerializeField, Min(0f)] private float boltDrawHeight = 10f;

        [Tooltip("How the bolt prefab is turned to hang downwards. The Lightning Spell's rotation: " +
                 "the bolt is a flat plane and this is the face it is authored on.")]
        [SerializeField] private Vector3 boltEuler = new Vector3(90f, 0f, 0f);

        [Tooltip("The thunder clap, played at the cloud rather than at the strike — the flash is " +
                 "where the bolt lands, the noise comes from up there.")]
        [SerializeField] private SfxId boltSound = SfxId.AmbThunder;

        [Header("Parts")]
        [Tooltip("Drives _Form and _Flash on the cloud body and the rain veil. Found under this " +
                 "object when unset.")]
        [SerializeField] private StormCloudLook look;

        [Tooltip("Where a bolt leaves the cloud, and where its thunder is heard from — the cloud's " +
                 "underside. Falls back to this object when unset; any point on the cloud is " +
                 "close enough for a sound.")]
        [SerializeField] private Transform boltOrigin;

        /// <summary>
        /// When this storm broke, on the shared server clock, and how long it has. Replicated
        /// rather than local, for the late-joiner reason in the file header; server-write because
        /// the expiry is the server's.
        ///
        /// <para>
        /// A zero <see cref="lifetime"/> means "nobody has stamped this storm yet" — it was
        /// instantiated a moment ago and either the server has not reached <see cref="Begin"/> or
        /// this client has not unpacked the spawn payload. Derived from an unstamped clock it would
        /// read as an infinitely old storm and disperse on its first frame.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<double> brokeAt = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> lifetime = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>The last bolt, for the machines that have to draw it. See <see cref="StormStrike"/>.</summary>
        private readonly NetworkVariable<StormStrike> strike = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Reused by the rain tick and the bolt tick. Per instance, not static: two storms are ordinary.</summary>
        private readonly List<StatusReceiver> bodies = new List<StatusReceiver>();

        /// <summary>
        /// Who uncorked the flask, on the machine that decided to. Deliberately not replicated: the
        /// only question it answers is who a bolt is attributed to, and only the server bills one.
        /// </summary>
        private GameObject uncorker;

        /// <summary>
        /// Which chunk owns this storm, on the server. See the file header — the cloud ends when
        /// this chunk unloads, the way every other placed thing in this world does.
        /// </summary>
        private Vector2Int owningChunk;
        private bool hasOwningChunk;

        private float nextRainAt;
        private float nextBoltAt;

        /// <summary>The last bolt this machine has drawn. See <see cref="StormStrike.Ordinal"/>.</summary>
        private int drawnOrdinal;

        /// <summary>
        /// How far above the aimed point one of these hangs. Read off the prefab by the flask, so
        /// the height the cloud is placed at and the height its rain is scaled to are one number.
        /// </summary>
        public float Height => height;

        /// <summary>
        /// Where the storm meets the ground: the wet patch's centre and the base of the body sweep.
        ///
        /// <para>
        /// Derived from <see cref="height"/> rather than taken off a marker on the model, and
        /// deliberately. It is the same number the flask uses to place the cloud and the same
        /// number the rain veil is scaled to, so the ground that gets wet is by construction the
        /// ground the player can see rain falling on — where a marker somebody had to remember to
        /// drop at the foot of the veil would silently wet the sky the day it was left at the root.
        /// </para>
        /// </summary>
        private Vector3 GroundPoint => transform.position - Vector3.up * height;

        /// <summary>Has the server stamped this storm's clock yet?</summary>
        private bool Stamped => lifetime.Value > 0f;

        /// <summary>Seconds since the storm broke, on whatever clock both machines share.</summary>
        private float Age => Mathf.Max(0f, (float)(Now - brokeAt.Value));

        /// <summary>
        /// The clock every machine measures this storm against. The server's, when there is one:
        /// two machines drawing the same cloud have to agree about how old it is, and their own
        /// <c>Time.time</c> values started whenever each of them did.
        /// </summary>
        private static double Now =>
            Network.IsNetworked && NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ServerTime.Time
                : Time.timeAsDouble;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Break this storm. Server-side, immediately after the spawn.
        /// </summary>
        /// <param name="thrower">Who a bolt is attributed to. Not who it spares — see <see cref="Strike"/>.</param>
        public void Begin(GameObject thrower)
        {
            uncorker = thrower;

            brokeAt.Value = Now;
            lifetime.Value = stormSeconds;

            // Rain from the first frame — the ground is wet as soon as there is rain on it — but
            // the first bolt is one interval away, so a storm cannot strike before it has gathered.
            nextRainAt = 0f;
            nextBoltAt = boltInterval;

            // Which chunk owns it, decided from where the rain lands rather than from the cloud
            // plane fifteen metres up, and only where the streaming grid reaches. Server-side,
            // because chunk state is only ever populated there.
            hasOwningChunk = TryOwningChunk(GroundPoint, out owningChunk);
        }

        private void OnEnable()
        {
            if (look == null) look = GetComponentInChildren<StormCloudLook>(true);

            StormCloudField.Register(this);
            WorldStreamer.OnChunkWillUnload += OnChunkWillUnload;
        }

        private void OnDisable()
        {
            WorldStreamer.OnChunkWillUnload -= OnChunkWillUnload;
            StormCloudField.Unregister(this);
        }

        /// <summary>
        /// A late joiner is handed the LAST bolt as the strike variable's current value. Drawing it
        /// would be a strike out of a clear sky seconds after everyone else saw it, so whatever
        /// ordinal is already on the wire counts as seen and only the bolts after it are drawn.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            drawnOrdinal = strike.Value.Ordinal;
            strike.OnValueChanged += OnStrikeChanged;
        }

        public override void OnNetworkDespawn() => strike.OnValueChanged -= OnStrikeChanged;

        /// <summary>
        /// Take this storm out of the world now, because its ground has gone or because too many
        /// are standing at once.
        ///
        /// <para>
        /// It leaves the field BEFORE the despawn rather than waiting for its own OnDisable.
        /// <c>Object.Destroy</c> is deferred to the end of the frame, so a caller retiring several
        /// in a row would otherwise keep finding the ones it had already retired — and, being a
        /// loop over a count that never falls, would never come back.
        /// </para>
        /// </summary>
        public void Retire()
        {
            StormCloudField.Unregister(this);
            GameServices.World.Despawn(gameObject);
        }

        private void Update()
        {
            // Every machine, from the replicated clock: what is drawn is never this machine's own
            // idea of how far along the storm is.
            if (look != null) look.SetForm(FormNow);

            if (!Network.Simulates(this) || !Stamped) return;

            float age = Age;

            // Expiry is the server's, on the storm itself, so one machine decides and the despawn
            // reaches the rest as an ordinary spawn message. Every peer has already drawn the
            // dispersal to the end by the time it arrives. Through Retire rather than straight to
            // Despawn, so a storm that ends on the same frame a flask is uncorked is already out of
            // the budget's count — Object.Destroy is deferred to the end of the frame.
            if (age >= lifetime.Value)
            {
                Retire();
                return;
            }

            if (age >= nextRainAt)
            {
                nextRainAt = age + rainInterval;
                Rain();
            }

            if (age >= nextBoltAt)
            {
                nextBoltAt = age + boltInterval;
                Strike();
            }
        }

        /// <summary>
        /// How much of the storm exists right now, 0 to 1: it gathers over
        /// <see cref="formSeconds"/> and disperses over the same at the end.
        ///
        /// Derived from the age rather than stored, so a machine that joined halfway through picks
        /// the storm up where everyone else has it.
        /// </summary>
        private float FormNow
        {
            get
            {
                if (!Stamped) return 0f;

                float total = lifetime.Value;
                float age = Age;

                if (formSeconds <= 0f) return age < total ? 1f : 0f;

                float gathering = Mathf.Clamp01(age / formSeconds);
                float dispersing = Mathf.Clamp01((total - age) / formSeconds);

                return Mathf.SmoothStep(0f, 1f, Mathf.Min(gathering, dispersing));
            }
        }

        // ── The rain ───────────────────────────────────────────────────────────

        /// <summary>
        /// One tick of rain, server-side: wet ground and no fires.
        ///
        /// <para>
        /// Neither of these is this cloud's work. The coat field owns the patch, its merge rule,
        /// its expiry, its cap, its chunk and its replication; the status receiver owns what being
        /// on fire is and what puts it out. What the storm owns is WHERE the rain falls and how
        /// often — and nothing else (GDC-L1-SYS-0005).
        /// </para>
        /// </summary>
        private void Rain()
        {
            Vector3 ground = GroundPoint;

            // Radius passed, duration NOT. Spraying Wet onto ground that is already wet refreshes
            // the patch rather than laying a second one, and that refresh once a second IS what
            // makes the ground stay wet for as long as the cloud plus a little. A duration here
            // would replace the coat's own answer with this cloud's guess at it.
            SurfaceCoats.Spray(SurfaceCoatKind.Wet, ground, radius);

            StormCloudTargets.Under(ground, radius, reachBelow, height, bodyMask, bodies);

            for (int i = 0; i < bodies.Count; i++)
            {
                StatusReceiver body = bodies[i];

                // Harmless on a body that is not alight, and idempotent by construction — which it
                // has to be, because this runs once a second for thirty seconds.
                if (body != null) body.Clear(StatusKind.Burning);
            }
        }

        // ── The lightning ──────────────────────────────────────────────────────

        /// <summary>
        /// One bolt, server-side: pick the tallest body under the storm, bill it, and tell every
        /// machine where to draw the strike.
        ///
        /// <para>
        /// The server never takes anybody's word for a target. It sweeps the column itself, which
        /// is what stops a client naming a body across the map (GDC-L1-MP-0004) and, more to the
        /// point here, is the only way the rule can be the one the design states.
        /// </para>
        /// <para>
        /// A storm with nothing under it throws no bolt. That is deliberate: a strike means "the
        /// tallest thing down there just got hit", and one thrown at empty sand would teach the
        /// player that the flashes are weather rather than a rule they can stand outside of
        /// (GDC-L1-SYS-0006).
        /// </para>
        /// </summary>
        private void Strike()
        {
            StormCloudTargets.Under(GroundPoint, radius, reachBelow, height, bodyMask, bodies);

            StatusReceiver target = StormCloudTargets.Tallest(bodies);
            if (target == null || boltDamage <= 0 || boltRadius <= 0f) return;

            Vector3 point = target.transform.position;

            // Nothing is excluded, and whoever uncorked the flask least of all. A storm that spared
            // its own thrower would be a turret pointed away from them; the design's whole tension
            // is that standing next to your own weather on high ground is a way to be hit by it
            // (GDC-L1-BAL-0004). Colliders are not creatures, which is RadiusDamage's job to know.
            RadiusDamage.Apply(point, boltRadius, damageMask, boltDamage, SourceTransform,
                               exclude: null);

            strike.Value = new StormStrike { Point = point, Ordinal = strike.Value.Ordinal + 1 };
        }

        private Transform SourceTransform => uncorker != null ? uncorker.transform : transform;

        private void OnStrikeChanged(StormStrike previous, StormStrike current)
        {
            if (current.Ordinal == drawnOrdinal) return;

            drawnOrdinal = current.Ordinal;
            DrawBolt(current.Point);
        }

        /// <summary>
        /// Every machine, including the server's own: the flash inside the cloud, the clap, and the
        /// bolt itself.
        ///
        /// The bolt is the Lightning Spell's presentation on the Lightning Spell's terms — a plain
        /// local Instantiate of a prefab authored to fall through some sky into what it earths in.
        /// </summary>
        private void DrawBolt(Vector3 point)
        {
            if (look != null) look.Flash();

            if (boltSound != SfxId.None)
                Sfx.Play(boltSound, boltOrigin != null ? boltOrigin.position : transform.position);

            if (boltVfxPrefab == null) return;

            Instantiate(boltVfxPrefab, point + Vector3.up * boltDrawHeight,
                        Quaternion.Euler(boltEuler));
        }

        // ── Streaming ──────────────────────────────────────────────────────────

        /// <summary>
        /// The ground this storm is raining on is going away, and with it every reason to keep the
        /// storm: nobody can stand in rain that falls on an unloaded chunk, and a cloud left behind
        /// would go on drawing and go on billing over an empty scene.
        ///
        /// Nothing is folded into a record first, unlike a saved coat — a storm is thirty seconds
        /// of weather and is deliberately not saved at all. See the file header.
        /// </summary>
        private void OnChunkWillUnload(Vector2Int coord, Scene scene)
        {
            if (!Network.Simulates(this) || !hasOwningChunk || coord != owningChunk) return;

            Retire();
        }

        /// <summary>
        /// Which chunk sits under <paramref name="point"/>, or false when the streaming grid does
        /// not reach it — an interior, the arena, anywhere off the world's grid. A storm there has
        /// no chunk to outlive and simply runs its clock out.
        /// </summary>
        private static bool TryOwningChunk(Vector3 point, out Vector2Int coord)
        {
            coord = default;

            WorldStreamer world = FindFirstObjectByType<WorldStreamer>();
            return world != null && world.Config != null &&
                   world.Config.TryGetStreamingCoord(point, out coord);
        }

        private void OnValidate()
        {
            // A storm shorter than its own gather plus its own dispersal never reaches full form:
            // it would fade in and straight back out, and _Form would never once read 1.
            stormSeconds = Mathf.Max(formSeconds * 2f, stormSeconds);
        }
    }
}
