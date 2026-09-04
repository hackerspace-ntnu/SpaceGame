using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The hotbar's palette and the one texture <see cref="UITheme"/> does not have.
    ///
    /// <para>
    /// <b>Its palette is now the visor's.</b> This file used to hold the expedition's warm colours,
    /// on the reasoning that the hotbar sits over the world and so should not look like the menu.
    /// That reasoning is gone: an item tile is drawn on the helmet glass along with everything
    /// else, and on the visor <b>blue is the language and warm is the alarm</b> — a hotbar in amber
    /// would read as a permanent warning. See <see cref="VisorStyle"/>.
    /// </para>
    /// <para>
    /// The colours therefore delegate to <see cref="VisorStyle"/> rather than being written out
    /// again here. What stays local is the GEOMETRY and the refusal shake, which are about the size
    /// and behaviour of a tile rather than its colour, and which the visor has no opinion on.
    /// </para>
    /// <para>
    /// One palette for every tile, deliberately: the hotbar, the worn-gear strip and the body
    /// screen all draw the same <see cref="GearTile"/>, so a slot looks the same everywhere it
    /// appears. Changing it here changes it in all three at once, which is the point.
    /// </para>
    /// <para>
    /// Nothing here is an imported asset, for the reason <see cref="UITheme"/> gives at length: the
    /// HUD prefab has to keep working when it is dropped into a scene, without a folder of PNGs
    /// arriving with it.
    /// </para>
    /// </summary>
    public static class HotbarStyle
    {
        // ── The palette ──────────────────────────────────────────────────────

        /// <summary>The tile itself: near-black glass carrying a trace of the visor's ink.</summary>
        public static readonly Color Tile = new(0.043f, 0.075f, 0.094f, 0.88f);

        /// <summary>The quiet states: hover, and the reserved hatch.</summary>
        public static readonly Color Thread = VisorStyle.InkDim;

        /// <summary>
        /// The one lit thing on the bar: the selected slot. Named Amber for its history — it is
        /// the visor's ink now, and there is nothing warm on a healthy hotbar.
        /// </summary>
        public static readonly Color Amber = VisorStyle.Ink;

        /// <summary>
        /// A live drop target. Brighter than the selection rather than a different hue, because a
        /// place you can drop something is not a danger and must not read as one.
        /// </summary>
        public static readonly Color SafetyOrange = Color.Lerp(VisorStyle.Ink, Color.white, 0.55f);

        /// <summary>The slot numbers.</summary>
        public static readonly Color Stencil = VisorStyle.InkDim;

        // ── Geometry ─────────────────────────────────────────────────────────

        /// <summary>One tile, in reference pixels. Square, because the icon is the whole point.</summary>
        public const float SlotWidth = 116f;

        public const float SlotHeight = 116f;

        /// <summary>Gap between tiles.</summary>
        public const float SlotSpacing = 12f;

        /// <summary>Corner radius of the tile, and therefore of its ring.</summary>
        public const int TileRadius = 14;

        /// <summary>How far the selected tile lifts off the row, in pixels.</summary>
        public const float SelectedLift = 8f;

        /// <summary>Margin between the tile's edge and the item icon, per side.</summary>
        public const float IconInset = 15f;

        // ── The refusal shake ────────────────────────────────────────────────
        //
        // A slot's whole answer to "no room on the pack for this" — see InventorySlotUI.Shake.
        // There is deliberately no text notice to tune alongside these, which is the point of
        // the feature these three describe.

        /// <summary>Seconds one refusal shake lasts.</summary>
        public const float ShakeSeconds = 0.25f;

        /// <summary>The shake's starting amplitude, in pixels.</summary>
        public const float ShakePixels = 6f;

        /// <summary>The wiggle's own frequency, in radians per second.</summary>
        public const float ShakeFrequency = 55f;

        // ── Generated textures ───────────────────────────────────────────────

        private static Sprite hatchSprite;

        /// <summary>
        /// Diagonal hatching. The empty-but-reserved fill for a tile whose item is currently in
        /// the player's hand — "something lives here, it is just not here right now".
        /// </summary>
        public static Sprite HatchSprite => Ensure(ref hatchSprite, MakeHatch);

        /// <summary>
        /// <see cref="HatchSprite"/>'s texture, for a <c>RawImage</c>.
        ///
        /// A repeating fill wants a RawImage and a scaled uv rect, not <c>Image.Type.Tiled</c>: a
        /// ten-pixel tile over a hundred-pixel slot is a few hundred quads the tiled Image path
        /// would generate every time the slot is rebuilt, against one for the uv.
        /// </summary>
        public static Texture HatchTexture => HatchSprite.texture;

        /// <summary>
        /// Returns the cached sprite, remaking it if it has been destroyed.
        ///
        /// The same trap <see cref="UITheme"/> documents: it is
        /// <see cref="HideFlags.HideAndDontSave"/>, so a domain reload destroys it while the
        /// static field still holds a wrapper that is not C# null.
        /// </summary>
        private static Sprite Ensure(ref Sprite cached, System.Func<Sprite> make)
        {
            if (cached == null) cached = make();
            return cached;
        }

        private static Sprite MakeHatch()
        {
            const int size = 10;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // A 45-degree band: the diagonal repeats every `size` pixels by construction,
                    // which is what makes the tile seamless.
                    int diagonal = (x + y) % size;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, diagonal < 2 ? 1f : 0f);
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Hotbar_Hatch",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave,
            };

            tex.SetPixels(pixels);
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "Hotbar_Hatch";
            sprite.hideFlags = HideFlags.HideAndDontSave;

            return sprite;
        }
    }
}
