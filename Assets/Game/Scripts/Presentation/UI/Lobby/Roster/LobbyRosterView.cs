using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core.Lobbies;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The in-lobby page: who is here, the code to let others in, and the decisions left.
    ///
    /// <para>
    /// It is the only page in the multiplayer flow that is <b>live</b> — <see cref="LobbySession"/>
    /// polls twice a second and this redraws from whatever came back — which is why it is kept
    /// apart from the join page, which is built once and then only read.
    /// </para>
    ///
    /// <para>
    /// Who is here is answered by <see cref="LobbyPreviewRank"/> — astronauts standing in the menu
    /// scene with their names over their heads — and not by a list of text rows. What is left on
    /// the page is a strip of session controls along the top (<see cref="LobbySessionStrip"/>), the
    /// VS team rules under it (<see cref="LobbyTeamRulesStrip"/>), a status line and a footer.
    /// </para>
    ///
    /// <para>
    /// It performs no service calls of its own, and it never sees a <c>Lobby</c> — everything it
    /// needs arrives either through <see cref="RosterSnapshot"/> (see <see cref="Render"/>) or
    /// through <see cref="SetSession"/> for the handful of things a snapshot does not carry. That
    /// split is what lets this whole view be built and driven without a lobby, a network, or Unity
    /// Gaming Services at all.
    /// </para>
    /// </summary>
    public class LobbyRosterView
    {
        /// <summary>
        /// The session name, at a fraction of a page title's size. Down from
        /// <see cref="MenuEntry.TitleSize"/> because this page is mostly a picture: the astronauts
        /// are what the player is looking at, and a 110pt word beside them competes with them.
        /// </summary>
        private const int TitleSize = 44;

        private const float TitleHeight = 62f;
        private const float StatusHeight = 44f;
        private const float FooterSpacing = 64f;
        private const float StartWidth = 420f;
        private const float LeaveWidth = 260f;

        /// <summary>
        /// Where a busy rule sits, measured up from the bottom of the page: in the gap between the
        /// status line (168 to 212) and the footer (64 to 142). Chosen rather than derived because
        /// both of those are shared constants other screens anchor to, and a rule computed off them
        /// would move whenever they did.
        /// </summary>
        private const float StatusRuleBottom = 154f;

        /// <summary>What the host can do from this page. Supplied by the screen that owns it.</summary>
        public readonly struct Actions
        {
            public readonly System.Action Start;
            public readonly System.Action Leave;
            public readonly System.Action CopyCode;

            /// <summary>Called with the privacy the host just asked for.</summary>
            public readonly System.Action<bool> SetPrivacy;

            /// <summary>Called with -1 or +1 when a suit colour chevron is pressed.</summary>
            public readonly System.Action<int> StepColor;

            /// <summary>Called with a team number when its plate is clicked.</summary>
            public readonly System.Action<int> JoinTeam;

            /// <summary>Called with (teamCount, teamSize) when a team-rules chevron is pressed.</summary>
            public readonly System.Action<int, int> SetTeamRules;

            public Actions(System.Action start, System.Action leave, System.Action copyCode,
                System.Action<bool> setPrivacy, System.Action<int> stepColor,
                System.Action<int> joinTeam, System.Action<int, int> setTeamRules)
            {
                Start = start;
                Leave = leave;
                CopyCode = copyCode;
                SetPrivacy = setPrivacy;
                StepColor = stepColor;
                JoinTeam = joinTeam;
                SetTeamRules = setTeamRules;
            }
        }

        private readonly Actions actions;

        private readonly TextMeshProUGUI title;
        private readonly LobbySessionStrip strip;
        private readonly LobbyTeamRulesStrip teamRules;
        private readonly CanvasGroup footerGroup;
        private readonly GameObject startAction;
        private readonly MenuStatusLine status;

        /// <summary>The empty row under the status line that a sweeping rule is built into.</summary>
        private readonly RectTransform statusRuleSlot;

        private MenuBusy statusRule;
        private LobbyPreviewRank rank;

        // The things a snapshot cannot answer — see the class doc — mirrored here so Render can
        // read them without ever being handed a Lobby.
        private string sessionName;
        private string sessionCode;
        private bool sessionPlaying;
        private bool sessionIsPrivate;

        public LobbyRosterView(RectTransform page, GameObject entryPrefab, Actions actions)
        {
            this.actions = actions;

            title = UIBuilder.Label(
                UIBuilder.PinnedTop(page, "Title", MenuEntry.ColumnX, MenuEntry.TitleTop,
                                    MenuEntry.ColumnWidth, TitleHeight),
                string.Empty, TitleSize, MenuEntry.Title, TextAlignmentOptions.Left, FontStyles.Bold);

            strip = new LobbySessionStrip(page, entryPrefab, () => actions.CopyCode?.Invoke(), TogglePrivacy);
            teamRules = new LobbyTeamRulesStrip(page, entryPrefab,
                                                (teams, size) => actions.SetTeamRules?.Invoke(teams, size));

            status = new MenuStatusLine(UIBuilder.Label(
                UIBuilder.PinnedBottom(page, "Status", MenuEntry.ColumnX, MenuEntry.MessageBottom,
                                       MenuEntry.ColumnWidth, StatusHeight),
                string.Empty, MenuEntry.CaptionSize, MenuEntry.Caption));

            statusRuleSlot = UIBuilder.PinnedBottom(page, "StatusRule", MenuEntry.ColumnX, StatusRuleBottom,
                                                    MenuEntry.ColumnWidth, MenuBusy.RuleThickness);

            RectTransform footer = UIBuilder.PinnedBottom(page, "Footer", MenuEntry.ColumnX,
                                                          MenuEntry.FooterBottom, MenuEntry.ColumnWidth,
                                                          MenuEntry.ActionHeight);
            footerGroup = footer.gameObject.AddComponent<CanvasGroup>();
            UIBuilder.Row(footer, FooterSpacing);

            Button start = MenuEntry.Create(entryPrefab, footer, "StartButton", "Start game",
                                            MenuEntry.ActionSize, MenuEntry.ActionHeight,
                                            () => actions.Start?.Invoke(), out _);
            MenuEntry.Width(start, StartWidth);
            startAction = start.gameObject;

            MenuEntry.Width(MenuEntry.Create(entryPrefab, footer, "LeaveButton", "Leave",
                                             MenuEntry.ActionSize, MenuEntry.ActionHeight,
                                             () => actions.Leave?.Invoke(), out _), LeaveWidth);

            rank = LobbyPreviewRank.Create(page, entryPrefab,
                                           direction => actions.StepColor?.Invoke(direction),
                                           team => actions.JoinTeam?.Invoke(team));
        }

        /// <summary>The Teams stepper in the host strip, exposed so a test can read it.</summary>
        public MenuStepper TeamsStepper => teamRules.Teams;

        /// <summary>The Team size stepper in the host strip, exposed so a test can read it.</summary>
        public MenuStepper TeamSizeStepper => teamRules.TeamSize;

        /// <summary>Whether the host strip is showing at all — only true for a VS lobby.</summary>
        public bool TeamRulesShown => teamRules.Shown;

        /// <summary>Whether Start is offered — only true for the host.</summary>
        public bool StartShown => startAction != null && startAction.activeSelf;

        /// <summary>The status line's current text, exposed so a test can read it.</summary>
        public string StatusText => status.Text;

        /// <summary>
        /// Tears down the astronauts.
        ///
        /// Needed as its own step because they are the only thing this view puts outside the page:
        /// they hang off an anchor in the scene, so destroying the page leaves them standing in the
        /// menu. Called by <see cref="LobbyUI"/> when it swaps pages.
        /// </summary>
        public void Dispose()
        {
            if (rank != null) rank.Dispose();
            rank = null;

            // The rule and the dots are inside the page and go with it, but the references are not
            // — and a stopped animation is what stops SetBusy(false) later touching dead objects.
            StopRule();
            status.Stop();
        }

        /// <summary>
        /// Tells this view the handful of things a <see cref="RosterSnapshot"/> cannot answer.
        /// Called by <see cref="LobbyUI"/> immediately before every <see cref="Render"/>.
        /// </summary>
        public void SetSession(string name, string code, bool playing, bool isPrivate)
        {
            sessionName = name;
            sessionCode = code;
            sessionPlaying = playing;
            sessionIsPrivate = isPrivate;
        }

        /// <summary>
        /// Redraws from the session's current roster. Called on every change, so it has to be cheap
        /// enough to run twice a second.
        /// </summary>
        /// <param name="snapshot">Everything about who is here and how the teams are shaped.</param>
        /// <param name="isHost">
        /// Whether this peer is genuinely running the session — false, notably, for the second or so
        /// while a host is still waiting on <c>CreateAsync</c>. Gates Start, privacy and the team
        /// steppers, but not the title — see <paramref name="hostTitle"/>.
        /// </param>
        /// <param name="hostTitle">
        /// The world's own display name, for a story host only — a VS lobby has no world, and its
        /// title is its own session name instead. Trusted unconditionally rather than gated on
        /// <paramref name="isHost"/>: the caller only ever hands one over for a story host, and does
        /// so from the moment the roster goes up, before <c>session.IsHost</c> can answer true.
        /// </param>
        public void Render(RosterSnapshot snapshot, bool isHost, string hostTitle)
        {
            title.text = (!string.IsNullOrEmpty(hostTitle) ? hostTitle : sessionName ?? "Session")
                .ToUpperInvariant();

            strip.Render(sessionCode, sessionIsPrivate, isHost);

            // Only the host can start. A client shown it gets a control whose whole behaviour is
            // to refuse.
            if (startAction != null) startAction.SetActive(isHost);

            // Only what has to be read. A host is told nothing; a joiner is told the one thing they
            // cannot see for themselves, which is whether they are waiting or already being pulled
            // into a running world.
            status.Polled(isHost
                ? string.Empty
                : sessionPlaying
                    ? "The host is already playing. Joining the world…"
                    : "Waiting for the host to start.");

            teamRules.Render(snapshot, isHost);

            if (rank != null) rank.Render(snapshot);
        }

        /// <summary>
        /// Holds the page while something the host asked for is still in flight.
        ///
        /// There is exactly one such wait today: the roster is put up before the session exists, so
        /// that pressing Host is answered immediately rather than after two round trips. For that
        /// second and a half the controls on this page act on a lobby that is not there yet — Copy
        /// has no code to copy, Start has no server — so they go quiet rather than staying live and
        /// failing. <paramref name="caption"/> is a stem without its ellipsis.
        /// </summary>
        public void SetBusy(bool isBusy, string caption = null)
        {
            MenuLock.Set(strip.Group, isBusy);
            MenuLock.Set(footerGroup, isBusy);

            StopRule();
            if (isBusy && statusRuleSlot != null) statusRule = MenuBusy.Rule(statusRuleSlot);

            if (isBusy && !string.IsNullOrEmpty(caption))
            {
                status.BeginWait(caption);
                return;
            }

            if (!isBusy) status.EndWait();
        }

        /// <summary>A transient line — "Copied ABC123". The next poll replaces it.</summary>
        public void SetStatus(string message) => status.Say(message);

        /// <summary>A failure the player has to actually read. Survives the poll's redraws.</summary>
        public void SetWarning(string message) => status.Warn(message);

        /// <summary>
        /// Repaints our own astronaut, without waiting for the poll.
        ///
        /// The lobby's copy of our colour is a debounce and a poll behind what was just pressed, so
        /// rendering ours from the poll like everyone else's would make our own figure the last one
        /// on screen to show our own choice.
        /// </summary>
        public void SetLocalColor(int color)
        {
            if (rank != null) rank.SetLocalColor(color);
        }

        /// <summary>
        /// Pressing the toggle again is how a host retries after a failure, so the failure that is
        /// being retried stops being the thing pinned to the status line.
        /// </summary>
        private void TogglePrivacy(bool wanted)
        {
            status.Say(string.Empty);
            actions.SetPrivacy?.Invoke(wanted);
        }

        private void StopRule()
        {
            if (statusRule == null) return;

            statusRule.Stop();
            statusRule = null;
        }
    }
}
