using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The visor's design language — the third in the project, and the one that governs everything
    /// drawn on the inside of the helmet.
    ///
    /// <para>
    /// <see cref="UITheme"/> is the look of full-screen MENUS you read: near-black panels, a cold
    /// blue accent, a screen you stop and study. <see cref="VisorStyle"/> is light projected on
    /// glass a few centimetres from the player's eye — thin strokes, wide tracking, everything
    /// glowing slightly, and one colour.
    /// </para>
    /// <para>
    /// <b>Blue is the language; warm is the alarm.</b> <see cref="Ink"/> draws every normal
    /// readout. <see cref="Alarm"/> and <see cref="Critical"/> are spent ONLY on danger, and that
    /// is what makes an alarm unmissable without being loud: nothing else on the visor is ever
    /// warm. The two warm values are lifted from the model library's material table
    /// (<c>Assets/Game/Art/Models/_Source~/PALETTE.md</c>) rather than invented, so the amber on
    /// the visor is the amber that glows on the rig. The hex is written beside each one because
    /// that table is the source and this is a copy of it.
    /// </para>
    /// <para>
    /// Colour is never the only signal (<c>GDC-L1-UX-0003</c>, <c>GDC-L1-UX-0006</c>): every alarm
    /// state also changes SHAPE — see <see cref="HatchSprite"/> — and wording. A player who cannot
    /// separate the amber from the blue still reads the state.
    /// </para>
    /// <para>
    /// Nothing here is an imported asset, for the reason <see cref="UITheme"/> gives at length: the
    /// HUD has to keep working when it is dropped into a scene without a folder of PNGs arriving
    /// with it.
    /// </para>
    /// </summary>
    public static class VisorStyle
    {
        // ── The palette ──────────────────────────────────────────────────────

        /// <summary>The one colour of the visor. Every normal readout is drawn in it.</summary>
        public static readonly Color Ink = new(0.478f, 0.831f, 1f, 1f);          // #7AD4FF

        /// <summary>Ink at reading weight for secondary rows — chat, settled messages.</summary>
        public static readonly Color InkDim = new(0.478f, 0.831f, 1f, 0.62f);

        /// <summary>Ink at the edge of legibility. Empty gauge tracks, hairlines.</summary>
        public static readonly Color InkFaint = new(0.478f, 0.831f, 1f, 0.16f);

        /// <summary>Mat_Emissive_Amber, #FFB347. A gauge past its warning threshold.</summary>
        public static readonly Color Alarm = new(1f, 0.702f, 0.278f, 1f);

        /// <summary>Mat_Paint_Safety_Orange, #D9541F. Critical only: damage arcs, alarms.</summary>
        public static readonly Color Critical = new(0.851f, 0.329f, 0.122f, 1f);

        // ── Type ramp ────────────────────────────────────────────────────────
        //
        // Four sizes, in reference pixels at UIScale's 1920x1080. Wide tracking on the small sizes
        // is what makes uppercase labels read as machine print rather than as shouting.

        /// <summary>Uppercase field labels: "SUIT INTEGRITY".</summary>
        public const int LabelSize = 15;

        /// <summary>Message and chat rows.</summary>
        public const int BodySize = 17;

        /// <summary>The big number on a gauge.</summary>
        public const int ReadoutSize = 38;

        /// <summary>Distances on markers, units and suffixes beside a readout.</summary>
        public const int MicroSize = 13;

        /// <summary>Tracking applied to label-sized text, in TMP units.</summary>
        public const float LabelTracking = 12f;

        // ── Geometry ─────────────────────────────────────────────────────────

        /// <summary>Stroke weight of every line the visor draws, in reference pixels.</summary>
        public const float Stroke = 1.5f;

        /// <summary>Height of a gauge's track.</summary>
        public const int TrackHeight = 6;

        /// <summary>Width of a gauge, label and number included.</summary>
        public const float GaugeWidth = 250f;

        /// <summary>Height of a gauge's whole block: label, track, number, suffix.</summary>
        public const float GaugeHeight = 96f;

        /// <summary>Margin from the canvas edge to any pinned readout.</summary>
        public const float ScreenMargin = 64f;

        // ── Motion ───────────────────────────────────────────────────────────
        //
        // "Alive, restrained." Motion is a signal, not a texture: idle movement stays under the
        // threshold of noticing, so that the movement which MEANS something still reads.
        // GDC-L1-FEEL-0004's recorded disagreement is the binding constraint here — reflexive
        // juice obscures game state. Every value below is deliberately small.

        /// <summary>How far the layer lags behind a fast head turn, in reference pixels.</summary>
        public const float SwayPixels = 7f;

        /// <summary>How quickly the layer eases back to centre, per second.</summary>
        public const float SwayRecovery = 7f;

        /// <summary>Seconds a changed readout stays bloomed.</summary>
        public const float BloomSeconds = 0.16f;

        /// <summary>Brightness multiplier at the peak of a bloom.</summary>
        public const float BloomStrength = 1.7f;

        /// <summary>Seconds the boot sweep takes. Purely visual — it never gates input.</summary>
        public const float BootSeconds = 0.65f;

        // ── Generated sprites ────────────────────────────────────────────────
        //
        // Cached per parameter for the reason UITheme.Rounded is: a generator called from a draw
        // path allocates a texture per call, and a 9-sliced sprite whose border exceeds its rect
        // draws its corners over each other.

        private static readonly Dictionary<int, Sprite> trackByHeight = new();
        private static Sprite hatchSprite;
        private static Sprite bracketSprite;

        /// <summary>A gauge track: a rounded capsule of the given height.</summary>
        public static Sprite Track(int height)
        {
            height = Mathf.Clamp(height, 2, 64);
            if (trackByHeight.TryGetValue(height, out Sprite cached) && cached != null) return cached;

            Sprite made = Capsule(height, $"Visor_Track{height}");
            trackByHeight[height] = made;
            return made;
        }

        /// <summary>
        /// Diagonal hatching. This is the SHAPE half of an alarm — the danger zone that appears on
        /// a gauge's track the moment it crosses a threshold, so the state is legible without
        /// colour vision.
        /// </summary>
        public static Sprite HatchSprite => Ensure(ref hatchSprite, () => Hatch(32, "Visor_Hatch"));

        /// <summary>One corner of an interaction bracket. Rotated into the other three.</summary>
        public static Sprite BracketSprite => Ensure(ref bracketSprite, () => BracketCorner(32, "Visor_Bracket"));

        private static Sprite Ensure(ref Sprite cached, System.Func<Sprite> make)
        {
            if (cached == null) cached = make();
            return cached;
        }

        private static Sprite Capsule(int height, string name)
        {
            int size = Mathf.NextPowerOfTwo(Mathf.Max(4, height * 2));
            Texture2D tex = NewTexture(size, name);
            float radius = height * 0.5f;

            for (int y = 0; y < size; y++)
            {
                // Distance to the capsule's spine, which runs horizontally through the middle.
                float dy = Mathf.Abs(y - (size * 0.5f) + 0.5f);
                float alpha = Mathf.Clamp01(radius - dy + 0.5f);
                Color pixel = new(1f, 1f, 1f, alpha);

                for (int x = 0; x < size; x++)
                {
                    tex.SetPixel(x, y, pixel);
                }
            }

            tex.Apply();
            int border = Mathf.Max(1, height / 2);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(border, 0f, border, 0f));
        }

        private static Sprite Hatch(int size, string name)
        {
            Texture2D tex = NewTexture(size, name);
            tex.wrapMode = TextureWrapMode.Repeat;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // A 45-degree stripe, lit for three of every eight pixels along the diagonal.
                bool lit = (x + y) % 8 < 3;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, lit ? 1f : 0f));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite BracketCorner(int size, string name)
        {
            Texture2D tex = NewTexture(size, name);
            int arm = Mathf.Max(2, Mathf.RoundToInt(Stroke));

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Two arms meeting at the bottom-left, which is the corner this sprite is.
                bool onCorner = x < arm || y < arm;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, onCorner ? 1f : 0f));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Texture2D NewTexture(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }
    }
}
