using System.Collections.Generic;
using SpaceGame.World.Posters;
using UnityEngine;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>The three faces a poster is set in.</summary>
    internal sealed class PosterFonts
    {
        /// <summary>WANTED across the banner.</summary>
        public Font Title;
        /// <summary>Field labels and the DEAD / ALIVE tag.</summary>
        public Font Label;
        /// <summary>Field values.</summary>
        public Font Value;
    }

    /// <summary>
    /// Lays out one wanted poster: a crumpled sheet, a sun backdrop in the poster's colour, the
    /// screenprinted portrait between heavy corner brackets, an ink banner with WANTED knocked out
    /// of it, a DEAD or ALIVE tag, and the reward and name. Positions come from
    /// <see cref="WantedPosterStyle"/> in sheet units (100 wide, 125 tall).
    /// </summary>
    internal static class PosterLayout
    {
        public static PosterImage Compose(WantedPoster poster, WantedPosterStyle style,
            PosterImage portrait, string heightText, PosterFonts fonts)
        {
            var sheet = new Sheet(style, poster);
            sheet.Backdrop();
            sheet.Portrait(portrait);
            sheet.Brackets();
            sheet.Tag(fonts.Label);
            sheet.Fields(fonts, heightText);
            sheet.Stain();
            sheet.Banner(fonts.Title);
            sheet.Paper();
            return sheet.Image;
        }

        private sealed class Sheet
        {
            public readonly PosterImage Image;
            private readonly WantedPosterStyle style;
            private readonly WantedPoster poster;
            private readonly PosterNoise noise;
            private readonly float unit;
            private readonly int width;
            private readonly int height;
            private readonly Rect box;

            public Sheet(WantedPosterStyle style, WantedPoster poster)
            {
                this.style = style;
                this.poster = poster;
                width = style.sheetPixels.x;
                height = style.sheetPixels.y;
                unit = width / 100f;
                noise = new PosterNoise(PosterNoise.SeedOf(poster.DisplayName));
                box = Units(style.pictureBox);

                Image = new PosterImage(width, height, style.paper);
                Vector2 centre = new(width * 0.5f, height * 0.45f);
                Image.Paint(PosterMask.From(width, height, (x, y) =>
                {
                    float d = new Vector2((x - centre.x) / (width * 0.5f), (y - centre.y) / (height * 0.5f)).magnitude / Mathf.Sqrt(2f);
                    return Mathf.InverseLerp(0.5f, 1f, d) * style.vignette;
                }), style.vignetteColour);
            }

            /// <summary>A setting sun striped on its lower half, a dotted moon, a hatched ridge of mesas.</summary>
            public void Backdrop()
            {
                Color accent = poster.Accent;
                Color soft = Color.Lerp(style.paper, accent, style.sunTint);
                Color texture = Color.Lerp(style.paper, accent, style.textureTint);
                Color cool = Color.Lerp(style.paper, style.slate, style.mesaTint);

                Vector2 sun = style.sunCentre * unit;
                PosterMask disc = PosterShapes.Circle(width, height, sun, style.sunRadius * unit);
                Image.Darken(disc, soft);
                PosterMask lower = PosterMask.From(width, height, (_, y) => y - sun.y);
                Image.Darken(PosterShapes.Stripes(width, height, style.sunStripes.x * unit, style.sunStripes.y * unit, 0f)
                    .Times(disc).Times(lower), texture);

                Vector3 dots = style.moonDots;
                float dotCoverage = Mathf.PI * dots.y * dots.y / (dots.x * dots.x);
                PosterMask moon = PosterShapes.Circle(width, height, style.moonCentre * unit, style.moonRadius * unit).Times(dotCoverage);
                Image.Darken(PosterShapes.Halftone(moon, dots.x * unit, dots.z), texture);

                var ridge = new List<Vector2>();
                foreach (Vector2 p in style.mesas) ridge.Add(p * unit);
                Vector3 hatch = style.mesaHatch;
                Image.Darken(PosterShapes.Polygon(width, height, ridge)
                    .Times(PosterShapes.Stripes(width, height, hatch.x * unit, hatch.y * unit, hatch.z)), cool);
            }

            /// <summary>The portrait stands on the picture box's floor and may rise a little above its top.</summary>
            public void Portrait(PosterImage portrait)
            {
                float h = box.height * style.pictureOverscan;
                float w = h * portrait.Width / portrait.Height;
                Image.Draw(portrait, new Rect(box.center.x - w * 0.5f, box.yMax - h, w, h));
            }

            public void Brackets()
            {
                PosterMask brackets = BracketShapes();
                Vector2 shadow = style.bracketShadow * unit;
                Image.Paint(brackets.Shift(Mathf.RoundToInt(shadow.x), Mathf.RoundToInt(shadow.y)), style.ink);
                Image.Paint(brackets, poster.Accent);
            }

            private PosterMask BracketShapes()
            {
                float len = style.bracketLength * unit, thick = style.bracketThickness * unit, outset = style.bracketOutset * unit;
                float left = box.xMin - outset, right = box.xMax + outset;
                float top = box.yMin - outset, bottom = box.yMax + outset;
                Rect[] bars =
                {
                    new(left, top, len, thick), new(left, top, thick, len),
                    new(right - len, top, len, thick), new(right - thick, top, thick, len),
                    new(left, bottom - thick, len, thick), new(left, bottom - len, thick, len),
                    new(right - len, bottom - thick, len, thick), new(right - thick, bottom - len, thick, len),
                };
                PosterMask mask = new(width, height);
                foreach (Rect bar in bars) mask = mask.Union(PosterShapes.Box(width, height, bar));
                return mask;
            }

            /// <summary>A luggage tag hung off the picture's bottom-right corner, saying how to bring them in.</summary>
            public void Tag(Font font)
            {
                string word = poster.Status == WantedStatus.Dead ? "DEAD" : "ALIVE";
                Color fill = poster.Status == WantedStatus.Dead ? style.dead : style.slate;
                float cap = style.tagCap * unit;
                PosterMask text = PosterText.Render(font, word, cap, style.tagTracking, out float baseline);

                Vector4 pad = style.tagPadding * unit;
                float w = pad.x + text.Width + pad.y, h = pad.z + cap + pad.w;
                Vector2 shadow = style.tagShadow * unit;
                int iw = Mathf.CeilToInt(w + shadow.x), ih = Mathf.CeilToInt(h + shadow.y);
                var tag = new PosterImage(iw, ih, Color.clear);

                float notch = style.tagNotch * unit, cut = style.tagCornerCut * unit;
                Vector2[] outline = { new(notch, 0f), new(w, 0f), new(w, h - cut), new(w - cut, h), new(notch, h), new(0f, h * 0.5f) };
                var shadowOutline = new Vector2[outline.Length];
                for (int i = 0; i < outline.Length; i++) shadowOutline[i] = outline[i] + shadow;
                tag.Paint(PosterShapes.Polygon(iw, ih, shadowOutline), style.ink);
                tag.Paint(PosterShapes.Polygon(iw, ih, outline), fill);

                Vector3 eyelet = style.tagEyelet;
                Vector2 hole = new(eyelet.x * unit, h * 0.5f);
                float r = style.tagEyeletRadius * unit;
                tag.Paint(PosterShapes.Circle(iw, ih, hole, r), style.paper);
                tag.Paint(PosterShapes.Circle(iw, ih, hole, r).Minus(PosterShapes.Circle(iw, ih, hole, r - eyelet.y * unit)),
                    new Color(0f, 0f, 0f, eyelet.z));

                tag.PaintAt(text, Mathf.RoundToInt(pad.x), Mathf.RoundToInt(pad.z + cap - baseline), style.paper);

                Vector2 offset = style.tagOffset * unit;
                var place = new Rect(box.xMax + offset.x - w, box.yMax + offset.y - h, w, h);
                Image.DrawRotated(tag, place.center + shadow * 0.5f, style.tagAngle);
            }

            /// <summary>REWARD: and NAME: in two rows, with HT: at the right end of the name row.</summary>
            public void Fields(PosterFonts fonts, string heightText)
            {
                float labelCap = style.labelCap * unit, valueCap = style.valueCap * unit;
                string[] labels = { "REWARD:", "NAME:" };
                string[] values = { poster.Reward, poster.DisplayName };

                var labelMasks = new PosterMask[labels.Length];
                var labelBaselines = new float[labels.Length];
                float column = 0f;
                for (int i = 0; i < labels.Length; i++)
                {
                    labelMasks[i] = PosterText.Render(fonts.Label, labels[i], labelCap, style.labelTracking, out labelBaselines[i]);
                    column = Mathf.Max(column, labelMasks[i].Width);
                }

                float left = style.fieldLeft * unit;
                for (int i = 0; i < labels.Length; i++)
                {
                    float baseline = (style.fieldFirstBaseline + i * style.fieldLineGap) * unit;
                    Label(labelMasks[i], left, baseline, labelBaselines[i]);
                    PosterMask value = PosterText.Render(fonts.Value, values[i].ToUpperInvariant(), valueCap, style.valueTracking, out float vb);
                    Image.PaintAt(value, Mathf.RoundToInt(left + column + style.labelGap * unit), Mathf.RoundToInt(baseline - vb), style.ink);
                }

                float nameBaseline = (style.fieldFirstBaseline + style.fieldLineGap) * unit;
                PosterMask height = PosterText.Render(fonts.Value, heightText, valueCap, style.valueTracking, out float hb);
                float right = style.fieldRight * unit - height.Width;
                Image.PaintAt(height, Mathf.RoundToInt(right), Mathf.RoundToInt(nameBaseline - hb), style.ink);
                PosterMask ht = PosterText.Render(fonts.Label, "HT:", labelCap, style.labelTracking, out float lb);
                Label(ht, right - style.heightGap * unit - ht.Width, nameBaseline, lb);
            }

            private void Label(PosterMask text, float left, float baseline, float textBaseline)
            {
                int x = Mathf.RoundToInt(left), y = Mathf.RoundToInt(baseline - textBaseline);
                int s = Mathf.Max(1, Mathf.RoundToInt(style.labelShadow * unit));
                Image.PaintAt(text, x + s, y + s, style.slate);
                Image.PaintAt(text, x, y, poster.Accent);
            }

            /// <summary>One coffee ring, somewhere of the poster's own choosing.</summary>
            public void Stain()
            {
                var random = new System.Random(PosterNoise.SeedOf(poster.DisplayName + ".stain"));
                Rect area = Units(style.stainArea);
                Vector2 centre = new(area.xMin + (float)random.NextDouble() * area.width, area.yMin + (float)random.NextDouble() * area.height);
                float radius = style.stain.x * unit, strength = style.stain.y;
                Image.Paint(PosterMask.From(width, height, (x, y) =>
                {
                    float d = Vector2.Distance(new Vector2(x, y), centre) / radius;
                    if (d < RingInner || d > RingOuter) return 0f;
                    return d < RingRim ? Mathf.InverseLerp(RingInner, RingRim, d) * strength
                        : d < RingFade ? Mathf.Lerp(strength, strength * RingFadeLevel, Mathf.InverseLerp(RingRim, RingFade, d))
                        : Mathf.Lerp(strength * RingFadeLevel, 0f, Mathf.InverseLerp(RingFade, RingOuter, d));
                }), style.stainColour);
            }

            // A dried coffee ring as fractions of its radius: a sharp dark rim, then a paler fade outwards.
            private const float RingInner = 0.61f;
            private const float RingRim = 0.63f;
            private const float RingFade = 0.67f;
            private const float RingOuter = 0.70f;
            private const float RingFadeLevel = 0.4f;

            /// <summary>A band across the top in a dark shade of the accent, WANTED knocked out of it in paper, worn in places.</summary>
            public void Banner(Font font)
            {
                float band = style.bannerHeight * unit;
                int layerHeight = Mathf.CeilToInt(band + style.bannerRule * unit);
                var layer = new PosterImage(width, layerHeight, Color.clear);
                Color shade = poster.Accent * style.bannerShade;
                shade.a = 1f;
                layer.Paint(PosterShapes.Box(width, layerHeight, new Rect(0f, 0f, width, band)), shade);
                layer.Paint(PosterShapes.Box(width, layerHeight, new Rect(0f, band, width, layerHeight - band)), poster.Accent);

                float cap = style.titleCap * unit;
                PosterMask title = PosterText.Render(font, "WANTED", cap, style.titleTracking, out float baseline);
                float widest = style.titleMaxWidth * width;
                if (title.Width > widest)
                {
                    cap *= widest / title.Width;
                    title = PosterText.Render(font, "WANTED", cap, style.titleTracking, out baseline);
                }

                layer.PaintAt(title, Mathf.RoundToInt((width - title.Width) * 0.5f), Mathf.RoundToInt(band * 0.5f + cap * 0.5f - baseline), style.paper);

                float feature = style.wear.x * unit;
                for (int y = 0; y < layerHeight; y++)
                for (int x = 0; x < width; x++)
                {
                    float wearDepth = Mathf.InverseLerp(style.wear.y, style.wear.y + WearSoftness, noise.Fractal(x / feature, y / feature));
                    int i = y * width + x;
                    Color c = layer.Pixels[i];
                    layer.Pixels[i] = new Color(c.r, c.g, c.b, c.a * (1f - wearDepth));
                }

                Image.Draw(layer, new Rect(0f, 0f, width, layerHeight));
            }

            private const float WearSoftness = 0.03f;

            /// <summary>Grain and crease relief over everything, so the ink folds with the paper.</summary>
            public void Paper()
            {
                Vector3 creases = style.creases;
                float feature = creases.x * unit;
                float az = style.creaseLight.x * Mathf.Deg2Rad, el = style.creaseLight.y * Mathf.Deg2Rad;
                var light = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el));
                var grainNoise = new PosterNoise(PosterNoise.SeedOf(poster.DisplayName + ".grain"));

                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float hx = noise.Ridged((x + 1) / feature, y / feature) - noise.Ridged((x - 1) / feature, y / feature);
                    float hy = noise.Ridged(x / feature, (y + 1) / feature) - noise.Ridged(x / feature, (y - 1) / feature);
                    var normal = new Vector3(-hx * creases.y, -hy * creases.y, 1f).normalized;
                    float lit = Mathf.Clamp01(Vector3.Dot(normal, light));
                    float shade = Mathf.Lerp(1f, lit, creases.z);

                    float grain = grainNoise.Fractal(x * GrainScale, y * GrainScale) * style.grain;
                    int i = y * width + x;
                    Color c = Image.Pixels[i];
                    Image.Pixels[i] = new Color(
                        c.r * shade * Mathf.Lerp(1f, style.ink.r, grain),
                        c.g * shade * Mathf.Lerp(1f, style.ink.g, grain),
                        c.b * shade * Mathf.Lerp(1f, style.ink.b, grain),
                        1f);
                }
            }

            // Paper grain is a few pixels across whatever the sheet's size.
            private const float GrainScale = 0.45f;

            private Rect Units(Rect r) => new(r.x * unit, r.y * unit, r.width * unit, r.height * unit);
        }
    }
}
