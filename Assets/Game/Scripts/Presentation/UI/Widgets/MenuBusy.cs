using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The menu's way of saying "this has not finished yet".
    ///
    /// <para>
    /// Two forms, because a page needs to point at a <i>place</i> and a caption needs to speak in
    /// <i>words</i>. <see cref="Rule"/> sweeps a short dash along a track — the same 3px rule the
    /// menu already draws under every <see cref="MenuField"/> and across the loading screen, with
    /// motion added, so it introduces no new visual idea. <see cref="Dots"/> cycles the trailing
    /// periods of a caption already on screen.
    /// </para>
    ///
    /// <para>
    /// There is deliberately no rotating glyph. The obvious spinners — braille cells, box-drawing
    /// arcs, chevrons — are not in LiberationSans, which is the font this project actually ships
    /// with; the suit-colour cycler already shipped once with <c>◀</c> and <c>▶</c> rendering as
    /// nothing at all. A rule is an <see cref="Image"/> and a period is ASCII, so neither can fail
    /// that way.
    /// </para>
    ///
    /// <para>
    /// The two timing functions are static and pure, so their cadence can be tested without a
    /// scene, a canvas or a frame.
    /// </para>
    /// </summary>
    public class MenuBusy : MonoBehaviour
    {
        /// <summary>One pass of the traveller, from off one end of the track to off the other.</summary>
        public const float SweepSeconds = 1.15f;

        /// <summary>How long each dot stays before the next one lands.</summary>
        public const float DotSeconds = 0.36f;

        /// <summary>The count cycles 0..this, so the caption visibly restarts rather than drifting.</summary>
        public const int MaxDots = 3;

        /// <summary>Height of a busy rule, matching the rest of the menu's rules.</summary>
        public const float RuleThickness = 3f;

        /// <summary>How much of the track the moving dash covers.</summary>
        private const float TravellerFraction = 0.28f;

        /// <summary>Below this the dash reads as a dot rather than as a sweep.</summary>
        private const float MinTravellerWidth = 48f;

        private RectTransform traveller;

        private TextMeshProUGUI label;
        private string stem = string.Empty;

        private float elapsed;

        /// <summary>The dot count currently written, so the label is only touched when it changes.</summary>
        private int shownDots = -1;

        // ──────────────────────────────────────────────────────────────────── building

        /// <summary>
        /// A sweeping rule filling <paramref name="track"/>, which the caller has already sized and
        /// placed. The rule is built as a child rather than as components on the track itself, so
        /// <see cref="Stop"/> can take the whole thing away and leave the slot behind for next time.
        /// </summary>
        public static MenuBusy Rule(RectTransform track, Color? color = null)
        {
            if (track == null) return null;

            RectTransform host = UIBuilder.Fill(UIBuilder.Rect("BusyRule", track));

            // Clips the dash at both ends, which is what makes it read as passing through rather
            // than as a bar that grows and then shrinks.
            host.gameObject.AddComponent<RectMask2D>();

            RectTransform dash = UIBuilder.Rect("Traveller", host);
            dash.anchorMin = new Vector2(0f, 0f);
            dash.anchorMax = new Vector2(0f, 1f);
            dash.pivot = new Vector2(0f, 0.5f);
            dash.anchoredPosition = Vector2.zero;
            dash.sizeDelta = Vector2.zero;
            UIBuilder.Solid(dash, color ?? MenuEntry.Idle);

            var busy = host.gameObject.AddComponent<MenuBusy>();
            busy.traveller = dash;
            return busy;
        }

        /// <summary>
        /// Animates the trailing periods of <paramref name="label"/>.
        ///
        /// <paramref name="stem"/> is the caption <b>without</b> its ellipsis — pass "Joining", not
        /// "Joining…", or the dots land after a character that already means the same thing.
        /// </summary>
        public static MenuBusy Dots(TextMeshProUGUI label, string stem)
        {
            if (label == null) return null;

            // A second animator on the same label would fight the first for its text every frame.
            // Stopped rather than merely destroyed: Destroy does not take effect until the end of
            // the frame, so the outgoing one would otherwise get one more Update in which to write
            // its own stem over the new one.
            MenuBusy running = label.GetComponent<MenuBusy>();
            if (running != null) running.Stop();

            var busy = label.gameObject.AddComponent<MenuBusy>();
            busy.label = label;
            busy.stem = stem ?? string.Empty;
            label.text = busy.stem;
            return busy;
        }

        /// <summary>
        /// Ends the animation. A rule takes its own object with it; dots leave the label standing,
        /// holding whatever it last said. Safe to call twice.
        /// </summary>
        public void Stop()
        {
            if (traveller != null)
            {
                traveller = null;
                Destroy(gameObject);
                return;
            }

            label = null;
            Destroy(this);
        }

        // ───────────────────────────────────────────────────────────────────── cadence

        /// <summary>
        /// Where the dash's left edge sits, measured from the track's left edge.
        ///
        /// It runs from fully off the left to fully off the right and restarts, rather than
        /// bouncing: a bounce has two ends to arrive at, and something that keeps arriving reads as
        /// something that keeps nearly finishing.
        /// </summary>
        public static float SweepOffset(float elapsed, float trackWidth, float travellerWidth)
        {
            float t = Mathf.Repeat(Mathf.Max(0f, elapsed), SweepSeconds) / SweepSeconds;

            // Smoothstep, so it eases out of the left edge and into the right one instead of
            // starting and stopping at full speed.
            float eased = t * t * (3f - 2f * t);

            return Mathf.Lerp(-travellerWidth, trackWidth, eased);
        }

        /// <summary>How many dots belong on the caption at <paramref name="elapsed"/> seconds.</summary>
        public static int DotCount(float elapsed) =>
            Mathf.FloorToInt(Mathf.Max(0f, elapsed) / DotSeconds) % (MaxDots + 1);

        /// <summary>The dots themselves, ready to append to a stem.</summary>
        public static string DotSuffix(float elapsed) => new string('.', DotCount(elapsed));

        // ──────────────────────────────────────────────────────────────────── animation

        private void Update()
        {
            // Unscaled: these screens can be raised over a paused game, where a scaled clock is
            // stopped and the animation would sit still while the wait itself carried on.
            elapsed += Time.unscaledDeltaTime;

            Sweep();
            Speak();
        }

        private void Sweep()
        {
            if (traveller == null) return;

            float track = ((RectTransform)transform).rect.width;
            if (track <= 0f) return;

            float width = Mathf.Max(MinTravellerWidth, track * TravellerFraction);

            traveller.sizeDelta = new Vector2(width, 0f);
            traveller.anchoredPosition = new Vector2(SweepOffset(elapsed, track, width), 0f);
        }

        private void Speak()
        {
            if (label == null) return;

            int dots = DotCount(elapsed);
            if (dots == shownDots) return;

            shownDots = dots;
            label.text = stem + new string('.', dots);
        }
    }
}
