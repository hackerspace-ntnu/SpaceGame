using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The root of the helmet visor — the blue layer drawn on the inside of the glass.
    ///
    /// <para>
    /// Drop this on a child of the PlayerHUD canvas. Its whole job is to build the layer and own
    /// its lifecycle; it deliberately does not bind anything except the health the gauge reads.
    /// Each module below resolves its own source and binds it in its own <c>OnEnable</c>, which is
    /// what stops this class turning into the place every HUD feature ends up.
    /// </para>
    /// <para>
    /// <b>Two sublayers, not one flat set.</b> <see cref="Vitals"/> holds the readouts you play by
    /// — the gauges, the damage arcs. <see cref="Annotations"/> holds the things that describe the
    /// world — the target bracket and its look-at info box. <see cref="HelmetOverlayVisibility"/> cycles
    /// between them on H, so there is a state that quiets the world commentary without hiding the
    /// player's own health.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HelmetHUDController : MonoBehaviour
    {
        [Header("References (auto-resolve at runtime if null)")]
        [Tooltip("Optional override. Left empty — which is how PlayerHUD.prefab ships — the health " +
                 "of the player this HUD hangs under is used.")]
        [SerializeField] private HealthComponent playerHealth;

        [Header("Subsystems")]
        [SerializeField] private HelmetDangerVignette dangerVignette;

        /// <summary>Things you play by. Drawn at every detail level except Off.</summary>
        public RectTransform Vitals { get; private set; }

        /// <summary>Things that describe the world. Drawn only at the Full detail level.</summary>
        public RectTransform Annotations { get; private set; }

        /// <summary>
        /// What the integrity gauge reads. Held rather than re-created, so re-resolving the
        /// player's health does not require rebuilding the gauge that is pointed at it.
        /// </summary>
        private readonly HealthGaugeSource healthSource = new();

        /// <summary>What the oxygen gauge reads. Held for <see cref="healthSource"/>'s reason.</summary>
        private readonly OxygenGaugeSource oxygenSource = new();

        private VisorGauge integrityGauge;
        private VisorGauge oxygenGauge;
        private VisorReticle reticle;

        /// <summary>Whose health this visor is currently showing. Null until one resolves.</summary>
        public HealthComponent BoundHealth => healthSource.Health;

        /// <summary>
        /// Points the visor at the health of the player wearing it. Safe to call repeatedly — the
        /// source holds a reference rather than subscribing, so there is no double-subscription to
        /// get wrong here.
        /// </summary>
        public void RebindHealth()
        {
            PlayerController player = GameplayMenuScope.FindLocalPlayer(this);

            healthSource.Bind(ResolveHealth(player));
            if (integrityGauge != null) integrityGauge.Bind(healthSource);
            if (dangerVignette != null) dangerVignette.Watch(healthSource.Health);

            oxygenSource.Bind(player != null ? player.GetComponentInChildren<SuitOxygen>() : null);
            if (oxygenGauge != null) oxygenGauge.Bind(oxygenSource);
        }

        private void Awake()
        {
            EnsureCanvas();
            EnsureSubsystems();
        }

        private void OnEnable()
        {
            // Resolved here rather than once in Awake. This HUD is switched on from
            // PlayerController.EnablePlayer, and on a networked session that happens inside
            // OnNetworkSpawn — a moment at which Netcode has not yet published the local player
            // object. Resolution has to be allowed to fail and be retried, which is what Update
            // below does; a single Awake-time attempt is how a HUD ends up permanently blank.
            RebindHealth();
        }

        private void OnDisable()
        {
            healthSource.Bind(null);
            oxygenSource.Bind(null);
        }

        private void EnsureCanvas()
        {
            if (GetComponentInParent<Canvas>() == null)
            {
                Debug.LogWarning("[HelmetHUDController] No parent Canvas found. Place this component " +
                                 "under a UI Canvas (e.g. PlayerHUD).", this);
            }
        }

        /// <summary>
        /// Whose health this helmet shows.
        /// <para>
        /// This used to be <c>FindGameObjectWithTag("Player")</c>, which is wrong the moment a
        /// second player exists: every player object in the session carries that tag, so the search
        /// returned an arbitrary one and two of three players watched a stranger's health bar.
        /// <see cref="GameplayMenuScope.FindLocalPlayer(Component)"/> reads it off this HUD's own
        /// parent chain instead — a helmet HUD is a child of the player wearing it, and
        /// PlayerController only switches on the owner's.
        /// </para>
        /// </summary>
        private HealthComponent ResolveHealth(PlayerController player)
        {
            if (playerHealth != null) return playerHealth;

            return player != null ? player.GetComponentInChildren<HealthComponent>() : null;
        }

        private void EnsureSubsystems()
        {
            RectTransform root = (RectTransform)transform;
            Stretch(root);

            // The layer's ambient motion lives on the root, so one sway and one boot cover
            // everything the visor draws rather than each module animating itself.
            if (GetComponent<CanvasGroup>() == null) gameObject.AddComponent<CanvasGroup>();
            if (GetComponent<VisorBoot>() == null) gameObject.AddComponent<VisorBoot>();
            if (GetComponent<VisorSway>() == null) gameObject.AddComponent<VisorSway>();

            Vitals ??= MakeLayer("Vitals", root);
            Annotations ??= MakeLayer("Annotations", root);

            // The two survival numbers go in opposite top corners: furthest apart, so neither can
            // hide the other, and both out of the sightline.
            if (oxygenGauge == null)
            {
                oxygenGauge = VisorGauge.Create(Vitals, "OxygenGauge",
                                                VisorGauge.Align.Left, oxygenSource);
            }

            if (integrityGauge == null)
            {
                integrityGauge = VisorGauge.Create(Vitals, "IntegrityGauge",
                                                   VisorGauge.Align.Right, healthSource);
            }

            if (dangerVignette == null)
            {
                dangerVignette = MakeLayer("DangerVignette", Vitals)
                                 .gameObject.AddComponent<HelmetDangerVignette>();
            }

            // Annotations, not Vitals: the reticle describes something in the world rather than
            // being a readout you play by, so it goes quiet along with the middle H level.
            if (reticle == null)
            {
                reticle = MakeLayer("Reticle", Annotations)
                          .gameObject.AddComponent<VisorReticle>();
            }
        }

        private static RectTransform MakeLayer(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            Stretch(rect);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            // Retried until it lands. The player object this HUD hangs under is spawned
            // asynchronously and its chunk is still streaming, so OnEnable's attempt is allowed to
            // come back empty; the cost while it does is one walk up the parent chain per frame,
            // and it stops the moment there is something to bind.
            if (healthSource.Health == null || oxygenSource.Suit == null)
                RebindHealth();
        }
    }
}
