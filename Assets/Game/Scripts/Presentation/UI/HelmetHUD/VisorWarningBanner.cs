using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The one place on the visor that ever interrupts: a single banner, top centre, showing the
    /// highest-severity <see cref="SystemMessages"/> entry at
    /// <see cref="MessageSeverity.Warning"/> or above.
    ///
    /// <para>
    /// One banner, never a stack. Two warnings at once means two systems both claiming this is the
    /// most urgent thing on screen, and showing both teaches neither — the more severe one wins,
    /// and among equals the newer. Empty until it isn't, which is what makes it worth looking at.
    /// </para>
    /// <para>
    /// <b>Colour is never the only signal</b> (<c>GDC-L1-UX-0003</c>): the banner carries a warning
    /// glyph and its own text, and pulses at <see cref="MessageSeverity.Alarm"/>. A player who
    /// cannot separate the amber from the orange still reads which one this is.
    /// </para>
    /// <para>
    /// Self-instantiating and <c>DontDestroyOnLoad</c> for the same reason as
    /// <see cref="VisorMessageStack"/>: the arrival warns about things while the player's HUD is
    /// switched off.
    /// </para>
    /// </summary>
    public class VisorWarningBanner : MonoBehaviour
    {
        private const int SortingOrder = 30950;

        private static VisorWarningBanner instance;

        /// <summary>
        /// Brought into being at boot rather than on the first warning, for
        /// <see cref="VisorMessageStack"/>'s reason: nothing calls into this class to post, so
        /// without this the first alarm would have nothing drawing it.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => _ = Instance;

        /// <summary>Builds the overlay on first ask. Never returns null.</summary>
        public static VisorWarningBanner Instance
        {
            get
            {
                if (instance != null) return instance;

                instance = FindFirstObjectByType<VisorWarningBanner>();
                if (instance != null) return instance;

                var go = new GameObject("VisorWarningBanner");
                instance = go.AddComponent<VisorWarningBanner>();
                return instance;
            }
        }

        [Tooltip("Where the banner sits, as a fraction of the screen. High and centred — read " +
                 "once, then glanced past, and clear of the crosshair.")]
        [SerializeField] private Vector2 screenAnchor = new(0.5f, 0.86f);

        [SerializeField] private Vector2 panelSize = new(560f, 44f);

        [Tooltip("Seconds to fade in and out.")]
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.16f;

        [Tooltip("Pulses per second at Alarm severity. Slow enough to read the words through.")]
        [SerializeField, Min(0f)] private float alarmPulseHz = 1.6f;

        [Tooltip("How far the alarm pulse dips the banner's opacity. Never to zero — a banner " +
                 "that blinks out is unreadable exactly when it matters most.")]
        [SerializeField, Range(0f, 0.8f)] private float alarmPulseDepth = 0.35f;

        private CanvasGroup group;
        private RectTransform panel;
        private Image background;
        private Image edge;
        private TextMeshProUGUI label;

        private float alpha;
        private bool shown = true;

        /// <summary>
        /// Hides or shows the banner. Driven by <see cref="HelmetOverlayVisibility"/> so H reaches
        /// it even though it is not parented under the visor.
        /// </summary>
        public static void SetShown(bool shown)
        {
            // Not Instance: hiding must never be the thing that builds the overlay.
            if (instance != null) instance.shown = shown;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            Build();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            SystemMessages.DropExpired();

            // Asked unconditionally rather than behind `shown &&`: short-circuiting past a
            // TryGet leaves its out parameter only conditionally assigned, which the compiler
            // rejects outright.
            bool hasBanner = SystemMessages.TryGetBanner(out SystemMessages.Entry banner);
            bool wanted = shown && hasBanner;
            bool critical = wanted && banner.Severity == MessageSeverity.Alarm;

            if (wanted)
            {
                Color colour = critical ? VisorStyle.Critical : VisorStyle.Alarm;

                // The mark is the SHAPE half of the signal — one bang for a warning, two for an
                // alarm — so the two states are distinguishable with no colour vision at all.
                //
                // ASCII on purpose. LiberationSans has no warning triangle (and no arrows, and no
                // box-drawing); a glyph it lacks renders as literally nothing, which is how a
                // banner ends up silently missing the one mark that says how bad this is. See the
                // "no glyph spinners" gotcha in UI.md.
                label.text = $"{(critical ? "!!" : "!")}  {banner.Text}";
                label.color = colour;
                edge.color = colour;
                background.color = new Color(colour.r, colour.g, colour.b, 0.18f);
            }

            float target = wanted ? 1f : 0f;
            if (!Mathf.Approximately(alpha, target))
                alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeDuration);

            float pulse = 1f;
            if (critical && !GameSettings.ReduceVisorMotion)
            {
                // Sine rather than a square blink, and never down to zero: the banner breathes so
                // it draws the eye, but stays readable at every point in the cycle.
                float wave = (Mathf.Sin(Time.unscaledTime * alarmPulseHz * Mathf.PI * 2f) + 1f) * 0.5f;
                pulse = 1f - (alarmPulseDepth * wave);
            }

            group.alpha = alpha * pulse;
            panel.gameObject.SetActive(group.alpha > 0.001f);
        }

        private void Build()
        {
            var canvasObject = new GameObject("BannerCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Under LetterboxOverlay's 32000, above the message stack: an alarm outranks a notice.
            canvas.sortingOrder = SortingOrder;

            // UIScale is the only thing in the project that may configure a CanvasScaler.
            UIScale.Configure(canvasObject.GetComponent<CanvasScaler>());

            group = canvasObject.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            panel = UIBuilder.Rect("Banner", (RectTransform)canvasObject.transform);
            panel.anchorMin = panel.anchorMax = screenAnchor;
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = panelSize;

            background = UIBuilder.Sprite(panel, VisorStyle.Track(8), new Color(1f, 1f, 1f, 0.18f));

            RectTransform edgeRect = UIBuilder.Fill(UIBuilder.Rect("Edge", panel));
            edge = UIBuilder.Sprite(edgeRect, UITheme.Edge(4), VisorStyle.Alarm);

            label = UIBuilder.LabelIn(panel, "Text", string.Empty, VisorStyle.BodySize,
                                      VisorStyle.Alarm, TextAlignmentOptions.Center);
            label.characterSpacing = VisorStyle.LabelTracking;

            panel.gameObject.SetActive(false);
        }
    }
}
