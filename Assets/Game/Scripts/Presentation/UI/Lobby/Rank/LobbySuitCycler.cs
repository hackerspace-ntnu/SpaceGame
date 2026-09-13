using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Characters;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The colour cycler under the local player's own figure: a chevron, the swatch, its name, a
    /// chevron.
    ///
    /// <para>
    /// The name is its own object rather than a chevron's label, because the menu button's
    /// animator rewrites its own label's colour on every state change — anything written there
    /// survives until the next frame. The swatch itself is shown because a colour's name is not a
    /// colour: "Aqua" and "Cyan" are indistinguishable as words and obvious as chips.
    /// </para>
    /// </summary>
    internal sealed class LobbySuitCycler
    {
        // ASCII, not ◀ and ▶. The project's TMP default is LiberationSans SDF, which has neither
        // U+25C0 nor U+25B6 and no fallback that does — TMP silently substitutes U+25A1 and both
        // arrows render as empty BOXES. Anything fancier than this has to be checked against the
        // font first.
        private const string PreviousGlyph = "<";
        private const string NextGlyph = ">";

        private const float Width = 460f;
        private const float Height = 74f;
        private const float ChevronWidth = 74f;
        private const float ChipSize = 34f;
        private const float ChipInset = 6f;
        private const float NameInset = 48f;

        private readonly LobbyOverlayLayer layer;
        private readonly RectTransform row;
        private readonly TextMeshProUGUI name;
        private readonly Image chip;

        /// <param name="onStep">Called with -1 or +1 when a chevron is pressed.</param>
        public LobbySuitCycler(LobbyOverlayLayer layer, GameObject entryPrefab, Action<int> onStep)
        {
            this.layer = layer;

            row = layer.Centred("SuitCycler", Width, Height);
            row.gameObject.SetActive(false);

            MenuEntry.Create(entryPrefab, UIBuilder.Slice(row, "LeftSlot", 0f, ChevronWidth), "PrevColor",
                             PreviousGlyph, MenuEntry.ActionSize, Height, () => onStep?.Invoke(-1), out _);

            MenuEntry.Create(entryPrefab, UIBuilder.Slice(row, "RightSlot", Width - ChevronWidth, ChevronWidth),
                             "NextColor", NextGlyph, MenuEntry.ActionSize, Height, () => onStep?.Invoke(1), out _);

            RectTransform middle = UIBuilder.Slice(row, "Value", ChevronWidth, Width - ChevronWidth * 2f);

            RectTransform chipRect = UIBuilder.Rect("Chip", middle);
            chipRect.anchorMin = new Vector2(0f, 0.5f);
            chipRect.anchorMax = new Vector2(0f, 0.5f);
            chipRect.pivot = new Vector2(0f, 0.5f);
            chipRect.anchoredPosition = new Vector2(ChipInset, 0f);
            chipRect.sizeDelta = new Vector2(ChipSize, ChipSize);
            chip = UIBuilder.Solid(chipRect, Color.white);

            RectTransform text = UIBuilder.Rect("Name", middle);
            text.anchorMin = Vector2.zero;
            text.anchorMax = Vector2.one;
            text.pivot = new Vector2(0.5f, 0.5f);
            text.offsetMin = new Vector2(NameInset, 0f);
            text.offsetMax = Vector2.zero;
            name = UIBuilder.Label(text, string.Empty, MenuEntry.RowSize, MenuEntry.Idle,
                                   TextAlignmentOptions.Left, FontStyles.Bold);
        }

        public void SetColor(int index)
        {
            if (name != null) name.text = SuitPalette.NameOf(index);
            if (chip != null) chip.color = SuitPalette.ColorOf(index);
        }

        /// <summary>Puts the cycler under <paramref name="worldPoint"/>, or hides it when there is nobody to sit under.</summary>
        public void Position(Camera camera, bool wanted, Vector3 worldPoint)
        {
            if (row == null) return;

            row.gameObject.SetActive(wanted && layer.Place(camera, row, worldPoint));
        }
    }
}
