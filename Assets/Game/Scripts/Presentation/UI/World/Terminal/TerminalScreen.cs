using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// What the standing terminal draws on its glass: a header with three tabs and a clock, one
    /// page at a time under it, and a blinking cursor. Built by <c>StandingTerminalBuilder</c>
    /// as a world-space canvas laid 2 mm over the screen plate; this only ever moves text,
    /// colours and dots around inside it.
    ///
    /// <para>
    /// Purely presentational. Which page is up comes from <see cref="TerminalConsole"/>, which
    /// replicates it, so a crewmate looking over the operator's shoulder sees the page the
    /// operator chose; what the page says comes from a <see cref="TelemetrySnapshot"/> handed
    /// in by <see cref="ShipTelemetrySource"/>, composed by <see cref="ShipTelemetry"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerminalScreen : MonoBehaviour
    {
        /// <summary>Page order — also the tab order and the 1/2/3 keys. Names live on the console, which owns the page list.</summary>
        public const int ShipPage = 0, StatusPage = 1, GpsPage = 2;

        [Header("Wiring")]
        [SerializeField] private TerminalConsole console;
        [SerializeField] private Button[] tabs;
        [SerializeField] private Image[] tabBackgrounds;
        [SerializeField] private TextMeshProUGUI[] tabLabels;
        [SerializeField] private GameObject[] pages;
        [SerializeField] private TextMeshProUGUI clockText;
        [SerializeField] private TextMeshProUGUI cursorText;

        [Header("Ship page")]
        [Tooltip("The 3D hull in the hole in the glass. Presents itself off the same reading.")]
        [SerializeField] private ShipSchematicView schematic;

        [Tooltip("The strip under the schematic: every subsystem in four words, each coloured by its own state.")]
        [SerializeField] private TextMeshProUGUI shipSummaryText;

        [Header("Status page")]
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("GPS page")]
        [SerializeField] private TextMeshProUGUI gpsText;
        [SerializeField] private RectTransform radar;

        [Header("Phosphor")]
        [SerializeField] private Color phosphor = new(0.42f, 1f, 0.6f);
        [SerializeField] private Color ink = new(0.02f, 0.075f, 0.045f);
        [SerializeField] private Color warn = new(1f, 0.7f, 0.28f);
        [SerializeField] private Color fault = new(1f, 0.27f, 0.21f);

        [Tooltip("Seconds per cursor blink.")]
        [SerializeField, Min(0.1f)] private float blinkPeriod = 1f;

        [Tooltip("Pixels (canvas units) across a crew dot on the radar.")]
        [SerializeField, Min(2f)] private float dotSize = 10f;

        private readonly List<Image> dots = new();
        private int shown = -1;

        private void Awake()
        {
            // Wired here rather than serialized: a persistent UnityEvent listener needs an
            // editor-time API to bind an int argument, and the index is the tab's own position.
            for (int i = 0; i < (tabs?.Length ?? 0); i++)
            {
                int page = i;
                if (tabs[i] != null) tabs[i].onClick.AddListener(() => TabClicked(page));
            }
        }

        private void OnEnable()
        {
            if (console != null)
            {
                console.PageChanged += ShowPage;
                ShowPage(console.Page);
            }
            else
            {
                ShowPage(ShipPage);
            }
        }

        private void OnDisable()
        {
            if (console != null) console.PageChanged -= ShowPage;
        }

        private void Update()
        {
            if (cursorText != null)
            {
                bool on = Mathf.Repeat(Time.unscaledTime, blinkPeriod) < blinkPeriod * 0.5f;
                cursorText.enabled = on;
            }
        }

        private void TabClicked(int page)
        {
            if (console != null) console.RequestPage(page);
            else ShowPage(page);
        }

        /// <summary>Puts one page up and lights its tab. Idempotent.</summary>
        public void ShowPage(int page)
        {
            page = Mathf.Clamp(page, 0, TerminalConsole.PageCount - 1);
            shown = page;

            for (int i = 0; i < (pages?.Length ?? 0); i++)
                if (pages[i] != null) pages[i].SetActive(i == page);

            for (int i = 0; i < (tabBackgrounds?.Length ?? 0); i++)
            {
                bool active = i == page;
                if (tabBackgrounds[i] != null) tabBackgrounds[i].color = active ? phosphor : Color.clear;
                if (tabLabels != null && i < tabLabels.Length && tabLabels[i] != null)
                    tabLabels[i].color = active ? ink : phosphor;
            }
        }

        /// <summary>
        /// Esc's first job, before it closes the terminal: back the schematic out of the module it
        /// is framing. True when the press was spent doing that.
        ///
        /// <para>
        /// Asked of the screen rather than of the schematic directly because only the screen knows
        /// which page is up, and a hidden page must not eat a key.
        /// </para>
        /// </summary>
        public bool TryStepBack() =>
            shown == ShipPage && schematic != null && schematic.isActiveAndEnabled &&
            schematic.TryStepBack();

        /// <summary>Redraws every page from one reading. Cheap enough to call a few times a second.</summary>
        public void Present(in TelemetrySnapshot s)
        {
            if (clockText != null) clockText.text = ShipTelemetry.Clock(s.TimeOfDay01);

            if (shipSummaryText != null) shipSummaryText.text = Summary(s);
            if (schematic != null) schematic.Present(s);

            if (statusText != null) statusText.text = ShipTelemetry.StatusPage(s);

            if (gpsText != null) gpsText.text = ShipTelemetry.GpsPage(s);
            PlotCrew(s.CrewOffsets);
        }

        /// <summary>
        /// The subsystem strip as one run of rich text, each segment in its own state's colour.
        /// Inline tags rather than a label per segment: the segments change width as the words
        /// change, and four labels laid out for the widest case leave gaps for the usual one.
        /// </summary>
        private string Summary(in TelemetrySnapshot s)
        {
            var line = new StringBuilder();

            foreach (ShipTelemetry.Segment segment in ShipTelemetry.SummarySegments(s))
            {
                if (line.Length > 0) line.Append("<color=#").Append(Hex(phosphor)).Append(">  \u00b7  </color>");
                line.Append("<color=#").Append(Hex(Colour(segment.State))).Append('>')
                    .Append(segment.Text).Append("</color>");
            }

            return line.ToString();
        }

        private Color Colour(PipState state) => state switch
        {
            PipState.Ok => phosphor,
            PipState.Warn => warn,
            _ => fault,
        };

        private static string Hex(Color colour) => ColorUtility.ToHtmlStringRGB(colour);

        /// <summary>
        /// One dot per crew member inside <see cref="ShipTelemetry.RadarRange"/>, ship-up: the
        /// plot's +y is the ship's forward. Dots are made on demand and kept; a crew that shrinks
        /// leaves its spare dots switched off rather than destroyed.
        /// </summary>
        private void PlotCrew(IReadOnlyList<Vector2> offsets)
        {
            if (radar == null) return;

            float half = Mathf.Min(radar.rect.width, radar.rect.height) * 0.5f - dotSize;
            int shownDots = 0;

            for (int i = 0; offsets != null && i < offsets.Count; i++)
            {
                Vector2 offset = offsets[i];
                if (offset.magnitude > ShipTelemetry.RadarRange) continue;

                Image dot = shownDots < dots.Count ? dots[shownDots] : MakeDot();
                dot.gameObject.SetActive(true);
                dot.rectTransform.anchoredPosition = offset / ShipTelemetry.RadarRange * half;
                shownDots++;
            }

            for (int i = shownDots; i < dots.Count; i++) dots[i].gameObject.SetActive(false);
        }

        private Image MakeDot()
        {
            var go = new GameObject("CrewDot", typeof(RectTransform), typeof(Image));
            go.layer = radar.gameObject.layer;
            go.transform.SetParent(radar, false);
            var image = go.GetComponent<Image>();
            image.color = phosphor;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = Vector2.one * dotSize;
            dots.Add(image);
            return image;
        }
    }
}
