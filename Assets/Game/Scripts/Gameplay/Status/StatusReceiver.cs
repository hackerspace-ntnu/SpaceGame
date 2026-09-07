// Timed conditions on a body — burning, frozen, slick, inflated, foamed.
//
// Five of the new artifacts leave a condition behind on whatever they touch. Without one place for
// that, each of them grows its own timer, its own replication and its own answer to "what does a
// creature do about this", and the five answers drift apart. Here a condition is a kind and an
// expiry, the server owns it, every machine rebuilds the look from it, and what each kind DOES
// lives with the kind (StatusBehaviour) rather than in this class.
using System;
using SpaceGame.Core;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// The conditions one body is under. Put this on anything that can carry one: the player
    /// character, a creature, a loose prop.
    ///
    /// <para>
    /// <b>It must exist on every machine, not only the server.</b> A status arrives as a message on
    /// this body's own relay, and a message whose entity has nothing subscribed is dropped without a
    /// word — so a body with a receiver only on the host is a body that burns for the host and
    /// nobody else. Author it on the prefab, or let <c>StatusReactionModule</c> put it there (it
    /// calls <see cref="Ensure"/> from its own OnEnable, which runs on every machine).
    /// </para>
    /// <para>
    /// Nothing here is saved, and that is deliberate: these conditions are seconds long, and a
    /// quicksave taken while burning that loaded a body still on fire would hand the player back a
    /// situation they had already escaped. See the system doc.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class StatusReceiver : MonoBehaviour
    {
        // One field per kind rather than a polymorphic list: a concrete field gives every number
        // below an Inspector row on the body that carries it, and it keeps type names out of the
        // prefab, which a [SerializeReference] list would put there and a rename would then break.
        [Header("Conditions")]
        [SerializeField] private BurningStatus burning = new BurningStatus();
        [SerializeField] private FrozenStatus frozen = new FrozenStatus();
        [SerializeField] private SlickStatus slick = new SlickStatus();
        [SerializeField] private InflatedStatus inflated = new InflatedStatus();
        [SerializeField] private FoamedStatus foamed = new FoamedStatus();

        [Header("Body")]
        [Tooltip("What the Inflated condition scales. Leave empty to scale this object itself, " +
                 "which is right for a player or a loose prop.\n\n" +
                 "On a saved entity, point it at a model child instead: the world save records " +
                 "the entity ROOT's scale unconditionally, so a save taken mid-inflation would " +
                 "load the creature permanently oversized.")]
        [SerializeField] private Transform inflationTarget;

        /// <summary>
        /// One running condition. A struct in a per-kind array rather than a list of objects: there
        /// are five kinds, a body has at most one of each, and the lookup is by kind on a hot path.
        /// </summary>
        private struct ActiveStatus
        {
            public float Seconds;
            public float TotalSeconds;
            public float Magnitude;

            public bool Running => Seconds > 0f;
        }

        private readonly ActiveStatus[] active = new ActiveStatus[StatusKinds.Count];

        /// <summary>
        /// Who caused each condition, on the machine that decides. Deliberately not on
        /// <see cref="ActiveStatus"/> and deliberately not on the wire: only the deciding machine
        /// bills anything, so this is the only machine that has a use for it, and the attribution
        /// is what makes a burning animal run from the player who lit it rather than from nobody.
        /// </summary>
        private readonly Transform[] sources = new Transform[StatusKinds.Count];

        private StatusBehaviour[] behaviours;
        private HealthComponent health;
        private bool subscribed;
        private bool watchingForJoiners;

        /// <summary>
        /// A condition on this body started or stopped.
        ///
        /// <para>
        /// This is the whole of the coupling between conditions and everything that reacts to them.
        /// A creature panics because <c>StatusReactionModule</c> listens here, not because the
        /// flamethrower knows what an agent is — five artifacts do not each learn about AI
        /// (GDC-L1-ARCH-0003).
        /// </para>
        /// <para>
        /// Raised on every machine, since every machine runs the same clock off the same replicated
        /// expiry. Raised once per start and once per stop; a refresh is neither.
        /// </para>
        /// </summary>
        public event Action<StatusKind, bool> StatusChanged;

        /// <summary>
        /// Does this machine decide what happens to this body? Damage, deaths and early clears are
        /// this machine's alone (GDC-L1-MP-0004); drawing the condition is everybody's.
        ///
        /// True offline and for a body nobody has networked, which is what keeps a prop in an
        /// interior working exactly as it did before any of this existed.
        /// </summary>
        public bool Decides => Network.Simulates(this);

        /// <summary>
        /// This body's health, resolved on demand and from the PARENT — a status can be applied to
        /// whatever collider an artifact hit, and the health sits on the root. Not cached in Awake,
        /// because this component is often added at runtime and Unity does not raise Awake for an
        /// AddComponent outside play mode.
        /// </summary>
        public HealthComponent Health =>
            health != null ? health : health = GetComponentInParent<HealthComponent>();

        /// <summary>What <see cref="StatusKind.Inflated"/> scales. See <see cref="inflationTarget"/>.</summary>
        public Transform InflationTarget => inflationTarget != null ? inflationTarget : transform;

        /// <summary>
        /// The receiver for <paramref name="body"/>, creating one if it has none.
        ///
        /// <para>
        /// Resolved to the body's root rather than to whatever collider was hit, so a creature with
        /// its colliders on children ends up with one receiver rather than one per limb — and with
        /// the one on the object whose relay the status message will arrive on.
        /// </para>
        /// </summary>
        public static StatusReceiver Ensure(GameObject body)
        {
            if (body == null) return null;

            StatusReceiver existing = body.GetComponentInParent<StatusReceiver>();
            if (existing != null) return existing;

            return RootOf(body).AddComponent<StatusReceiver>();
        }

        /// <summary>The receiver for <paramref name="body"/>, or null. Never creates one.</summary>
        public static StatusReceiver Of(GameObject body) =>
            body != null ? body.GetComponentInParent<StatusReceiver>() : null;

        /// <summary>
        /// Is <paramref name="body"/> under <paramref name="kind"/> right now?
        ///
        /// The shape every caller outside this system wants — a catch that has to slide off a
        /// slicked animal, a UI asking what to draw — without each of them repeating the lookup and
        /// the null check.
        /// </summary>
        public static bool HasStatus(GameObject body, StatusKind kind)
        {
            StatusReceiver receiver = Of(body);
            return receiver != null && receiver.Has(kind);
        }

        /// <summary>Is this body under <paramref name="kind"/>?</summary>
        public bool Has(StatusKind kind) => IsKind(kind) && active[(int)kind].Running;

        /// <summary>Seconds left of <paramref name="kind"/>, or 0 when it is not running.</summary>
        public float SecondsLeft(StatusKind kind) => IsKind(kind) ? Mathf.Max(0f, active[(int)kind].Seconds) : 0f;

        /// <summary>
        /// How much of <paramref name="kind"/>'s time is left, 0 to 1. The shape a condition that
        /// fades rather than stops is drawn from — see <see cref="InflatedStatus"/>.
        /// </summary>
        public float RemainingShare(StatusKind kind)
        {
            if (!IsKind(kind)) return 0f;

            ActiveStatus status = active[(int)kind];
            if (!status.Running || status.TotalSeconds <= 0f) return 0f;

            return Mathf.Clamp01(status.Seconds / status.TotalSeconds);
        }

        /// <summary>
        /// The kind's own scalar, for the one kind that needs more than a flag and a clock.
        /// 1 unless whatever applied it said otherwise.
        /// </summary>
        public float MagnitudeOf(StatusKind kind) => IsKind(kind) && active[(int)kind].Running
            ? active[(int)kind].Magnitude
            : 0f;

        /// <summary>Who caused <paramref name="kind"/>, on the deciding machine. Null everywhere else.</summary>
        public Transform SourceOf(StatusKind kind) => IsKind(kind) ? sources[(int)kind] : null;

        /// <summary>
        /// Put this body under <paramref name="kind"/>, or push its expiry back out if it already is.
        ///
        /// <para>
        /// The deciding machine's call, and it does not apply anything itself: it announces, and
        /// every machine — this one included, through the same handler — applies what it hears. One
        /// code path for the host, the peers and the late joiner is what stops the three drifting
        /// apart, and it is why the announcement is idempotent by construction.
        /// </para>
        /// </summary>
        /// <param name="seconds">
        /// How long, or a non-positive value for the condition's own authored duration — which is
        /// the usual case. A flamethrower does not decide how long a fire burns.
        /// </param>
        /// <param name="magnitude">The kind's own scalar. See <see cref="MagnitudeOf"/>.</param>
        /// <param name="source">Who is responsible, for damage attribution. Never leaves this machine.</param>
        public void Apply(StatusKind kind, float seconds = 0f, float magnitude = 1f, Transform source = null)
        {
            if (!Decides || !IsKind(kind)) return;

            StatusBehaviour behaviour = BehaviourFor(kind);
            if (behaviour == null) return;

            sources[(int)kind] = source;

            Announce(kind, seconds > 0f ? seconds : behaviour.DefaultSeconds, magnitude);
        }

        /// <summary>
        /// End <paramref name="kind"/> now — rain putting a fire out, a hit breaking foam off.
        /// The deciding machine's call; harmless when the condition is not running.
        /// </summary>
        public void Clear(StatusKind kind)
        {
            if (!Decides || !Has(kind)) return;

            Announce(kind, 0f, 0f);
        }

        private void OnEnable() => EnsureSubscribed();

        private void OnDisable()
        {
            if (subscribed)
            {
                this.NetOff(NetMsg.StatusSet, OnStatusSet);
                subscribed = false;
            }

            StopWatchingForJoiners();

            // Everything every running condition took gets given back. A body torn down mid-burn
            // must not leave a grip answer in a static list or a ragdoll claim nothing will ever
            // release.
            for (int i = 0; i < active.Length; i++)
                Deactivate((StatusKind)i);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            for (int i = 0; i < active.Length; i++)
            {
                if (!active[i].Running) continue;

                active[i].Seconds -= deltaTime;

                // Ticked before the expiry check so the last frame of a condition is still drawn,
                // and so a fade lands on exactly zero rather than a frame short of it.
                Behaviours[i]?.OnTick(this, deltaTime);

                // A behaviour is allowed to end itself inside its own tick — a fire on a body that
                // just died does exactly that — and on the host that clear has already run inline.
                if (!active[i].Running || active[i].Seconds > 0f) continue;

                // Expiry is announced rather than left to each machine's own clock. Every machine
                // would have reached the same moment within a frame or two, but a machine that
                // missed the start is a machine that would otherwise draw flames forever.
                if (Decides) Announce((StatusKind)i, 0f, 0f);
                else Deactivate((StatusKind)i);
            }
        }

        /// <summary>
        /// The one message: A is the kind, B is milliseconds left with 0 meaning cleared, and P.x
        /// carries the kind's own scalar for the kind that has one. Add, refresh and clear are the
        /// same fact seen at different times, so they are the same id — a machine that missed the
        /// clear is the machine still drawing the fire.
        /// </summary>
        private void Announce(StatusKind kind, float seconds, float magnitude)
        {
            EnsureSubscribed();

            this.NetToAll(NetMsg.StatusSet, new NetArg
            {
                A = (int)kind,
                B = Mathf.Max(0, Mathf.RoundToInt(seconds * 1000f)),
                P = new Vector3(magnitude, 0f, 0f),
            });
        }

        private void OnStatusSet(in NetArg arg, ulong sender)
        {
            // An id this build does not have a kind for. Not an error: ids are append-only and a
            // peer on a newer build may legitimately know a condition this one does not.
            if (arg.A < 0 || arg.A >= StatusKinds.Count) return;

            var kind = (StatusKind)arg.A;
            float seconds = arg.B / 1000f;

            if (seconds <= 0f) Deactivate(kind);
            else Activate(kind, seconds, arg.P.x);
        }

        private void Activate(StatusKind kind, float seconds, float magnitude)
        {
            int index = (int)kind;
            bool wasRunning = active[index].Running;

            active[index].Seconds = seconds;
            active[index].TotalSeconds = seconds;
            active[index].Magnitude = magnitude;

            // A refresh is not a second application. A jet of flame held on a target sends one of
            // these fifteen times a second, and a body is burning or it is not.
            if (wasRunning) return;

            Behaviours[index]?.OnApplied(this);
            StatusChanged?.Invoke(kind, true);
            BeginWatchingForJoiners();
        }

        private void Deactivate(StatusKind kind)
        {
            int index = (int)kind;
            if (!active[index].Running) return;

            // Cleared before the behaviour is told, so anything that asks this receiver from inside
            // OnCleared — a reaction module reading the flag it was just handed — reads the state
            // as it is now rather than as it was.
            active[index] = default;
            sources[index] = null;

            Behaviours[index]?.OnCleared(this);
            StatusChanged?.Invoke(kind, false);

            StopWatchingIfIdle();
        }

        private void EnsureSubscribed()
        {
            // Guarded rather than left to OnEnable alone, because this component is added at
            // runtime and Unity raises no OnEnable for an AddComponent outside play mode — and
            // because registering the same handler twice would apply every condition twice.
            if (subscribed) return;

            subscribed = true;
            this.NetOn(NetMsg.StatusSet, OnStatusSet);
        }

        /// <summary>
        /// Say it all again when somebody new arrives.
        ///
        /// <para>
        /// A joining client instantiates this body with nothing on it, and no message it missed
        /// will be replayed to it by the transport. So the deciding machine restates every running
        /// condition on connect, as one ordinary announcement per kind — the joiner then walks
        /// exactly the code path every other machine already walked, and the machines that already
        /// knew read it as a refresh and do nothing.
        /// </para>
        /// <para>
        /// Subscribed only while something is actually running, so a world full of bodies that are
        /// not on fire costs one delegate each of nothing.
        /// </para>
        /// </summary>
        private void BeginWatchingForJoiners()
        {
            if (watchingForJoiners || !Network.Server) return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            manager.OnClientConnectedCallback += OnClientJoined;
            watchingForJoiners = true;
        }

        private void StopWatchingIfIdle()
        {
            for (int i = 0; i < active.Length; i++)
                if (active[i].Running) return;

            StopWatchingForJoiners();
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
            for (int i = 0; i < active.Length; i++)
            {
                if (!active[i].Running) continue;

                // Restated as it stands NOW: the time left, and the magnitude already scaled by how
                // much of the condition has run. Sent that way the joiner's copy is continuous with
                // everyone else's instead of snapping back to full and fading again.
                var kind = (StatusKind)i;
                Announce(kind, active[i].Seconds, active[i].Magnitude * RemainingShare(kind));
            }
        }

        private StatusBehaviour BehaviourFor(StatusKind kind) => IsKind(kind) ? Behaviours[(int)kind] : null;

        private static bool IsKind(StatusKind kind) => (int)kind >= 0 && (int)kind < StatusKinds.Count;

        /// <summary>
        /// The behaviours, slotted by kind so a lookup is an index rather than a switch — which is
        /// what keeps this class from growing one branch per condition as conditions are added.
        /// Built on demand for the reason <see cref="Health"/> is.
        /// </summary>
        private StatusBehaviour[] Behaviours
        {
            get
            {
                if (behaviours != null) return behaviours;

                behaviours = new StatusBehaviour[StatusKinds.Count];
                Slot(burning);
                Slot(frozen);
                Slot(slick);
                Slot(inflated);
                Slot(foamed);
                return behaviours;
            }
        }

        private void Slot(StatusBehaviour behaviour)
        {
            if (behaviour == null) return;

            int index = (int)behaviour.Kind;
            if (index < 0 || index >= behaviours.Length) return;

            behaviours[index] = behaviour;
        }

        /// <summary>
        /// The object a body's conditions belong on: the one its messages are addressed to, which
        /// is its NetworkObject — falling back to its Rigidbody and then to the object itself for
        /// anything that has neither. Deliberately not <c>transform.root</c>: a prop parented under
        /// a chunk scene's root would put its conditions on the chunk.
        /// </summary>
        private static GameObject RootOf(GameObject part)
        {
            NetworkObject networked = part.GetComponentInParent<NetworkObject>();
            if (networked != null) return networked.gameObject;

            Rigidbody body = part.GetComponentInParent<Rigidbody>();
            return body != null ? body.gameObject : part;
        }
    }
}
