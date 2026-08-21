using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Top-level controller for the helmet AR HUD overlay.
    ///
    /// Drop this on a child of the PlayerHUD canvas (or any UI Canvas). It will:
    ///   - Spawn a HelmetDangerVignette child (curved warning line on each side).
    ///   - Spawn a HelmetNavMarkers child (AR markers around the screen).
    ///   - Subscribe to the assigned HealthComponent's OnDamage event and grow
    ///     both warning lines whenever the player takes damage.
    ///
    /// Marker sources (zero scene setup needed):
    ///   - Every entity with an EntityFaction component shows up automatically,
    ///     colored by its relationship to the player faction.
    ///   - Every MapPOI / MapService static marker shows up too.
    ///
    /// References auto-resolve when not set:
    ///   - playerHealth -> the health of the player this HUD belongs to
    ///   - referenceCamera -> left null, which leaves each subsystem on Camera.main
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HelmetHUDController : MonoBehaviour
    {
        [Header("References (auto-resolve at runtime if null)")]
        [Tooltip("Optional override. Left empty — which is how PlayerHUD.prefab ships — the health of " +
                 "the player this HUD hangs under is used.")]
        [SerializeField] private HealthComponent playerHealth;

        [Tooltip("Optional override handed down to the nav markers. Left empty they project through " +
                 "Camera.main, which is this peer's own view.")]
        [SerializeField] private Camera referenceCamera;

        [Header("Damage Response")]
        [Tooltip("Damage amount that maps to a full-strength hit. Smaller hits scale down linearly.")]
        [SerializeField] private int damageForFullFlash = 25;

        [Header("Subsystems")]
        [SerializeField] private HelmetDangerVignette dangerVignette;
        [SerializeField] private HelmetNavMarkers navMarkers;

        private Canvas hudCanvas;

        /// <summary>
        /// The health this HUD is currently subscribed to, which is not the same thing as
        /// <see cref="playerHealth"/>: that field is a designer's override and must survive being
        /// resolved around, so what we actually bound to is tracked separately.
        /// </summary>
        private HealthComponent boundHealth;

        /// <summary>Whose health this visor is currently reacting to. Null until one resolves.</summary>
        public HealthComponent BoundHealth => boundHealth;

        /// <summary>
        /// Points the visor at the health of the player wearing it, moving the subscription if it
        /// was pointed somewhere else. Safe to call repeatedly — see <see cref="BindHealth"/>.
        /// </summary>
        public void RebindHealth() => BindHealth(ResolveHealth());

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
            BindHealth(null);
        }

        private void EnsureCanvas()
        {
            hudCanvas = GetComponentInParent<Canvas>();
            if (hudCanvas == null)
            {
                Debug.LogWarning("[HelmetHUDController] No parent Canvas found. Place this component under a UI Canvas (e.g. PlayerHUD).", this);
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
        private HealthComponent ResolveHealth()
        {
            if (playerHealth != null) return playerHealth;

            PlayerController player = GameplayMenuScope.FindLocalPlayer(this);
            return player != null ? player.GetComponentInChildren<HealthComponent>() : null;
        }

        /// <summary>
        /// Moves the damage subscription to <paramref name="next"/>. Idempotent, and safe with
        /// null, so it doubles as the unsubscribe path — a bare += here is what makes one hit
        /// flash the visor twice.
        /// </summary>
        private void BindHealth(HealthComponent next)
        {
            // Detach first, unconditionally: that is what makes re-binding the same component a
            // no-op rather than a second subscription, and it is why there is no early return.
            if (boundHealth != null) boundHealth.OnDamage -= HandleDamage;
            boundHealth = next;
            if (boundHealth != null) boundHealth.OnDamage += HandleDamage;
        }

        private void EnsureSubsystems()
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            if (dangerVignette == null)
            {
                var go = new GameObject("DangerVignette", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var dvRt = (RectTransform)go.transform;
                dvRt.anchorMin = Vector2.zero;
                dvRt.anchorMax = Vector2.one;
                dvRt.offsetMin = Vector2.zero;
                dvRt.offsetMax = Vector2.zero;
                dangerVignette = go.AddComponent<HelmetDangerVignette>();
            }

            if (navMarkers == null)
            {
                var go = new GameObject("NavMarkers", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var nmRt = (RectTransform)go.transform;
                nmRt.anchorMin = Vector2.zero;
                nmRt.anchorMax = Vector2.one;
                nmRt.offsetMin = Vector2.zero;
                nmRt.offsetMax = Vector2.zero;
                navMarkers = go.AddComponent<HelmetNavMarkers>();
            }

            // One camera decision for the whole helmet. Pushed down only when it was authored:
            // writing a null here would clear a camera the nav markers had wired themselves, and
            // null on either of them already means "use Camera.main", live, every frame.
            if (referenceCamera != null && navMarkers != null)
                navMarkers.ReferenceCamera = referenceCamera;
        }

        private void HandleDamage(int amount)
        {
            if (dangerVignette == null) return;
            float strength = Mathf.Clamp01((float)amount / Mathf.Max(1, damageForFullFlash));
            dangerVignette.HitBoth(strength);
        }

        private void Update()
        {
            // Retried until it lands. The player object this HUD hangs under is spawned
            // asynchronously and its chunk is still streaming, so OnEnable's attempt is allowed to
            // come back empty; the cost while it does is one walk up the parent chain per frame,
            // and it stops the moment there is something to bind.
            if (boundHealth == null)
                BindHealth(ResolveHealth());

            // Nav markers still need their per-frame projection update.
            if (navMarkers != null)
                navMarkers.Tick(out _, out _);
        }
    }
}
