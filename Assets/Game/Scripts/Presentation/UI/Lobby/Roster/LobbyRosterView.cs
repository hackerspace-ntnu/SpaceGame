using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The in-lobby page: who is here, the code to let others in, and the decisions left.
    ///
    /// <para>
    /// Split out of <see cref="LobbyUI"/> because it is the only page in the multiplayer flow that
    /// is <b>live</b> — <see cref="LobbySession"/> polls twice a second and this redraws from
    /// whatever came back. The join page is a static form, built once and then only read. Keeping the
    /// redrawing page apart from the one that never redraws is what stops "rebuild the page" and
    /// "update the page" being the same code path.
    /// </para>
    ///
    /// <para>
    /// Who is here is answered by <see cref="LobbyPreviewRank"/> — astronauts standing in the menu
    /// scene with their names over their heads, one cluster per team in a VS lobby — and not by a
    /// list of text rows any more. The list said exactly what the names above their heads say, and
    /// the page had no room for the first copy: everything on it has to sit below
    /// <see cref="MenuEntry.Horizon"/> to be legible against ground, and a title, a code, a copy
    /// action, a roster, a privacy toggle, a status line and a footer never fitted in the bottom half
    /// of the screen. What is left is one narrow column of controls and a footer, which does.
    /// </para>
    ///
    /// <para>
    /// It performs no service calls of its own, and it never sees a <c>Lobby</c> — everything it
    /// needs arrives either through <see cref="RosterSnapshot"/> (see <see cref="Render"/>) or
    /// through <see cref="SetSession"/> for the handful of things a snapshot does not carry (the
    /// session's own name, its code, whether the host has already started, and its privacy). That
    /// split is what lets this whole view be built and driven without a lobby, a network, or Unity
    /// Gaming Services at all.
    /// </para>
    /// </summary>
    public class LobbyRosterView
    {
        private const float ColumnX = MenuEntry.ColumnX;
        private const float ColumnWidth = MenuEntry.ColumnWidth;
        private const float ActionHeight = 78f;
        // The strip sits over sky, so the on/off state reads in white too — a translucent white for
        // "off" carries the same "not in force" meaning the navy version did over sand.
        private static readonly Color PrivacyOn = Color.white;
        private static readonly Color PrivacyOff = new(1f, 1f, 1f, 0.6f);

        /// <summary>
        /// The session name, at a fraction of a page title's size.
        ///
        /// Down from <see cref="MenuEntry.TitleSize"/> because this page is now mostly a picture:
        /// the astronauts are what the player is looking at, and a 110pt word beside them competes
        /// with them for no benefit — you already know which session you are in.
        /// </summary>
        private const int TitleSize = 44;

        private const float TitleHeight = 62f;

        /// <summary>
        /// The strip of session controls, along the very top of the page.
        ///
        /// They used to be a stack down the left, under the horizon, where dark navy reads against
        /// sand. Up here they are over sky instead — so the two plain labels carry a drop shadow, the
        /// same trick the nameplates use, and everything is set small. Small is the point: the code
        /// and the privacy toggle are things you glance at once and then ignore, and the astronauts
        /// are what the page is actually for.
        /// </summary>
        private const float TopBarTop = -36f;

        private const float TopBarHeight = 48f;

        /// <summary>Small. This strip is a caption, not a heading.</summary>
        private const int TopBarCaptionSize = 24;

        private const int TopBarValueSize = 34;

        // Left-to-right slots inside the strip, measured from the shared column inset.
        //
        // Widths are deliberate, not padding. UIBuilder labels are built with word wrap off and
        // Ellipsis overflow, so a slot narrower than its text does not overflow — it silently
        // TRUNCATES, which is how "CODE" first shipped reading as "CO…". Each slot below is sized to
        // its longest possible content: the caption to "CODE", the value to a six-character lobby
        // code, the privacy slot only just wide enough to hold "Private" and its on/off state, so the
        // two read as one control rather than as a word and a distant switch.
        private const float CodeCaptionX = 0f;
        private const float CodeCaptionWidth = 120f;

        private const float CodeValueX = 124f;

        /// <summary>A lobby code is six characters, which measures 142px at this size.</summary>
        private const float CodeValueWidth = 160f;

        private const float CopyX = 296f;
        private const float CopyWidth = 120f;

        private const float PrivacyX = 438f;

        // Sized backwards from the two things inside it. MenuField.Trailing right-ALIGNS the state
        // against the slot's right edge, so the only way to bring "off" nearer "Private" is to make
        // the SLOT narrower — widening or narrowing the state band alone moves nothing. The floor is
        // "Private" at 120px plus Trailing's own 24px inset plus the state band, so 190 is about as
        // tight as this goes before the word itself starts truncating.
        private const float PrivacyWidth = 190f;
        private const float PrivacyStateWidth = 40f;

        /// <summary>
        /// Where the VS team-rules strip sits, under the top bar rather than squeezed inside it.
        ///
        /// A <see cref="MenuStepper"/> is authored at <see cref="MenuStepper.Height"/> (74px) tall —
        /// nearly double <see cref="TopBarHeight"/> — because it is a control built for a full page
        /// column elsewhere (<c>VersusRulesUI</c>), not for a caption strip. Rather than squash it
        /// into 48px and have it read as cramped on the one page where the host is actually deciding
        /// something (not just glancing at a code), it gets its own row directly beneath the strip.
        /// </summary>
        private const float TeamStripTop = TopBarTop - TopBarHeight - 16f;

        private const float TeamStripSpacing = 64f;

        /// <summary>
        /// Where a busy rule sits, measured up from the bottom of the page.
        ///
        /// In the gap between the two rows already pinned down there: the status line runs from 168
        /// to 212 and the footer from 64 to 142, so anything between 142 and 168 lands clear of
        /// both. Chosen rather than derived because both of those are shared constants that other
        /// screens also anchor to, and a rule computed off them would move whenever they did.
        /// </summary>
        private const float StatusRuleBottom = 154f;

        /// <summary>What a locked control fades to. Present, plainly not available.</summary>
        private const float DimmedAlpha = 0.35f;

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

        private readonly GameObject entryPrefab;
        private readonly Actions actions;

        private TextMeshProUGUI title;
        private TextMeshProUGUI code;

        /// <summary>The navy copy behind <see cref="code"/>. Written with it or the shadow goes stale.</summary>
        private TextMeshProUGUI codeShadow;

        private TextMeshProUGUI status;
        private GameObject copyAction;

        private GameObject privacyRow;
        private TextMeshProUGUI privacyState;
        private GameObject startAction;

        private RectTransform teamStripRow;

        private LobbyPreviewRank rank;

        /// <summary>Mirrors the lobby's own flag so the toggle knows which way to flip.</summary>
        private bool isPrivate;

        /// <summary>Set while the status line is holding a failure the poll must not overwrite.</summary>
        private bool statusIsSticky;

        // ─────────────────────────────────────────────────────── the session, off the lobby
        //
        // The three things a snapshot cannot answer — see the class doc — mirrored here so Render
        // can read them without ever being handed a Lobby.

        private string sessionName;
        private string sessionCode;
        private bool sessionPlaying;
        private bool sessionIsPrivate;

        // Kept so each stepper's onChanged can report the OTHER axis's value alongside the one that
        // was actually pressed — VersusRules.ClampTeams/ClampTeamSize both need both numbers, and a
        // stepper only ever knows its own.
        private int shownTeamCount;
        private int shownTeamSize;

        // ─────────────────────────────────────────────────────────── the busy state
        //
        // The controls are locked as two groups rather than one, because they are two rows at
        // opposite ends of the page and there is nothing between them to hang a single group off.

        private CanvasGroup topBarGroup;
        private CanvasGroup footerGroup;

        /// <summary>The empty row under the status line that a sweeping rule is built into.</summary>
        private RectTransform statusRuleSlot;

        private MenuBusy statusRule;
        private MenuBusy statusDots;

        public LobbyRosterView(RectTransform page, GameObject entryPrefab, Actions actions)
        {
            this.entryPrefab = entryPrefab;
            this.actions = actions;

            Build(page);
        }

        /// <summary>The Teams stepper in the host strip, exposed so a test can read it.</summary>
        public MenuStepper TeamsStepper { get; private set; }

        /// <summary>The Team size stepper in the host strip, exposed so a test can read it.</summary>
        public MenuStepper TeamSizeStepper { get; private set; }

        /// <summary>Whether the host strip is showing at all — only true for a VS lobby.</summary>
        public bool TeamRulesShown => teamStripRow != null && teamStripRow.gameObject.activeSelf;

        /// <summary>Whether Start is offered — only true for the host.</summary>
        public bool StartShown => startAction != null && startAction.activeSelf;

        /// <summary>The status line's current text, exposed so a test can read it.</summary>
        public string StatusText => status != null ? status.text : string.Empty;

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
            if (statusRule != null) { statusRule.Stop(); statusRule = null; }
            StopStatusDots();
        }

        // ────────────────────────────────────────────────────────────────────── build

        private void Build(RectTransform page)
        {
            RectTransform titleRow = Pinned(page, "Title", MenuEntry.TitleTop, ColumnWidth, TitleHeight);
            title = UIBuilder.Label(titleRow, string.Empty, TitleSize, MenuEntry.Title,
                                    TextAlignmentOptions.Left, FontStyles.Bold);

            BuildTopBar(page);
            BuildFooter(page);

            rank = LobbyPreviewRank.Create(page, entryPrefab, Step, JoinTeam);
        }

        /// <summary>
        /// The code, the copy action, the privacy toggle and — for a VS lobby — the team-rules
        /// steppers, in a strip across the top.
        ///
        /// A single row rather than the stack this page used to have down the left. The rank of
        /// astronauts now occupies the middle and lower half of the frame, and controls sitting in
        /// front of them competed with the thing the page exists to show.
        /// </summary>
        private void BuildTopBar(RectTransform page)
        {
            RectTransform bar = Pinned(page, "TopBar", TopBarTop, ColumnWidth, TopBarHeight);
            topBarGroup = bar.gameObject.AddComponent<CanvasGroup>();

            Shadowed(bar, "CodeCaption", "CODE", CodeCaptionX, CodeCaptionWidth,
                     TopBarCaptionSize, out _);
            code = Shadowed(bar, "CodeValue", "—", CodeValueX, CodeValueWidth, TopBarValueSize,
                            out codeShadow);

            RectTransform copySlot = UIBuilder.Slice(bar, "CopySlot", CopyX, CopyWidth);
            Button copy = MenuEntry.Create(entryPrefab, copySlot, "CopyButton", "Copy",
                                           TopBarValueSize, TopBarHeight,
                                           () => actions.CopyCode?.Invoke(),
                                           out TextMeshProUGUI copyLabel);
            MenuEntry.MakeLight(copy, copyLabel);
            copyAction = copy.gameObject;

            RectTransform privacySlot = UIBuilder.Slice(bar, "PrivacySlot", PrivacyX, PrivacyWidth);
            privacyRow = privacySlot.gameObject;

            Button toggle = MenuEntry.Create(entryPrefab, privacySlot, "PrivacyButton", "Private",
                                             TopBarValueSize, TopBarHeight, TogglePrivacy,
                                             out TextMeshProUGUI label);
            MenuEntry.MakeLight(toggle, label);

            privacyState = MenuField.Trailing(toggle, label, "off", PrivacyStateWidth, PrivacyOff);

            BuildTeamRulesStrip(page);
        }

        /// <summary>
        /// The host's Teams / Team size steppers, hidden entirely outside a VS lobby.
        ///
        /// <para>
        /// A joiner sees these too, but never gets to press them: they need to know whether a team
        /// still has room before clicking its plate, which the plate's own dimming already answers
        /// visually, but the steppers say it in numbers as well. Only the host can change what is
        /// shown — see <see cref="Render"/>, which is the only place either stepper's own displayed
        /// value is ever written.
        /// </para>
        /// </summary>
        private void BuildTeamRulesStrip(RectTransform page)
        {
            teamStripRow = Pinned(page, "TeamRules", TeamStripTop, ColumnWidth, MenuStepper.Height);

            var layout = teamStripRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = TeamStripSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            TeamsStepper = MenuStepper.Create(entryPrefab, teamStripRow, "Teams",
                VersusRules.DefaultTeams, VersusRules.MinTeams, VersusRules.MaxTeams,
                value => actions.SetTeamRules?.Invoke(value, shownTeamSize));
            FixedStepperWidth(TeamsStepper);

            TeamSizeStepper = MenuStepper.Create(entryPrefab, teamStripRow, "Team size",
                VersusRules.DefaultTeamSize, VersusRules.MinTeamSize, VersusRules.MaxTeamSize,
                value => actions.SetTeamRules?.Invoke(shownTeamCount, value));
            FixedStepperWidth(TeamSizeStepper);

            teamStripRow.gameObject.SetActive(false);
        }

        /// <summary>
        /// Gives a stepper's row an explicit width inside the strip's <see cref="HorizontalLayoutGroup"/>.
        ///
        /// <see cref="MenuStepper"/> only ever sizes its own HEIGHT (its own <c>Create</c> already
        /// added a <see cref="LayoutElement"/> for that, via <c>UIBuilder.FixedHeight</c>) — it was
        /// built to sit in a vertical column that expands its children to the full column width,
        /// which this strip does not do (two steppers share one row). The existing element is reused
        /// rather than a second one added — <see cref="LayoutElement"/> carries no
        /// <c>DisallowMultipleComponent</c>, so Unity would happily attach two, and which one the
        /// layout system then honours is not a bet worth making.
        /// </summary>
        private static void FixedStepperWidth(MenuStepper stepper)
        {
            float width = MenuStepper.LabelWidth + MenuStepper.ChevronWidth * 2f + MenuStepper.ValueWidth;

            var element = stepper.Root.GetComponent<LayoutElement>();
            if (element == null) element = stepper.Root.gameObject.AddComponent<LayoutElement>();

            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
        }

        /// <summary>
        /// A small white label over a navy copy of itself, three pixels off.
        ///
        /// The strip sits above the horizon, where the menu's dark navy disappears — but a head can
        /// pass behind it and the sky is not one brightness, so a plain white label is not safe
        /// either. Two offset copies read against both, which is exactly why the nameplates over the
        /// astronauts are built the same way.
        /// </summary>
        private static TextMeshProUGUI Shadowed(RectTransform bar, string name, string text,
            float fromLeft, float width, int size, out TextMeshProUGUI shadowLabel)
        {
            RectTransform slot = UIBuilder.Slice(bar, name, fromLeft, width);

            RectTransform shadow = UIBuilder.Fill(UIBuilder.Rect("Shadow", slot));
            shadow.anchoredPosition = new Vector2(2f, -2f);
            shadowLabel = UIBuilder.Label(shadow, text, size, MenuEntry.Idle,
                                          TextAlignmentOptions.Left, FontStyles.Bold);

            RectTransform front = UIBuilder.Fill(UIBuilder.Rect("Front", slot));
            return UIBuilder.Label(front, text, size, MenuEntry.Title, TextAlignmentOptions.Left,
                                   FontStyles.Bold);
        }

        private void BuildFooter(RectTransform page)
        {
            RectTransform statusRow = Pinned(page, "Status", 0f, ColumnWidth, 44f,
                                             fromBottom: MenuEntry.MessageBottom);
            status = UIBuilder.Label(statusRow, string.Empty, MenuEntry.CaptionSize, MenuEntry.Caption);

            // Sits in the gap between the status line's bottom edge and the footer's top one, so a
            // busy rule can appear and go without moving anything around it.
            statusRuleSlot = Pinned(page, "StatusRule", 0f, ColumnWidth, MenuBusy.RuleThickness,
                                    fromBottom: StatusRuleBottom);

            RectTransform footer = Pinned(page, "Footer", 0f, ColumnWidth, ActionHeight,
                                          fromBottom: MenuEntry.FooterBottom);
            footerGroup = footer.gameObject.AddComponent<CanvasGroup>();

            var layout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 64f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            startAction = MenuEntry.Create(entryPrefab, footer, "StartButton", "Start game",
                                           MenuEntry.ActionSize, ActionHeight,
                                           () => actions.Start?.Invoke(), out _).gameObject;
            MenuEntry.Width(startAction.GetComponent<Button>(), 420f);

            MenuEntry.Width(MenuEntry.Create(entryPrefab, footer, "LeaveButton", "Leave",
                                             MenuEntry.ActionSize, ActionHeight,
                                             () => actions.Leave?.Invoke(), out _), 260f);
        }

        // ───────────────────────────────────────────────────────────────────── render

        /// <summary>
        /// Tells this view the handful of things a <see cref="RosterSnapshot"/> cannot answer.
        ///
        /// Called by <see cref="LobbyUI"/> immediately before every <see cref="Render"/>, since those
        /// two are what stand in for the <c>Lobby</c> this view no longer sees at all.
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
        /// <paramref name="isHost"/>, because the caller already only ever hands one over for a
        /// story host, and does so from the moment the roster goes up — before <c>session.IsHost</c>
        /// can even answer true, while <c>CreateAsync</c> is still in flight.
        /// </param>
        public void Render(RosterSnapshot snapshot, bool isHost, string hostTitle)
        {
            // Not gated on isHost: the caller (LobbyUI.HostTitle) already only ever hands over a
            // non-null hostTitle for a story host, and it does so unconditionally — including for
            // the second or so while CreateAsync is still running and session.IsHost is therefore
            // still false. Gating here too would blank the world's name for exactly that window.
            title.text = (!string.IsNullOrEmpty(hostTitle) ? hostTitle : sessionName ?? "Session")
                .ToUpperInvariant();

            SetCode(string.IsNullOrEmpty(sessionCode) ? "—" : sessionCode);
            if (copyAction != null) copyAction.SetActive(!string.IsNullOrEmpty(sessionCode));

            // Only the host can start, and only the host can change privacy. A client shown either
            // gets a control whose whole behaviour is to refuse.
            if (startAction != null) startAction.SetActive(isHost);
            if (privacyRow != null) privacyRow.SetActive(isHost);

            isPrivate = sessionIsPrivate;

            if (privacyState != null)
            {
                privacyState.text = isPrivate ? "on" : "off";
                privacyState.color = isPrivate ? PrivacyOn : PrivacyOff;
            }

            // Only what has to be read. A host is told nothing; a joiner is told the one thing they
            // cannot see for themselves, which is whether they are waiting or already being pulled
            // into a running world.
            SetPolledStatus(isHost
                ? string.Empty
                : sessionPlaying
                    ? "The host is already playing. Joining the world…"
                    : "Waiting for the host to start.");

            RenderTeamRules(snapshot, isHost);

            if (rank != null) rank.Render(snapshot);
        }

        /// <summary>
        /// Shows and fills the Teams / Team size strip for a VS lobby, and hides it entirely for a
        /// story one. A joiner sees the rules in force but cannot press them — see
        /// <see cref="BuildTeamRulesStrip"/> for why they are shown at all.
        /// </summary>
        private void RenderTeamRules(RosterSnapshot snapshot, bool isHost)
        {
            if (teamStripRow == null) return;

            teamStripRow.gameObject.SetActive(snapshot.IsVersus);
            if (!snapshot.IsVersus) return;

            shownTeamCount = snapshot.TeamCount;
            shownTeamSize = snapshot.TeamSize;

            if (TeamsStepper != null)
            {
                TeamsStepper.SetLimits(VersusRules.MinTeams,
                                       VersusRules.ClampTeams(VersusRules.MaxTeams, shownTeamSize));
                TeamsStepper.SetValue(shownTeamCount);
                TeamsStepper.SetInteractable(isHost);
            }

            if (TeamSizeStepper != null)
            {
                TeamSizeStepper.SetLimits(VersusRules.MinTeamSize,
                                          VersusRules.ClampTeamSize(VersusRules.MaxTeamSize, shownTeamCount));
                TeamSizeStepper.SetValue(shownTeamSize);
                TeamSizeStepper.SetInteractable(isHost);
            }
        }

        /// <summary>
        /// Holds the page while something the host asked for is still in flight.
        ///
        /// <para>
        /// There is exactly one such wait today: the roster is put up before the session exists, so
        /// that pressing Host is answered immediately rather than after two round trips (see
        /// <c>LobbyUI.StartHosting</c>). For that second and a half the controls on this page act on
        /// a lobby that is not there yet — Copy has no code to copy, the privacy toggle has nothing
        /// to toggle, Start has no server — so they go quiet rather than staying live and failing.
        /// </para>
        ///
        /// <para>
        /// <paramref name="caption"/> is a stem without its ellipsis: the dots are animated, and a
        /// caption that already ends in one gets two.
        /// </para>
        /// </summary>
        public void SetBusy(bool isBusy, string caption = null)
        {
            Dim(topBarGroup, isBusy);
            Dim(footerGroup, isBusy);

            if (statusRule != null) { statusRule.Stop(); statusRule = null; }
            if (isBusy && statusRuleSlot != null) statusRule = MenuBusy.Rule(statusRuleSlot);

            if (isBusy && !string.IsNullOrEmpty(caption))
            {
                StopStatusDots();
                statusDots = MenuBusy.Dots(status, caption);

                // Sticky for the same reason a warning is: this page redraws twice a second, and a
                // caption written as an ordinary status would be gone before the wait it describes
                // had finished.
                statusIsSticky = true;
                return;
            }

            if (!isBusy)
            {
                // Only the caption is cleared, never the line. A failure that landed while the
                // operation was in flight has already gone through SetWarning, and wiping the
                // status here would replace the reason it failed with nothing at all.
                bool captionWasShowing = statusDots != null;
                StopStatusDots();

                if (captionWasShowing) SetStatus(string.Empty);
            }
        }

        private void StopStatusDots()
        {
            if (statusDots == null) return;

            statusDots.Stop();
            statusDots = null;
        }

        private static void Dim(CanvasGroup group, bool locked)
        {
            if (group == null) return;

            group.interactable = !locked;
            group.blocksRaycasts = !locked;
            group.alpha = locked ? DimmedAlpha : 1f;
        }

        /// <summary>A transient line — "Creating session…", "Copied ABC123". The next poll replaces it.</summary>
        public void SetStatus(string message)
        {
            StopStatusDots();

            status.text = message ?? string.Empty;
            statusIsSticky = false;
        }

        /// <summary>
        /// A failure the player has to actually read.
        ///
        /// Sticky, because this page redraws twice a second: written through
        /// <see cref="SetStatus"/> it would survive for as long as it took the next poll to land,
        /// which is not long enough to read "Could not change the session's privacy" — and the
        /// polled line that replaced it would say everything was fine.
        /// </summary>
        public void SetWarning(string message)
        {
            // Before the write, not after: the animator owns this label's text and would put its
            // own caption back on the next frame, taking the failure off the screen with it.
            StopStatusDots();

            status.text = message ?? string.Empty;
            statusIsSticky = true;
        }

        private void SetPolledStatus(string message)
        {
            if (statusIsSticky) return;
            status.text = message ?? string.Empty;
        }

        /// <summary>Writes the code to both copies, so the drop shadow cannot fall behind.</summary>
        private void SetCode(string value)
        {
            if (code != null) code.text = value;
            if (codeShadow != null) codeShadow.text = value;
        }

        // ───────────────────────────────────────────────────────────────────── actions

        /// <summary>
        /// Reports the privacy the host asked for.
        ///
        /// The label is not written here. It is rendered from the session the caller hands back
        /// through <see cref="SetSession"/>, so what the toggle says is what is actually in force
        /// rather than what was last asked for — which matters precisely when the update fails.
        /// </summary>
        private void TogglePrivacy()
        {
            // Pressing the toggle again is how a host retries after a failure, so the failure that
            // is being retried stops being the thing pinned to the status line.
            SetStatus(string.Empty);

            actions.SetPrivacy?.Invoke(!isPrivate);
        }

        private void Step(int direction) => actions.StepColor?.Invoke(direction);

        private void JoinTeam(int team) => actions.JoinTeam?.Invoke(team);

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
        /// A row placed against the page's top-left, or its bottom-left when
        /// <paramref name="fromBottom"/> is given.
        /// </summary>
        private static RectTransform Pinned(RectTransform parent, string name, float fromTop,
            float width, float height, float fromBottom = float.NaN)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);
            bool bottom = !float.IsNaN(fromBottom);

            rect.anchorMin = rect.anchorMax = new Vector2(0f, bottom ? 0f : 1f);
            rect.pivot = new Vector2(0f, bottom ? 0f : 1f);
            rect.anchoredPosition = new Vector2(ColumnX, bottom ? fromBottom : fromTop);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
    }
}
