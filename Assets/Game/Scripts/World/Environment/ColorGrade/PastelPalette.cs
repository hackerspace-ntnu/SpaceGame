using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Builds the pastel screen palette from a <see cref="PaletteShape"/>, and holds the
    /// Oklab conversion the quantize filter matches in. Kept apart from the render
    /// feature so the colour math can change without touching the render graph plumbing,
    /// and apart from <see cref="PaletteShape"/> so the numbers can be retuned without
    /// touching the math.
    /// </summary>
    public static class PastelPalette
    {
        // Halving 16 times resolves chroma far finer than an 8-bit channel can show.
        private const int GamutFitIterations = 16;
        private const float GamutEpsilon = 1e-4f;

        /// <summary>
        /// A lattice over Oklch — hue x lightness x chroma — so fields of similar colour
        /// snap to visibly distinct flats, plus a grey ramp so shadows keep their edges
        /// instead of collapsing into mush.
        ///
        /// <para>
        /// The caller is expected to have run <see cref="PaletteShape.Validate"/> first;
        /// this does no checking, because the two callers that exist both have somewhere
        /// better to report the problem than a colour array.
        /// </para>
        /// </summary>
        public static Color[] Build(in PaletteShape shape)
        {
            var colors = new Color[shape.EntryCount];
            int index = 0;

            for (int h = 0; h < shape.hueCount; h++)
            {
                float hue = h * (2f * Mathf.PI / shape.hueCount);
                foreach (float lightness in shape.lightnesses)
                {
                    // FitChroma of the ceiling *is* the in-gamut maximum: it returns the
                    // ceiling when that fits, and the gamut edge when it does not.
                    float ceiling = FitChroma(lightness, shape.chromaCeiling, hue);
                    foreach (float fraction in shape.chromaFractions)
                    {
                        colors[index++] = OklchToSrgb(lightness, ceiling * fraction, hue);
                    }
                }
            }

            for (int n = 0; n < shape.neutralCount; n++)
            {
                float lightness = Mathf.Lerp(
                    shape.neutralMinL, shape.neutralMaxL, n / (shape.neutralCount - 1f));
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
