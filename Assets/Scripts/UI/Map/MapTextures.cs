using UnityEngine;

/// <summary>
/// Procedural runtime textures used by MapUI to give the map a holographic look
/// without shipping any image assets.
/// </summary>
public static class MapTextures
{
    private static Sprite _white;
    private static Sprite _hexGrid;
    private static Sprite _scanlines;
    private static Sprite _triangle;
    private static Sprite _ringMarker;

    public static Sprite White
    {
        get
        {
            if (_white != null) return _white;
            var tex = Texture2D.whiteTexture;
            _white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            _white.name = "Map_White";
            return _white;
        }
    }

    /// <summary>Tileable hex-grid line texture, transparent background, white lines.</summary>
    public static Sprite HexGrid
    {
        get
        {
            if (_hexGrid != null) return _hexGrid;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            // Hexagon grid is awkward to tile cleanly without UV tricks — use an
            // equilateral triangle pattern instead, which reads as "tactical grid".
            float spacing = 24f;
            float thickness = 1.2f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x;
                    float fy = y;
                    float a = Mathf.Abs(((fx + fy * 0.57735f) % spacing) - spacing * 0.5f);
                    float b = Mathf.Abs(((fx - fy * 0.57735f) % spacing) - spacing * 0.5f);
                    float c = Mathf.Abs((fy % spacing) - spacing * 0.5f);
                    float d = Mathf.Min(a, Mathf.Min(b, c));
                    float alpha = Mathf.Clamp01(1f - (d / thickness));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha * 0.65f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            _hexGrid = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _hexGrid.name = "Map_HexGrid";
            return _hexGrid;
        }
    }

    /// <summary>Tileable horizontal scanlines.</summary>
    public static Sprite Scanlines
    {
        get
        {
            if (_scanlines != null) return _scanlines;
            const int w = 4;
            const int h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                // Bright lines on transparent — for additive blending they glow.
                float a = (y % 2 == 0) ? 0.22f : 0f;
                for (int x = 0; x < w; x++) pixels[y * w + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _scanlines = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            _scanlines.name = "Map_Scanlines";
            return _scanlines;
        }
    }

    /// <summary>Filled upward-pointing triangle for the player icon.</summary>
    public static Sprite Triangle
    {
        get
        {
            if (_triangle != null) return _triangle;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float t = (float)y / (size - 1);
                float halfWidth = (1f - t) * 0.5f * size;
                float cx = size * 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - cx);
                    float edge = halfWidth - dx;
                    float a = Mathf.Clamp01(edge);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _triangle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.3f), 100f);
            _triangle.name = "Map_Triangle";
            return _triangle;
        }
    }

    /// <summary>Hollow ring with soft inner glow for markers.</summary>
    public static Sprite RingMarker
    {
        get
        {
            if (_ringMarker != null) return _ringMarker;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float outer = size * 0.48f;
            float inner = size * 0.30f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    float ring = Mathf.Clamp01(1f - Mathf.Abs(r - (outer + inner) * 0.5f) / ((outer - inner) * 0.5f));
                    float dot = Mathf.Clamp01(1f - r / (size * 0.10f));
                    float a = Mathf.Max(ring, dot * 0.9f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _ringMarker = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _ringMarker.name = "Map_Ring";
            return _ringMarker;
        }
    }
}
