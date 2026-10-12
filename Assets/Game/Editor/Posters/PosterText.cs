using System;
using UnityEngine;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>
    /// Sets one line of text in a dynamic <see cref="Font"/> as a coverage mask, sized by its cap
    /// height so every face lines up the same way on the sheet. Glyphs are rasterised by Unity's
    /// own font engine into the font's atlas and read back from there; no scene, no camera.
    /// </summary>
    internal static class PosterText
    {
        // Pixels per em asked of the font. Large, because the glyphs are then scaled down.
        private const int GlyphSize = 192;
        private const int Pad = 2;

        /// <summary>
        /// The line as a mask with capital letters <paramref name="capHeight"/> pixels tall.
        /// <paramref name="tracking"/> is extra space between letters, as a fraction of the em.
        /// <paramref name="baseline"/> is the mask row the letters stand on.
        /// </summary>
        public static PosterMask Render(Font font, string text, float capHeight, float tracking, out float baseline)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));

            font.RequestCharactersInTexture(text + "H", GlyphSize, FontStyle.Normal);
            if (!font.GetCharacterInfo('H', out CharacterInfo cap, GlyphSize, FontStyle.Normal) || cap.maxY <= 0)
                throw new InvalidOperationException($"[Posters] Font '{font.name}' cannot set 'H'; is it imported as a dynamic font?");

            var glyphs = new CharacterInfo[text.Length];
            var pens = new float[text.Length];
            float pen = 0f, top = cap.maxY, bottom = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                if (!font.GetCharacterInfo(text[i], out glyphs[i], GlyphSize, FontStyle.Normal))
                    throw new InvalidOperationException($"[Posters] Font '{font.name}' has no glyph for '{text[i]}' in \"{text}\".");
                pens[i] = pen;
                pen += glyphs[i].advance + tracking * GlyphSize;
                top = Mathf.Max(top, glyphs[i].maxY);
                bottom = Mathf.Min(bottom, glyphs[i].minY);
            }

            float scale = capHeight / cap.maxY;
            var mask = new PosterMask(Mathf.CeilToInt(pen * scale) + Pad * 2, Mathf.CeilToInt((top - bottom) * scale) + Pad * 2);
            baseline = Pad + top * scale;

            Atlas atlas = Atlas.Read(font);
            for (int i = 0; i < text.Length; i++)
                Stamp(mask, atlas, glyphs[i], Pad + pens[i] * scale, baseline, scale);

            return mask;
        }

        private static void Stamp(PosterMask mask, Atlas atlas, CharacterInfo glyph, float penX, float baseline, float scale)
        {
            float x0 = penX + glyph.minX * scale, x1 = penX + glyph.maxX * scale;
            float y0 = baseline - glyph.maxY * scale, y1 = baseline - glyph.minY * scale;
            if (x1 <= x0 || y1 <= y0) return;

            for (int y = Mathf.Max(0, (int)y0); y < Mathf.Min(mask.Height, Mathf.CeilToInt(y1)); y++)
            for (int x = Mathf.Max(0, (int)x0); x < Mathf.Min(mask.Width, Mathf.CeilToInt(x1)); x++)
            {
                float u = (x + 0.5f - x0) / (x1 - x0);
                float v = (y1 - (y + 0.5f)) / (y1 - y0);
                // The four corner uvs carry any rotation the atlas packer applied to the glyph.
                Vector2 uv = Vector2.Lerp(
                    Vector2.Lerp(glyph.uvBottomLeft, glyph.uvBottomRight, u),
                    Vector2.Lerp(glyph.uvTopLeft, glyph.uvTopRight, u), v);
                float a = atlas.Sample(uv);
                int index = y * mask.Width + x;
                if (a > mask.Values[index]) mask.Values[index] = a;
            }
        }

        /// <summary>A CPU copy of a font's glyph atlas; the atlas itself is GPU-only.</summary>
        private readonly struct Atlas
        {
            private readonly float[] coverage;
            private readonly int width;
            private readonly int height;

            private Atlas(float[] coverage, int width, int height)
            {
                this.coverage = coverage;
                this.width = width;
                this.height = height;
            }

            public static Atlas Read(Font font)
            {
                Texture source = font.material.mainTexture;
                RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                RenderTexture previous = RenderTexture.active;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
                try
                {
                    Graphics.Blit(source, rt);
                    RenderTexture.active = rt;
                    copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                    copy.Apply();
                    Color32[] pixels = copy.GetPixels32();
                    var values = new float[pixels.Length];
                    for (int i = 0; i < pixels.Length; i++) values[i] = pixels[i].a / 255f;
                    return new Atlas(values, source.width, source.height);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                    UnityEngine.Object.DestroyImmediate(copy);
                }
            }

            public float Sample(Vector2 uv)
            {
                float x = uv.x * width - 0.5f, y = uv.y * height - 0.5f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, width - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, height - 1);
                int x1 = Mathf.Min(x0 + 1, width - 1), y1 = Mathf.Min(y0 + 1, height - 1);
                float tx = Mathf.Clamp01(x - x0), ty = Mathf.Clamp01(y - y0);
                float a = Mathf.Lerp(coverage[y0 * width + x0], coverage[y0 * width + x1], tx);
                float b = Mathf.Lerp(coverage[y1 * width + x0], coverage[y1 * width + x1], tx);
                return Mathf.Lerp(a, b, ty);
            }
        }
    }
}
