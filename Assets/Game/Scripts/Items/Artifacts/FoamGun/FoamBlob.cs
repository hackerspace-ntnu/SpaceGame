// One lump of foam standing in the world.
//
// It is a SPAWNED NETWORK OBJECT rather than a Present() visual, and that is the whole reason it
// costs a prefab registration: unlike a projectile, everybody has to collide with the same lump.
// A ramp one player builds is a ramp every player climbs.
//
// EVERY MACHINE RUNS THE SAME CLOCK OFF THE SAME TWO NUMBERS. The server stamps when the blob was
// laid and how long it has, both on the shared server clock, and each machine derives its growth,
// its dissolve and its silhouette from them. A late joiner receives the pair with the spawn and
// picks the blob up half grown and half dissolved, exactly where everyone else has it — which is
// what a locally started clock could not do, because it would re-grow a fifty-second-old ramp from
// nothing the moment somebody walked in the door.
//
// IT IS NOT SAVED, and that is load-bearing rather than an omission. Every blob dies inside a
// minute, so a save taken mid-spray loads a world with no foam in it. The prefab therefore must NOT
// carry a non-kinematic Rigidbody, a HealthComponent, a PickupableItem, a NavMeshAgent or a
// SceneTracked: SaveablePolicy.EnsureSpawned reads exactly those, and any one of them would give
// this object a SaveableEntity with no stamped prefab id — captured faithfully into every save file
// and dropped with a warning on every load.
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Status;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A dab of foam: swells into a sphere you can stand on, welds with its neighbours through
    /// <see cref="FoamField"/>, and dissolves when its clock runs out.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoamBlob : NetworkBehaviour
    {
        [Header("Shape")]
        [Tooltip("Radius of a fully grown blob, in metres. The mesh is a UNIT sphere, so this is " +
                 "the transform scale and nothing else — the collider grows with it because it is " +
                 "the same number, which is the only reason the shape you see and the shape you " +
                 "stand on cannot drift apart.")]
        [SerializeField, Min(0.01f)] private float radius = 0.45f;

        [Tooltip("Seconds the blob takes to swell to full size. It grows rather than appearing, " +
                 "so foam sprayed at your own feet pushes you out gently instead of launching you.")]
        [SerializeField, Min(0f)] private float growSeconds = 0.4f;

        [Tooltip("How big a blob is at the instant it lands, as a share of its full radius. Not " +
                 "zero: a SphereCollider at zero scale is degenerate, and a blob that spent a " +
                 "frame as one would be a lump nothing could stand on.")]
        [SerializeField, Range(0.01f, 0.5f)] private float birthRadiusShare = 0.15f;

        [Header("Expiry")]
        [Tooltip("Seconds of dissolve at the end of the blob's life. The shader eats it away from " +
                 "its own bubbles outward over this, so it pops apart instead of blinking out.")]
        [SerializeField, Min(0.05f)] private float dissolveSeconds = 0.8f;

        [Header("Bodies")]
        [Tooltip("What the blob looks for when it lands, to avoid shoving whatever it landed on.")]
        [SerializeField] private LayerMask bodyMask = ~0;

        [Tooltip("The sphere. Found in children when unset; it is what the dissolve is painted on.")]
        [SerializeField] private Renderer surface;

        [Tooltip("The lump's own collider, so a body already inside it at birth can be excused " +
                 "from colliding with it. Found on this object when unset.")]
        [SerializeField] private Collider shell;

        /// <summary>Scratch for the birth-time body sweep. Nothing is held between calls.</summary>
        private static readonly Collider[] Touching = new Collider[16];

        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        /// <summary>
        /// When this blob was laid, on the shared server clock, and how long it has. Replicated
        /// rather than local, for the late-joiner reason in the file header. Server-write because
        /// the expiry is the server's — see the design doc.
        /// </summary>
        private readonly NetworkVariable<double> bornAt = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> lifetime = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock dissolveBlock;
        private float drawnDissolve = -1f;

        /// <summary>
        /// Who sprayed this, on the machine that decided to. Deliberately not replicated: the only
        /// question it answers is which of a player's blobs to retire when they exceed their live
        /// budget, and that is the server's question alone.
        /// </summary>
        public GameObject Sprayer { get; private set; }

        /// <summary>The sphere's centre in world space — the dab's placement point.</summary>
        public Vector3 Centre => transform.position;

        /// <summary>How big this blob is right now, in metres. What the shader field is told.</summary>
        public float Radius { get; private set; }

        /// <summary>
        /// How big one of these ends up. Read off the prefab by the gun, so the sweep that decides
        /// who a dab encases and the lump that dab becomes are the same number.
        /// </summary>
        public float FullRadius => radius;

        /// <summary>Seconds since this blob was laid, on whatever clock both machines share.</summary>
        private float Age => Mathf.Max(0f, (float)(Now - bornAt.Value));

        /// <summary>
        /// The clock every machine measures this blob against. The server's, when there is one:
        /// two machines drawing the same lump have to agree about how old it is, and their own
        /// <c>Time.time</c> values started whenever each of them did.
        /// </summary>
        private static double Now =>
            Network.IsNetworked && NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ServerTime.Time
                : Time.timeAsDouble;

        /// <summary>
        /// Start this blob's life. Server-side, immediately after the spawn.
        /// </summary>
        /// <param name="sprayer">Whose budget this counts against, and who is blamed for a hold.</param>
        /// <param name="seconds">
        /// How long it stands: the design's sixty on the ground, ten on a body. A setback rather
        /// than a sentence for whoever is inside it, while a ramp is still there when they climb
        /// back down (GDC-L1-BAL-0004).
        /// </param>
        public void Begin(GameObject sprayer, float seconds)
        {
            Sprayer = sprayer;

            bornAt.Value = Now;
            lifetime.Value = Mathf.Max(dissolveSeconds, seconds);

            // The pose is already right, but the size is not: without this the blob spends its
            // first frame at the prefab's authored scale, which is a fully grown sphere appearing
            // inside whatever it just landed on.
            ApplyShape();
        }

        private void OnEnable()
        {
            if (surface == null) surface = GetComponentInChildren<Renderer>(true);
            if (shell == null) shell = GetComponent<Collider>();

            ApplyShape();

            // Every machine, at the moment the lump appears, and off a query whose inputs are
            // identical everywhere: the spawn position and the prefab's own radius. Foam does not
            // shove what it landed on — it holds it — so anything already standing inside a fresh
            // blob is excused from colliding with it for the blob's whole life. Foam sprayed at
            // your feet is the case the design flagged: a solid sphere appearing around a capsule
            // is a launch, and growth alone only softens it.
            ExcuseBodiesAlreadyInside();

            FoamField.Register(this);
        }

        private void OnDisable() => FoamField.Unregister(this);

        /// <summary>
        /// Take this blob out of the world now, because whoever sprayed it has laid too many.
        /// Server-side, like the expiry it stands in for.
        ///
        /// <para>
        /// It leaves the field BEFORE the despawn rather than waiting for its own OnDisable.
        /// <c>Object.Destroy</c> is deferred to the end of the frame, so a caller retiring several
        /// lumps in a row would otherwise keep finding the ones it had already retired — and, being
        /// a while loop over a count that never falls, would never come back.
        /// </para>
        /// </summary>
        public void Retire()
        {
            FoamField.Unregister(this);
            GameServices.World.Despawn(gameObject);
        }

        private void Update()
        {
            ApplyShape();

            // Expiry is the server's, on the blob itself, so one machine decides and the despawn
            // reaches the rest as an ordinary spawn message. Every peer has already drawn the
            // dissolve to the end by the time it arrives.
            if (!Network.Simulates(this)) return;
            if (lifetime.Value <= 0f || Age < lifetime.Value) return;

            GameServices.World.Despawn(gameObject);
        }

        /// <summary>
        /// Size and dissolve, both derived from the age rather than stored, so a machine that
        /// joined halfway through picks the blob up where everyone else has it.
        /// </summary>
        private void ApplyShape()
        {
            float total = lifetime.Value;

            // Nobody has stamped this blob yet: it was instantiated a moment ago and either the
            // server has not reached Begin or the client has not unpacked the spawn payload. Held
            // at its birth size rather than derived from an unstamped clock, which would read as
            // an infinitely old blob — a fully grown sphere, fully dissolved, for a frame.
            if (total <= 0f)
            {
                Radius = radius * birthRadiusShare;
                transform.localScale = Vector3.one * Radius;
                PaintDissolve(0f);
                return;
            }

            float age = Age;

            float grown = growSeconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, age / growSeconds);
            Radius = Mathf.Lerp(radius * birthRadiusShare, radius, grown);
            transform.localScale = Vector3.one * Radius;

            PaintDissolve(Mathf.Clamp01((age - (total - dissolveSeconds)) / dissolveSeconds));
        }

        /// <summary>
        /// Push the blob's own place in its life into the material.
        ///
        /// Through a property block, so the one shared foam material is untouched and twenty-four
        /// blobs do not become twenty-four material instances. Skipped when the value has not
        /// moved, because a property block write is not free and most frames of a blob's life do
        /// not change it.
        /// </summary>
        private void PaintDissolve(float dissolve)
        {
            if (surface == null || Mathf.Approximately(dissolve, drawnDissolve)) return;

            drawnDissolve = dissolve;

            dissolveBlock ??= new MaterialPropertyBlock();
            surface.GetPropertyBlock(dissolveBlock);
            dissolveBlock.SetFloat(DissolveId, dissolve);
            surface.SetPropertyBlock(dissolveBlock);
        }

        /// <summary>
        /// Stop this blob colliding with anything that was already standing where it landed.
        ///
        /// <para>
        /// <c>Physics.IgnoreCollision</c> rather than a trigger or a disabled collider: the lump
        /// still has to be solid to everybody else, and a collider that is switched off drops out
        /// of every raycast, spherecast and overlap in the game as well as out of contacts.
        /// </para>
        /// </summary>
        private void ExcuseBodiesAlreadyInside()
        {
            if (shell == null) return;

            int count = Physics.OverlapSphereNonAlloc(Centre, radius, Touching, bodyMask,
                                                      QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider other = Touching[i];
                if (other == null || other == shell || other.transform.IsChildOf(transform)) continue;

                // Bodies only. A dune is not going to be launched by a blob, and excusing the
                // ground from collision would drop the lump straight through it.
                if (other.GetComponentInParent<HealthComponent>() == null) continue;

                Physics.IgnoreCollision(shell, other, true);
            }
        }

        /// <summary>
        /// Encase <paramref name="body"/>: hand it to <see cref="StatusKind.Foamed"/> and let that
        /// system answer "held in place", how long for, and what breaks it early.
        ///
        /// <para>
        /// Server-side, called by the gun at the moment the dab lands. Here rather than in the gun
        /// so that "what a blob does to a body" reads in one place with what a blob is.
        /// </para>
        /// </summary>
        public static bool Encase(GameObject body, GameObject sprayer)
        {
            StatusReceiver receiver = StatusReceiver.Ensure(body);
            if (receiver == null) return false;

            // No duration: the condition's own authored ten seconds is the number, not the gun's.
            // A sprayer does not get to decide how long being stuck lasts.
            receiver.Apply(StatusKind.Foamed, source: sprayer != null ? sprayer.transform : null);
            return true;
        }
    }
}
