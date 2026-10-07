using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The operator's on-screen readout: the dish's azimuth and elevation, the elevation's safe range,
    /// which way the operator is commanding, and how to leave.
    ///
    /// <para>
    /// The command line is drawn from the operator's own input on this frame, not from the dish, so a
    /// press is acknowledged at once while the motor takes its time to spool up and the round trip to
    /// the server runs (<c>GDC-L1-FEEL-0002</c>: acknowledge immediately; <c>GDC-L1-FEEL-0008</c>: the
    /// heavy motor's resolution time is the design). Built in code with <see cref="UIBuilder"/> when a
    /// session opens and destroyed when it closes; local to the operator's machine.
    /// </para>
    /// </summary>
    public sealed class DishReadout : MonoBehaviour
    {
        private TextMeshProUGUI angles;
        private TextMeshProUGUI command;
        private DishRig rig;
        private string activeHex;
        private string idleHex;

        /// <summary>The two angles as the crosshair and the readout both print them.</summary>
        public static string Format(float azimuth, float elevation) =>
            $"AZ {azimuth:000.0}°   EL {elevation:00.0}°";

        public static DishReadout Show(DishRig dish, in Style style)
        {
            var go = new GameObject("DishReadout", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = style.SortingOrder;
            UIScale.Apply(go);

            RectTransform panel = UIBuilder.PinnedTop((RectTransform)go.transform, "Panel",
                                                      style.Margin, -style.Margin, style.Width, style.Height);
            UIBuilder.Solid(panel, style.Backdrop);
            UIBuilder.Column(panel, style.LineSpacing, new RectOffset(style.Padding, style.Padding, style.Padding, style.Padding));

            var readout = go.AddComponent<DishReadout>();
            readout.rig = dish;
            readout.activeHex = "#" + ColorUtility.ToHtmlStringRGB(style.CommandActive);
            readout.idleHex = "#" + ColorUtility.ToHtmlStringRGB(style.CommandIdle);
            UIBuilder.Label(Line(panel, "Title", style), "DISH CONTROL", style.TitleSize, style.Text);
            readout.angles = UIBuilder.Label(Line(panel, "Angles", style), string.Empty, style.BodySize, style.Text);
            UIBuilder.Label(Line(panel, "Range", style),
                            $"EL limits {dish.MinElevation:0}°–{dish.MaxElevation:0}°   AZ full turn",
                            style.HintSize, style.Hint);
            readout.command = UIBuilder.Label(Line(panel, "Command", style), string.Empty, style.BodySize, style.Text);
            UIBuilder.Label(Line(panel, "Hint", style), "WASD / left stick: slew    RMB / Esc / B: leave",
                            style.HintSize, style.Hint);
            return readout;
        }

        /// <summary>Repaints both live lines. <paramref name="input"/> is this frame's command.</summary>
        public void Present(Vector2 input)
        {
            if (rig == null) return;

            angles.text = Format(rig.Azimuth, rig.Elevation) + (rig.IsSlewing ? "   MOTOR" : string.Empty);
            // Plain words rather than arrow glyphs, which the default TMP font does not carry.
            command.text = $"{Arrow("LEFT", input.x < 0f)}  {Arrow("RIGHT", input.x > 0f)}   " +
                           $"{Arrow("UP", input.y > 0f)}  {Arrow("DOWN", input.y < 0f)}";
        }

        private string Arrow(string glyph, bool lit) => $"<color={(lit ? activeHex : idleHex)}>{glyph}</color>";

        private static RectTransform Line(RectTransform panel, string name, in Style style)
        {
            RectTransform rect = UIBuilder.Rect(name, panel);
            UIBuilder.FixedHeight(rect, style.LineHeight);
            return rect;
        }

        /// <summary>The readout's look. Serialized on <see cref="DishControlSession"/>, so it is tuned in the Inspector.</summary>
        [System.Serializable]
        public struct Style
        {
            public float Margin;
            public float Width;
            public float Height;
            public int Padding;
            public float LineSpacing;
            public float LineHeight;
            public int TitleSize;
            public int BodySize;
            public int HintSize;
            public int SortingOrder;
            public Color Backdrop;
            public Color Text;
            public Color Hint;
            public Color CommandActive;
            public Color CommandIdle;

            public static Style Default => new()
            {
                Margin = 32f,
                Width = 520f,
                Height = 200f,
                Padding = 16,
                LineSpacing = 4f,
                LineHeight = 30f,
                TitleSize = 26,
                BodySize = 24,
                HintSize = 18,
                SortingOrder = 500,
                Backdrop = new Color(0.03f, 0.05f, 0.06f, 0.72f),
                Text = new Color(0.85f, 1f, 0.88f, 1f),
                Hint = new Color(0.62f, 0.7f, 0.66f, 1f),
                CommandActive = new Color(1f, 0.82f, 0.48f, 1f),
                CommandIdle = new Color(0.36f, 0.4f, 0.44f, 1f),
            };
        }
    }
}
