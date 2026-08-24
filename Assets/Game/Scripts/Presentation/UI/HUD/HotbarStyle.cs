using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The hotbar's palette and the one texture <see cref="UITheme"/> does not have.
    ///
    /// <para>
    /// Separate from <see cref="UITheme"/> on purpose. That is the look of the game's full-screen
    /// MENUS — near-black panels and a cold blue accent, a screen you are reading — and the hotbar
    /// sits over the world, so it keeps the expedition's warm palette instead of the menu's blue.
    /// </para>
    /// <para>
    /// The colours are lifted straight from the model library's material table
    /// (<c>Assets/Game/Art/Models/_Source~/PALETTE.md</c>) rather than invented, so the amber on
    /// the HUD is the same amber that glows on the rig. The hex is written beside each one because
    /// that table is the source and this is a copy of it.
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

        /// <summary>Mat_Fabric_Canvas_Faded in shadow, near #32312A. The tile itself.</summary>
        public static readonly Color Tile = new(0.196f, 0.192f, 0.165f, 1f);

        /// <summary>Mat_Fabric_Rope_Hemp, #B89968. The quiet states: hover, and the reserved hatch.</summary>
        public static readonly Color Thread = new(0.722f, 0.600f, 0.408f, 1f);

        /// <summary>Mat_Emissive_Amber, #FFB347. The one lit thing on the bar: the selected slot.</summary>
        public static readonly Color Amber = new(1f, 0.702f, 0.278f, 1f);

        /// <summary>Mat_Paint_Safety_Orange, #D9541F. High-vis, and used only for a live drop target.</summary>
        public static readonly Color SafetyOrange = new(0.851f, 0.329f, 0.122f, 1f);

        /// <summary>Mat_Fabric_Flag_Bleached, #D8D2C2. The slot numbers.</summary>
        public static readonly Color Stencil = new(0.847f, 0.824f, 0.761f, 1f);

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
