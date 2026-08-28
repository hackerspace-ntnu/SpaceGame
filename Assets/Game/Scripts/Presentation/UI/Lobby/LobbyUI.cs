using TMPro;
using UnityEngine;
using SpaceGame.Core.Lobbies;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The multiplayer lobby, drawn in the main menu's own language: a page over the menu rather
    /// than a scene of its own, with nothing resolved by name.
    ///
    /// <para>
    /// Two pages, one screen. Find a session (<see cref="LobbyJoinFlow"/>) and wait in the one you
    /// are in (<see cref="LobbyRosterFlow"/>). Which page you start on is the <see cref="LobbyRoute"/>
    /// handed to <see cref="Open"/>. This class swaps the pages, owns the moves between them —
    /// hosting, a join landing, starting the game, leaving — and redraws the roster from what the
    /// session reports.
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
        private enum Page { None, Join, Roster }

        private LobbySession session;

        /// <summary>Which door into this screen the player took. Set once, by <see cref="Open"/>.</summary>
        private LobbyRoute route;

        private Page current = Page.None;
        private RectTransform page;

        private LobbyJoinFlow join;
        private LobbyRosterFlow roster;

        /// <summary>A message that has to survive the page it was raised on being rebuilt.</summary>
        private string pendingMessage;

        /// <summary>
        /// The last thing the session reported going wrong. Kept because the event that explains a
        /// lobby disappearing arrives just before the one that says it disappeared.
        /// </summary>
        private string lastWarning;

        public static LobbyUI Open(MainMenuUI owner, LobbyRoute route)
        {
            var existing = FindFirstObjectByType<LobbyUI>();
            if (existing != null) return existing;

            var ui = new GameObject(nameof(LobbyUI)).AddComponent<LobbyUI>();
            ui.route = route;
            ui.Present(owner);
            return ui;
        }

        /// <summary>True when this screen is here to run a session rather than find one.</summary>
        private bool IsHosting => route.IsHosting();

        /// <summary>
        /// The world's own display name, for a story host only — a VS lobby stages no world and
        /// its title is its own session name instead.
        /// </summary>
        private string HostTitle => IsHosting && !route.IsVersus() ? WorldSession.DisplayName : null;

        private string GameScene => Menu != null ? Menu.GameSceneName : null;

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
            // Both pages own things outside their own rect — the roster its astronauts, the join
            // flow a join that may still be in flight — and NewPage's own disposal only covers
            // swapping between them, not this object going away.
            join?.Dispose();
            roster?.Dispose();

            if (session == null) return;

            session.Changed -= Render;
            session.Failed -= Warn;
        }

        private void Update() => join?.Tick(Time.unscaledDeltaTime);

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
            roster.SetBusy(true, "Creating session");

            if (!await session.EnsureReadyAsync()) { EndHosting(); return; }

            // A story lobby is named after the world the host chose one screen ago. A VS lobby has
            // no world to be named after, so null is passed and CreateAsync falls back to
            // "{PlayerName}'s game", which is the identity a VS host actually has at this point.
            //
            // Public to start with. A host who wanted it hidden can say so on the roster, and
            // creating it listed is the choice that matches pressing "Host a game".
            string lobbyName = route.IsVersus() ? null : WorldSession.DisplayName;

            VersusSetup versus = route.IsVersus()
                ? new VersusSetup(VersusRulesUI.StagedTeams, VersusRulesUI.StagedTeamSize)
                : VersusSetup.None;

            await session.CreateAsync(lobbyName, false, versus);

            EndHosting();
        }

        /// <summary>
        /// Gives the roster its controls back. Guarded on the page as well as on the object,
        /// because both can have gone: the host can press Leave while creation is still in flight.
        /// </summary>
        private void EndHosting()
        {
            if (this == null || current != Page.Roster || roster == null) return;

            roster.SetBusy(false);
        }

        // ────────────────────────────────────────────────────────────────── joining

        private void StartJoining()
        {
            ShowJoin();
            join.Begin();
        }

        /// <summary>
        /// A joiner whose host is already playing never waits in the lobby. Netcode's scene
        /// synchronisation pulls them into the running world; this only puts something on screen
        /// while that happens.
        /// </summary>
        private void OnJoined()
        {
            ShowRoster();

            if (session.State != LobbyState.InGame) return;

            string scene = GameScene;
            if (string.IsNullOrEmpty(scene)) return;

            LoadingScreenUI.ShowUntilReady(scene);
            HandOff();
        }

        // ─────────────────────────────────────────────────────────── leaving the page

        private async void StartGame()
        {
            string scene = GameScene;

            if (string.IsNullOrEmpty(scene))
            {
                roster.Say("No game scene is configured on the menu.");
                return;
            }

            // Up before the load starts and held until terrain streaming and the NavMesh bake
            // finish. It sorts above this screen, so the lobby is covered, not layered.
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

            if (this == null) return;

            if (!wasHosting)
            {
                // A joiner came here to find a session, so leaving one puts them back in the list
                // rather than all the way out to the menu.
                pendingMessage = "You left the session.";
                ShowJoin();
                join.Refresh();
                return;
            }

            // A host's staged world must not follow them back to the menu, or the next thing they
            // do — joining someone else — starts with a save of their own waiting to be restored.
            WorldSession.Clear();

            // A VS host's staged team rules are statics that outlive this screen, the same reason
            // WorldSession's staged world does.
            if (route.IsVersus()) VersusRulesUI.ResetToDefaults();

            Close();
        }

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
                join.Refresh();
                return;
            }

            if (current != Page.Roster) return;

            roster.Render(HostTitle);
        }

        private void Warn(string text)
        {
            lastWarning = text;

            // Warn, not Say, on the roster: it redraws twice a second, and a failure written as an
            // ordinary status would be gone before it could be read.
            if (current == Page.Roster && roster != null) roster.Warn(text);
            else join?.Status.Say(text);
        }

        // ────────────────────────────────────────────────────────────────────── pages

        private void ShowJoin()
        {
            RectTransform root = NewPage(Page.Join, "JOIN A GAME");

            join = new LobbyJoinFlow(session, route, root, EntryPrefab, OnJoined, Close);

            join.Status.Say(pendingMessage);
            pendingMessage = null;
        }

        private void ShowRoster()
        {
            RectTransform root = NewPage(Page.Roster, null);

            roster = new LobbyRosterFlow(session, route, root, EntryPrefab, StartGame, Leave);
            roster.Render(HostTitle);
        }

        /// <summary>
        /// Swaps the visible page.
        ///
        /// The outgoing one is switched off before it is destroyed because Destroy does not take
        /// effect until the end of the frame, and two live pages would both draw and both take
        /// clicks until then. Each page is disposed first, because both own things the rect does
        /// not: the roster's astronauts stand in the menu scene, and the join flow may have a join
        /// in flight that now belongs to nobody.
        /// </summary>
        private RectTransform NewPage(Page which, string title)
        {
            roster?.Dispose();
            roster = null;

            join?.Dispose();
            join = null;

            if (page != null)
            {
                page.gameObject.SetActive(false);
                Destroy(page.gameObject);
            }

            // Belongs to the page being torn down. Carried across, it would explain a later lobby
            // disappearing with the reason a previous one did.
            lastWarning = null;

            current = which;
            page = UIBuilder.Fill(UIBuilder.Rect(which.ToString(), Surface));

            if (!string.IsNullOrEmpty(title))
            {
                RectTransform titleRect = PinnedRow(page, MenuEntry.TitleTop, MenuEntry.TitleHeight);
                UIBuilder.Label(titleRect, title, MenuEntry.TitleSize, MenuEntry.Title,
                                TextAlignmentOptions.Left, FontStyles.Bold);
            }

            return page;
        }
    }
}
