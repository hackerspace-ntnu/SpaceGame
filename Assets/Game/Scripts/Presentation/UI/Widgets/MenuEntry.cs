using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// One clickable line of the main menu, and the palette the menu is drawn in.
    ///
    /// The colours are not a choice made here — they are read off
    /// <c>Art/Animations/UI/Buttons/Menu Button.controller</c>, whose clips drive the label's
    /// <c>m_fontColor</c> and the root's scale on every state change. That has one consequence worth
    /// knowing before building anything on top of an entry: <b>the animator owns the label's
    /// colour</b>. Tinting an entry's own text to mark it selected lasts until the next frame.
    /// Anything a screen wants to say with colour has to be said on a different object.
    ///
    /// Entries are cloned from the menu's button prefab so they carry its hover animation and its
    /// two FMOD sounds. When no prefab is available the same line is built from scratch in the same
    /// palette — silent and unanimated, but present. A screen whose buttons vanish because a
    /// serialized reference was never assigned is the failure this project already builds screens
    /// from code to avoid.
    /// </summary>
    public static class MenuEntry
    {
        /// <summary>Resting colour of a menu entry, as authored in MainMenu.unity.</summary>
        public static readonly Color Idle = new(0.043f, 0.118f, 0.227f, 1f);

        /// <summary>What the animator turns an entry into on hover.</summary>
        public static readonly Color Hover = Color.white;

        /// <summary>The pressed state, and the colour this menu says "destructive" in.</summary>
        public static readonly Color Warning = new(0.545f, 0f, 0f, 1f);

        /// <summary>Page titles, matching the menu's own "Space Game".</summary>
        public static readonly Color Title = Color.white;

        /// <summary>Secondary text: dates, hints, the line under a heading.</summary>
        public static readonly Color Caption = new(0.043f, 0.118f, 0.227f, 0.62f);

        public const int TitleSize = 110;
        public const int ActionSize = 64;
        public const int RowSize = 52;
        public const int CaptionSize = 30;

        /// <summary>The height every action row — a footer button, a Join, a Start — is built at.</summary>
        public const float ActionHeight = 78f;

        // ───────────────────────────────────────────────────────────────────── layout
        //
        // Shared so the screens the menu opens agree with each other and with the menu. Every one
        // of them is a left-aligned column of text over the same 3D set, and three files each
        // choosing their own inset is how a flow ends up looking like three flows.

        /// <summary>Left inset of every menu column.</summary>
        public const float ColumnX = 96f;

        public const float ColumnWidth = 1500f;

        /// <summary>
        /// A conservative horizon, in top-down pixels on the 1080-high reference canvas: everything
        /// below this is reliably ground.
        ///
        /// <para>
        /// This used to claim MainMenu.unity's camera sat at zero pitch, which put the horizon exactly
        /// across the middle of the frame. It does not — the camera is pitched about 11.6° and the
        /// measured skyline sits nearer 40% of the way down, so there is MORE ground than this value
        /// implies. That makes 540 safe for the thing it is used for (keeping dark navy entries off
        /// the sky) and wrong for the opposite question.
        /// </para>
        ///
        /// <para>
        /// So do not read it as "above this is sky". Anything that has to be legible <i>above</i> this
        /// line has to carry its own contrast — see how <c>LobbyPreviewRank</c> builds its nameplates,
        /// which sit over heads that are below this line and over sand.
        /// </para>
        /// </summary>
        public const float Horizon = 540f;

        /// <summary>
        /// Where a page's title sits, measured down from the top.
        ///
        /// Above the horizon, which is fine and deliberate: the title is drawn in
        /// <see cref="Title"/>, which is white, and white is what reads against sky. It is the
        /// entries that cannot go there.
        /// </summary>
        public const float TitleTop = -400f;

        public const float TitleHeight = 130f;

        /// <summary>
        /// The first row of content, and the first thing that has to be legible.
        ///
        /// Below <see cref="Horizon"/> on purpose. Entries are drawn in <see cref="Idle"/>, a dark
        /// navy that vanishes against a bright sky and reads cleanly against sand — so every
        /// clickable thing on a page belongs in the bottom half of the screen. MainMenu.unity's own
        /// ButtonRow arrives at the same place from the other direction, by anchoring at 0.5 and
        /// growing downward.
        /// </summary>
        public const float ContentTop = -560f;

        /// <summary>Height of the footer row of actions, pinned to the bottom of a page.</summary>
        public const float FooterBottom = 64f;

        /// <summary>The status line, sitting just above the footer.</summary>
        public const float MessageBottom = 168f;

        /// <summary>
        /// A menu entry inside a layout column. <paramref name="prefab"/> may be null, in which case
        /// a plain text button is built in the same palette.
        /// </summary>
        public static Button Create(GameObject prefab, RectTransform parent, string name, string label,
            int fontSize, float height, UnityAction onClick, out TextMeshProUGUI text)
        {
            Button button = FromPrefab(prefab, parent, name, out text) ??
                            FromScratch(parent, name, out text);

            text.text = label;
            text.fontSize = fontSize;

            // Fills the row it was put in. A layout group parent overrides these on its next pass,
            // so this only decides the cases where the entry is placed by anchors instead.
            var rect = (RectTransform)button.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            UIBuilder.FixedHeight(rect, height);
            if (onClick != null) button.onClick.AddListener(onClick);

            return button;
        }

        /// <summary>
        /// Repaints an entry white, for use over sky rather than over sand.
        ///
        /// <para>
        /// The menu's default entry is <see cref="Idle"/>, a dark navy chosen to read against ground.
        /// An entry placed above the horizon needs the opposite, and it cannot simply be tinted:
        /// <b>the button prefab's animator rewrites its own label's colour every state change</b>, so
        /// an assignment lasts exactly one frame. The animator is therefore switched off and the
        /// colour handed to the Button's own tint block instead.
        /// </para>
        ///
        /// <para>
        /// Switching the animator off costs the hover scale-up, and nothing else:
        /// <see cref="UIButton"/> plays both FMOD sounds from its own pointer handlers rather than
        /// from animation events, so a light entry still clicks and still sounds like the rest of the
        /// menu. The tint block below puts the hover feedback back as a colour shift.
        /// </para>
        /// </summary>
        public static void MakeLight(Button button, TextMeshProUGUI label)
        {
            var animator = button.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;

            // The tint multiplies the graphic's own colour, so the label goes white and the block
            // carries every state's actual colour.
            label.color = Color.white;

            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = label;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.selectedColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.9f, 0.68f);
            colors.pressedColor = new Color(0.78f, 0.68f, 0.5f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.09f;
            button.colors = colors;
        }

        /// <summary>Sets an entry's width inside a horizontal layout group.</summary>
        public static void Width(Button button, float width)
        {
            var element = button.GetComponent<LayoutElement>();
            if (element == null) element = button.gameObject.AddComponent<LayoutElement>();

            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
        }

        /// <summary>Null when there is no usable prefab, which is the caller's cue to build one.</summary>
        private static Button FromPrefab(GameObject prefab, RectTransform parent, string name,
            out TextMeshProUGUI text)
        {
            text = null;
            if (prefab == null) return null;

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = name;

            // The Button has to be on the instance root: everything else here — the layout element,
            // the fill, the marker and date drawn beside the label — is positioned against it.
            var button = instance.GetComponent<Button>();
            text = instance.GetComponentInChildren<TextMeshProUGUI>(true);

            if (button == null || text == null)
            {
                Debug.LogError($"[MenuEntry] '{prefab.name}' is not shaped like a menu button " +
                               "(a Button on the root, a TMP label beneath it). Building a plain one.");
                Object.Destroy(instance);
                text = null;
                return null;
            }

            // The prefab ships wired to whatever it was last used for; a clone that kept those
            // listeners would do two things at once.
            button.onClick.RemoveAllListeners();
            return button;
        }

        private static Button FromScratch(RectTransform parent, string name, out TextMeshProUGUI text)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);

            // The whole line takes the click, not only the glyphs.
            UIBuilder.HitArea(rect);

            text = UIBuilder.LabelIn(rect, "Text (TMP)", string.Empty, ActionSize, Idle,
                                     TextAlignmentOptions.Left, FontStyles.Bold);

            // The tint block multiplies, so the hit area carries the colour and the glyphs stay
            // white — which is why the text is the tint target rather than the invisible image.
            return UIBuilder.Clickable(rect, text, Idle, Hover, Warning);
        }

        /// <summary>
        /// Insets an entry's label, making room for something drawn beside it — a selection marker
        /// on the left, a timestamp on the right. The prefab's label fills the whole entry, so
        /// without this the two overlap.
        /// </summary>
        public static void InsetLabel(TextMeshProUGUI text, float left, float right)
        {
            var rect = (RectTransform)text.transform;
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-right, rect.offsetMax.y);
        }
    }
}
