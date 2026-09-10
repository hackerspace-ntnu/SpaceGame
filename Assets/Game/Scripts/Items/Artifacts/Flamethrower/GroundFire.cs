using UnityEngine;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>
    /// One patch of fire left burning on the ground where a flame landed.
    ///
    /// <para>
    /// <b>It is not a networked object and must not become one.</b> A sweeping jet lays several of
    /// these a second and each lives five; spawning them through <c>GameServices.World.Spawn</c>
    /// would put a NetworkObject, a spawn message and a despawn message on the wire for something
    /// whose entire job is to be looked at. Instead every machine lays its own from the same aim
    /// stream, exactly the way a bullet's tracer is instantiated locally by everyone while only the
    /// authority's copy resolves anything.
    /// </para>
    /// <para>
    /// <b>Every machine sets things alight, and nothing here asks who decides.</b> That is not an
    /// oversight: <see cref="StatusReceiver.Apply"/> returns early on a machine that does not
    /// simulate the body, so the announcement is billed exactly once however many patches on
    /// however many machines make it. Running it everywhere is what puts the receiver on the same
    /// bodies everywhere — and a receiver only the server invented is a body that burns for the
    /// server and for nobody else, because the status arrives on that body's own relay.
    /// </para>
    /// <para>
    /// The patch does not bill damage itself. It announces <see cref="StatusKind.Burning"/> on
    /// whatever is standing in it and the fire's own clock does the rest — the same contract the
    /// cone uses, so standing in a patch and being swept by the jet cannot stack into double damage
    /// (GDC-L1-ARCH-0003 — the sender announces, it does not call). The announcement is repeated
    /// while a body stands here and refused while it is already alight: a fire is five seconds
    /// whoever keeps shouting about it, which is what stops a body in a patch burning for ever.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundFire : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("The flames. Played with its children, so extra layers can be hung under it in " +
                 "the builder without a new field here.")]
        [SerializeField] private ParticleSystem flame;

        [Tooltip("The scorch and smoke left on the sand. Outlives the flames.")]
        [SerializeField] private ParticleSystem scorch;

        [Tooltip("What the patch throws into the world around it. Budgeted by GroundFireField: " +
                 "only the few nearest the camera are ever switched on.")]
        [SerializeField] private Light glow;

        [Header("Burn")]
        [Tooltip("How long a patch burns once nothing is feeding it any more. Matched to the " +
                 "Burning status's own five seconds, so a body lit as the patch dies is out at " +
                 "about the moment the patch is.")]
        [SerializeField] private float lifetime = 5f;

        [Tooltip("Seconds at the end of that life over which the flames die back. The patch is " +
                 "still lit and still burning during it — this is how it goes out, not when.")]
        [SerializeField] private float fadeTime = 1.5f;

        [Tooltip("How far from the centre the patch sets things alight, in metres. Also the size " +
                 "the flames are authored at, so changing one without the other lies about reach.")]
        [SerializeField] private float radius = 1.1f;

        [Tooltip("How often the patch looks for something to set alight, in seconds. It refreshes " +
                 "a status rather than dealing damage, so a coarse interval costs nothing but how " +
                 "quickly someone walking in catches.")]
        [SerializeField] private float igniteInterval = 0.25f;

        [Tooltip("What may catch fire from the ground. Everything on these layers burns — a " +
                 "receiver is put on whatever does not already have one — so this mask is the " +
                 "only thing deciding what a patch can and cannot set alight.")]
        [SerializeField] private LayerMask bodyMask = ~0;

        [Header("Glow")]
        [Tooltip("Light intensity of a patch at full burn.")]
        [SerializeField] private float glowIntensity = 5f;

        [Tooltip("Light range of a patch at full burn, in metres.")]
        [SerializeField] private float glowRange = 6f;

        [Tooltip("How hard the glow flickers, 0 for a steady lamp.")]
        [SerializeField, Range(0f, 1f)] private float flicker = 0.35f;

        /// <summary>Seconds of burning left. Refilled to <see cref="lifetime"/> by every refresh.</summary>
        private float remaining;

        private Transform source;
        private float igniteTimer;

        /// <summary>Cleared by the field's own budget pass; the patch never turns its own light on.</summary>
        private bool glowAllowed;

        /// <summary>
        /// Has the field given this patch one of its light slots? Read by the budget's own test —
        /// the light itself is only switched on from <c>Update</c>, which does not run in the
        /// editor, so the grant is the honest thing to assert on.
        /// </summary>
        public bool GlowAllowed => glowAllowed;

        /// <summary>A per-patch offset so a line of patches does not flicker as one lamp.</summary>
        private float flickerPhase;

        private readonly Collider[] caught = new Collider[16];

        /// <summary>
        /// The flames and every layer hung under them, throttled as one. See
        /// <see cref="FlameLayers"/> for why the fade cannot simply be written into the emission
        /// multiplier.
        /// </summary>
        private FlameLayers flameLayers;

        /// <summary>The smoke, which is stopped and started with the flames but never faded.</summary>
        private FlameLayers scorchLayers;

        /// <summary>Where the patch stands. What the field merges new fire against.</summary>
        public Vector3 Centre => transform.position;

        public float Radius => radius;

        /// <summary>
        /// Still burning, as opposed to burning out. A patch that has stopped emitting is still
        /// alive — it is holding its cell while the last flames go out — and answers false here.
        /// </summary>
        public bool Alight => remaining > 0f;

        /// <summary>
        /// Light this patch, or feed one that is already alight.
        ///
        /// <para>
        /// Idempotent on purpose: a jet held on one spot calls this several times a second on the
        /// same patch, and each call is a refill of the clock rather than a second fire. It also
        /// revives a patch part-way through burning out, which is what lets a player sweep back
        /// across ground they already lit without laying a second patch on top of the first.
        /// </para>
        /// </summary>
        public void Kindle(Vector3 point, Transform igniter)
        {
            transform.position = point;
            source = igniter;
            remaining = lifetime;

            if (flame != null && !flame.isEmitting) flameLayers?.SetEmitting(true);
            if (scorch != null && !scorch.isEmitting) scorchLayers?.SetEmitting(true);
        }

        /// <summary>
        /// May the patch light the world this frame? Decided by <see cref="GroundFireField"/>,
        /// which knows how many patches are competing and where the camera is; a patch on its own
        /// knows neither.
        /// </summary>
        public void AllowGlow(bool allowed)
        {
            glowAllowed = allowed;
            if (!allowed && glow != null && glow.enabled) glow.enabled = false;
        }

        private void Awake()
        {
            flameLayers = new FlameLayers(flame);
            scorchLayers = new FlameLayers(scorch);
        }

        private void OnEnable()
        {
            igniteTimer = 0f;
            flickerPhase = Random.value * 100f;
        }

        private void Update()
        {
            if (remaining <= 0f)
            {
                BurnOut();
                return;
            }

            float deltaTime = Time.deltaTime;
            remaining -= deltaTime;

            float strength = Mathf.Clamp01(remaining / Mathf.Max(0.01f, fadeTime));
            DrawFlames(strength);
            DrawGlow(strength);

            // The moment it stops burning it stops emitting, but it keeps its cell and keeps
            // ticking until the last flames have gone out. Releasing it here instead would put the
            // patch back in the pool with fire still in the air, and the next Kindle would teleport
            // that fire across the world.
            if (remaining <= 0f) Snuff();

            igniteTimer += deltaTime;
            if (igniteTimer < igniteInterval) return;

            igniteTimer = 0f;
            Ignite();
        }

        /// <summary>
        /// The flames die back rather than blinking out: emission falls to nothing over the fade,
        /// and what is already in the air burns out at its own pace.
        /// </summary>
        private void DrawFlames(float strength)
        {
            flameLayers?.SetRate(strength);
        }

        private void DrawGlow(float strength)
        {
            if (glow == null) return;

            bool lit = glowAllowed && strength > 0.01f;
            if (glow.enabled != lit) glow.enabled = lit;
            if (!lit) return;

            // Two frequencies that do not divide into each other, so the flicker never settles into
            // a rhythm the eye starts reading as a pulse.
            float wobble = 1f
                + flicker * 0.6f * Mathf.Sin((Time.time + flickerPhase) * 27f)
                + flicker * 0.4f * Mathf.Sin((Time.time + flickerPhase) * 41.7f);

            glow.intensity = glowIntensity * strength * wobble;
            glow.range = glowRange * Mathf.Lerp(0.55f, 1f, strength);
        }

        /// <summary>
        /// Set fire to everything standing in the patch, on every machine — see the class remarks
        /// for why running it everywhere is what makes the fire agree between them. A body already
        /// alight is left as it is by the status itself, so this stays a plain announcement.
        /// </summary>
        private void Ignite()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, caught, bodyMask,
                                                      QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                // Attributed to whoever lit the patch, not to the patch: ProvocationModule reads
                // the source to work out who a creature is now afraid of, and a scrap of fire lying
                // in the sand is not something anything can run away from.
                Ignition.Light(caught[i].gameObject, source);
            }
        }

        /// <summary>Waiting for the last flames. Handed back the frame nothing is left in the air.</summary>
        private void BurnOut()
        {
            if (flameLayers != null && flameLayers.Alive) return;
            if (scorchLayers != null && scorchLayers.Alive) return;

            GroundFireField.Release(this);
        }

        /// <summary>Stop feeding the fire, and let what is lit burn out where it stands.</summary>
        private void Snuff()
        {
            remaining = 0f;
            source = null;

            flameLayers?.SetEmitting(false);
            scorchLayers?.SetEmitting(false);
            if (glow != null) glow.enabled = false;
        }

        /// <summary>
        /// Out now, with nothing left in the air. What the pool uses, and what a world unload
        /// reaches through <c>OnDisable</c>.
        /// </summary>
        public void Douse()
        {
            Snuff();

            flameLayers?.Clear();
            scorchLayers?.Clear();
        }

        private void OnDisable() => Douse();

        private void OnValidate()
        {
            lifetime = Mathf.Max(0.1f, lifetime);
            fadeTime = Mathf.Clamp(fadeTime, 0.05f, lifetime);
            radius = Mathf.Max(0.05f, radius);
            igniteInterval = Mathf.Max(0.02f, igniteInterval);
        }
    }
}
