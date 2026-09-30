using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The crew's current objective on the visor: a panel in the top-right corner under the
    /// integrity gauge — heading, title, the step's status lines, range — and a waypoint mark on
    /// the place it points at.
    ///
    /// <para>
    /// <b>Built to be noticed</b> (<c>GDC-L1-UX-0003</c>): during the opening it is the most
    /// important thing on the visor, so it gets the largest text after the gauge numbers, a backing
    /// that keeps it legible against bright sand, and an accent bar. A new objective announces
    /// itself — the heading reads <see cref="announceHeading"/>, the panel pops and the bar pulses
    /// for <see cref="announceSeconds"/> — because motion is what the eye catches, and the visor
    /// spends motion only on things that mean something.
    /// </para>
    /// <para>
    /// <b>An explicit waypoint, on purpose.</b> The desert is four kilometres across and has few
    /// landmarks, which is the exception <c>GDC-L1-LEVEL-0001</c> carves out of "guide with the
    /// environment". Where the step is about something lying in the open, the beacon
    /// (<c>ObjectiveBeacon</c>) carries the environmental half.
    /// </para>
    /// <para>
    /// On the Annotations layer: it describes the world, so it goes quiet with the middle H level
    /// along with the reticle. Reads <see cref="ObjectiveDirector"/> on this machine every frame and
    /// holds nothing of its own, so a late joiner and a reloaded world draw it with no catching up.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VisorObjective : MonoBehaviour
    {
        /// <summary>Vertical gap between the panel's lines, and between the diamond and its range.</summary>
        private const float LineGap = 4f;

        /// <summary>Room for "1234 m" under the diamond.</summary>
        private const float MarkerLabelWidth = 120f;

        /// <summary>How often the title and status are recomposed while the step stays the same.</summary>
        private const float TextRefreshSeconds = 0.1f;

        [Header("Panel")]
        [Tooltip("Distance from the top of the screen to the panel, in canvas units. Clears the " +
                 "integrity gauge above it.")]
        [SerializeField] private float topOffset = VisorStyle.ScreenMargin + VisorStyle.GaugeHeight + 28f;

        [Tooltip("Width of the panel, in canvas units. Long titles wrap.")]
        [SerializeField] private float width = 440f;

        [Tooltip("Inner margin of the panel, in canvas units.")]
        [SerializeField] private float padding = 14f;

        [Tooltip("Width of the accent bar down the panel's left edge, in canvas units.")]
        [SerializeField] private float accentWidth = 4f;

        [Tooltip("Behind the text, so blue ink stays legible against bright sand.")]
        [SerializeField] private Color backing = new(0f, 0.03f, 0.06f, 0.55f);

        [SerializeField] private int titleSize = 28;
        [SerializeField] private int statusSize = 20;

        [SerializeField] private string heading = "OBJECTIVE";

        [Tooltip("Seconds the panel takes to fade in or out when an objective starts or ends.")]
        [SerializeField, Min(0.01f)] private float fadeSeconds = 0.3f;

        [Header("New objective")]
        [SerializeField] private string announceHeading = "NEW OBJECTIVE";

        [Tooltip("How long a new objective is announced, seconds.")]
        [SerializeField, Min(0.1f)] private float announceSeconds = 3f;

        [Tooltip("How much larger the panel starts when a new objective arrives, as a share of its size.")]
        [SerializeField, Range(0f, 0.5f)] private float announcePop = 0.12f;

        [Tooltip("Seconds the pop takes to settle.")]
        [SerializeField, Min(0.01f)] private float popSeconds = 0.35f;

        [Tooltip("Accent bar pulses per second while announcing.")]
        [SerializeField, Min(0.1f)] private float pulseRate = 2f;

        [Header("Waypoint")]
        [Tooltip("Camera the waypoint is projected through. Empty means Camera.main, re-read each frame.")]
        [SerializeField] private Camera referenceCamera;

        [Tooltip("Size of the waypoint diamond, in canvas units.")]
        [SerializeField, Min(2f)] private float markerSize = 20f;

        [Tooltip("How far from the screen edge an off-screen waypoint is pinned, in screen pixels.")]
        [SerializeField, Min(0f)] private float edgeInset = 90f;

        [Tooltip("Opacity of a pinned waypoint. Dimmer than one on target, so an arrow at the edge " +
                 "is never mistaken for something in view.")]
        [SerializeField, Range(0f, 1f)] private float pinnedAlpha = 0.55f;

        private RectTransform root;
        private Canvas canvas;

        private RectTransform panel;
        private CanvasGroup panelGroup;
        private Image accent;
        private TextMeshProUGUI headingLabel;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI statusLabel;
        private TextMeshProUGUI rangeLabel;

        private RectTransform marker;
        private CanvasGroup markerGroup;
        private TextMeshProUGUI markerRange;

        private float panelAlpha;
        private ObjectiveStep shownStep;
        private float announcedAt = float.NegativeInfinity;
        private float nextTextRefresh;
        private int shownMetres = -1;

        private void Awake()
        {
            root = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            Build();
        }

        private void LateUpdate()
        {
            ObjectiveDirector director = ObjectiveDirector.Instance;
            ObjectiveStep step = director != null ? director.Current : null;
            float now = Time.unscaledTime;

            panelAlpha = Mathf.MoveTowards(panelAlpha, step != null ? 1f : 0f, Time.unscaledDeltaTime / fadeSeconds);
            panelGroup.alpha = panelAlpha;

            if (step == null)
            {
                markerGroup.alpha = 0f;
                return;
            }

            if (step != shownStep)
            {
                shownStep = step;
                announcedAt = now;
                nextTextRefresh = now;
            }

            ShowText(director.World, step, now);
            Announce(now);
            ShowWaypoint(director.World, step);
        }

        private void ShowText(ObjectiveWorld world, ObjectiveStep step, float now)
        {
            // Status can change every frame (a checklist ticking) but composing it every frame
            // would allocate a string a frame for nothing.
            if (now < nextTextRefresh) return;
            nextTextRefresh = now + TextRefreshSeconds;

            SetText(titleLabel, step.Title);
            SetText(statusLabel, step.Status(world));
        }

        private void Announce(float now)
        {
            float since = now - announcedAt;
            bool announcing = since < announceSeconds;

            SetText(headingLabel, announcing ? announceHeading : heading);

            float pop = 1f + announcePop * Mathf.Pow(1f - Mathf.Clamp01(since / popSeconds), 2f);
            panel.localScale = new Vector3(pop, pop, 1f);

            float pulse = announcing ? 0.5f + 0.5f * Mathf.Cos(since * pulseRate * 2f * Mathf.PI) : 1f;
            Color colour = VisorStyle.Ink;
            colour.a = Mathf.Lerp(VisorStyle.InkDim.a, 1f, pulse);
            accent.color = colour;
        }

        /// <summary>Writes a label only when its words change, and hides it while it has none.</summary>
        private static void SetText(TextMeshProUGUI label, string text)
        {
            bool shown = !string.IsNullOrEmpty(text);
            if (label.gameObject.activeSelf != shown) label.gameObject.SetActive(shown);
            if (shown && label.text != text) label.text = text;
        }

        private void ShowWaypoint(ObjectiveWorld world, ObjectiveStep step)
        {
            Camera view = referenceCamera != null ? referenceCamera : Camera.main;

            if (view == null || !step.TryGetWaypoint(world, out Vector3 waypoint))
            {
                markerGroup.alpha = 0f;
                ShowRange(-1);
                return;
            }

            ShowRange(Mathf.RoundToInt(Vector3.Distance(view.transform.position, waypoint)));

            Vector2 screenSize = new(view.pixelWidth, view.pixelHeight);
            Vector2 pinned = VisorProjection.PinToScreen(view.WorldToScreenPoint(waypoint), screenSize,
                                                         edgeInset, out bool onScreen);

            if (!VisorProjection.TryScreenToLayer(root, canvas, pinned, out Vector2 layerPoint))
            {
                markerGroup.alpha = 0f;
                return;
            }

            marker.anchoredPosition = layerPoint;
            markerGroup.alpha = panelAlpha * (onScreen ? 1f : pinnedAlpha);
        }

        /// <summary>Rewrites the two range readouts only when the whole-metre figure moves.</summary>
        private void ShowRange(int metres)
        {
            if (metres == shownMetres) return;

            shownMetres = metres;
            string text = metres >= 0 ? $"{metres} m" : string.Empty;
            SetText(rangeLabel, text);
            markerRange.text = text;
        }

        private void Build()
        {
            panel = UIBuilder.Rect("ObjectivePanel", root);
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
            panel.anchoredPosition = new Vector2(-VisorStyle.ScreenMargin, -topOffset);
            panel.sizeDelta = new Vector2(width, 0f);

            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            UIBuilder.Solid(panel, backing);

            int inset = Mathf.RoundToInt(padding);
            UIBuilder.Column(panel, LineGap, new RectOffset(inset + Mathf.RoundToInt(accentWidth), inset, inset, inset));
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform bar = UIBuilder.Rect("Accent", panel);
            bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(accentWidth, 0f);
            accent = UIBuilder.Solid(bar, VisorStyle.Ink);

            headingLabel = Line("Heading", VisorStyle.LabelSize, VisorStyle.InkDim);
            headingLabel.characterSpacing = VisorStyle.LabelTracking;

            titleLabel = Line("Title", titleSize, VisorStyle.Ink);
            titleLabel.fontStyle = FontStyles.Bold;

            statusLabel = Line("Status", statusSize, VisorStyle.Ink);
            rangeLabel = Line("Range", VisorStyle.MicroSize, VisorStyle.InkDim);

            marker = UIBuilder.Rect("Waypoint", root);
            marker.anchorMin = marker.anchorMax = Vector2.zero;
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(markerSize, markerSize);

            markerGroup = marker.gameObject.AddComponent<CanvasGroup>();
            markerGroup.alpha = 0f;
            markerGroup.interactable = false;
            markerGroup.blocksRaycasts = false;

            // A square turned onto its point reads as a waypoint diamond, and is not the reticle's
            // corner bracket — the two must never be confused for one another.
            RectTransform diamond = UIBuilder.Fill(UIBuilder.Rect("Diamond", marker));
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            UIBuilder.Solid(diamond, VisorStyle.Ink);

            RectTransform rangeSlot = UIBuilder.Rect("Range", marker);
            rangeSlot.anchorMin = rangeSlot.anchorMax = new Vector2(0.5f, 0f);
            rangeSlot.pivot = new Vector2(0.5f, 1f);
            rangeSlot.anchoredPosition = new Vector2(0f, -LineGap);
            rangeSlot.sizeDelta = new Vector2(MarkerLabelWidth, VisorStyle.MicroSize + LineGap);
            markerRange = UIBuilder.Label(rangeSlot, string.Empty, VisorStyle.MicroSize, VisorStyle.Ink,
                                          TextAlignmentOptions.Center);
        }

        /// <summary>
        /// One wrapping line of the panel. The label sits on the column's direct child, so the
        /// column reads its preferred height and the panel grows with the text.
        /// </summary>
        private TextMeshProUGUI Line(string name, int size, Color color)
        {
            TextMeshProUGUI label = UIBuilder.Label(UIBuilder.Rect(name, panel), string.Empty, size, color);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }
    }
}
