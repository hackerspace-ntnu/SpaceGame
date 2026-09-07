// The thrown bottle, and the three seconds of inward pull it becomes.
//
// It is a SPAWNED NETWORK OBJECT rather than a Present() visual, for the reason FoamBlob is: it is a
// thing in the world that everybody collides with, and the pile it makes is a pile every player has
// to agree on. The swirl, the dust and the burst are NOT — those are SingularityShell, running
// locally on every machine off the same replicated clock.
//
// EVERY MACHINE RUNS THE SAME CLOCK OFF THE SAME FIVE NUMBERS. The server stamps the launch (origin,
// velocity, seed, time) and later the opening (centre, time), all on the shared server clock, and
// each machine derives the arc, the phase and the pose from them. A machine that joins mid-flight
// picks the bottle up exactly where everyone else has it, which a locally started clock could not
// do.
//
// ─── WHO MOVES WHAT, AND ON WHICH MACHINE ────────────────────────────────────────────────────────
//
// The sweep runs on EVERY machine, because the three kinds of target it finds are owned by three
// different ones. The branch is a property of the TARGET, exactly as it is in BlastPush:
//
//   • A PLAYER ON THEIR OWN FEET is owner-authoritative. A velocity written on the server is
//     overwritten within a tick with nothing in the console, so the pull is applied by the machine
//     that OWNS them (Network.Owns) — the server owns the fact that a well is open and when, and
//     the owner's own machine reads that and moves its own body. No message: the well is a spawned
//     object, so "a well is open here" has already replicated.
//   • ANYTHING TOWABLE — a walking animal, a mount with somebody on its back, an ornithopter — is
//     asked through ITowable by the machine that OWNS it. That is the server for a loose creature
//     and the RIDER's machine for a ridden mount, which is the whole answer to the mounted-rider
//     question; see the note on Drag.
//   • ANY OTHER RIGIDBODY is moved by the SERVER, and only by it, exactly as the repulsor gauntlet
//     and the sucker puncher already do. A networked prop then replicates; an un-networked chunk
//     prop moves on the server alone, which is the limitation those two weapons already ship and
//     not a new one this introduces.
//
// The release reuses BlastPush unchanged, so a player takes NetMsg.Flung on their own machine, a
// creature takes Knockdown or Leap, and a loose body takes a mass-scaled impulse — the same three
// routes, and the same ragdoll path, as every other blast in the game.
//
// IT IS NOT SAVED, and that is load-bearing rather than an omission. A bottle mid-effect is three
// seconds of world state; a save taken during it loads with the bottle spent and everything at
// rest. The prefab therefore must NOT carry a non-kinematic Rigidbody, a HealthComponent, a
// PickupableItem or a NavMeshAgent: SaveablePolicy.NeedsSaving reads exactly those, and any one of
// them would give this object a SaveableEntity with no stamped prefab id — captured faithfully into
// every save file and dropped with a warning on every load.
using System.Collections.Generic;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>Where a thrown bottle is in its short life.</summary>
    public enum SingularityPhase
    {
        /// <summary>In the air, on its closed-form arc, not yet open.</summary>
        Flying,

        /// <summary>Open and pulling everything loose within reach toward itself.</summary>
        Inhaling,

        /// <summary>It has let go. Everything is on its way out and the bottle is finished.</summary>
        Spent,
    }

    /// <summary>
    /// A bottled singularity: flies where it was thrown, opens where it lands, inhales for three
    /// seconds and then flings the lot back out. It does not know who threw it.
    /// </summary>
    [DisallowMultipleComponent]
    // After PlayerMovement, and that is the difference between a pull that drags a walking player
    // and one that does nothing at all. PlayerMovement.FixedUpdate ASSIGNS horizontal velocity —
    // Lerp(current, desired, 1) while grounded — so anything written before it runs is deleted
    // rather than reduced. The lasso and FlungBody carry the same attribute for the same reason.
    [DefaultExecutionOrder(200)]
    public sealed class SingularityWell : NetworkBehaviour
    {
        [Header("Reach")]
        [Tooltip("How far the pull and the fling reach from the open bottle, in metres.")]
        [SerializeField, Min(0.5f)] private float radius = 8f;

        [Tooltip("Seconds the bottle inhales before it lets go.")]
        [SerializeField, Min(0.1f)] private float inhaleSeconds = 3f;

        [Header("Pull")]
        [Tooltip("How fast the bottle drags a body at the very centre, in metres per second. Read " +
                 "against a 9 m/s sprint and a 6 m/s walk: at this figure a body near the middle " +
                 "cannot walk out and one near the rim can, which is where the item's counterplay " +
                 "lives (GDC-L1-BAL-0004) — the decision is how close you dare come, not whether " +
                 "you happen to be holding the answer.")]
        [SerializeField, Min(0f)] private float pullSpeed = 14f;

        [Tooltip("Share of the centre pull that survives at the rim. The design's 'roughly a " +
                 "quarter'.")]
        [SerializeField, Range(0f, 1f)] private float pullEdgeShare = 0.25f;

        [Tooltip("Fraction of the radius that takes the UNDIMINISHED pull. Zero falls off from the " +
                 "centre outward, which is right for a field centred on a point in the world rather " +
                 "than on somebody's chest — see RepulsorBlast.DistanceFalloff.")]
        [SerializeField, Range(0f, 1f)] private float pullCoreFraction = 0f;

        [Header("Release")]
        [Tooltip("Outward speed at the centre when the knot lets go. Repulsor-class on purpose: " +
                 "this is the same launch the thundergun hands out, and reading them against each " +
                 "other is how a player learns what being thrown looks like.")]
        [SerializeField, Min(0f)] private float flingSpeed = 48f;

        [Tooltip("Upward tilt of every fling, degrees. Load-bearing: vertical velocity is the half " +
                 "PlayerMovement never deletes, and while the victim is still RISING it holds on " +
                 "to the horizontal half too (PlayerMovement.ShouldEndCarry). Too small a tilt and " +
                 "the fling is deleted on the tick it is applied.")]
        [SerializeField] private float flingUpwardTilt = 30f;

        [Tooltip("Fraction of the radius thrown at undiminished speed. Zero on purpose: this IS a " +
                 "detonation centred on the point of contact, which is the case RepulsorBlast " +
                 "documents a zero core for.")]
        [SerializeField, Range(0f, 1f)] private float flingCoreFraction = 0f;

        [Tooltip("Fling strength at the rim relative to the centre. A body that was barely caught " +
                 "is barely thrown.")]
        [SerializeField, Range(0f, 1f)] private float flingEdgeFalloff = 0.5f;

        [Tooltip("How far off true the release fans bodies out, in degrees. Zero throws a clean " +
                 "radial star, which reads as a diagram rather than as a knot letting go.")]
        [SerializeField, Range(0f, 90f)] private float scatterDegrees = 22f;

        [Tooltip("How many times the scatter folds around the compass. One tips the whole burst to " +
                 "one side; three breaks it into gusts. It need not be a whole number — this is a " +
                 "frequency, and a fractional one simply makes the fan asymmetric.")]
        [SerializeField, Min(0.25f)] private float scatterLobes = 3f;

        [Tooltip("How long a body stays down after being thrown, seconds. Travels with the " +
                 "knockdown so every machine agrees when it ends.")]
        [SerializeField, Min(0f)] private float downedSeconds = 1.2f;

        [Tooltip("Impulse scaling reference for loose items: a body this heavy takes the full fling.")]
        [SerializeField, Min(0.1f)] private float itemMassReference = 18f;

        [Tooltip("Bounds on that mass scaling. The floor stops a crate shrugging the burst off; " +
                 "the ceiling stops a tin can leaving the chunk.")]
        [SerializeField] private Vector2 itemMassScaleRange = new Vector2(0.3f, 1.6f);

        [Header("Release — creatures")]
        [Tooltip("How far a creature that cannot be knocked down is thrown, metres. A ridden mount " +
                 "is the case: it must not go limp, because its rider is parented to the seat and " +
                 "would be dragged through the ground with it.")]
        [SerializeField, Min(0f)] private float leapDistance = 13f;

        [Tooltip("Peak height of that leap, metres.")]
        [SerializeField, Min(0f)] private float leapHeight = 3f;

        [Tooltip("How long that leap takes, seconds.")]
        [SerializeField, Min(0.05f)] private float leapDuration = 0.6f;

        [Header("Flight")]
        [Tooltip("Radius of the sweep that decides the bottle has landed, in metres. Roughly the " +
                 "bottle's own size: a point sample of something moving 18 m/s can pass clean " +
                 "through a thin surface between two physics steps.")]
        [SerializeField, Min(0.01f)] private float landingProbeRadius = 0.12f;

        [Tooltip("Seconds after which a bottle that has hit nothing opens anyway. A throw at open " +
                 "sky is an ordinary thing to do and has to end somewhere.")]
        [SerializeField, Min(0.1f)] private float maxFlightSeconds = 6f;

        [Tooltip("What the bottle can land on.")]
        [SerializeField] private LayerMask landingMask = ~0;

        [Tooltip("How far above the landing point the mouth of the bottle sits, in metres — the " +
                 "point the pull is measured from and thrown from.\n\n" +
                 "Not zero, and not decoration. The landing point is ON a surface, and a field " +
                 "centred there drags every body into the floor and casts its line-of-sight rays " +
                 "from inside the ground they would have to leave. Roughly the bottle's own height.")]
        [SerializeField, Min(0f)] private float mouthHeight = 0.15f;

        [Header("Bodies")]
        [Tooltip("What the pull and the fling look for.")]
        [SerializeField] private LayerMask catchMask = ~0;

        [Tooltip("What blocks the pull. A steady drag toward a point on the far side of a wall is a " +
                 "way into the terrain, so a body the bottle cannot see is a body it cannot hold.")]
        [SerializeField] private LayerMask sightMask = ~0;

        [Header("Life")]
        [Tooltip("Seconds the spent bottle lies there after the release before it is despawned. " +
                 "Long enough for the burst to read; short enough that nobody trips over it.")]
        [SerializeField, Min(0f)] private float spentLingerSeconds = 0.5f;

        [Header("Parts")]
        [Tooltip("The collar, the core and the effects. Found in children when unset.")]
        [SerializeField] private SingularityShell shell;

        [Tooltip("The bottle's own body, when the prefab carries one. MUST be kinematic if it is " +
                 "there at all: a non-kinematic Rigidbody would opt this object into the save " +
                 "system (see the file header), and a bare transform write to an interpolating " +
                 "body is undone within the frame anyway. Found on this object when unset.")]
        [SerializeField] private Rigidbody body;

        /// <summary>
        /// Scratch for the sweeps. Nothing is held between calls, and its length is the budget:
        /// past this many colliders in one sphere the overlap truncates and the rest of the pile is
        /// simply not inhaled that step (GDC-L1-PERF-0004 — the array is what this costs).
        /// </summary>
        private static readonly Collider[] Caught = new Collider[64];

        /// <summary>The flight trace's own buffer. Reused: this runs every step of every throw.</summary>
        private static readonly RaycastHit[] FlightHits = new RaycastHit[16];

        /// <summary>
        /// The line-of-sight check's buffer, separate from the flight's because the two ask
        /// different questions and sharing one would make either of them somebody else's business.
        /// </summary>
        private static readonly RaycastHit[] SightHits = new RaycastHit[16];

        /// <summary>
        /// One entry per body per sweep. A vehicle has a dozen colliders and a creature has more,
        /// and pulling each of them would drag the thing by its collider count.
        /// </summary>
        private static readonly HashSet<GameObject> Swept = new HashSet<GameObject>();

        /// <summary>Every well standing on this machine, oldest first. See <see cref="CountFor"/>.</summary>
        private static readonly List<SingularityWell> Live = new List<SingularityWell>();

        /// <summary>
        /// When the bottle left the hand, on the shared server clock, and the arc it left on.
        /// Replicated rather than local so a machine that joins mid-flight draws the same throw.
        /// Server-write: the throw is the server's, and a client that could restate it could put a
        /// singularity anywhere in the world.
        /// </summary>
        private readonly NetworkVariable<double> launchedAt = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> launchOrigin = new(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> launchVelocity = new(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// The owner's roll, carried here from <c>NetArg.B</c>. The whole of the release's scatter
        /// is derived from it by pure static math, so every machine fans the pile out identically.
        /// </summary>
        private readonly NetworkVariable<int> seed = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// When the bottle opened, on the shared clock. Zero means it is still in the air, and it
        /// is the one fact that decides the phase on every machine.
        /// </summary>
        private readonly NetworkVariable<double> openedAt = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Where it opened. Authoritative — the local trace below is only a prediction.</summary>
        private readonly NetworkVariable<Vector3> centre = new(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// Where this machine's own trace stopped the bottle, before the server's word arrived.
        ///
        /// <para>
        /// Every machine traces the same arc against the same geometry, so this is normally the
        /// answer the server is about to send. It exists so a client does not watch the bottle sail
        /// half a round trip through the ground and then snap back; a peer whose chunk is not loaded
        /// simply predicts nothing and adopts <see cref="centre"/> when it arrives.
        /// </para>
        /// </summary>
        private Vector3 predictedRest;
        private bool predicted;

        /// <summary>The release, once. Server-side, like the fling it performs.</summary>
        private bool released;

        /// <summary>
        /// Who threw this, on the machine that decided to. Deliberately not replicated: the only
        /// question it answers is whose live wells to retire when they exceed their budget, and
        /// that is the server's question alone. It buys the thrower nothing — the pull has no
        /// exemptions, including for them.
        /// </summary>
        public GameObject Thrower { get; private set; }

        /// <summary>
        /// The clock every machine measures this bottle against. The server's, when there is one:
        /// two machines drawing the same effect have to agree how old it is, and their own
        /// <c>Time.time</c> values started whenever each of them did.
        /// </summary>
        private static double Now =>
            Network.IsNetworked && NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ServerTime.Time
                : Time.timeAsDouble;

        /// <summary>
        /// The point the pull is measured from and the release is thrown from: the mouth of the
        /// bottle, which stands <see cref="mouthHeight"/> above where the bottle itself sits.
        /// </summary>
        private Vector3 Mouth => centre.Value + Vector3.up * mouthHeight;

        /// <summary>Seconds since the throw, or 0 before the server has stamped one.</summary>
        private float FlightAge =>
            launchedAt.Value <= 0d ? 0f : Mathf.Max(0f, (float)(Now - launchedAt.Value));

        /// <summary>Seconds since the bottle opened, or 0 while it is still in the air.</summary>
        private float OpenAge =>
            openedAt.Value <= 0d ? 0f : Mathf.Max(0f, (float)(Now - openedAt.Value));

        /// <summary>
        /// Derived, never stored. A machine that joins halfway through reads the same phase off the
        /// same two stamps as everyone else, with nothing to catch up on.
        /// </summary>
        private SingularityPhase Phase
        {
            get
            {
                if (openedAt.Value <= 0d) return SingularityPhase.Flying;
                return OpenAge < inhaleSeconds ? SingularityPhase.Inhaling : SingularityPhase.Spent;
            }
        }

        // Statics survive a world unload, a return to the menu and — with Enter Play Mode Options
        // on, which they are here — play mode itself. A list still holding last session's destroyed
        // wells would refuse the next session's throws as over budget.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Live.Clear();
            Swept.Clear();
        }

        /// <summary>
        /// How many wells <paramref name="thrower"/> has standing. Server-side only — a well's
        /// thrower is not replicated, because the budget is the only thing that ever asks.
        /// </summary>
        public static int CountFor(GameObject thrower)
        {
            if (thrower == null) return 0;

            int count = 0;
            for (int i = 0; i < Live.Count; i++)
                if (Live[i] != null && Live[i].Thrower == thrower) count++;

            return count;
        }

        /// <summary>
        /// The first well <paramref name="thrower"/> threw that is still standing, or null. Spawn
        /// order, because <see cref="Live"/> is appended to.
        /// </summary>
        public static SingularityWell OldestOf(GameObject thrower)
        {
            if (thrower == null) return null;

            for (int i = 0; i < Live.Count; i++)
                if (Live[i] != null && Live[i].Thrower == thrower) return Live[i];

            return null;
        }

        /// <summary>
        /// Start this bottle's flight. Server-side, immediately after the spawn.
        /// </summary>
        /// <param name="thrower">Whose budget this counts against. Buys no exemption from the pull.</param>
        /// <param name="origin">Where it left the hand, as the owner reported it.</param>
        /// <param name="velocity">The throw, as the owner reported it.</param>
        /// <param name="throwSeed">The owner's roll. Decides the release's scatter and nothing else.</param>
        public void Begin(GameObject thrower, Vector3 origin, Vector3 velocity, int throwSeed)
        {
            Thrower = thrower;

            launchOrigin.Value = origin;
            launchVelocity.Value = velocity;
            seed.Value = throwSeed;
            launchedAt.Value = Now;

            // The pose is already right, but only by accident of where it was spawned. Written here
            // so the first frame draws the bottle on its arc rather than at the prefab's pose.
            Place(origin, FacingAlong(velocity));
        }

        /// <summary>
        /// Take this bottle out of the world now, because whoever threw it has thrown too many.
        ///
        /// <para>
        /// It leaves the list BEFORE the despawn rather than waiting for its own OnDisable.
        /// <c>Object.Destroy</c> is deferred to the end of the frame, so a caller retiring several
        /// in a row would otherwise keep finding the ones it had already retired.
        /// </para>
        /// </summary>
        public void Retire()
        {
            Live.Remove(this);
            GameServices.World.Despawn(gameObject);
        }

        private void OnEnable()
        {
            if (shell == null) shell = GetComponentInChildren<SingularityShell>(true);
            if (body == null) body = GetComponent<Rigidbody>();

            if (!Live.Contains(this)) Live.Add(this);
        }

        private void OnDisable() => Live.Remove(this);

        private void FixedUpdate()
        {
            switch (Phase)
            {
                case SingularityPhase.Flying:
                    Fly();
                    break;

                case SingularityPhase.Inhaling:
                    Place(centre.Value, RestingRotation);
                    Inhale();
                    break;

                case SingularityPhase.Spent:
                    Place(centre.Value, RestingRotation);
                    Finish();
                    break;
            }
        }

        /// <summary>
        /// The bottle as it looks, on every machine, every frame. Nothing here decides anything —
        /// see <see cref="SingularityShell"/>.
        /// </summary>
        private void Update()
        {
            if (shell == null) return;

            SingularityPhase phase = Phase;
            float progress = phase == SingularityPhase.Inhaling
                ? Mathf.Clamp01(OpenAge / inhaleSeconds)
                : 0f;

            shell.Show(phase, progress);
        }

        // ── Flight ─────────────────────────────────────────────────────────────

        /// <summary>
        /// One step along the arc, and the trace that decides it is over.
        ///
        /// <para>
        /// Run on every machine so the throw is drawn without waiting for a round trip, but only
        /// the server's answer counts: it stamps <see cref="openedAt"/> and <see cref="centre"/>,
        /// and every machine adopts those the moment they arrive. A peer whose chunk is not loaded
        /// traces nothing, keeps drawing the arc, and snaps to the server's landing point — which is
        /// the honest picture of a machine that cannot see the ground the bottle hit.
        /// </para>
        /// </summary>
        private void Fly()
        {
            if (launchedAt.Value <= 0d) return;

            float step = Time.fixedDeltaTime;
            float now = FlightAge;

            if (predicted)
            {
                Place(predictedRest, RestingRotation);
            }
            else
            {
                Vector3 from = FlightPointAt(Mathf.Max(0f, now - step));
                Vector3 to = FlightPointAt(now);

                if (TryFindLanding(from, to, out Vector3 landing))
                {
                    predictedRest = landing;
                    predicted = true;
                    Place(landing, RestingRotation);
                }
                else
                {
                    // Along the arc it is actually on, not the direction it was thrown — a lobbed
                    // bottle should come down nose first.
                    Place(to, FacingAlong(SingularityMath.FlightVelocity(launchVelocity.Value,
                                                                        Physics.gravity, now)));
                }
            }

            if (!Network.Simulates(this)) return;

            // The server's word. A throw that has hit nothing for long enough opens where it is:
            // a bottle lobbed off a cliff has to become something rather than fall forever.
            if (!predicted && now < maxFlightSeconds) return;

            centre.Value = predicted ? predictedRest : FlightPointAt(now);
            openedAt.Value = Now;
        }

        private Vector3 FlightPointAt(float seconds) =>
            SingularityMath.FlightPoint(launchOrigin.Value, launchVelocity.Value, Physics.gravity,
                                        seconds);

        /// <summary>
        /// Did the bottle hit anything between <paramref name="from"/> and <paramref name="to"/>?
        ///
        /// <para>
        /// Swept rather than sampled: the throw covers a third of a metre per physics step, and a
        /// point test would drop it through a floor. The bottle's own colliders are excluded — asked
        /// of the COLLIDER's transform, not the hit's, because <c>RaycastHit.transform</c> is the
        /// rigidbody's and over anything with one that is its root.
        /// </para>
        /// </summary>
        private bool TryFindLanding(Vector3 from, Vector3 to, out Vector3 landing)
        {
            landing = to;

            Vector3 step = to - from;
            float distance = step.magnitude;
            if (distance < 1e-4f) return false;

            int count = Physics.SphereCastNonAlloc(from, landingProbeRadius, step / distance,
                                                   FlightHits, distance, landingMask,
                                                   QueryTriggerInteraction.Ignore);

            bool found = false;
            float nearest = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = FlightHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance >= nearest) continue;

                nearest = hit.distance;
                landing = hit.point;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// How the bottle stands once it is down: upright, turned back the way it came, so the
        /// collar faces the thrower rather than whatever slope it happened to land on.
        /// </summary>
        private Quaternion RestingRotation
        {
            get
            {
                Vector3 back = Vector3.ProjectOnPlane(-launchVelocity.Value, Vector3.up);
                return Quaternion.LookRotation(SafeForward(back), Vector3.up);
            }
        }

        /// <summary>Nose down <paramref name="velocity"/>, upright, with a degenerate one excused.</summary>
        private static Quaternion FacingAlong(Vector3 velocity) =>
            Quaternion.LookRotation(SafeForward(velocity), Vector3.up);

        private static Vector3 SafeForward(Vector3 direction) =>
            direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;

        /// <summary>
        /// Put the bottle somewhere.
        ///
        /// <para>
        /// Through the Rigidbody when the prefab carries one, because a bare <c>transform.position</c>
        /// on a body is undone within the frame in this project. The body must be kinematic — see
        /// the field's tooltip — and a kinematic body is moved with MovePosition.
        /// </para>
        /// </summary>
        private void Place(Vector3 position, Quaternion rotation)
        {
            // A settled bottle is asked for the same pose every step for three seconds. Writing it
            // back each time is a transform change the physics scene has to notice, for nothing.
            if (transform.position == position && transform.rotation == rotation) return;

            if (body != null && body.isKinematic)
            {
                body.MovePosition(position);
                body.MoveRotation(rotation);
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        // ── The pull ───────────────────────────────────────────────────────────

        /// <summary>
        /// One step of inhaling: sweep the sphere and drag everything in it toward the centre.
        ///
        /// <para>
        /// The sweep is the same shape on every machine and the routing inside <see cref="Drag"/>
        /// decides which of them acts on which body — see the file header. Nothing is taken on
        /// anybody's word: the well overlaps the world itself rather than being told who is caught,
        /// which is the same rule the foam gun follows (GDC-L1-MP-0004).
        /// </para>
        /// </summary>
        private void Inhale()
        {
            Vector3 origin = Mouth;

            int count = Physics.OverlapSphereNonAlloc(origin, radius, Caught, catchMask,
                                                      QueryTriggerInteraction.Ignore);

            Swept.Clear();

            for (int i = 0; i < count; i++)
            {
                Collider hit = Caught[i];
                if (!IsCatchable(hit, origin, out GameObject root, out Vector3 at)) continue;

                Vector3 toCentre = origin - at;
                float distance = toCentre.magnitude;

                // Already in the knot. There is no direction left to pull it in, and normalising
                // this would be a normalise of nothing.
                if (distance < 1e-3f) continue;

                float speed = pullSpeed * RepulsorBlast.DistanceFalloff(distance, radius,
                                                                        pullCoreFraction,
                                                                        pullEdgeShare);

                Drag(hit, root, toCentre / distance, speed, origin);
            }
        }

        /// <summary>
        /// Is this collider a body the bottle may act on this step, and where is it?
        ///
        /// <para>
        /// Shared by the pull and the release so the two provably agree about who is caught: a body
        /// the bottle could not hold must not be one it throws.
        /// </para>
        /// </summary>
        private bool IsCatchable(Collider hit, Vector3 origin, out GameObject root, out Vector3 at)
        {
            root = null;
            at = origin;

            if (hit == null || hit.transform.IsChildOf(transform)) return false;

            // transform.root, so a MOUNTED player folds into the vehicle they are strapped into
            // rather than appearing as a body of their own — which is exactly the answer the
            // mounted-rider question wants. See Drag.
            root = hit.transform.root.gameObject;
            if (!Swept.Add(root)) return false;

            at = hit.bounds.center;

            return HasClearLine(origin, at, root);
        }

        /// <summary>
        /// Can the bottle see <paramref name="at"/>?
        ///
        /// <para>
        /// A steady pull toward a point on the far side of a wall is a way into the terrain — the
        /// design named it as the risk — so a body the bottle cannot see is a body it does not
        /// touch. The wall stops the pull rather than the pull threading it.
        /// </para>
        /// <para>
        /// A plain <c>Linecast</c> cannot answer this: the far end of the line is inside the
        /// target's own collider and the near end is inside the bottle's, so it reports a block
        /// every time. Every hit is therefore examined, and the two ends are struck out — asked of
        /// the COLLIDER's transform, because <c>RaycastHit.transform</c> is the rigidbody's and over
        /// anything with one that is its root.
        /// </para>
        /// </summary>
        private bool HasClearLine(Vector3 origin, Vector3 at, GameObject root)
        {
            Vector3 step = at - origin;
            float distance = step.magnitude;
            if (distance < 1e-4f) return true;

            int count = Physics.RaycastNonAlloc(origin, step / distance, SightHits, distance,
                                                sightMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider blocker = SightHits[i].collider;
                if (blocker == null) continue;
                if (blocker.transform.IsChildOf(transform)) continue;
                if (blocker.transform.root.gameObject == root) continue;

                return false;
            }

            return true;
        }

        /// <summary>
        /// Drag one body toward the centre — on the machine that is entitled to move it.
        ///
        /// <para>
        /// <b>A player on their own feet</b> is owner-authoritative. Their machine does it; every
        /// other machine's copy of this well finds them and declines. Nothing is sent, because the
        /// well itself has already replicated: the server owns the fact that a singularity is open
        /// here and when, and the owner's own movement step reads that fact and moves their body
        /// (GDC-L1-MP-0004).
        /// </para>
        ///
        /// <para>
        /// <b>A MOUNTED player is never reached by that branch at all</b>, and this is the answer to
        /// the design's open question. A rider's body is kinematic and parented into the seat, so a
        /// velocity written to it is discarded in silence — and because this sweep keys on
        /// <c>transform.root</c>, a rider does not even appear as a body of their own: the vehicle
        /// they are strapped into does. The pull therefore goes through the vehicle, through
        /// <see cref="ITowable"/>, which is the one seam in the game that can move a rider. It is
        /// asked by the machine that OWNS the vehicle — the server for a loose creature, the RIDER's
        /// machine for a ridden mount, since MountNetworkSync hands ownership to the rider — and the
        /// motor refuses on <c>ExternallyPosed</c> as a second belt.
        /// </para>
        /// <para>
        /// Ignoring mounted riders was the alternative, and it was rejected: riding would have been
        /// a total, invisible immunity to the item, which is the dominant strategy a systemic effect
        /// most reliably grows (GDC-L1-SYS-0007), and it would have contradicted the release, which
        /// throws mounts already through BlastPush's leap. A vehicle whose motor does not implement
        /// ITowable — anything on a NavMeshAgentMotor, a seat on the ship — is still not dragged,
        /// and that is ITowable's own documented answer rather than an exemption invented here: it
        /// is the same set of machines a rope hangs slack from. It is still thrown at the release.
        /// </para>
        ///
        /// <para>
        /// <b>Anything else with a Rigidbody</b> is moved by the server alone, exactly as the
        /// repulsor gauntlet and the sucker puncher already move loose bodies.
        /// </para>
        /// </summary>
        private void Drag(Collider hit, GameObject root, Vector3 inward, float speed, Vector3 origin)
        {
            if (root.TryGetComponent(out PlayerMovement movement))
            {
                if (!Network.Owns(movement)) return;

                Rigidbody walker = movement.GetComponent<Rigidbody>();

                // Kinematic here means dead, loading, or a remote replica. It is kinematic on
                // purpose in each of those cases and the write would be discarded anyway.
                if (walker == null || walker.isKinematic) return;

                Haul(walker, inward, speed);
                return;
            }

            ITowable towable = root.GetComponentInChildren<ITowable>();
            if (towable != null)
            {
                if (!Network.Owns(root.transform)) return;

                Vector3 attach = towable.TowAttachPoint;
                Vector3 toCentre = origin - attach;
                if (toCentre.sqrMagnitude < 1e-6f) return;

                // One step's worth of pull rather than the centre itself. LeggedDriver clamps the
                // ask to the animal's own top speed, so handing it the far-off centre would drag
                // every walker at its maximum regardless of how far out it is and throw the
                // falloff away. A flight motor prices its own tow and ignores the distance, which
                // is ITowable's contract: the vehicle owns what a pull costs.
                //
                // The false it can answer is not acted on. A rope drops on false because it is a
                // rope; this is a three-second field, and a machine that refuses one step is asked
                // again on the next until the inhale ends.
                towable.RequestTow(attach + toCentre.normalized * (speed * Time.fixedDeltaTime));
                return;
            }

            if (!Network.Simulates(this)) return;

            Rigidbody loose = hit.attachedRigidbody;

            // A kinematic body is kinematic on purpose — a fixture, a replica, a part of a machine
            // that solves its own pose. The blast is allowed to wake one because it is an impulse
            // and it is over; a three-second drag that un-kinematicked a door would leave it loose
            // for the rest of the session.
            if (loose == null || loose.isKinematic) return;

            Haul(loose, inward, speed);
        }

        /// <summary>
        /// Pull <paramref name="target"/> toward the bottle at <paramref name="speed"/>.
        ///
        /// <para>
        /// <b>SET the inward component up to the pull speed; never add to it.</b> An addition that
        /// lands every step at fifty hertz is not a drag, it is a rocket — and the vertical half is
        /// the one PlayerMovement never deletes, so it would be a rocket in exactly the direction
        /// nothing takes back. Reading the current component first also makes the pull one-way: the
        /// bottle may haul a body that is slower than it, and must never brake one that is already
        /// falling in faster. This is the lasso's rule, and this component carries the same
        /// execution order that makes it land.
        /// </para>
        /// <para>
        /// Deliberately NOT <c>PlayerMovement.SetTethered</c>, and deliberately not
        /// <c>CarryMomentum</c>. The tether hands the whole body over the way the grappling hook
        /// does for a swing and suppresses fall damage while it is set: a player who stepped into a
        /// singularity beside a cliff would take none, which turns a three-second inconvenience into
        /// a parachute. The lasso and the leash both refused it for the same reason.
        /// </para>
        /// </summary>
        private static void Haul(Rigidbody target, Vector3 inward, float speed)
        {
            float current = Vector3.Dot(target.linearVelocity, inward);
            if (speed > current) target.linearVelocity += inward * (speed - current);
        }

        // ── The release ────────────────────────────────────────────────────────

        /// <summary>
        /// The inhale is over: throw everything out, once, and then take the bottle away.
        /// </summary>
        private void Finish()
        {
            if (!Network.Simulates(this)) return;

            if (!released)
            {
                released = true;
                Fling();
            }

            if (OpenAge < inhaleSeconds + spentLingerSeconds) return;

            // Despawned, never hidden: a collider that is switched off drops out of every raycast
            // and overlap in the game, so a "finished" bottle would still be something the aim, the
            // ground probes and the next throw's trace all trip over.
            Retire();
        }

        /// <summary>
        /// One outward frame. Server-side, and routed through <see cref="BlastPush"/> so a player,
        /// a creature and a crate each take the fling by the route their own authority allows — the
        /// same three routes, and the same ragdoll path, as every other blast in the game.
        /// </summary>
        private void Fling()
        {
            Vector3 origin = Mouth;

            int count = Physics.OverlapSphereNonAlloc(origin, radius, Caught, catchMask,
                                                      QueryTriggerInteraction.Ignore);

            Swept.Clear();

            for (int i = 0; i < count; i++)
            {
                Collider hit = Caught[i];
                if (!IsCatchable(hit, origin, out GameObject root, out Vector3 at)) continue;

                // aimBias 0 and no core: a sphere is a detonation centred on the point of contact,
                // which is the case RepulsorBlast documents both of those for. Vector3.up as the
                // "aim" is what a body standing exactly on the bottle is thrown along, and straight
                // up is the right answer for one.
                Vector3 fling = RepulsorBlast.DirectedFling(origin, Vector3.up, at, radius,
                                                            flingSpeed, flingUpwardTilt,
                                                            flingCoreFraction, flingEdgeFalloff,
                                                            aimBias: 0f);

                fling = SingularityMath.Scatter(seed.Value, fling, scatterDegrees, scatterLobes);

                BlastPush.Apply(hit, root, fling, flingSpeed,
                                BlastPush.Leap.Proportional(leapDistance, leapHeight, leapDuration),
                                itemMassReference, itemMassScaleRange, Knock);
            }
        }

        /// <summary>
        /// Tell every machine to put this body on the ground.
        ///
        /// Sent on the VICTIM's relay, and to everyone: bone transforms do not replicate, so a
        /// ragdoll is not something one machine can run on another's behalf.
        /// </summary>
        private void Knock(GameObject victim, Vector3 fling)
        {
            NetMessaging.NetSendTo(victim, NetMsg.Knockdown, new NetArg
            {
                P = fling,
                A = Mathf.RoundToInt(downedSeconds * 1000f),
            }, NetTo.All);
        }
    }
}
