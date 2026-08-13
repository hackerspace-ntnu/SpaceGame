using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The row controls the settings pages are made of: a slider, a switch, a left/right cycler and
    /// a text field, all sharing one silhouette — a rounded chip, label on the left, control on the
    /// right.
    /// <para>
    /// Each builder returns a handle whose <see cref="Row.Refresh"/> re-reads the value from
    /// wherever it lives. Rows never cache the value they show, so "reset to defaults" or a setting
    /// changed from another screen only has to call Refresh on the page rather than rebuild it.
    /// </para>
    /// </summary>
    public static class SettingsWidgets
    {
        public const float RowHeight = 54f;
        public const float RowSpacing = 8f;
        private const float LabelWidth = 300f;
        private const float SidePadding = 20f;
        private const float ValueWidth = 110f;

        /// <summary>A built row, and the one thing every row can do.</summary>
        public class Row
        {
            public RectTransform Rect;
            public Action RefreshAction;
            public void Refresh() => RefreshAction?.Invoke();
        }

        // ------------------------------------------------------------------ structure

        public static TextMeshProUGUI Heading(RectTransform parent, string text)
        {
            var rect = UIBuilder.Rect("Heading", parent);
            UIBuilder.FixedHeight(rect, 46f);

            var labelRect = UIBuilder.Fill(UIBuilder.Rect("Text", rect), SidePadding, 0f, 0f, 0f);
            var label = UIBuilder.Label(labelRect, text.ToUpperInvariant(), UITheme.CaptionSize,
                UITheme.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            label.characterSpacing = 12f;
            label.alignment = TextAlignmentOptions.BottomLeft;
            return label;
        }

        public static RectTransform Divider(RectTransform parent)
        {
            var rect = UIBuilder.Rect("Divider", parent);
            UIBuilder.FixedHeight(rect, 1f);
            UIBuilder.Solid(UIBuilder.Fill(UIBuilder.Rect("Line", rect), SidePadding, 0f, SidePadding, 0f),
                UITheme.Hairline);
            return rect;
        }

        /// <summary>A read-only line of small print, for hints under a control.</summary>
        public static TextMeshProUGUI Caption(RectTransform parent, string text)
        {
            var rect = UIBuilder.Rect("Caption", parent);
            UIBuilder.FixedHeight(rect, 26f);

            var labelRect = UIBuilder.Fill(UIBuilder.Rect("Text", rect), SidePadding, 0f, SidePadding, 0f);
            return UIBuilder.Label(labelRect, text, UITheme.CaptionSize, UITheme.Faint);
        }

        // --------------------------------------------------------------------- rows

        /// <summary>
        /// Label, track, and the live value on the right. <paramref name="format"/> turns the raw
        /// number into what the player reads — a percentage, a multiplier, degrees.
        /// </summary>
        public static Row Slider(RectTransform parent, string label, float min, float max,
            Func<float> get, Action<float> set, Func<float, string> format, float step = 0f)
        {
            RectTransform row = NewRow(parent, label, out RectTransform control);

            var valueRect = UIBuilder.RightColumn(UIBuilder.Rect("Value", control), 0f, ValueWidth);
            var valueLabel = UIBuilder.Label(valueRect, string.Empty, UITheme.ValueSize, UITheme.Bright,
                TextAlignmentOptions.Right, FontStyles.Bold);

            var sliderRect = UIBuilder.Fill(UIBuilder.Rect("Slider", control), 0f, 0f, ValueWidth + 18f, 0f);
            var slider = BuildSlider(sliderRect, min, max);

            var handle = new Row { Rect = row };
            bool applying = false;

            slider.onValueChanged.AddListener(value =>
            {
                if (applying) return;

                // Snapping happens here rather than on the Slider itself: a listener that rounds
                // the slider's own value cannot change the argument already handed to the
                // listeners queued behind it, so the setter would still see the raw drag value.
                float snapped = step > 0f ? Mathf.Round(value / step) * step : value;
                set(snapped);

                applying = true;
                slider.SetValueWithoutNotify(Mathf.Clamp(get(), min, max));
                applying = false;

                valueLabel.text = format(get());
            });

            handle.RefreshAction = () =>
            {
                applying = true;
                slider.SetValueWithoutNotify(Mathf.Clamp(get(), min, max));
                applying = false;
                valueLabel.text = format(get());
            };

            handle.Refresh();
            return handle;
        }

        /// <summary>An on/off switch whose knob slides across when it flips.</summary>
        public static Row Toggle(RectTransform parent, string label, Func<bool> get, Action<bool> set)
        {
            RectTransform row = NewRow(parent, label, out RectTransform control);

            const float switchWidth = 62f;
            const float switchHeight = 30f;
            const float knobSize = 22f;

            var switchRect = UIBuilder.Rect("Switch", control);
            switchRect.anchorMin = new Vector2(1f, 0.5f);
            switchRect.anchorMax = new Vector2(1f, 0.5f);
            switchRect.pivot = new Vector2(1f, 0.5f);
            switchRect.sizeDelta = new Vector2(switchWidth, switchHeight);
            switchRect.anchoredPosition = Vector2.zero;

            // Radius is half the track's height, which is what makes it read as a pill rather
            // than a rounded box.
            Image track = UIBuilder.Sprite(switchRect, UITheme.Rounded((int)(switchHeight * 0.5f)), UITheme.TrackEmpty);

            var knobRect = UIBuilder.Rect("Knob", switchRect);
            knobRect.anchorMin = new Vector2(0f, 0.5f);
            knobRect.anchorMax = new Vector2(0f, 0.5f);
            knobRect.pivot = new Vector2(0.5f, 0.5f);
            knobRect.sizeDelta = new Vector2(knobSize, knobSize);
            Image knob = UIBuilder.Sprite(knobRect, UITheme.CircleSprite, UITheme.Bright, Image.Type.Simple);

            var checkRect = UIBuilder.Rect("Check", knobRect);
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = new Vector2(4f, 4f);
            checkRect.offsetMax = new Vector2(-4f, -4f);
            Image check = UIBuilder.Sprite(checkRect, UITheme.CheckSprite, UITheme.Panel, Image.Type.Simple);

            var handle = new Row { Rect = row };
            handle.RefreshAction = () =>
            {
                bool on = get();
                track.color = on ? UITheme.Accent : UITheme.TrackEmpty;
                knob.color = on ? UITheme.Bright : UITheme.Faint;
                check.enabled = on;
                float inset = knobSize * 0.5f + 4f;
                knobRect.anchoredPosition = new Vector2(on ? switchWidth - inset : inset, 0f);
            };

            // The whole row flips it, not just the switch — a 62px target on a 300px-wide label is
            // needlessly precise for a binary choice.
            Image hit = UIBuilder.HitArea(row);
            UIBuilder.Clickable(row, hit, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.05f))
                .onClick.AddListener(() =>
                {
                    set(!get());
                    handle.Refresh();
                });

            handle.Refresh();
            return handle;
        }

        /// <summary>
        /// A value with a chevron on either side, for lists that are short and ordered — quality
        /// level, resolution, frame cap. Preferred over a dropdown because a dropdown needs a
        /// popup layer, and this menu draws over live gameplay where a stray popup left behind
        /// after a resume is worse than one extra click.
        /// </summary>
        public static Row Cycler(RectTransform parent, string label, Func<string> describe,
            Action step, Action stepBack)
        {
            RectTransform row = NewRow(parent, label, out RectTransform control);

            const float arrowSize = 30f;
            const float valueWidth = 190f;

            var handle = new Row { Rect = row };

            var valueRect = UIBuilder.Rect("Value", control);
            valueRect.anchorMin = new Vector2(1f, 0.5f);
            valueRect.anchorMax = new Vector2(1f, 0.5f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(valueWidth, RowHeight);
            valueRect.anchoredPosition = new Vector2(-arrowSize - 10f, 0f);
            var valueLabel = UIBuilder.Label(valueRect, string.Empty, UITheme.ValueSize, UITheme.Bright,
                TextAlignmentOptions.Center, FontStyles.Bold);

            Arrow(control, -(valueWidth + arrowSize + 20f), arrowSize, flip: true, () => { stepBack(); handle.Refresh(); });
            Arrow(control, 0f, arrowSize, flip: false, () => { step(); handle.Refresh(); });

            handle.RefreshAction = () => valueLabel.text = describe();
            handle.Refresh();
            return handle;
        }

        /// <summary>An editable single-line field, used for the player's own name.</summary>
        public static Row TextField(RectTransform parent, string label, Func<string> get, Action<string> set,
            int characterLimit, string placeholder = "")
        {
            RectTransform row = NewRow(parent, label, out RectTransform control);

            const float fieldWidth = 320f;

            var fieldRect = UIBuilder.Rect("Field", control);
            fieldRect.anchorMin = new Vector2(1f, 0.5f);
            fieldRect.anchorMax = new Vector2(1f, 0.5f);
            fieldRect.pivot = new Vector2(1f, 0.5f);
            fieldRect.sizeDelta = new Vector2(fieldWidth, 38f);
            fieldRect.anchoredPosition = Vector2.zero;

            Image background = UIBuilder.Sprite(fieldRect, UITheme.ChipSprite, new Color(1f, 1f, 1f, 0.07f));
            background.raycastTarget = true;

            // TMP_InputField needs the caret and selection to be clipped to a viewport that is not
            // the field itself, otherwise long text draws outside the chip.
            var viewport = UIBuilder.Fill(UIBuilder.Rect("Text Area", fieldRect), 14f, 4f, 14f, 4f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var textRect = UIBuilder.Fill(UIBuilder.Rect("Text", viewport));
            var text = UIBuilder.Label(textRect, string.Empty, UITheme.ValueSize, UITheme.Bright);
            text.alignment = TextAlignmentOptions.Left;

            var placeholderRect = UIBuilder.Fill(UIBuilder.Rect("Placeholder", viewport));
            var placeholderLabel = UIBuilder.Label(placeholderRect, placeholder, UITheme.ValueSize, UITheme.Faint);
            placeholderLabel.fontStyle = FontStyles.Italic;

            var field = fieldRect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = placeholderLabel;
            field.targetGraphic = background;
            field.characterLimit = characterLimit;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.caretColor = UITheme.Accent;
            field.customCaretColor = true;
            field.selectionColor = UITheme.AccentSoft;
            field.restoreOriginalTextOnEscape = true;
            field.text = get();

            var handle = new Row { Rect = row };
            handle.RefreshAction = () =>
            {
                if (!field.isFocused) field.SetTextWithoutNotify(get());
            };

            // Committed on submit and on losing focus, never per keystroke: a name that replicates
            // on every character would broadcast half-typed names to the whole session.
            field.onSubmit.AddListener(value => { set(value); handle.Refresh(); });
            field.onDeselect.AddListener(value => { set(value); handle.Refresh(); });

            return handle;
        }

        /// <summary>
        /// A full-width button row, for actions that live inside a page rather than the footer
        /// (reset to defaults, and similar).
        /// </summary>
        public static Row Action(RectTransform parent, string label, string buttonText, Action onClick,
            Color? tint = null)
        {
            RectTransform row = NewRow(parent, label, out RectTransform control);

            var buttonRect = UIBuilder.Rect("Button", control);
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.sizeDelta = new Vector2(190f, 38f);
            buttonRect.anchoredPosition = Vector2.zero;

            Color color = tint ?? UITheme.Accent;
            Image background = UIBuilder.Sprite(buttonRect, UITheme.ChipSprite, Color.white);
            UIBuilder.LabelIn(buttonRect, "Text", buttonText, UITheme.LabelSize, color,
                TextAlignmentOptions.Center, FontStyles.Bold);

            UIBuilder.Clickable(buttonRect, background,
                    new Color(color.r, color.g, color.b, 0.16f),
                    new Color(color.r, color.g, color.b, 0.32f))
                .onClick.AddListener(() => onClick());

            return new Row { Rect = row };
        }

        // ----------------------------------------------------------------- plumbing

        /// <summary>The shared chip-and-label shell every row above is built on.</summary>
        private static RectTransform NewRow(RectTransform parent, string label, out RectTransform control)
        {
            var row = UIBuilder.Rect(string.IsNullOrEmpty(label) ? "Row" : label, parent);
            UIBuilder.FixedHeight(row, RowHeight);

            UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Chip", row)), UITheme.ChipSprite,
                new Color(1f, 1f, 1f, 0.035f));

            var labelRect = UIBuilder.LeftColumn(UIBuilder.Rect("Label", row), SidePadding, LabelWidth);
            UIBuilder.Label(labelRect, label, UITheme.LabelSize, UITheme.Muted);

            control = UIBuilder.Fill(UIBuilder.Rect("Control", row), SidePadding + LabelWidth, 0f, SidePadding, 0f);
            return row;
        }

        private static void Arrow(RectTransform parent, float x, float size, bool flip, Action onClick)
        {
            var rect = UIBuilder.Rect(flip ? "Prev" : "Next", parent);
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(x, 0f);

            Image hit = UIBuilder.HitArea(rect);

            var glyphRect = UIBuilder.Fill(UIBuilder.Rect("Glyph", rect), 6f, 6f, 6f, 6f);
            Image glyph = UIBuilder.Sprite(glyphRect, UITheme.ChevronSprite, UITheme.Muted, Image.Type.Simple);
            if (flip) glyphRect.localScale = new Vector3(-1f, 1f, 1f);

            var button = UIBuilder.Clickable(rect, hit, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.12f));
            button.onClick.AddListener(() => onClick());

            // The invisible hit graphic is what the tint block drives, so the chevron itself is
            // brightened by hand on hover through the button's own transition target list.
            var hover = rect.gameObject.AddComponent<HoverTint>();
            hover.Bind(glyph, UITheme.Muted, UITheme.Bright);
        }

        /// <summary>
        /// Standard uGUI Slider assembled by hand. The fill's anchors are overwritten by the
        /// Slider every frame it changes, so its offsets — not its anchors — are what position it.
        /// </summary>
        private static UnityEngine.UI.Slider BuildSlider(RectTransform rect, float min, float max)
        {
            UIBuilder.HitArea(rect);

            const float trackHeight = 10f;
            const float handleSize = 20f;
            Sprite trackSprite = UITheme.Rounded((int)(trackHeight * 0.5f));

            var background = UIBuilder.Rect("Track", rect);
            background.anchorMin = new Vector2(0f, 0.5f);
            background.anchorMax = new Vector2(1f, 0.5f);
            background.pivot = new Vector2(0.5f, 0.5f);
            background.offsetMin = new Vector2(handleSize * 0.5f, -trackHeight * 0.5f);
            background.offsetMax = new Vector2(-handleSize * 0.5f, trackHeight * 0.5f);
            UIBuilder.Sprite(background, trackSprite, UITheme.TrackEmpty);

            var fillArea = UIBuilder.Rect("Fill Area", rect);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.pivot = new Vector2(0.5f, 0.5f);
            fillArea.offsetMin = new Vector2(handleSize * 0.5f, -trackHeight * 0.5f);
            fillArea.offsetMax = new Vector2(-handleSize * 0.5f, trackHeight * 0.5f);

            var fill = UIBuilder.Rect("Fill", fillArea);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            UIBuilder.Sprite(fill, trackSprite, UITheme.Accent);

            // The slide area is only as tall as the handle. The Slider overwrites the handle's
            // anchors every time the value changes — anchorMin.y stays 0 and anchorMax.y stays 1 —
            // so the handle always stretches to its parent's full height. Given the whole row, a
            // 20px circle comes out as a 20x54 ellipse.
            var handleArea = UIBuilder.Rect("Handle Slide Area", rect);
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.pivot = new Vector2(0.5f, 0.5f);
            handleArea.offsetMin = new Vector2(handleSize * 0.5f, -handleSize * 0.5f);
            handleArea.offsetMax = new Vector2(-handleSize * 0.5f, handleSize * 0.5f);

            var handle = UIBuilder.Rect("Handle", handleArea);
            handle.sizeDelta = new Vector2(handleSize, handleSize);
            Image handleImage = UIBuilder.Sprite(handle, UITheme.CircleSprite, Color.white, Image.Type.Simple);
            handleImage.raycastTarget = true;

            var slider = rect.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;

            var colors = slider.colors;
            colors.normalColor = UITheme.Bright;
            colors.highlightedColor = Color.white;
            colors.pressedColor = UITheme.Accent;
            colors.selectedColor = UITheme.Bright;
            colors.fadeDuration = 0.08f;
            slider.colors = colors;

            // No snapping listener here on purpose — see the comment in Slider(), which does the
            // rounding before calling the setter. Rounding the slider's own value from a listener
            // cannot change the argument already handed to listeners queued behind it.
            return slider;
        }
    }
}
