using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Text typed on a rule — this menu's substitute for an input box.
    ///
    /// The menu's language has no boxes, so a field cannot be drawn as one. What is left is the
    /// line under the words, which is also the only part of a text field that has to be visible:
    /// the box was never doing anything the underline and the caret do not.
    ///
    /// Built here rather than per screen so that every field in the menus agrees on type size, rule
    /// weight and caret colour — otherwise a flow reads as several screens by several authors.
    ///
    /// <para>
    /// That same rule is also the field's only way to show its state, which
    /// <see cref="MenuFieldRule"/> drives: hovered, focused, or neither. It shipped for a while with
    /// no state at all, which made a field indistinguishable from the caption above it and left
    /// players typing lobby codes into a screen that never acknowledged them.
    /// </para>
    /// </summary>
    public static class MenuField
    {
        /// <summary>Tall enough for a 64pt line plus the rule beneath it.</summary>
        public const float Height = 96f;

        /// <summary>Wide enough to be seen at this type size. See where it is assigned.</summary>
        private const int CaretWidth = 4;

        /// <summary>
        /// A single-line field drawn on a rule <paramref name="width"/> wide.
        ///
        /// <paramref name="onSubmit"/> is wired to Enter, because a player who has just typed a
        /// lobby code has already told us they are done and making them find the button as well is
        /// a step for nothing.
        /// </summary>
        public static TMP_InputField Rule(RectTransform parent, string name, string placeholder,
            float width, UnityAction<string> onSubmit = null, int characterLimit = 0)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);

            // Both placements, the way MenuEntry.Create handles them. Filling the parent is what
            // positions this when it is dropped into a plain pinned row; the layout element is what
            // sizes it when the parent is a layout group, which overrides the anchors on its next
            // pass. Without the fill, a field in a pinned row keeps Unity's default 100x100 rect
            // and lands in the middle of it.
            UIBuilder.Fill(rect);
            UIBuilder.FixedHeight(rect, Height);

            // The whole row takes the click, not just the glyphs — otherwise an empty field, which
            // is every field before it is used, has almost no hit area at all.
            Image hit = UIBuilder.HitArea(rect);

            RectTransform underline = UIBuilder.Rect("Underline", rect);
            underline.anchorMin = new Vector2(0f, 0f);
            underline.anchorMax = new Vector2(0f, 0f);
            underline.pivot = new Vector2(0f, 0f);
            underline.anchoredPosition = Vector2.zero;
            underline.sizeDelta = new Vector2(width, 3f);
            Image rule = UIBuilder.Solid(underline, MenuEntry.Caption);

            // The focused state, drawn over the resting rule rather than by recolouring it: growing
            // a second, thicker line across the field is a cue you can catch from the corner of your
            // eye, and a colour change on a 3px line over sand is not. Starts at zero width — see
            // MenuFieldRule, which owns everything about it from here on.
            RectTransform focusRule = UIBuilder.Rect("FocusRule", rect);
            focusRule.anchorMin = new Vector2(0f, 0f);
            focusRule.anchorMax = new Vector2(0f, 0f);
            focusRule.pivot = new Vector2(0f, 0f);
            focusRule.anchoredPosition = Vector2.zero;
            focusRule.sizeDelta = new Vector2(0f, MenuFieldRule.FocusThickness);
            UIBuilder.Solid(focusRule, MenuEntry.Idle);

            RectTransform textArea = UIBuilder.Rect("TextArea", rect);
            textArea.anchorMin = new Vector2(0f, 0f);
            textArea.anchorMax = new Vector2(0f, 1f);
            textArea.pivot = new Vector2(0f, 0.5f);
            textArea.offsetMin = new Vector2(0f, 12f);
            textArea.offsetMax = new Vector2(width, 0f);
            textArea.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI hint = UIBuilder.LabelIn(textArea, "Placeholder", placeholder,
                                                     MenuEntry.ActionSize, MenuEntry.Caption,
                                                     TextAlignmentOptions.Left, FontStyles.Bold);
            TextMeshProUGUI text = UIBuilder.LabelIn(textArea, "Text", string.Empty,
                                                     MenuEntry.ActionSize, MenuEntry.Idle,
                                                     TextAlignmentOptions.Left, FontStyles.Bold);

            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = hit;
            input.textViewport = textArea;
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretColor = MenuEntry.Idle;
            input.customCaretColor = true;
            input.characterLimit = characterLimit;

            // Unity's default is one pixel, which beside 64pt bold type is not a caret so much as a
            // rendering artefact. Most of why these fields read as dead even while focused was that
            // the only thing saying otherwise was invisible.
            input.caretWidth = CaretWidth;

            if (onSubmit != null) input.onSubmit.AddListener(onSubmit);

            rect.gameObject.AddComponent<MenuFieldRule>()
                .Bind(input, rule, focusRule, hint, width);

            return input;
        }

        /// <summary>
        /// A right-aligned value drawn beside an entry's label — the "2/4" on a session row, the
        /// "on" on a toggle.
        ///
        /// It has to be its own object rather than a tint on the entry's own label: the menu
        /// button's animator rewrites that label's colour on every state change, so anything said
        /// there lasts until the next frame. <see cref="MenuEntry"/> documents the same trap.
        /// </summary>
        public static TextMeshProUGUI Trailing(Button entry, TextMeshProUGUI label, string value,
            float width, Color color)
        {
            MenuEntry.InsetLabel(label, 0f, width + 24f);

            RectTransform rect = UIBuilder.Rect("Trailing", (RectTransform)entry.transform);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-width, 0f);
            rect.offsetMax = Vector2.zero;

            return UIBuilder.Label(rect, value, MenuEntry.CaptionSize, color,
                                   TextAlignmentOptions.Right);
        }
    }
}
