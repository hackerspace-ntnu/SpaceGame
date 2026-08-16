using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The multiplayer lobby, drawn in the main menu's own language.
    ///
    /// It replaces LobbyMenu.unity, which was a scene of its own with a camera, a flat background
    /// and four tabs — Browse, Create, Join by code, Direct — half of which were switched off at
    /// runtime depending on which route the player had taken. Two things made that worth deleting
    /// rather than restyling. The scene never showed the 3D menu behind it, so the lobby was the
    /// one screen in the game that looked like a different game; and every control in it was bound
    /// to a method name by string through a UnityEvent, which resolves at runtime and silently
    /// drops anything it cannot find. Nothing here is resolved by name.
    ///
    /// Two pages, one component: find a session, and wait in the one you are in. Which page you
    /// start on is not a mode flag — it is read from <see cref="WorldSession.IsActive"/>, because a
    /// staged world IS the difference between the two routes. MainMenuUI.HostMultiplayer stages one
    /// and JoinMultiplayer clears it, so a flag beside it could only ever disagree.
    ///
    /// <para>
    /// There are no passwords anywhere in this flow. A session is either listed in the browser or
    /// hidden from it, and a hidden one is reached with the code — which is already a secret you
    /// have to be told. A password on top of that guarded nothing the code did not already guard,
    /// and cost a whole page to collect.
    /// </para>
    ///
    /// <para>
    /// The host is not asked to create anything. They have already pressed Multiplayer, then Host,
    /// then picked a world; a "Create session" form after that is a fourth confirmation of a
    /// decision already made. The session is created on arrival, named after the world, and the
    /// host lands on the roster with a code to share.
    /// </para>
    /// </summary>
    public class LobbyUI : MenuScreen
    {
        private const float ColumnX = MenuEntry.ColumnX;
        private const float ColumnWidth = MenuEntry.ColumnWidth;

        private const float TitleTop = MenuEntry.TitleTop;
        private const float TitleHeight = MenuEntry.TitleHeight;

        private const float ActionHeight = 78f;
        private const float RowHeight = 72f;

        /// <summary>Right-hand column of a browser row, holding "playing · 3/4".</summary>
        private const float OccupancyWidth = 300f;

        private const float FieldWidth = 560f;

        // The join page is the one page that cannot be a single stack. Everything on it has to sit
        // below MenuEntry.Horizon to be readable, and a title, a code field, its action, a session
        // list and a footer do not fit in half a screen stacked on top of each other. Side by side
        // they do: the code on the left, the browser on the right, one footer under both.
        private const float LeftWidth = 620f;
        private const float RightX = 820f;
        private const float RightWidth = 1000f;

        private enum Page { None, Join, Roster }

        private MainMenuUI menu;
        private LobbySession session;

        private Page current = Page.None;
        private RectTransform page;

        private LobbyRosterView roster;
        private TMP_InputField codeField;
        private RectTransform browser;
        private TextMeshProUGUI message;

        /// <summary>A message that has to survive the page it was raised on being rebuilt.</summary>
        private string pendingMessage;

        /// <summary>
        /// The last thing the session reported going wrong. Kept because the event that explains a
        /// lobby disappearing arrives just before the one that says it disappeared.
        /// </summary>
        private string lastWarning;

        public static LobbyUI Open(MainMenuUI owner)
        {
            var existing = FindFirstObjectByType<LobbyUI>();
            if (existing != null) return existing;

            var ui = new GameObject(nameof(LobbyUI)).AddComponent<LobbyUI>();
            ui.menu = owner;
            ui.Present();
            return ui;
        }

        private GameObject EntryPrefab => menu != null ? menu.MenuButtonPrefab : null;

        /// <summary>True when this screen is here to run a session rather than find one.</summary>
        private static bool IsHosting => WorldSession.IsActive;

        protected override void Build()
        {
            session = LobbySession.Instance;
            session.Changed += Render;
            session.Failed += Warn;

            if (IsHosting) StartHosting();
            else StartJoining();
        }

        /// <summary>
        /// The session outlives this screen, so it can still raise events at a destroyed object.
        /// Unsubscribing is what stops a poll landing after the player has backed out and driving
        /// a page whose rects Unity has already thrown away.
        /// </summary>
        private void OnDestroy()
        {
            if (session == null) return;

            session.Changed -= Render;
            session.Failed -= Warn;
        }

        // ─────────────────────────────────────────────────────────────────── hosting

        /// <summary>
        /// Puts the roster up first and creates the session behind it.
        ///
        /// In that order on purpose: allocating a Relay server and creating a lobby is two round
        /// trips, and a host staring at an unchanged main menu for a second and a half has no way
        /// to tell it from a press that did not register.
        /// </summary>
        private async void StartHosting()
        {
            ShowRoster();
            roster.SetStatus("Creating session…");

            if (!await session.EnsureReadyAsync()) return;

            // The world's name, not the player's. It is what the host chose one screen ago and what
            // everyone in the browser is being invited into.
            //
            // Public to start with. A host who wanted it hidden can say so on the roster, and
            // creating it listed is the choice that matches pressing "Host a game" — the one that
            // lets people find you without being told anything first.
            await session.CreateAsync(WorldSession.DisplayName, false);
        }

        // ────────────────────────────────────────────────────────────────── joining

        private async void StartJoining()
        {
            ShowJoin();

            if (!await session.EnsureReadyAsync()) return;

            Refresh();
        }

        private const string Searching = "Looking for open sessions…";

        private async void Refresh()
        {
            if (browser == null) return;

            SetMessage(Searching);

            List<Lobby> lobbies = await session.QueryAsync();

            // The list arrives after an await, by which time the player may have moved on to the
            // password page or backed out of the screen entirely.
            if (browser == null || current != Page.Join) return;

            ClearBrowser();

            // Only overwrite the line if nothing more important landed on it while the query was
            // in flight. QueryAsync reports its own failures through Failed, which Warn has already
            // written here — and "No open sessions right now" on top of "the lobby service is
            // unreachable" replaces the reason with a symptom.
            if (message != null && message.text == Searching)
                SetMessage(lobbies.Count == 0 ? "No open sessions right now." : string.Empty);

            foreach (Lobby lobby in lobbies) BuildBrowserRow(lobby);
        }

        private async void JoinByCode()
        {
            string typed = codeField != null ? codeField.text : null;

            if (string.IsNullOrWhiteSpace(typed))
            {
                SetMessage("Enter a code first.");
                return;
            }

            SetMessage("Joining…");

            Route(await session.JoinByCodeAsync(typed), "That code did not work.");
        }

        private async void JoinById(string lobbyId, string lobbyName)
        {
            SetMessage($"Joining {lobbyName}…");

            Route(await session.JoinByIdAsync(lobbyId), "Could not join that session.");
        }

        /// <summary>Sends a finished join attempt to the page it deserves.</summary>
        private void Route(bool joined, string failureMessage)
        {
            if (joined)
            {
                ShowRoster();
                EnterIfPlaying();
                return;
            }

            // LobbySession already reported the specific reason through Failed, which Warn has
            // written to this page. Only say something when it did not.
            if (string.IsNullOrEmpty(message != null ? message.text : null))
                SetMessage(failureMessage);
        }

        /// <summary>
        /// A joiner whose host is already playing never waits in the lobby. Netcode's scene
        /// synchronisation pulls them into the running world; this only puts something on screen
        /// while that happens.
        /// </summary>
        private void EnterIfPlaying()
        {
            if (session.State != LobbyState.InGame) return;

            string scene = menu != null ? menu.GameSceneName : null;
            if (string.IsNullOrEmpty(scene)) return;

            LoadingScreenUI.ShowUntilReady(scene);
            HandOff();
        }

        // ─────────────────────────────────────────────────────────────── in the lobby

        private async void StartGame()
        {
            string scene = menu != null ? menu.GameSceneName : null;

            if (string.IsNullOrEmpty(scene))
            {
                roster.SetStatus("No game scene is configured on the menu.");
                return;
            }

            // Up before the load starts and held until terrain streaming and the NavMesh bake
            // finish — those run after the scene reports loaded and are what makes the first
            // seconds stutter. It sorts above this screen, so the lobby is covered, not layered.
            LoadingScreenUI.ShowUntilReady(scene);

            // Torn down only once the load is actually under way. Handing off first would leave a
            // failed start with no lobby to return to and no way back to the menu.
            if (await session.BeginGameAsync(scene)) HandOff();
            else LoadingScreenUI.Dismiss();
        }

        private async void Leave()
        {
            bool wasHosting = IsHosting;

            await session.LeaveAsync();

            if (!wasHosting)
            {
                // A joiner came here to find a session, so leaving one puts them back in the list
                // rather than all the way out to the menu.
                pendingMessage = "You left the session.";
                ShowJoin();
                Refresh();
                return;
            }

            // A host's staged world must not follow them back to the menu, or the next thing they
            // do — joining someone else — starts with a save of their own waiting to be restored.
            WorldSession.Clear();
            Close();
        }

        private void CopyCode()
        {
            Lobby lobby = session.Current;
            if (lobby == null || string.IsNullOrEmpty(lobby.LobbyCode)) return;

            GUIUtility.systemCopyBuffer = lobby.LobbyCode;
            roster.SetStatus($"Copied {lobby.LobbyCode} to the clipboard.");
        }

        private async void SetPrivacy(bool isPrivate) => await session.SetPrivacyAsync(isPrivate);

        // ───────────────────────────────────────────────────────────────────── render

        /// <summary>
        /// Redraws from the session on every change it reports.
        ///
        /// This is also where a lobby disappearing is handled: the poll nulls Current when the host
        /// closes it, and a joiner left looking at an empty roster has no way to understand what
        /// happened or to get anywhere else.
        /// </summary>
        private void Render()
        {
            if (session.Current == null)
            {
                if (current != Page.Roster || IsHosting) return;

                // LobbySession raises Failed with the reason immediately before forgetting the
                // lobby, so the specific one — "The host closed the lobby", "Lost connection" — is
                // already in hand. Falling back to a generic line only when it is not.
                pendingMessage = string.IsNullOrEmpty(lastWarning) ? "The session ended." : lastWarning;
                ShowJoin();
                Refresh();
                return;
            }

            if (current != Page.Roster) return;

            roster.Render(session.Current, session.IsHost,
                          IsHosting ? WorldSession.DisplayName : null);
        }

        private void Warn(string text)
        {
            lastWarning = text;

            // SetWarning, not SetStatus: the roster redraws twice a second, and a failure written
            // as an ordinary status would be gone before it could be read.
            if (current == Page.Roster && roster != null) roster.SetWarning(text);
            else SetMessage(text);
        }

        // ────────────────────────────────────────────────────────────────────── pages

        private void ShowJoin()
        {
            RectTransform root = NewPage(Page.Join, "JOIN A GAME");

            float top = MenuEntry.ContentTop;

            RectTransform head = PinnedRow(root, top, LeftWidth, 44f);
            UIBuilder.Label(head, "Enter a code", MenuEntry.CaptionSize, MenuEntry.Caption);

            RectTransform codeRow = PinnedRow(root, top - 46f, LeftWidth, MenuField.Height);
            codeField = MenuField.Rule(codeRow, "CodeField", "Lobby code…", FieldWidth,
                                       _ => JoinByCode(), characterLimit: 12);

            RectTransform joinRow = PinnedRow(root, top - 152f, 420f, ActionHeight);
            MenuEntry.Create(EntryPrefab, joinRow, "JoinButton", "Join",
                             MenuEntry.ActionSize, ActionHeight, JoinByCode, out _);

            RectTransform listHead = PinnedRow(root, top, RightWidth, 44f, fromLeft: RightX);
            UIBuilder.Label(listHead, "Open sessions", MenuEntry.CaptionSize, MenuEntry.Caption);

            BuildBrowser(root);
            BuildJoinFooter(root);

            SetMessage(pendingMessage);
            pendingMessage = null;
        }

        private void BuildBrowser(RectTransform root)
        {
            // The right-hand column, running from just under its heading down to the message line.
            // Anchored to both edges vertically so the list uses whatever the band actually is
            // rather than a height computed here and left to drift.
            RectTransform frame = UIBuilder.Rect("Browser", root);
            frame.anchorMin = new Vector2(0f, 0f);
            frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 0.5f);
            frame.offsetMin = new Vector2(RightX, MenuEntry.MessageBottom + 48f);
            frame.offsetMax = new Vector2(RightX + RightWidth, MenuEntry.ContentTop - 48f);

            RectTransform viewport = UIBuilder.Fill(UIBuilder.Rect("Viewport", frame));
            viewport.gameObject.AddComponent<RectMask2D>();

            browser = UIBuilder.Rect("Content", viewport);
            browser.anchorMin = new Vector2(0f, 1f);
            browser.anchorMax = new Vector2(1f, 1f);
            browser.pivot = new Vector2(0.5f, 1f);
            browser.offsetMin = Vector2.zero;
            browser.offsetMax = Vector2.zero;

            UIBuilder.Column(browser, 6f);
            var fitter = browser.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = browser;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
        }

        private void BuildBrowserRow(Lobby lobby)
        {
            // Captured into locals so every row's handler closes over its own session.
            string id = lobby.Id;
            string name = lobby.Name;

            Button row = MenuEntry.Create(EntryPrefab, browser, "SessionRow", name,
                                          MenuEntry.RowSize, RowHeight,
                                          () => JoinById(id, name), out TextMeshProUGUI label);

            string occupancy = LobbySession.DescribeOccupancy(lobby.MaxPlayers, lobby.AvailableSlots);

            // Rows already in progress are labelled rather than hidden: joining one works — Netcode
            // synchronises a late arrival into the running world — and a session that vanishes from
            // the list the moment friends start playing is the opposite of useful.
            MenuField.Trailing(row, label,
                               LobbySession.IsPlaying(lobby) ? $"playing · {occupancy}" : occupancy,
                               OccupancyWidth, MenuEntry.Caption);
        }

        private void ClearBrowser()
        {
            if (browser == null) return;

            // Collected before anything moves. Transform's enumerator walks its children by index,
            // so reparenting inside the loop shifts every later child down one and the walk skips
            // half the list — which shows up as rows from the previous query surviving a refresh.
            var stale = new List<Transform>(browser.childCount);
            foreach (Transform child in browser) stale.Add(child);

            foreach (Transform child in stale)
            {
                // Unparented as well as destroyed: Destroy does not take effect until the end of
                // the frame, so a refresh landing twice in one frame would stack two lists in the
                // layout group before settling.
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        private void BuildJoinFooter(RectTransform root)
        {
            RectTransform messageRow = PinnedRow(root, 0f, ColumnWidth, 44f,
                                                 fromBottom: MenuEntry.MessageBottom);
            message = UIBuilder.Label(messageRow, string.Empty, MenuEntry.CaptionSize, MenuEntry.Caption);

            RectTransform footer = PinnedRow(root, 0f, ColumnWidth, ActionHeight,
                                             fromBottom: MenuEntry.FooterBottom);

            var layout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 64f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            MenuEntry.Width(MenuEntry.Create(EntryPrefab, footer, "RefreshButton", "Refresh",
                                             MenuEntry.ActionSize, ActionHeight, Refresh, out _), 340f);

            MenuEntry.Width(MenuEntry.Create(EntryPrefab, footer, "BackButton", "Back",
                                             MenuEntry.ActionSize, ActionHeight, Close, out _), 260f);
        }

        private void ShowRoster()
        {
            RectTransform root = NewPage(Page.Roster, null);

            roster = new LobbyRosterView(root, EntryPrefab,
                new LobbyRosterView.Actions(StartGame, Leave, CopyCode, SetPrivacy));

            roster.Render(session.Current, session.IsHost,
                          IsHosting ? WorldSession.DisplayName : null);
        }

        // ──────────────────────────────────────────────────────────────────── plumbing

        /// <summary>
        /// Swaps the visible page.
        ///
        /// The outgoing one is switched off before it is destroyed because Destroy does not take
        /// effect until the end of the frame, and two live pages would both draw and both take
        /// clicks until then. Every per-page field is cleared with it, so a stale reference cannot
        /// outlive the rects behind it.
        /// </summary>
        private RectTransform NewPage(Page which, string title)
        {
            if (page != null)
            {
                page.gameObject.SetActive(false);
                Destroy(page.gameObject);
            }

            roster = null;
            codeField = null;
            browser = null;
            message = null;

            // Belongs to the page being torn down. Carried across, it would explain a later lobby
            // disappearing with the reason a previous one did.
            lastWarning = null;

            current = which;
            page = UIBuilder.Fill(UIBuilder.Rect(which.ToString(), Surface));

            if (!string.IsNullOrEmpty(title))
            {
                RectTransform titleRect = PinnedRow(page, TitleTop, ColumnWidth, TitleHeight);
                UIBuilder.Label(titleRect, title, MenuEntry.TitleSize, MenuEntry.Title,
                                TextAlignmentOptions.Left, FontStyles.Bold);
            }

            return page;
        }

        /// <summary>
        /// A left-aligned row pinned to the top of the page, or to the bottom when asked.
        /// <paramref name="fromLeft"/> overrides the shared column inset, which the join page uses
        /// to put its second column beside the first.
        /// </summary>
        private static RectTransform PinnedRow(RectTransform parent, float fromTop, float width,
            float height, float fromBottom = float.NaN, float fromLeft = ColumnX)
        {
            RectTransform rect = UIBuilder.Rect("Row", parent);
            bool bottom = !float.IsNaN(fromBottom);

            rect.anchorMin = rect.anchorMax = new Vector2(0f, bottom ? 0f : 1f);
            rect.pivot = new Vector2(0f, bottom ? 0f : 1f);
            rect.anchoredPosition = new Vector2(fromLeft, bottom ? fromBottom : fromTop);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private void SetMessage(string text)
        {
            if (message != null) message.text = text ?? string.Empty;
        }
    }
}
