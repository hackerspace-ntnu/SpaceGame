using UnityEngine;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>
    /// Turns the portrait passes into a screenprint: flat inks chosen per material, the lit tone
    /// cut into a highlight, a band of shadow dots and solid black, eyes kept in full colour, and
    /// ink outlines along silhouettes, material borders, creases and depth steps. The fills sit a
    /// few pixels off register from the outlines, like a print pulled by hand.
    /// </summary>
    internal static class PosterStylizer
    {
        public static PosterImage Stylize(PortraitPasses passes, Color accent, WantedPosterStyle style)
        {
            int w = passes.Width, h = passes.Height, n = w * h;
            var role = new InkRole?[n];
            for (int i = 0; i < n; i++)
                if (passes.Material[i] >= 0) role[i] = passes.Roles[passes.Material[i]];

            PosterMask figure = PosterMask.Where(w, h, i => role[i].HasValue);
            PosterMask skin = PosterMask.Where(w, h, i => role[i] == InkRole.Skin);
            PosterMask warm = PosterMask.Where(w, h, i => role[i] == InkRole.Accent);
            PosterMask neutral = PosterMask.Where(w, h, i => role[i] == InkRole.Neutral);
            PosterMask eyes = PosterMask.Where(w, h, i => role[i] == InkRole.Eye);

            PosterMask tone = Tone(passes, skin, style);
            PosterMask high = Band(tone, figure, style.highlight, above: true, style.bandCleanup);
            PosterMask shadow = Band(tone, figure, style.shadow, above: false, style.bandCleanup);
            PosterMask deep = Band(tone, figure, style.deep, above: false, style.bandCleanup * 2);

            var print = new PosterImage(w, h, Color.clear);

            // Fills, off register. Undyed cloth and accent highlights share one pale tint of the
            // accent, so nothing on the figure prints as bare cream.
            Vector2Int r = style.register;
            Color wash = Color.Lerp(style.cream, accent, style.washTint);
            print.Paint(neutral.Shift(r.x, r.y), wash);
            print.Paint(warm.Shift(r.x, r.y), accent);
            print.Paint(warm.Times(high).Shift(r.x, r.y), wash);
            print.Paint(skin.Shift(r.x, r.y), style.slate);
            print.Paint(skin.Times(high).Shift(r.x, r.y), style.slateLight);

            // Shadows: one band of dots (denser on undyed cloth), then solid ink.
            var coverage = new PosterMask(w, h);
            for (int i = 0; i < n; i++)
                coverage.Values[i] = shadow.Values[i] * (neutral.Values[i] > 0f ? style.neutralShadowCoverage : style.shadowCoverage);
            print.Paint(PosterShapes.Halftone(coverage, style.shadowCell * w, 45f), style.ink);
            print.Paint(deep.Minus(eyes), style.ink);

            // Eyes in their own colours.
            for (int i = 0; i < n; i++)
            {
                if (eyes.Values[i] <= 0f) continue;
                Color c = passes.Shade.Pixels[i] * style.eyeBoost;
                print.Pixels[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }

            // Outlines, and a heavier line round the outside.
            PosterMask lines = Edges(passes, style).Dilate(style.lineRadius);
            PosterMask outside = figure.Dilate(style.silhouetteRadius).Minus(figure);
            print.Paint(lines.Union(outside), style.ink);
            return print;
        }

        /// <summary>
        /// Lit tone, 0 black to 1 white. Skin reads its brightest channel, so a saturated blue
        /// body is a mid tone rather than a near-black one.
        /// </summary>
        private static PosterMask Tone(PortraitPasses passes, PosterMask skin, WantedPosterStyle style)
        {
            var tone = new PosterMask(passes.Width, passes.Height);
            for (int i = 0; i < tone.Values.Length; i++)
            {
                Color c = passes.Shade.Pixels[i];
                float t = skin.Values[i] > 0f
                    ? Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * style.skinValueScale
                    : 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                tone.Values[i] = Mathf.Clamp01((t - style.toneFloor) / style.toneRange);
            }

            return tone.Blur(style.toneBlur);
        }

        private static PosterMask Band(PosterMask tone, PosterMask figure, float edge, bool above, int cleanup)
        {
            var band = new PosterMask(tone.Width, tone.Height);
            for (int i = 0; i < band.Values.Length; i++)
                band.Values[i] = figure.Values[i] > 0f && (above ? tone.Values[i] > edge : tone.Values[i] < edge) ? 1f : 0f;
            return band.Clean(cleanup).Times(figure);
        }

        /// <summary>
        /// One-pixel edges inside the figure: a material changes, a neighbour's normal turns past
        /// the crease angle, or the surface steps back in depth.
        /// </summary>
        private static PosterMask Edges(PortraitPasses passes, WantedPosterStyle style)
        {
            int w = passes.Width, h = passes.Height;
            float creaseDot = Mathf.Cos(style.creaseAngle * Mathf.Deg2Rad);
            float step = style.depthStep * passes.SubjectHeight;
            var edges = new PosterMask(w, h);
            for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                int i = y * w + x;
                if (Differs(passes, i, i + 1, creaseDot, step) || Differs(passes, i, i + w, creaseDot, step))
                    edges.Values[i] = 1f;
            }

            return edges;
        }

        private static bool Differs(PortraitPasses passes, int a, int b, float creaseDot, float step)
        {
            int ma = passes.Material[a], mb = passes.Material[b];
            if (ma < 0 && mb < 0) return false;
            if (ma != mb) return true;
            if (Mathf.Abs(passes.Depth[a] - passes.Depth[b]) > step) return true;
            return Vector3.Dot(passes.Normal[a], passes.Normal[b]) < creaseDot;
        }
    }
}
