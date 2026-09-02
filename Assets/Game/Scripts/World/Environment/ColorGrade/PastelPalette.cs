using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// The default pastel screen palette and the Oklab conversions the quantize filter
    /// matches in. Kept apart from the render feature so the colour data and colour
    /// math can change without touching the render graph plumbing.
    /// </summary>
    public static class PastelPalette
    {
        // 15 hues x 6 (lightness, chroma) variants + 10 neutrals = 100 colours.
        private const int HueCount = 15;

        // From pale pastel down to dark muted, with one high-chroma variant per hue
        // for vibrancy. Chroma stays modest so the whole lattice reads as pastel.
        private static readonly Vector2[] LightnessChroma =
        {
            new Vector2(0.90f, 0.05f),
            new Vector2(0.84f, 0.09f),
            new Vector2(0.76f, 0.12f),
            new Vector2(0.72f, 0.17f),
            new Vector2(0.64f, 0.13f),
            new Vector2(0.52f, 0.10f),
        };

        private const int NeutralCount = 10;
        private const float NeutralMinL = 0.22f;
        private const float NeutralMaxL = 0.97f;

        /// <summary>
        /// A lattice over Oklch: even hue steps so fields of similar colour snap to
        /// visibly distinct flats, plus a neutral ramp so shadows keep their edges
        /// instead of collapsing into pastel mush.
        /// </summary>
        public static Color[] Default()
        {
            var colors = new Color[HueCount * LightnessChroma.Length + NeutralCount];
            int index = 0;

            for (int h = 0; h < HueCount; h++)
            {
                float hue = h * (2f * Mathf.PI / HueCount);
                foreach (Vector2 lc in LightnessChroma)
                {
                    colors[index++] = OklchToSrgb(lc.x, lc.y, hue);
                }
            }

            for (int n = 0; n < NeutralCount; n++)
            {
                float lightness = Mathf.Lerp(NeutralMinL, NeutralMaxL, n / (NeutralCount - 1f));
                colors[index++] = OklchToSrgb(lightness, 0f, 0f);
            }

            return colors;
        }

        /// <summary>
        /// Linear RGB to Oklab (Bjorn Ottosson). Must stay in lockstep with
        /// LinearToOklab in PastelQuantize.shader — the shader matches screen pixels
        /// against values this produces.
        /// </summary>
        public static Vector4 LinearToOklab(Color linear)
        {
            float l = 0.4122214708f * linear.r + 0.5363325363f * linear.g + 0.0514459929f * linear.b;
            float m = 0.2119034982f * linear.r + 0.6806995451f * linear.g + 0.1073969566f * linear.b;
            float s = 0.0883024619f * linear.r + 0.2817188376f * linear.g + 0.6299787005f * linear.b;

            l = Cbrt(l);
            m = Cbrt(m);
            s = Cbrt(s);

            return new Vector4(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s,
                0f);
        }

        private static Color OklchToSrgb(float lightness, float chroma, float hueRadians)
        {
            float a = chroma * Mathf.Cos(hueRadians);
            float b = chroma * Mathf.Sin(hueRadians);

            float l = lightness + 0.3963377774f * a + 0.2158037573f * b;
            float m = lightness - 0.1055613458f * a - 0.0638541728f * b;
            float s = lightness - 0.0894841775f * a - 1.2914855480f * b;
            l = l * l * l;
            m = m * m * m;
            s = s * s * s;

            var linear = new Color(
                Mathf.Clamp01(+4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s),
                Mathf.Clamp01(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s),
                Mathf.Clamp01(-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s));

            return linear.gamma;
        }

        private static float Cbrt(float v) => Mathf.Pow(Mathf.Max(v, 0f), 1f / 3f);
    }
}
