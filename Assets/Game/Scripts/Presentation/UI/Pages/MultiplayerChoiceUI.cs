using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The first multiplayer question: host a session, or join someone else's.
    ///
    /// It exists because the two answers need different things and the menu used to demand both.
    /// "Multiplayer" opened the world list, so a player who only wanted to join a friend had to
    /// invent a world first — and having done that, arrived in the lobby carrying a staged save that
    /// the host's world would then load over. There was no route into the lobby that did not go
    /// through picking a world, which is why joining looked impossible.
    ///
    /// Host keeps the old route (pick a world, then the lobby). Join goes straight to the lobby and
    /// clears the staged world on the way, because a joiner's world comes from the host.
    ///
    /// Built at runtime rather than authored into MainMenu.unity: the menu's buttons are bound to
    /// MainMenuUI methods by string through the Inspector, so re-pointing "Multiplayer" at this
    /// screen needs no scene edit at all — MainMenuUI.StartMultiPlayer opens this, and this calls
    /// back.
    ///
    /// <para>
    /// Entries come from <see cref="MenuEntry"/> rather than being built here in white. This screen
    /// sits between the main menu and <see cref="LobbyUI"/>, both of which clone the menu's own
    /// button prefab and draw in its navy palette; a screen in the middle with its own colours and
    /// its own type scale reads as a seam in a flow that is supposed to be one.
    /// </para>
    /// </summary>
    public class MultiplayerChoiceUI : MenuScreen
    {
        private const float ColumnX = MenuEntry.ColumnX;
        private const float ColumnWidth = MenuEntry.ColumnWidth;

        private const float TitleTop = MenuEntry.TitleTop;
        private const float TitleHeight = MenuEntry.TitleHeight;

        private const float ActionHeight = 78f;

        private MainMenuUI menu;

        public static MultiplayerChoiceUI Open(MainMenuUI owner)
        {
            var existing = FindFirstObjectByType<MultiplayerChoiceUI>();
            if (existing != null) return existing;

            var ui = new GameObject(nameof(MultiplayerChoiceUI)).AddComponent<MultiplayerChoiceUI>();
            ui.menu = owner;
            ui.Present();
            return ui;
        }

        private GameObject EntryPrefab => menu != null ? menu.MenuButtonPrefab : null;

        // ---------------------------------------------------------------- actions

        /// <summary>
        /// Hands off to the world list, which opens the lobby once a world is chosen.
        ///
        /// The menu's canvases go back on FIRST, inside Close: the world list switches off whatever
        /// it finds enabled and turns those back on when the player backs out of it, so leaving them
        /// off here would strand the player on an empty screen.
        /// </summary>
        private void HostGame() => Route(owner => owner.HostMultiplayer());

        /// <summary>Straight to the lobby. The world arrives with the host.</summary>
        private void JoinGame() => Route(owner => owner.JoinMultiplayer());

        /// <summary>
        /// Closes this screen, then routes.
        ///
        /// Closing first is not optional on either branch any more. It used to be true that joining
        /// loaded LobbyMenu.unity and took this object with it, so restoring canvases was work for
        /// nobody — but the lobby is now a page over this same menu, and a screen left alive with
        /// the menu switched off would sit underneath it forever, holding canvases off that only it
        /// remembers having hidden.
        ///
        /// The owner is read into a local because Close destroys this object, and reaching for a
        /// serialized field afterwards is reading a field on a corpse.
        /// </summary>
        private void Route(System.Action<MainMenuUI> go)
        {
            if (menu == null)
            {
                Debug.LogError("[MultiplayerChoiceUI] No MainMenuUI to route through.");
                return;
            }

            MainMenuUI owner = menu;
            Close();
            go(owner);
        }

        // ----------------------------------------------------------------- build

        protected override void Build()
        {
            RectTransform titleRect = PinnedRow(Surface, TitleTop, TitleHeight);
            UIBuilder.Label(titleRect, "MULTIPLAYER", MenuEntry.TitleSize, MenuEntry.Title,
                            TextAlignmentOptions.Left, FontStyles.Bold);

            RectTransform column = UIBuilder.Rect("Column", Surface);
            column.anchorMin = column.anchorMax = new Vector2(0f, 1f);
            column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = new Vector2(ColumnX, MenuEntry.ContentTop);
            column.sizeDelta = new Vector2(ColumnWidth, 0f);

            UIBuilder.Column(column, 6f);
            var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Entry(column, "HostButton", "Host a game", HostGame);

            UIBuilder.Spacer(column, 30f);

            Entry(column, "JoinButton", "Join a game", JoinGame);

            UIBuilder.Spacer(column, 44f);

            Entry(column, "BackButton", "Back", Close);
        }

        private void Entry(RectTransform column, string name, string label, UnityEngine.Events.UnityAction onClick) =>
            MenuEntry.Create(EntryPrefab, column, name, label, MenuEntry.ActionSize, ActionHeight,
                             onClick, out _);

        private static RectTransform PinnedRow(RectTransform parent, float fromTop, float height)
        {
            RectTransform rect = UIBuilder.Rect("Row", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(ColumnX, fromTop);
            rect.sizeDelta = new Vector2(ColumnWidth, height);
            return rect;
        }
    }
}
