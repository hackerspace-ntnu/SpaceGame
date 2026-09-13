using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A "−  3  +" number row: a label, a value, and the two chevrons that move it.
    ///
    /// <para>
    /// A stepper rather than a slider because there is nothing here for a slider's coloured bar and
    /// handle to sit on — this menu draws no boxes, and a filled track is a box by another name.
    /// <see cref="MinigameConfigUI"/> reached the same conclusion first, in the comment on its own
    /// (private) version of this row: "a number field made of text, which suits this screen better
    /// than a slider's coloured bar and handle." That row is where this widget was lifted from, once
    /// a second and third screen needed the identical control and copying it a second time stopped
    /// being the cheaper option.
    /// </para>
    ///
    /// <para>
    /// <b>The load-bearing rule: this widget REPORTS, it does not DECIDE.</b> Pressing a chevron never
    /// changes anything on screen by itself — it clamps the value that was asked for to
    /// <c>[min, max]</c> and hands it to <c>onChanged</c>. The row keeps showing whatever
    /// <see cref="SetValue"/> last told it to show, until a caller calls it again. That is what lets a
    /// caller refuse a change outright — a lobby saying "Team One already has 3 players" — without the
    /// row having visibly moved and then needing to be put back, which is the kind of one-frame flicker
    /// a player notices even when they can't say why.
    /// </para>
    /// </summary>
    public class MenuStepper
    {
        /// <summary>Matches MinigameConfigUI's own (private) LabelWidth, the row this was lifted from.</summary>
        public const float LabelWidth = 330f;

        /// <summary>
        /// Matches <c>LobbyPreviewRank</c>'s own chevron column (its <c>ChevronWidth</c>,
        /// beside its colour cycler) rather than inventing a second number for the same idea.
        /// </summary>
        public const float ChevronWidth = 74f;

        public const float ValueWidth = 96f;

        /// <summary>
        /// Also <c>LobbyPreviewRank</c>'s number: its cycler is the same shape as this row —
        /// chevron, value, chevron — just spent on a colour name instead of an integer, and there is
        /// no reason the two should end up a different height by accident.
        /// </summary>
        public const float Height = 74f;

        /// <summary>
        /// The measurements and palette one row is drawn with. The parameterless
        /// <see cref="Create(GameObject, RectTransform, string, int, int, int, Action{int})"/>
        /// builds the full-page-column row every existing screen uses; a caller squeezing a stepper
        /// into a caption-scale strip passes its own numbers instead.
        ///
        /// <para>
        /// <see cref="Light"/> flips the palette for a row that sits over SKY rather than sand: the
        /// two labels become white with the navy drop shadow the top-strip captions carry, and the
        /// chevrons go through <see cref="MenuEntry.MakeLight"/> like every other button up there,
        /// instead of the navy-ground <see cref="ChevronIdle"/> treatment.
        /// </para>
        /// </summary>
        public readonly struct Skin
        {
            public readonly int LabelSize;
            public readonly int ValueSize;
            public readonly float LabelWidth;
            public readonly float ChevronWidth;
            public readonly float ValueWidth;
            public readonly float Height;
            public readonly bool Light;

            /// <summary>Drop-shadow offset for the light palette's shadowed labels.</summary>
            public readonly Vector2 ShadowOffset;

            public Skin(int labelSize, int valueSize, float labelWidth, float chevronWidth,
                float valueWidth, float height, bool light, Vector2 shadowOffset = default)
            {
                LabelSize = labelSize;
                ValueSize = valueSize;
                LabelWidth = labelWidth;
                ChevronWidth = chevronWidth;
                ValueWidth = valueWidth;
                Height = height;
                Light = light;
                ShadowOffset = shadowOffset;
            }

            /// <summary>The whole row's width — label, both chevrons, value.</summary>
            public float Width => LabelWidth + ChevronWidth * 2f + ValueWidth;
        }

        // ASCII, not ◀ / ▶. The project's TMP default is LiberationSans SDF, which has neither
        // U+25C0 nor U+25B6 and no fallback that does — TMP silently substitutes U+25A1, and both
        // arrows render as empty boxes. LobbyPreviewRank hit exactly this ("caught from a warning in
        // a capture, where the cycler read as '□ Ember □'"). Anything fancier than plain ASCII has to
        // be checked against the font first.
        private const string DecreaseGlyph = "<";
        private const string IncreaseGlyph = ">";

        /// <summary>
        /// What a chevron is drawn in — deliberately NOT <see cref="MenuEntry.Caption"/>.
        ///
        /// <para>
        /// Caption is the same navy as <see cref="MenuEntry.Idle"/> at 62% alpha, so against dark
        /// ground it is the resting entry colour with MORE of the background showing through, not
        /// less — the opposite of what a control you actually aim at needs. This is the opaque light
        /// blue-grey <c>MinigameConfigUI</c> arrived at for the very stepper this widget was extracted
        /// from (its own <c>Dim</c> field, applied to its stepper chevrons in <c>AddStepperButton</c>),
        /// for exactly the same reason.
        /// </para>
        ///
        /// <para>
        /// Nobody has seen this rendered: the editor is contended and the MCP bridge refuses play
        /// mode, so this value is reasoned from the palette's own numbers and from that precedent, not
        /// from a capture. Treat it as a considered guess until someone can screenshot the first screen
        /// that uses this widget.
        /// </para>
        /// </summary>
        private static readonly Color ChevronIdle = new(0.55f, 0.60f, 0.68f, 1f);

        public RectTransform Root { get; private set; }
        public TextMeshProUGUI ValueLabel { get; private set; }
        public Button Decrease { get; private set; }
        public Button Increase { get; private set; }

        /// <summary>The value's navy shadow copy, when the skin is light. Written with it or the shadow goes stale.</summary>
        private TextMeshProUGUI valueShadow;

        private int value;
        private int min;
        private int max;

        /// <summary>
        /// Builds one row at the full-page-column measurements above. <paramref name="prefab"/> is
        /// forwarded to <see cref="MenuEntry.Create"/> for both chevrons exactly the way
        /// <c>LobbyPreviewRank</c> forwards its own <c>entryPrefab</c> into its cycler's chevrons —
        /// pass null for a plain, unanimated pair built from scratch.
        /// </summary>
        public static MenuStepper Create(GameObject prefab, RectTransform parent, string label,
            int value, int min, int max, Action<int> onChanged)
        {
            return Create(prefab, parent, label, value, min, max, onChanged,
                          new Skin(MenuEntry.RowSize, MenuEntry.ActionSize, LabelWidth, ChevronWidth,
                                   ValueWidth, Height, light: false));
        }

        /// <summary>Builds one row to the given <see cref="Skin"/> instead of the full-size default.</summary>
        public static MenuStepper Create(GameObject prefab, RectTransform parent, string label,
            int value, int min, int max, Action<int> onChanged, Skin skin)
        {
            var stepper = new MenuStepper { value = value, min = min, max = max };

            // Both placements, the way MenuEntry.Create and MenuField.Rule handle them: Fill is what
            // positions the row when it is dropped somewhere pinned by anchors, and the LayoutElement
            // from FixedHeight is what sizes it when the parent is a layout column, which overrides
            // the anchors on its next pass regardless. Root never carries a LayoutGroup of its own —
            // everything inside it is placed against its own edges by Slice below, not by a nested
            // layout group — so this does not hit the LayoutElement/LayoutGroup priority collision
            // UIBuilder's class doc describes (and MinigameConfigUI works around with an extra
            // "Content" child per row). There is nothing to work around here because there is no
            // second layout group on this object to begin with.
            stepper.Root = UIBuilder.Rect(label, parent);
            UIBuilder.Fill(stepper.Root);
            UIBuilder.FixedHeight(stepper.Root, skin.Height);

            RectTransform labelSlot = UIBuilder.Slice(stepper.Root, "Label", 0f, skin.LabelWidth);
            if (skin.Light)
                UIBuilder.ShadowedLabel(labelSlot, label, skin.LabelSize, MenuEntry.Title,
                                        MenuEntry.Idle, skin.ShadowOffset, TextAlignmentOptions.Left,
                                        out _);
            else
                UIBuilder.Label(labelSlot, label, skin.LabelSize, MenuEntry.Caption);

            RectTransform lessSlot = UIBuilder.Slice(stepper.Root, "Less", skin.LabelWidth, skin.ChevronWidth);
            stepper.Decrease = Chevron(prefab, lessSlot, "Less", DecreaseGlyph, skin,
                                       () => stepper.Report(-1, onChanged));

            RectTransform valueSlot = UIBuilder.Slice(stepper.Root, "Value",
                                                      skin.LabelWidth + skin.ChevronWidth, skin.ValueWidth);
            if (skin.Light)
                stepper.ValueLabel = UIBuilder.ShadowedLabel(valueSlot, value.ToString(), skin.ValueSize,
                                                             MenuEntry.Title, MenuEntry.Idle,
                                                             skin.ShadowOffset,
                                                             TextAlignmentOptions.Center,
                                                             out stepper.valueShadow);
            else
                stepper.ValueLabel = UIBuilder.Label(valueSlot, value.ToString(), skin.ValueSize,
                                                     MenuEntry.Idle, TextAlignmentOptions.Center,
                                                     FontStyles.Bold);

            RectTransform moreSlot = UIBuilder.Slice(stepper.Root, "More",
                                           skin.LabelWidth + skin.ChevronWidth + skin.ValueWidth,
                                           skin.ChevronWidth);
            stepper.Increase = Chevron(prefab, moreSlot, "More", IncreaseGlyph, skin,
                                       () => stepper.Report(1, onChanged));

            return stepper;
        }

        /// <summary>
        /// Repaints the row with a value a caller has already decided on. Never called by the row
        /// itself — see the class doc's reports-not-decides rule.
        /// </summary>
        public void SetValue(int newValue)
        {
            value = newValue;
            if (ValueLabel != null) ValueLabel.text = newValue.ToString();
            if (valueShadow != null) valueShadow.text = newValue.ToString();
        }

        /// <summary>
        /// Narrows or widens the range the next chevron press clamps into.
        ///
        /// <para>
        /// Deliberately does not re-clamp <see cref="value"/> or touch <see cref="ValueLabel"/>: this
        /// widget only ever changes what is on screen from <see cref="SetValue"/>, and a limits change
        /// is not a value change. If a caller narrows the range below the value currently shown — a
        /// lobby polling team sizes down while a team shrinks — the row keeps displaying the old
        /// number until told otherwise, and the next chevron press clamps <i>from</i> that stale
        /// number <i>into</i> the new range rather than from wherever the new range happens to start.
        /// That is still the right answer (the reported value lands on the new boundary either way),
        /// but a caller that wants the display to catch up immediately, rather than on the next press,
        /// has to pair this with its own <see cref="SetValue"/> call.
        /// </para>
        /// </summary>
        public void SetLimits(int newMin, int newMax)
        {
            min = newMin;
            max = newMax;
        }

        public void SetInteractable(bool interactable)
        {
            if (Decrease != null) Decrease.interactable = interactable;
            if (Increase != null) Increase.interactable = interactable;
        }

        private void Report(int direction, Action<int> onChanged)
        {
            int wanted = Mathf.Clamp(value + direction, min, max);
            onChanged?.Invoke(wanted);
        }

        private static Button Chevron(GameObject prefab, RectTransform slot, string name, string glyph,
            Skin skin, Action onClick)
        {
            Button button = MenuEntry.Create(prefab, slot, name, glyph, skin.ValueSize, skin.Height,
                                             () => onClick(), out TextMeshProUGUI label);
            label.alignment = TextAlignmentOptions.Center;

            if (skin.Light) MenuEntry.MakeLight(button, label);
            else TintChevron(button, label);

            return button;
        }

        /// <summary>
        /// Recolours a chevron to <see cref="ChevronIdle"/> instead of the menu's usual
        /// <see cref="MenuEntry.Idle"/> navy that <see cref="MenuEntry.Create"/> defaults every
        /// text button to. See <see cref="ChevronIdle"/> itself for why the replacement is that
        /// colour and not <see cref="MenuEntry.Caption"/>.
        ///
        /// <para>
        /// Handles the prefab path, not only the from-scratch one: <see cref="MenuEntry"/>'s class doc
        /// warns that a menu-button prefab's Animator rewrites its label's colour on every state
        /// change, so a plain assignment sticks for one frame and is then overwritten. Disabling the
        /// animator and driving the colour through the Button's own tint block instead is the exact
        /// fix <see cref="MenuEntry.MakeLight"/> uses for the same trap, just aimed at a different
        /// colour.
        /// </para>
        /// </summary>
        private static void TintChevron(Button button, TextMeshProUGUI label)
        {
            var animator = button.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;

            // The tint multiplies the graphic's own colour, so the label goes white and the block
            // carries every state's actual colour — the same split MenuEntry.MakeLight uses.
            label.color = Color.white;
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = label;

            ColorBlock colors = button.colors;
            colors.normalColor = ChevronIdle;
            colors.selectedColor = ChevronIdle;

            // Brighter on hover, a step darker when pressed — the same lerp-toward-white/lerp-toward-
            // black shape MinigameConfigUI.TintText and UIBuilder.Clickable's own default pressed
            // colour both already use for exactly this kind of tint block.
            colors.highlightedColor = Color.Lerp(ChevronIdle, Color.white, 0.5f);
            colors.pressedColor = Color.Lerp(ChevronIdle, Color.black, 0.25f);

            // Fades ChevronIdle's own ALPHA rather than switching to navy: a locked chevron still
            // has to read as present-but-not-yours, not vanish, the way MenuEntry.MakeLight fades its
            // own light disabled colour by alpha instead of by hue.
            colors.disabledColor = new Color(ChevronIdle.r, ChevronIdle.g, ChevronIdle.b, 0.35f);

            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.09f;
            button.colors = colors;
        }
    }
}
