using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Presentation;

namespace SpaceGame.Core
{
    /// <summary>
    /// What the player reads when a session ended without them asking it to.
    ///
    /// <para>
    /// A <see cref="MenuScreen"/> like MultiplayerChoiceUI, so the notice arrives in the menu's own
    /// type and palette over the menu's own 3D set instead of as a panel from somewhere else. That
    /// also buys the part that is easy to get wrong: the menu's canvases are switched off while
    /// this is up and switched back on when it closes, so "Continue" lands on a working main menu
    /// rather than on a screen with two sets of buttons on it.
    /// </para>
    ///
    /// <para>
    /// It lives beside the multiplayer code rather than with the other menu pages because it is the
    /// visible half of <see cref="SessionExit"/> — nothing else opens it, and it says nothing the
    /// session teardown did not hand it.
    /// </para>
    /// </summary>
    public class SessionEndedScreen : MenuScreen
    {
        /// <summary>
        /// This page carries a line of prose the other menu screens do not, so it sets its own
        /// vertical rhythm rather than using <c>MenuEntry.TitleTop</c>. Those constants leave 30 px
        /// between the title and the first entry, which is a rule, not a paragraph. Everything else
        /// — the column inset, the type scale, the entry height — is still the menu's.
        /// </summary>
        private const float TitleTop = -300f;

        private const float ReasonTop = -420f;
        private const float ReasonHeight = 130f;
        private const float ActionHeight = 78f;

        private string notice;

        public static SessionEndedScreen Open(string notice)
        {
            // One at a time. A disconnect and a lobby poll can both report the session gone, and
            // two of these stacked would each hide the other's canvas on the way out.
            var existing = FindFirstObjectByType<SessionEndedScreen>();
            if (existing != null) return existing;

            var ui = new GameObject(nameof(SessionEndedScreen)).AddComponent<SessionEndedScreen>();
            ui.notice = notice;
            ui.Present();
            return ui;
        }

        /// <summary>
        /// The menu's own button, so this screen clicks and sounds like the rest of the flow.
        /// Null is fine — <see cref="MenuEntry.Create"/> builds a plain line in the same palette,
        /// which is what keeps a screen the player cannot dismiss from being possible.
        /// </summary>
        private static GameObject EntryPrefab
        {
            get
            {
                var menu = FindFirstObjectByType<MainMenuUI>();
                return menu != null ? menu.MenuButtonPrefab : null;
            }
        }

        protected override void Build()
        {
            RectTransform titleRect = PinnedRow(Surface, TitleTop, MenuEntry.TitleHeight);
            UIBuilder.Label(titleRect, "SESSION ENDED", MenuEntry.TitleSize, MenuEntry.Title,
                            TextAlignmentOptions.Left, FontStyles.Bold);

            // Drawn in the title's white rather than MenuEntry.Caption, which is a dark navy meant
            // for text over sand. This line sits above MenuEntry.Horizon, with sky behind it.
            RectTransform reasonRect = PinnedRow(Surface, ReasonTop, ReasonHeight);
            TextMeshProUGUI reason = UIBuilder.Label(reasonRect, notice, MenuEntry.RowSize,
                                                    MenuEntry.Title, TextAlignmentOptions.TopLeft);

            // The one label in the project that has to wrap: every other menu line is authored text
            // of a known length, and this one is whatever the host's server sent.
            reason.enableWordWrapping = true;

            RectTransform column = UIBuilder.Rect("Column", Surface);
            column.anchorMin = column.anchorMax = new Vector2(0f, 1f);
            column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = new Vector2(MenuEntry.ColumnX, MenuEntry.ContentTop);
            column.sizeDelta = new Vector2(MenuEntry.ColumnWidth, 0f);

            UIBuilder.Column(column, 6f);
            var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Close, not a route: the session is already gone and the teardown already ran, so
            // there is nothing left to do but put the menu back.
            MenuEntry.Create(EntryPrefab, column, "ContinueButton", "Continue", MenuEntry.ActionSize,
                             ActionHeight, Close, out _);
        }

        /// <summary>
        /// A row in the menu's column, measured down from the top of the page. The same helper
        /// MultiplayerChoiceUI and MinigameConfigUI each keep a copy of — it is four lines of anchor
        /// arithmetic, and hoisting it would put a layout detail into MenuScreen's contract.
        /// </summary>
        private static RectTransform PinnedRow(RectTransform parent, float fromTop, float height)
        {
            RectTransform rect = UIBuilder.Rect("Row", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(MenuEntry.ColumnX, fromTop);
            rect.sizeDelta = new Vector2(MenuEntry.ColumnWidth, height);
            return rect;
        }
    }
}
