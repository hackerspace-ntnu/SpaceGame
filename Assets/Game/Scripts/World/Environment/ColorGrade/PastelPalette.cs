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
        // 16 hues x 6 lightnesses x 2 chromas + 12 neutrals = 204 colours.
        //
        // The hard ceiling is 256 — MaxPaletteSize in PastelQuantizeRenderFeature and
        // MAX_PALETTE in the shader. The shader walks the whole palette per pixel, so
        // the count is also the per-pixel cost of a fullscreen pass; this is not a free
        // dial to max out.
        private const int HueCount = 16;

        // Chroma is a real axis, not one value bolted to each lightness. That is what
        // decides how well the filter tells similar colours apart: with a single chroma
        // per lightness the palette is a thin shell in Oklab, so anything
        // muted-but-coloured has no entry near it and falls back to the grey ramp.
        // Measured over a sweep of sRGB, splitting chroma in two cuts the mean distance
        // to the nearest entry by about a third at the *same* entry count — far better
        // value than spending those slots on more hues.
        private static readonly float[] Lightnesses =
        {
            0.92f, 0.82f, 0.72f, 0.61f, 0.49f, 0.36f,
        };

        // Fractions of the chroma ceiling below: a muted and a vivid variant per hue and
        // lightness. Fractions rather than absolute chroma because sRGB holds very
        // different amounts of chroma per hue and lightness (0.038 at pale yellow-green,
        // 0.30 at mid blue) — a fixed pair would be clipped back to the same colour at
        // some hues and collapse into duplicate entries.
        private static readonly float[] ChromaFractions = { 0.5f, 1f };

        // Ceiling on the vivid variant. Without it the top fraction sits exactly on the
        // sRGB boundary and the palette goes neon, which is not this filter's look.
        // Raising it past ~0.22 buys no measurable separation.
        private const float ChromaCeiling = 0.20f;

        private const int NeutralCount = 12;
        private const float NeutralMinL = 0.16f;
        private const float NeutralMaxL = 0.97f;

        // Halving 16 times resolves chroma far finer than an 8-bit channel can show.
        private const int GamutFitIterations = 16;
        private const float GamutEpsilon = 1e-4f;

        /// <summary>
        /// A lattice over Oklch — hue x lightness x chroma — so fields of similar colour
        /// snap to visibly distinct flats, plus a grey ramp so shadows keep their edges
        /// instead of collapsing into mush.
        /// </summary>
        public static Color[] Default()
        {
            var colors = new Color[HueCount * Lightnesses.Length * ChromaFractions.Length + NeutralCount];
            int index = 0;

            for (int h = 0; h < HueCount; h++)
            {
                float hue = h * (2f * Mathf.PI / HueCount);
                foreach (float lightness in Lightnesses)
                {
                    // FitChroma of the ceiling *is* the in-gamut maximum: it returns the
                    // ceiling when that fits, and the gamut edge when it does not.
                    float ceiling = FitChroma(lightness, ChromaCeiling, hue);
                    foreach (float fraction in ChromaFractions)
                    {
                        colors[index++] = OklchToSrgb(lightness, ceiling * fraction, hue);
                    }
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
            Vector3 linear = OklchToLinear(lightness, FitChroma(lightness, chroma, hueRadians), hueRadians);

            return new Color(
                Mathf.Clamp01(linear.x),
                Mathf.Clamp01(linear.y),
                Mathf.Clamp01(linear.z)).gamma;
        }

        /// <summary>
        /// The largest chroma up to <paramref name="chroma"/> that still lands inside
        /// sRGB. Clamping an out-of-gamut colour instead shifts its hue and drops its
        /// lightness, so an authored entry quietly came out darker and dirtier than the
        /// ramp says. sRGB holds far less chroma in blue than in yellow, so a flat
        /// chroma ramp goes out of gamut at some hues and not others — which is why the
        /// same ramp step could look right in one hue family and wrong in the next.
        /// </summary>
        private static float FitChroma(float lightness, float chroma, float hueRadians)
        {
            if (InGamut(lightness, chroma, hueRadians))
            {
                return chroma;
            }

            float low = 0f;
            float high = chroma;
            for (int i = 0; i < GamutFitIterations; i++)
            {
                float mid = 0.5f * (low + high);
                if (InGamut(lightness, mid, hueRadians))
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        private static bool InGamut(float lightness, float chroma, float hueRadians)
        {
            Vector3 linear = OklchToLinear(lightness, chroma, hueRadians);

            return linear.x >= -GamutEpsilon && linear.x <= 1f + GamutEpsilon
                && linear.y >= -GamutEpsilon && linear.y <= 1f + GamutEpsilon
                && linear.z >= -GamutEpsilon && linear.z <= 1f + GamutEpsilon;
        }

        /// <summary>Unclamped, so <see cref="InGamut"/> can see the overflow.</summary>
        private static Vector3 OklchToLinear(float lightness, float chroma, float hueRadians)
        {
            float a = chroma * Mathf.Cos(hueRadians);
            float b = chroma * Mathf.Sin(hueRadians);

            float l = lightness + 0.3963377774f * a + 0.2158037573f * b;
            float m = lightness - 0.1055613458f * a - 0.0638541728f * b;
            float s = lightness - 0.0894841775f * a - 1.2914855480f * b;
            l = l * l * l;
            m = m * m * m;
            s = s * s * s;

            return new Vector3(
                +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        }

        private static float Cbrt(float v) => Mathf.Pow(Mathf.Max(v, 0f), 1f / 3f);
    }
}
