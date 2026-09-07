using System;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// The lattice the pastel palette is built from, as data rather than as constants
    /// scattered through <see cref="PastelPalette"/>.
    ///
    /// <para>
    /// Deliberately not serialized anywhere. It stays a code default with exactly one
    /// committed value, so the PC and Mobile renderers cannot drift into building
    /// different palettes. The only things that change it are a code edit and the
    /// editor-only Look Lab bridge, whose values are in memory and ephemeral.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PaletteShape : IEquatable<PaletteShape>
    {
        public int hueCount;

        /// <summary>Lightness steps, palest first. Not a ramp: the committed values are
        /// hand-tuned and unevenly spaced.</summary>
        public float[] lightnesses;

        /// <summary>Fractions of the in-gamut chroma ceiling at each hue and lightness —
        /// a muted and a vivid variant. Fractions rather than absolute chroma because
        /// sRGB holds very different amounts of chroma per hue and lightness (0.038 at
        /// pale yellow-green, 0.30 at mid blue), so a fixed pair would be clipped back to
        /// the same colour at some hues and collapse into duplicates.
        ///
        /// <para>This is the axis that decides how well the filter tells similar colours
        /// apart — not <see cref="hueCount"/>. With one chroma per lightness the palette
        /// is a thin shell in Oklab, so anything muted-but-coloured has no entry near it
        /// and falls back to the grey ramp. Splitting chroma in two cut the mean distance
        /// from a sweep of sRGB to the nearest entry by about a third at the *same* entry
        /// count; spending those slots on more hues bought almost nothing.</para></summary>
        public float[] chromaFractions;

        /// <summary>Ceiling on the vivid variant, before the per-hue gamut fit. Without
        /// it the top fraction sits on the sRGB boundary and the palette goes neon, which
        /// is not this filter's look. Raising it past ~0.22 buys no measurable
        /// separation.</summary>
        public float chromaCeiling;

        public int neutralCount;
        public float neutralMinL;
        public float neutralMaxL;

        /// <summary>
        /// Exactly the values the game shipped with before <see cref="PaletteShape"/>
        /// existed. Changing any number here changes the committed look, and
        /// <c>tools/palette_golden.txt</c> will fail — which is the point.
        ///
        /// <para>A property, not a static readonly field: the arrays are mutable, and a
        /// shared instance would let one caller's edit reach every other caller.</para>
        /// </summary>
        public static PaletteShape Default => new PaletteShape
        {
            hueCount = 16,
            lightnesses = new[] { 0.92f, 0.82f, 0.72f, 0.61f, 0.49f, 0.36f },
            chromaFractions = new[] { 0.5f, 1f },
            chromaCeiling = 0.20f,
            neutralCount = 12,
            neutralMinL = 0.16f,
            neutralMaxL = 0.97f,
        };

        /// <summary>How many colours <see cref="PastelPalette.Build"/> will emit. The
        /// shader walks every entry for every pixel of a fullscreen pass, so this is also
        /// the per-pixel cost — it is not a free dial to max out, and the hard ceiling is
        /// <see cref="PastelQuantizeRenderFeature.MaxPaletteSize"/>.</summary>
        public int EntryCount =>
            hueCount * (lightnesses?.Length ?? 0) * (chromaFractions?.Length ?? 0) + neutralCount;

        /// <summary>
        /// True when this came back from JsonUtility over JSON that carried no palette
        /// at all. JsonUtility cannot report a missing field, so the caller has to tell
        /// "absent" from "present but wrong" itself — and the two deserve different
        /// answers: absent means keep what is committed, wrong means say so loudly.
        /// </summary>
        public bool IsUnset =>
            hueCount == 0 && neutralCount == 0 && lightnesses == null && chromaFractions == null;

        /// <summary>
        /// Whether this shape can be built at all. Every rejection is a shape that would
        /// otherwise produce a silently wrong palette — an empty one, one that overruns
        /// the shader's array, or a neutral ramp that divides by zero.
        /// </summary>
        public bool Validate(int maxEntries, out string error)
        {
            if (hueCount < 1)
            {
                error = $"hueCount is {hueCount}; it must be at least 1.";
                return false;
            }

            if (lightnesses == null || lightnesses.Length == 0)
            {
                error = "lightnesses is empty; the lattice needs at least one lightness step.";
                return false;
            }

            if (chromaFractions == null || chromaFractions.Length == 0)
            {
                error = "chromaFractions is empty; the lattice needs at least one chroma variant.";
                return false;
            }

            // The neutral ramp interpolates over neutralCount - 1, so a single neutral
            // divides by zero and writes a NaN colour into the palette.
            if (neutralCount < 2)
            {
                error = $"neutralCount is {neutralCount}; the grey ramp needs at least 2 steps.";
                return false;
            }

            if (chromaCeiling <= 0f)
            {
                error = $"chromaCeiling is {chromaCeiling}; it must be above 0.";
                return false;
            }

            int count = EntryCount;
            if (count > maxEntries)
            {
                error = $"the lattice would build {count} colours but the shader holds {maxEntries}.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Element-wise, because the array fields make the default struct equality a
        /// reference comparison — which would report "unchanged" for a shape whose
        /// lightness ramp was rewritten in place, and the palette would never rebuild.
        /// </summary>
        public bool Equals(PaletteShape other)
        {
            return hueCount == other.hueCount
                && neutralCount == other.neutralCount
                && neutralMinL.Equals(other.neutralMinL)
                && neutralMaxL.Equals(other.neutralMaxL)
                && chromaCeiling.Equals(other.chromaCeiling)
                && SameValues(lightnesses, other.lightnesses)
                && SameValues(chromaFractions, other.chromaFractions);
        }

        public override bool Equals(object obj) => obj is PaletteShape other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + hueCount;
                hash = hash * 31 + neutralCount;
                hash = hash * 31 + neutralMinL.GetHashCode();
                hash = hash * 31 + neutralMaxL.GetHashCode();
                hash = hash * 31 + chromaCeiling.GetHashCode();
                hash = hash * 31 + ValuesHash(lightnesses);
                hash = hash * 31 + ValuesHash(chromaFractions);
                return hash;
            }
        }

        private static bool SameValues(float[] a, float[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int ValuesHash(float[] values)
        {
            if (values == null)
            {
                return 0;
            }

            unchecked
            {
                int hash = values.Length;
                foreach (float value in values)
                {
                    hash = hash * 31 + value.GetHashCode();
                }

                return hash;
            }
        }
    }
}
