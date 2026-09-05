using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// One readout on the visor: an uppercase label, a track with a fill, a hatched danger zone
    /// that appears when the value crosses a threshold, the number, and a word for the state.
    ///
    /// <para>
    /// Built in code, drawn in <see cref="VisorStyle"/>, bound to an
    /// <see cref="IVisorGaugeSource"/> so integrity and oxygen are two instances rather than two
    /// copies. The decisions live in static helpers so they can be reasoned about — and tested —
    /// without a canvas, since <c>Awake</c> does not run on an <c>AddComponent</c> in an EditMode
    /// test.
    /// </para>
    /// </summary>
    public class VisorGauge : MonoBehaviour
    {
        /// <summary>How urgent the value is. Drives colour, hatching and the suffix together.</summary>
        public enum State { Normal, Warning, Critical }

        /// <summary>Which screen edge the gauge is pinned to, and which way its text reads.</summary>
        public enum Align { Left, Right }

        // ── Decisions: static, so they hold without a canvas ─────────────────

        /// <summary>
        /// The fill fraction, 0 to 1. Zero when <see cref="IVisorGaugeSource.Max"/> is zero: a
        /// source that has not spawned must not draw a full bar, which reads as "you are fine".
        /// </summary>
        public static float FractionOf(IVisorGaugeSource source)
        {
            if (source == null || source.Max <= 0f) return 0f;
            return Mathf.Clamp01(source.Current / source.Max);
        }

        /// <summary>
        /// Which state the value is in.
        /// <para>
        /// The boundary belongs to the calmer state — strictly below, never at — so a value
        /// resting exactly on a threshold does not flicker between two presentations.
        /// </para>
        /// </summary>
        public static State StateOf(IVisorGaugeSource source)
        {
            if (source == null) return State.Normal;

            float fraction = FractionOf(source);
            if (fraction < source.AlarmFraction) return State.Critical;
            if (fraction < source.WarnFraction) return State.Warning;
            return State.Normal;
        }

        /// <summary>The colour half of the signal.</summary>
        public static Color ColourFor(State state) => state switch
        {
            State.Critical => VisorStyle.Critical,
            State.Warning => VisorStyle.Alarm,
            _ => VisorStyle.Ink,
        };

        /// <summary>
        /// The shape half. <c>GDC-L1-UX-0003</c>: never encode critical information in colour
        /// alone, so a gauge past its threshold also grows a hatched danger zone.
        /// </summary>
        public static bool ShowsHatch(State state) => state != State.Normal;

        /// <summary>The word half — readable with no colour vision at all.</summary>
        public static string SuffixFor(State state) => state switch
        {
            State.Critical => "CRITICAL",
            State.Warning => "LOW",
            _ => string.Empty,
        };

        // ── Drawing ──────────────────────────────────────────────────────────

        private IVisorGaugeSource source;
        private Align align;

        private CanvasGroup group;
        private TextMeshProUGUI labelText;
        private TextMeshProUGUI valueText;
        private TextMeshProUGUI suffixText;
        private Image fillImage;
        private Image hatchImage;

        private State lastState = State.Normal;
        private float bloomUntil;
        private bool built;

        /// <summary>
        /// Builds a gauge under <paramref name="parent"/> and binds it. There is no authored
        /// prefab for this — the whole visor is drawn in code, like every other HUD surface here.
        /// </summary>
        public static VisorGauge Create(RectTransform parent, string name, Align align,
                                        IVisorGaugeSource source)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);
            VisorGauge gauge = rect.gameObject.AddComponent<VisorGauge>();
            gauge.align = align;
            gauge.source = source;
            gauge.Build(rect);
            return gauge;
        }

        private void Build(RectTransform rect)
        {
            bool right = align == Align.Right;
            float margin = VisorStyle.ScreenMargin;

            // Pinned to a top corner by anchors rather than by offsets, so it stays put on every
            // canvas size UIScale can produce.
            rect.anchorMin = rect.anchorMax = new Vector2(right ? 1f : 0f, 1f);
            rect.pivot = new Vector2(right ? 1f : 0f, 1f);
            rect.anchoredPosition = new Vector2(right ? -margin : margin, -margin);
            rect.sizeDelta = new Vector2(VisorStyle.GaugeWidth, VisorStyle.GaugeHeight);

            group = rect.gameObject.AddComponent<CanvasGroup>();

            TextAlignmentOptions side = right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;

            labelText = UIBuilder.LabelIn(rect, "Label", string.Empty, VisorStyle.LabelSize,
                                          VisorStyle.InkDim, side);
            labelText.characterSpacing = VisorStyle.LabelTracking;
            PinRow((RectTransform)labelText.transform, 0f, 18f);

            RectTransform trackRect = UIBuilder.Rect("Track", rect);
            PinRow(trackRect, 24f, VisorStyle.TrackHeight);
            UIBuilder.Sprite(trackRect, VisorStyle.Track(VisorStyle.TrackHeight), VisorStyle.InkFaint);

            RectTransform fillRect = UIBuilder.Fill(UIBuilder.Rect("Fill", trackRect));
            fillImage = UIBuilder.Sprite(fillRect, VisorStyle.Track(VisorStyle.TrackHeight), VisorStyle.Ink);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = right ? 1 : 0;

            RectTransform hatchRect = UIBuilder.Fill(UIBuilder.Rect("Hatch", trackRect));
            hatchImage = UIBuilder.Sprite(hatchRect, VisorStyle.HatchSprite, VisorStyle.Alarm);
            hatchImage.type = Image.Type.Tiled;
            hatchImage.enabled = false;

            valueText = UIBuilder.LabelIn(rect, "Value", string.Empty, VisorStyle.ReadoutSize,
                                          VisorStyle.Ink, side);
            PinRow((RectTransform)valueText.transform, 36f, 46f);

            suffixText = UIBuilder.LabelIn(rect, "Suffix", string.Empty, VisorStyle.MicroSize,
                                           VisorStyle.Alarm, side);
            suffixText.characterSpacing = VisorStyle.LabelTracking;
            PinRow((RectTransform)suffixText.transform, 80f, 16f);

            built = true;
            Refresh(bloomed: false);
        }

        /// <summary>Stretches a row across the gauge's width, <paramref name="fromTop"/> down.</summary>
        private static void PinRow(RectTransform row, float fromTop, float height)
        {
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = Vector2.zero;
            row.offsetMax = Vector2.zero;
            row.anchoredPosition = new Vector2(0f, -fromTop);
            row.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>Points the gauge at a different source. Safe with null.</summary>
        public void Bind(IVisorGaugeSource next)
        {
            source = next;
            if (built) Refresh(bloomed: false);
        }

        private void Update()
        {
            if (!built) return;

            // A bloom is the gauge's whole reaction to a state change, and it is the only motion
            // here: a gauge that shimmered while nothing was happening would spend the attention
            // its alarms need. See VisorStyle's note on motion.
            bool bloomed = Time.unscaledTime < bloomUntil && !GameSettings.ReduceVisorMotion;
            Refresh(bloomed);
        }

        private void Refresh(bool bloomed)
        {
            bool available = source != null && source.Available;
            if (group != null) group.alpha = available ? 1f : 0f;
            if (!available) return;

            State state = StateOf(source);
            if (state != lastState)
            {
                lastState = state;
                bloomUntil = Time.unscaledTime + VisorStyle.BloomSeconds;
            }

            Color colour = ColourFor(state);
            if (bloomed) colour *= VisorStyle.BloomStrength;

            labelText.text = source.Label;
            valueText.text = Mathf.CeilToInt(source.Current).ToString();
            valueText.color = colour;

            fillImage.fillAmount = FractionOf(source);
            fillImage.color = colour;

            hatchImage.enabled = ShowsHatch(state);
            hatchImage.color = new Color(colour.r, colour.g, colour.b, 0.5f);

            string suffix = SuffixFor(state);
            suffixText.text = suffix;
            suffixText.color = colour;
            suffixText.gameObject.SetActive(suffix.Length > 0);
        }
    }
}
