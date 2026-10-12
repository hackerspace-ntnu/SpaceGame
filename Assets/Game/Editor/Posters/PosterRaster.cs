using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>A coverage mask: one 0..1 value per pixel, row 0 at the top.</summary>
    internal sealed class PosterMask
    {
        public readonly int Width;
        public readonly int Height;
        public readonly float[] Values;

        public PosterMask(int width, int height)
        {
            Width = width;
            Height = height;
            Values = new float[width * height];
        }

        /// <summary>Evaluates <paramref name="coverage"/> at every pixel centre.</summary>
        public static PosterMask From(int width, int height, Func<float, float, float> coverage)
        {
            var mask = new PosterMask(width, height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                mask.Values[y * width + x] = Mathf.Clamp01(coverage(x + 0.5f, y + 0.5f));
            return mask;
        }

        /// <summary>1 where <paramref name="test"/> holds for the pixel index, 0 elsewhere.</summary>
        public static PosterMask Where(int width, int height, Func<int, bool> test)
        {
            var mask = new PosterMask(width, height);
            for (int i = 0; i < mask.Values.Length; i++) mask.Values[i] = test(i) ? 1f : 0f;
            return mask;
        }

        /// <summary>Bilinear sample in pixel coordinates, clamped at the edges.</summary>
        public float Sample(float x, float y)
        {
            x = Mathf.Clamp(x - 0.5f, 0f, Width - 1f);
            y = Mathf.Clamp(y - 0.5f, 0f, Height - 1f);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Mathf.Min(x0 + 1, Width - 1), y1 = Mathf.Min(y0 + 1, Height - 1);
            float fx = x - x0, fy = y - y0;
            float top = Mathf.Lerp(Values[y0 * Width + x0], Values[y0 * Width + x1], fx);
            float bottom = Mathf.Lerp(Values[y1 * Width + x0], Values[y1 * Width + x1], fx);
            return Mathf.Lerp(top, bottom, fy);
        }

        /// <summary>A separable box blur of the given radius. Returns a new mask.</summary>
        public PosterMask Blur(int radius)
        {
            if (radius <= 0) return Copy();
            var across = new PosterMask(Width, Height);
            float norm = 1f / (radius * 2 + 1);
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++) sum += Values[row + Mathf.Clamp(x + k, 0, Width - 1)];
                    across.Values[row + x] = sum * norm;
                }
            }

            var result = new PosterMask(Width, Height);
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++) sum += across.Values[Mathf.Clamp(y + k, 0, Height - 1) * Width + x];
                result.Values[y * Width + x] = sum * norm;
            }

            return result;
        }

        /// <summary>Grows every covered area by <paramref name="radius"/> pixels (a disc). Returns a new mask.</summary>
        public PosterMask Dilate(int radius)
        {
            var result = new PosterMask(Width, Height);
            int r2 = radius * radius;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float v = Values[y * Width + x];
                if (v <= 0f) continue;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= Height) continue;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= Width || dx * dx + dy * dy > r2) continue;
                        int i = yy * Width + xx;
                        if (result.Values[i] < v) result.Values[i] = v;
                    }
                }
            }

            return result;
        }

        /// <summary>The mask moved by whole pixels; what slides in is empty. Returns a new mask.</summary>
        public PosterMask Shift(int dx, int dy)
        {
            var result = new PosterMask(Width, Height);
            for (int y = 0; y < Height; y++)
            {
                int sy = y - dy;
                if (sy < 0 || sy >= Height) continue;
                for (int x = 0; x < Width; x++)
                {
                    int sx = x - dx;
                    if (sx >= 0 && sx < Width) result.Values[y * Width + x] = Values[sy * Width + sx];
                }
            }

            return result;
        }

        /// <summary>A band cleaned into shapes: blurred, then cut at half. Returns a new mask.</summary>
        public PosterMask Clean(int radius)
        {
            PosterMask soft = Blur(radius);
            for (int i = 0; i < soft.Values.Length; i++) soft.Values[i] = soft.Values[i] > 0.5f ? 1f : 0f;
            return soft;
        }

        public PosterMask Copy()
        {
            var copy = new PosterMask(Width, Height);
            Array.Copy(Values, copy.Values, Values.Length);
            return copy;
        }

        public PosterMask Times(PosterMask other)
        {
            PosterMask result = Copy();
            for (int i = 0; i < Values.Length; i++) result.Values[i] *= other.Values[i];
            return result;
        }

        public PosterMask Times(float factor)
        {
            PosterMask result = Copy();
            for (int i = 0; i < Values.Length; i++) result.Values[i] *= factor;
            return result;
        }

        public PosterMask Union(PosterMask other)
        {
            PosterMask result = Copy();
            for (int i = 0; i < Values.Length; i++) result.Values[i] = Mathf.Max(result.Values[i], other.Values[i]);
            return result;
        }

        public PosterMask Minus(PosterMask other)
        {
            PosterMask result = Copy();
            for (int i = 0; i < Values.Length; i++) result.Values[i] = Mathf.Clamp01(result.Values[i] - other.Values[i]);
            return result;
        }
    }

    /// <summary>A straight-alpha sRGB image, row 0 at the top.</summary>
    internal sealed class PosterImage
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color[] Pixels;

        public PosterImage(int width, int height, Color fill)
        {
            Width = width;
            Height = height;
            Pixels = new Color[width * height];
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = fill;
        }

        /// <summary>Wraps pixels read back from a render, which arrive bottom row first.</summary>
        public static PosterImage FromBottomUp(Color[] bottomUp, int width, int height)
        {
            var image = new PosterImage(width, height, Color.clear);
            for (int y = 0; y < height; y++)
                Array.Copy(bottomUp, (height - 1 - y) * width, image.Pixels, y * width, width);
            return image;
        }

        /// <summary>Source-over: <paramref name="colour"/> painted through <paramref name="mask"/>.</summary>
        public void Paint(PosterMask mask, Color colour)
        {
            for (int i = 0; i < Pixels.Length; i++)
            {
                float a = mask.Values[i] * colour.a;
                if (a > 0f) Pixels[i] = Over(Pixels[i], colour, a);
            }
        }

        /// <summary>Source-over through a small mask whose top-left pixel lands on (<paramref name="left"/>, <paramref name="top"/>).</summary>
        public void PaintAt(PosterMask mask, int left, int top, Color colour)
        {
            for (int y = 0; y < mask.Height; y++)
            {
                int ty = top + y;
                if (ty < 0 || ty >= Height) continue;
                for (int x = 0; x < mask.Width; x++)
                {
                    int tx = left + x;
                    if (tx < 0 || tx >= Width) continue;
                    float a = mask.Values[y * mask.Width + x] * colour.a;
                    if (a > 0f) Pixels[ty * Width + tx] = Over(Pixels[ty * Width + tx], colour, a);
                }
            }
        }

        /// <summary>Multiply blend: the sheet darkened by <paramref name="colour"/> through <paramref name="mask"/>, alpha kept.</summary>
        public void Darken(PosterMask mask, Color colour)
        {
            for (int i = 0; i < Pixels.Length; i++)
            {
                float a = mask.Values[i];
                if (a <= 0f) continue;
                Color p = Pixels[i];
                Pixels[i] = new Color(
                    p.r * Mathf.Lerp(1f, colour.r, a),
                    p.g * Mathf.Lerp(1f, colour.g, a),
                    p.b * Mathf.Lerp(1f, colour.b, a),
                    p.a);
            }
        }

        /// <summary>Draws <paramref name="source"/> scaled into <paramref name="destination"/> (pixels), bilinear, source-over.</summary>
        public void Draw(PosterImage source, Rect destination)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(destination.xMin));
            int x1 = Mathf.Min(Width, Mathf.CeilToInt(destination.xMax));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(destination.yMin));
            int y1 = Mathf.Min(Height, Mathf.CeilToInt(destination.yMax));
            float sx = source.Width / destination.width;
            float sy = source.Height / destination.height;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                Color c = source.Sample((x + 0.5f - destination.xMin) * sx, (y + 0.5f - destination.yMin) * sy);
                if (c.a > 0f) Pixels[y * Width + x] = Over(Pixels[y * Width + x], c, c.a);
            }
        }

        /// <summary>
        /// Draws <paramref name="source"/> at its own size, centred on <paramref name="centre"/>
        /// and turned by <paramref name="degrees"/> (positive is anticlockwise on the sheet).
        /// </summary>
        public void DrawRotated(PosterImage source, Vector2 centre, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            float reach = Mathf.Sqrt(source.Width * source.Width + source.Height * source.Height) * 0.5f + 2f;
            int x0 = Mathf.Max(0, (int)(centre.x - reach)), x1 = Mathf.Min(Width, (int)(centre.x + reach));
            int y0 = Mathf.Max(0, (int)(centre.y - reach)), y1 = Mathf.Min(Height, (int)(centre.y + reach));
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                // Rows run down the sheet, so an anticlockwise turn on paper is clockwise in (x, y).
                float dx = x + 0.5f - centre.x, dy = y + 0.5f - centre.y;
                float u = cos * dx - sin * dy + source.Width * 0.5f;
                float v = sin * dx + cos * dy + source.Height * 0.5f;
                if (u < 0f || v < 0f || u > source.Width || v > source.Height) continue;
                Color c = source.Sample(u, v);
                if (c.a > 0f) Pixels[y * Width + x] = Over(Pixels[y * Width + x], c, c.a);
            }
        }

        /// <summary>Bilinear sample in pixel coordinates; outside the image is transparent.</summary>
        public Color Sample(float x, float y)
        {
            float fx = x - 0.5f, fy = y - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            Color a = At(x0, y0), b = At(x0 + 1, y0), c = At(x0, y0 + 1), d = At(x0 + 1, y0 + 1);
            // Premultiply so transparent texels do not bleed their colour into the edge.
            Color top = Color.Lerp(Pre(a), Pre(b), tx);
            Color bottom = Color.Lerp(Pre(c), Pre(d), tx);
            Color p = Color.Lerp(top, bottom, ty);
            return p.a > 0f ? new Color(p.r / p.a, p.g / p.a, p.b / p.a, p.a) : Color.clear;
        }

        /// <summary>The image as an sRGB texture, flipped to Unity's bottom-up row order.</summary>
        public Texture2D ToTexture()
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            var bottomUp = new Color[Pixels.Length];
            for (int y = 0; y < Height; y++)
                Array.Copy(Pixels, y * Width, bottomUp, (Height - 1 - y) * Width, Width);
            texture.SetPixels(bottomUp);
            texture.Apply();
            return texture;
        }

        private Color At(int x, int y) =>
            x < 0 || y < 0 || x >= Width || y >= Height ? Color.clear : Pixels[y * Width + x];

        private static Color Pre(Color c) => new(c.r * c.a, c.g * c.a, c.b * c.a, c.a);

        private static Color Over(Color under, Color over, float a)
        {
            float outA = a + under.a * (1f - a);
            if (outA <= 0f) return Color.clear;
            float k = under.a * (1f - a);
            return new Color(
                (over.r * a + under.r * k) / outA,
                (over.g * a + under.g * k) / outA,
                (over.b * a + under.b * k) / outA,
                outA);
        }
    }

    /// <summary>Anti-aliased shapes and print screens as masks.</summary>
    internal static class PosterShapes
    {
        public static PosterMask Circle(int width, int height, Vector2 centre, float radius) =>
            PosterMask.From(width, height, (x, y) => radius - Vector2.Distance(new Vector2(x, y), centre) + 0.5f);

        public static PosterMask Box(int width, int height, Rect rect) =>
            PosterMask.From(width, height, (x, y) =>
                Mathf.Clamp01(Mathf.Min(x - rect.xMin, rect.xMax - x) + 0.5f) *
                Mathf.Clamp01(Mathf.Min(y - rect.yMin, rect.yMax - y) + 0.5f));

        /// <summary>A filled polygon (even-odd), 4 samples per pixel.</summary>
        public static PosterMask Polygon(int width, int height, IReadOnlyList<Vector2> points)
        {
            var mask = new PosterMask(width, height);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 p in points)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }

            int x0 = Mathf.Max(0, (int)minX), x1 = Mathf.Min(width, Mathf.CeilToInt(maxX));
            int y0 = Mathf.Max(0, (int)minY), y1 = Mathf.Min(height, Mathf.CeilToInt(maxY));
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int inside = 0;
                for (int s = 0; s < 4; s++)
                    if (Contains(points, x + 0.25f + 0.5f * (s & 1), y + 0.25f + 0.5f * (s >> 1))) inside++;
                mask.Values[y * width + x] = inside * 0.25f;
            }

            return mask;
        }

        /// <summary>Parallel bars: <paramref name="thickness"/> of every <paramref name="period"/>, turned by <paramref name="degrees"/>.</summary>
        public static PosterMask Stripes(int width, int height, float period, float thickness, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float nx = -Mathf.Sin(rad), ny = Mathf.Cos(rad);
            return PosterMask.From(width, height, (x, y) =>
            {
                float d = Mathf.Repeat(x * nx + y * ny, period);
                return Mathf.Min(d, thickness - d) + 0.5f;
            });
        }

        /// <summary>
        /// A halftone screen: round dots on a grid of spacing <paramref name="cell"/> turned by
        /// <paramref name="degrees"/>, each dot's area matching <paramref name="coverage"/> under it.
        /// </summary>
        public static PosterMask Halftone(PosterMask coverage, float cell, float degrees)
        {
            int width = coverage.Width, height = coverage.Height;
            var dots = new PosterMask(width, height);
            PosterMask soft = coverage.Blur(Mathf.Max(1, Mathf.RoundToInt(cell / 3f)));
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            float reach = Mathf.Max(width, height) * 1.5f;
            for (float i = -reach; i < reach; i += cell)
            for (float j = -reach; j < reach; j += cell)
            {
                float cx = i * cos - j * sin, cy = i * sin + j * cos;
                if (cx < 0f || cy < 0f || cx >= width || cy >= height) continue;
                float c = soft.Sample(cx, cy);
                if (c < 0.02f) continue;
                Disc(dots, cx, cy, cell * Mathf.Sqrt(c / Mathf.PI) * 1.05f);
            }

            return dots;
        }

        private static void Disc(PosterMask mask, float cx, float cy, float radius)
        {
            int x0 = Mathf.Max(0, (int)(cx - radius - 1)), x1 = Mathf.Min(mask.Width, (int)(cx + radius + 2));
            int y0 = Mathf.Max(0, (int)(cy - radius - 1)), y1 = Mathf.Min(mask.Height, (int)(cy + radius + 2));
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                int index = y * mask.Width + x;
                if (a > mask.Values[index]) mask.Values[index] = a;
            }
        }

        private static bool Contains(IReadOnlyList<Vector2> points, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                Vector2 a = points[i], b = points[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }

            return inside;
        }
    }
}
