using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Shared look for the game's full-screen menus: one palette, one type scale, and the handful
    /// of sprites those screens need, all generated at runtime.
    /// <para>
    /// Nothing here is an imported asset. The existing menu screens (MinigameConfigUI,
    /// MatchResultUI, MatchLeaderboardUI) already build themselves in code and ship no art, and a
    /// pause menu that needed a folder of 9-sliced PNGs would be the only screen in the project
    /// that could not be dropped into a scene without importing something first. The rounded
    /// panels are drawn once into small textures and 9-sliced, so they stay crisp at any size.
    /// </para>
    /// </summary>
    public static class UITheme
    {
        // Continuous with the minigame screens: same near-black backdrop, same blue accent.
        public static readonly Color Backdrop = new(0.02f, 0.03f, 0.06f, 0.92f);
        public static readonly Color Panel = new(0.055f, 0.075f, 0.11f, 0.96f);
        public static readonly Color PanelRaised = new(0.086f, 0.113f, 0.16f, 0.98f);
        public static readonly Color Accent = new(0.239f, 0.549f, 0.949f, 1f);
        public static readonly Color AccentSoft = new(0.239f, 0.549f, 0.949f, 0.18f);
        public static readonly Color AccentWarm = new(0.988f, 0.686f, 0.251f, 1f);
        public static readonly Color Danger = new(0.937f, 0.325f, 0.353f, 1f);
        public static readonly Color Bright = new(0.949f, 0.968f, 1f, 1f);
        public static readonly Color Muted = new(0.62f, 0.70f, 0.82f, 1f);
        public static readonly Color Faint = new(0.42f, 0.49f, 0.60f, 1f);
        public static readonly Color Hairline = new(1f, 1f, 1f, 0.08f);
        public static readonly Color TrackEmpty = new(1f, 1f, 1f, 0.10f);

        public const int TitleSize = 64;
        public const int TabSize = 26;
        public const int HeadingSize = 30;
        public const int LabelSize = 22;
        public const int ValueSize = 22;
        public const int CaptionSize = 17;

        public const float PanelRadius = 18f;
        public const float ChipRadius = 8f;

        private static readonly Dictionary<int, Sprite> roundedByRadius = new();
        private static readonly Dictionary<int, Sprite> edgeByRadius = new();
        private static Sprite circleSprite;
        private static Sprite outlineSprite;
        private static Sprite glowSprite;
        private static Sprite scanlineSprite;
        private static Sprite chevronSprite;
        private static Sprite checkSprite;
        private static Sprite gridSprite;

        /// <summary>
        /// Rounded rectangle 9-sliced at its own corner radius, cached per radius.
        /// <para>
        /// Cached per radius rather than shared because a 9-sliced sprite whose border is taller
        /// than the rect it fills draws its corners on top of each other. A 10px slider track and
        /// a 30px switch therefore cannot share one sprite: each asks for the radius that fits it,
        /// which for a pill is half its own height.
        /// </para>
        /// </summary>
        public static Sprite Rounded(int radius)
        {
            radius = Mathf.Clamp(radius, 1, 64);
            if (roundedByRadius.TryGetValue(radius, out Sprite cached) && cached != null) return cached;

            Sprite made = RoundedRect(radius, $"UI_Rounded{radius}");
            roundedByRadius[radius] = made;
            return made;
        }

        /// <summary>Rounded rectangle at the panel radius, 9-sliced so it scales without smearing.</summary>
        public static Sprite PanelSprite => Rounded((int)PanelRadius);

        /// <summary>Smaller radius for rows, chips and buttons sitting inside a panel.</summary>
        public static Sprite ChipSprite => Rounded((int)ChipRadius);

        /// <summary>
        /// A filled disc, drawn <see cref="UnityEngine.UI.Image.Type.Simple"/> so it stays a true
        /// circle at any size — slider handles and switch knobs, which a 9-slice cannot round.
        /// </summary>
        public static Sprite CircleSprite => Ensure(ref circleSprite, () => Disc(48, "UI_Circle"));

        /// <summary>Rounded 2px border, transparent inside, for focus and selection rings.</summary>
        public static Sprite OutlineSprite => Ensure(ref outlineSprite, () => RoundedOutline((int)ChipRadius, 2f, "UI_Outline"));

        /// <summary>
        /// A hollow rounded rectangle with a 2px stroke, cached per radius.
        /// <para>
        /// Not the same thing as drawing <see cref="Rounded"/> with the centre unfilled: 9-slicing
        /// leaves the whole border region — the corner radius, so 18px on a panel — showing, which
        /// reads as a thick frame rather than an edge. The stroke here is drawn into the texture,
        /// so it stays 2px however big the rect is.
        /// </para>
        /// </summary>
        public static Sprite Edge(int radius)
        {
            radius = Mathf.Clamp(radius, 2, 64);
            if (edgeByRadius.TryGetValue(radius, out Sprite cached) && cached != null) return cached;

            Sprite made = RoundedOutline(radius, 2f, $"UI_Edge{radius}");
            edgeByRadius[radius] = made;
            return made;
        }

        /// <summary>Radial falloff used to bloom the accent behind the active tab and the title.</summary>
        public static Sprite GlowSprite => Ensure(ref glowSprite, () => RadialGlow(64, "UI_Glow"));

        /// <summary>Tileable 1-in-3 scanline, faint, laid over the backdrop.</summary>
        public static Sprite ScanlineSprite => Ensure(ref scanlineSprite, () => Scanlines("UI_Scanlines"));

        /// <summary>Right-pointing solid chevron; mirrored with scale for the left one.</summary>
        public static Sprite ChevronSprite => Ensure(ref chevronSprite, () => Chevron(32, "UI_Chevron"));

        /// <summary>Tick mark drawn inside an on toggle.</summary>
        public static Sprite CheckSprite => Ensure(ref checkSprite, () => Check(32, "UI_Check"));

        /// <summary>Tileable fine grid, the faint blueprint texture behind the content pane.</summary>
        public static Sprite GridSprite => Ensure(ref gridSprite, () => Grid(64, "UI_Grid"));

        /// <summary>
        /// Returns the cached sprite, remaking it if it has been destroyed.
        /// <para>
        /// The obvious <c>field ??= Make()</c> is wrong for a UnityEngine.Object. These sprites and
        /// their textures are <see cref="HideFlags.HideAndDontSave"/>, so they are destroyed on a
        /// domain reload while the static field still holds the managed wrapper — which is not C#
        /// null, so <c>??=</c> keeps it and every menu built afterwards draws with a destroyed
        /// sprite. Unity's own <c>==</c> is the one that reports a destroyed object as null.
        /// </para>
        /// </summary>
        private static Sprite Ensure(ref Sprite cached, System.Func<Sprite> make)
        {
            if (cached == null) cached = make();
            return cached;
        }

        // -------------------------------------------------------------------- makers

        /// <summary>
        /// A rounded rectangle just big enough to hold its own corners, with the 9-slice border set
        /// to the corner radius. Unity then stretches only the flat middle, so one small texture
        /// serves a 24px chip and a 1200px panel at the same corner sharpness.
        /// </summary>
        private static Sprite RoundedRect(int radius, string name)
        {
            radius = Mathf.Max(1, radius);
            int size = radius * 2 + 2;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = new Color(1f, 1f, 1f, CornerAlpha(x, y, size, radius));
                }
            }

            var tex = MakeTexture(size, size, pixels, name);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.name = name;
            return sprite;
        }

        private static Sprite Disc(int size, string name)
        {
            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d + 0.5f));
                }
            }

            var tex = MakeTexture(size, size, pixels, name);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        private static Sprite RoundedOutline(int radius, float thickness, string name)
        {
            radius = Mathf.Max(2, radius);
            int size = radius * 2 + 2;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float outer = CornerAlpha(x, y, size, radius);
                    float inner = CornerAlpha(x, y, size, radius, inset: thickness);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(outer - inner));
                }
            }

            var tex = MakeTexture(size, size, pixels, name);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.name = name;
            return sprite;
        }

        /// <summary>
        /// Coverage of a rounded rectangle at one pixel, antialiased over the last pixel of the
        /// edge. <paramref name="inset"/> shrinks the shape, which is how the outline is punched
        /// out — the same rectangle a little smaller, subtracted.
        /// </summary>
        private static float CornerAlpha(int x, int y, int size, int radius, float inset = 0f)
        {
            float half = size * 0.5f;
            float px = x + 0.5f - half;
            float py = y + 0.5f - half;

            // Distance outside the rounded rect, measured from the inner box whose corners the
            // radius rounds off.
            float boxHalf = half - radius;
            float dx = Mathf.Max(Mathf.Abs(px) - boxHalf, 0f);
            float dy = Mathf.Max(Mathf.Abs(py) - boxHalf, 0f);
            float distance = Mathf.Sqrt(dx * dx + dy * dy);

            return Mathf.Clamp01(radius - inset - distance + 0.5f);
        }

        private static Sprite RadialGlow(int size, string name)
        {
            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));

                    // Squared falloff reads as light rather than a disc with a soft edge.
                    float a = Mathf.Pow(1f - r, 2.4f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            var tex = MakeTexture(size, size, pixels, name);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        private static Sprite Scanlines(string name)
        {
            const int size = 3;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float a = y == 0 ? 0.20f : 0f;
                for (int x = 0; x < size; x++) pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }

            var tex = MakeTexture(size, size, pixels, name, FilterMode.Point, TextureWrapMode.Repeat);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        private static Sprite Grid(int size, string name)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool line = x == 0 || y == 0;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, line ? 0.5f : 0f);
                }
            }

            var tex = MakeTexture(size, size, pixels, name, FilterMode.Bilinear, TextureWrapMode.Repeat);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        private static Sprite Chevron(int size, string name)
        {
            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float thickness = size * 0.17f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Two mirrored strokes meeting at the right-hand point: distance to the "\/"
                    // rotated a quarter turn, which is |x - (half - |y - half|)|.
                    float arm = half - Mathf.Abs(y - half);
                    float d = Mathf.Abs(x - (half - arm * 0.5f) - size * 0.16f);
                    float a = Mathf.Clamp01(thickness - d);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            var tex = MakeTexture(size, size, pixels, name);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        private static Sprite Check(int size, string name)
        {
            var pixels = new Color[size * size];
            float thickness = size * 0.11f;

            // Two segments of a tick, as distance-to-segment fields unioned together.
            var shortArmA = new Vector2(size * 0.22f, size * 0.52f);
            var shortArmB = new Vector2(size * 0.42f, size * 0.30f);
            var longArmB = new Vector2(size * 0.80f, size * 0.74f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Mathf.Min(DistanceToSegment(p, shortArmA, shortArmB),
                                        DistanceToSegment(p, shortArmB, longArmB));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(thickness - d));
                }
            }

            var tex = MakeTexture(size, size, pixels, name);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
            return Vector2.Distance(p, a + ab * t);
        }

        private static Texture2D MakeTexture(int width, int height, Color[] pixels, string name,
            FilterMode filter = FilterMode.Bilinear, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = filter,
                wrapMode = wrap,
                // Generated per session and never serialized — without this they leak into the
                // scene as unowned assets when a domain reload happens with the menu open.
                hideFlags = HideFlags.HideAndDontSave,
            };

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
