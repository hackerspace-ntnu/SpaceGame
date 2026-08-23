using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Characters;
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
    ///
    /// <para>
    /// <b>The session list is the page.</b> It used to be the narrow right-hand column, under a
    /// caption the same size as the one over the code field, refreshed only when asked — three
    /// separate ways of saying that the list of games you could actually join was the secondary
    /// thing on a screen called "Join a game". It is now the wide left column, read first, with a
    /// live count in its heading, occupancy drawn rather than spelled, and a refresh every second so
    /// that a friend opening a session appears without anyone pressing anything. The code entry is
    /// the compact aside it always was.
    /// </para>
    ///
    /// <para>
    /// That cadence is what forces the rows to be reconciled rather than rebuilt. Clearing and
    /// repopulating the list once a second would restart the hover animation under the player's
    /// pointer, drop the scroll position, and hand a click to a button destroyed in the same frame.
    /// </para>
    ///
    /// <para>
    /// <b>Every service call this screen makes is slow enough to need saying so.</b> Joining is four
    /// round trips — sign in, clear any stale membership, join the lobby, connect to Relay — and for
    /// a long time the only sign any of it was happening was one muted caption in the bottom-left
    /// corner, nowhere near the row that had just been clicked. Worse, the page stayed live
    /// underneath: a second click on any row hit <c>LobbySession</c>'s one-operation-at-a-time guard,
    /// which returns false silently, and painted "Could not join that session" over a join that was
    /// still on its way to succeeding. Pressing Refresh overwrote the caption outright. So the page
    /// now has a busy state — see <see cref="BusyState"/> — and nothing on it can be pressed while
    /// something is in flight.
    /// </para>
    /// </summary>
    public class LobbyUI : MenuScreen
    {
        /// <summary>Which wait the page is currently showing, if any.</summary>
        public enum BusyScope { None, SigningIn, Querying, JoiningByCode, JoiningRow }

        /// <summary>
        /// What a scope switches off.
        ///
        /// A table rather than a chain of conditions at each call site, because the rules are not
        /// uniform and the differences are the interesting part: a query leaves the code field alone
        /// (there is no reason you cannot type a code while the list loads) where a join does not,
        /// and only a join offers Cancel, because signing in and querying have nothing to hand back
        /// if you change your mind — Back already does everything cancelling them would.
        /// </summary>
        public readonly struct BusyState
        {
            public readonly bool LockCodeColumn;
            public readonly bool LockBrowser;
            public readonly bool LockRefresh;
            public readonly bool OfferCancel;

            private BusyState(bool codeColumn, bool browser, bool refresh, bool cancel)
            {
                LockCodeColumn = codeColumn;
                LockBrowser = browser;
                LockRefresh = refresh;
                OfferCancel = cancel;
            }

            public static BusyState For(BusyScope scope) => scope switch
            {
                BusyScope.SigningIn     => new BusyState(true,  true,  true,  false),
                BusyScope.Querying      => new BusyState(false, true,  true,  false),
                BusyScope.JoiningByCode => new BusyState(true,  true,  true,  true),
                BusyScope.JoiningRow    => new BusyState(true,  true,  true,  true),
                _                       => new BusyState(false, false, false, false)
            };
        }

        private const float ColumnX = MenuEntry.ColumnX;
        private const float ColumnWidth = MenuEntry.ColumnWidth;

        private const float TitleTop = MenuEntry.TitleTop;
        private const float TitleHeight = MenuEntry.TitleHeight;

        public const float ActionHeight = 78f;
        public const float RowHeight = 72f;

        // ──────────────────────────────────────────────────────────── the join page
        //
        // Two columns, because everything clickable has to sit below MenuEntry.Horizon to read
        // against ground, and a title, a code field, its action, a session list and a footer do not
        // fit in half a screen stacked on top of each other.
        //
        // Which column is which is the whole point of the layout. The sessions used to be on the
        // right, narrower than the page's own column width, under a caption the same size as the
        // one over the code field — three separate ways of saying that the list of games you could
        // actually join was the secondary thing on a screen called "Join a game". It is now the
        // wide left column, read first, with the code entry as the compact aside it always was.

        public const float ListX = ColumnX;
        public const float ListWidth = 1120f;

        public const float CodeX = 1264f;
        public const float CodeWidth = 560f;
        public const float FieldWidth = 520f;

        /// <summary>The heading over the list, set larger than a caption because it leads the page.</summary>
        private const int SectionSize = 36;

        /// <summary>
        /// A session's name, one step up from <see cref="MenuEntry.RowSize"/>.
        ///
        /// Local rather than a change to the shared constant: the world list is a list of things you
        /// own and is read at leisure, where this is the one thing on the page the player came to
        /// find. Raising RowSize would have moved both.
        /// </summary>
        private const int SessionNameSize = 58;

        // The list's own band, measured from the rows already pinned above and below it. Both insets
        // were 48 when the list was a side column; there is no reason for that much air around the
        // thing the page is for, and 12 is worth most of an extra row.
        public const float ListTopDrop = 46f;
        public const float ListBottomGap = 12f;

        // A row's right-hand furniture, laid out from the right edge inwards. Each slot is sized to
        // its longest content, because UIBuilder labels truncate rather than overflow.
        //
        // The state slot is wide enough for the animated "Joining…" caption that replaces the
        // occupancy during a join, not merely for "4/4" — a slot sized to the resting content would
        // silently clip the one message that matters.
        public const float StateWidth = 200f;
        public const float PipsGap = 16f;
        public const float PlayingWidth = 150f;

        /// <summary>One pip per player slot. Four of them, 22 wide with 8 between: 112 across.</summary>
        public const float PipWidth = 22f;
        public const float PipHeight = 10f;
        public const float PipGap = 8f;

        /// <summary>What a locked control fades to. Present, plainly not available.</summary>
        private const float DimmedAlpha = 0.35f;

        // Where the two busy rules sit, measured down from ContentTop. Both land in gaps that
        // already exist between rows rather than pushing anything around, so the page does not
        // reflow when a wait starts.
        //
        // The code column's rows run: heading at 0, field from 46 to 142, Join from 152. The
        // list column's heading occupies 0 to 44 and the list starts at 46.
        public const float CodeRuleDrop = 144f;
        public const float ListRuleDrop = 44f;

        // ──────────────────────────────────────────────────────── the auto refresh
        //
        // Lobby rate-limits QueryLobbies to one call per second, so this cadence is the ceiling
        // rather than a comfortable margin. Three things keep it inside the limit. The interval is
        // measured from the moment a query FINISHES, not on a fixed clock, so two requests can never
        // be in flight at once and a slow response spaces the next one out by however long it took.
        // A failure backs off, doubling up to the cap and resetting on the next success, so a
        // service that is refusing us is not asked again at full rate. And LobbySession.QueryAsync
        // holds every query — this one and the Refresh button's — to a shared minimum spacing,
        // which is the part this page cannot do for itself: the button fires at a moment of the
        // player's choosing, usually mid-interval, and neither caller can see the other's request.

        private const float AutoRefreshSeconds = 1f;
        private const float MaxBackoffSeconds = 15f;

        private enum Page { None, Join, Roster }

        private MainMenuUI menu;
        private LobbySession session;

        private Page current = Page.None;
        private RectTransform page;

        private LobbyRosterView roster;
        private TMP_InputField codeField;
        private RectTransform browser;
        private TextMeshProUGUI message;

        // ────────────────────────────────────────────────────────── the busy state
        //
        // Locked region by region rather than through one group over the whole page. Two reasons:
        // the footer's Cancel has to stay live while everything around it is dead, and the row
        // being joined has to stay lit while its neighbours recede — and a child CanvasGroup cannot
        // undo a parent's alpha, so "dim everything, then brighten one" is not available.

        private CanvasGroup codeGroup;
        private CanvasGroup browserGroup;
        private CanvasGroup refreshGroup;
        private GameObject backAction;
        private GameObject cancelAction;

        /// <summary>
        /// One session in the browser, kept so the row can be rewritten in place.
        ///
        /// <para>
        /// Held rather than rebuilt because the list now refreshes every second. Destroying and
        /// recreating every row on that cadence would restart the hover animation under the
        /// player's pointer, drop the scroll position, and hand a click to a button being destroyed
        /// in the same frame — so a row that is still there is updated, and only genuinely new and
        /// genuinely gone sessions cost an Instantiate or a Destroy.
        /// </para>
        ///
        /// <para>
        /// Everything writable here is on its own object. The row's own label is not: the button
        /// prefab's animator rewrites its colour on every state change, which is why the name is set
        /// once at build time and every changing value lives beside it.
        /// </para>
        /// </summary>
        private sealed class BrowserRow
        {
            public RectTransform Root;
            public CanvasGroup Group;

            /// <summary>Right-hand slot: the occupancy at rest, the "Joining…" caption during a join.</summary>
            public TextMeshProUGUI State;

            /// <summary>What <see cref="State"/> says when nothing is being joined.</summary>
            public string Occupancy;

            public TextMeshProUGUI Playing;
            public Image[] Pips;
        }

        /// <summary>Rows by lobby id, so the one being joined can be found again after the click.</summary>
        private readonly Dictionary<string, BrowserRow> rows = new();

        /// <summary>Which row is currently saying "Joining…" instead of its occupancy, or null.</summary>
        private string captionedRow;

        private MenuBusy rowDots;

        /// <summary>Empty rows a sweeping rule is built into. One per column.</summary>
        private RectTransform codeRuleSlot;
        private RectTransform browserRuleSlot;

        private MenuBusy busyRule;
        private MenuBusy busyDots;

        /// <summary>The scope in force, so the auto refresh can stand down while anything is in flight.</summary>
        private BusyScope scope;

        /// <summary>The live heading over the list — "OPEN SESSIONS · 3".</summary>
        private TextMeshProUGUI listHeading;

        /// <summary>Shown in the list's own area when there is nothing in it.</summary>
        private GameObject emptyState;

        // The auto refresh. `polling` is what stops Update queuing a second query behind the one
        // already running; the timer is reset when a query finishes rather than when it starts.
        private float pollTimer;
        private bool polling;
        private int pollFailures;

        /// <summary>Cleared once the first query has landed, so the empty state cannot flash before it.</summary>
        private bool listArrived;

        /// <summary>
        /// Which attempt is the current one.
        ///
        /// Bumped when an attempt starts and again when one is cancelled, so a result that lands
        /// after the player has moved on can recognise itself as stale. This is the whole of the
        /// cancellation story — see <see cref="CancelJoin"/> for why there is no token.
        /// </summary>
        private int attempt;

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
            // Anything still in flight belongs to nobody now. Settle reads this as abandonment and
            // hands back a join that lands after the screen has gone, which is the same cleanup
            // Cancel gets — the difference is only that nothing is left to say so on.
            attempt++;

            // The astronauts are parented to an anchor in the menu scene, not to this screen, so
            // they outlive it unless they are taken down explicitly. This covers Close() and
            // HandOff() together — both end in this object being destroyed, and NewPage's own
            // disposal only covers swapping between pages.
            if (roster != null) roster.Dispose();

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
            roster.SetBusy(true, "Creating session");

            if (!await session.EnsureReadyAsync()) { EndHosting(); return; }

            // The world's name, not the player's. It is what the host chose one screen ago and what
            // everyone in the browser is being invited into.
            //
            // Public to start with. A host who wanted it hidden can say so on the roster, and
            // creating it listed is the choice that matches pressing "Host a game" — the one that
            // lets people find you without being told anything first.
            await session.CreateAsync(WorldSession.DisplayName, false);

            EndHosting();
        }

        /// <summary>
        /// Gives the roster its controls back.
        ///
        /// Guarded on the page as well as on the object, because both can have gone: the host can
        /// press Leave while creation is still in flight, which swaps the page out from underneath
        /// this and leaves the roster it is holding destroyed.
        /// </summary>
        private void EndHosting()
        {
            if (this == null || current != Page.Roster || roster == null) return;

            roster.SetBusy(false);
        }

        // ────────────────────────────────────────────────────────────────── joining

        /// <summary>
        /// Signing in is the first thing that happens and the first thing that used to happen
        /// silently: the page was built, then this awaited Unity Services with nothing written
        /// anywhere, so "Join a game" opened onto an empty list and no explanation.
        /// </summary>
        private async void StartJoining()
        {
            ShowJoin();

            int mine = BeginAttempt(BusyScope.SigningIn, "Signing in", null);

            bool ready = await session.EnsureReadyAsync();

            if (!Owns(mine)) return;
            EndAttempt();

            if (!ready) return;

            Refresh();
        }

        /// <summary>What the Refresh button does: the same query, but said out loud.</summary>
        private void Refresh() => Query(announce: true);

        /// <summary>
        /// Fetches the session list and reconciles the browser against it.
        ///
        /// <para>
        /// <paramref name="announce"/> separates the two callers. The button and the first load lock
        /// the page and animate a caption, because the player asked and is waiting. The automatic
        /// refresh does neither: a list that dimmed itself and repainted a caption once a second
        /// would be unusable, and a page that locked its own controls every second would be worse
        /// than the silence this all started as.
        /// </para>
        /// </summary>
        private async void Query(bool announce)
        {
            if (browser == null || polling) return;

            polling = true;

            int mine = announce
                ? BeginAttempt(BusyScope.Querying, "Looking for open sessions", null)
                : attempt;

            List<Lobby> lobbies = await session.QueryAsync();

            if (this == null) return;

            polling = false;

            // A silent refresh has no claim on the page, so it checks only that the page it is
            // about to write to is still the one it queried for. An announced one owns an attempt,
            // and gives it back here.
            bool stillOurs = announce ? Owns(mine) : current == Page.Join && browser != null;
            if (!stillOurs) return;

            if (announce) EndAttempt();

            // Null means the query failed, which is not the same as finding nothing — see
            // LobbySession.QueryAsync. The list on screen is the last one known to be true, so it
            // stays, and the reason is already on the status line via Warn.
            if (lobbies == null)
            {
                pollFailures++;
                pollTimer = Mathf.Min(AutoRefreshSeconds * (1 << Mathf.Min(pollFailures, 4)),
                                      MaxBackoffSeconds);
                return;
            }

            pollFailures = 0;
            pollTimer = AutoRefreshSeconds;
            listArrived = true;

            ApplyLobbies(lobbies);
        }

        /// <summary>
        /// Drives the automatic refresh.
        ///
        /// Held off entirely while anything else is in flight: a query landing mid-join would
        /// rewrite the row under the "Joining…" caption, and one landing during sign-in would be
        /// refused anyway because nobody is signed in yet.
        /// </summary>
        private void Update()
        {
            if (current != Page.Join || browser == null) return;
            if (polling || scope != BusyScope.None) return;

            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer > 0f) return;

            Query(announce: false);
        }

        private async void JoinByCode()
        {
            string typed = codeField != null ? codeField.text : null;

            if (string.IsNullOrWhiteSpace(typed))
            {
                SetMessage("Enter a code first.");
                return;
            }

            int mine = BeginAttempt(BusyScope.JoiningByCode, "Joining", null);

            await Settle(mine, await session.JoinByCodeAsync(typed), "That code did not work.");
        }

        private async void JoinById(string lobbyId, string lobbyName)
        {
            int mine = BeginAttempt(BusyScope.JoiningRow, $"Joining {lobbyName}", lobbyId);

            await Settle(mine, await session.JoinByIdAsync(lobbyId), "Could not join that session.");
        }

        /// <summary>
        /// Sends a finished join somewhere, having first worked out whether anyone is still waiting
        /// for it.
        /// </summary>
        private async Task Settle(int mine, bool joined, string failureMessage)
        {
            // Destruction is checked before the generation, because a destroyed screen's fields are
            // still readable and its attempt counter may well still match.
            if (this == null || attempt != mine)
            {
                await Abandon(joined);

                if (this == null || current != Page.Join) return;

                EndAttempt();
                SetMessage("Cancelled.");
                return;
            }

            EndAttempt();
            Route(joined, failureMessage);
        }

        /// <summary>
        /// Hands back a session nobody is waiting for any more.
        ///
        /// A cancelled join that turns out to have succeeded cannot simply be forgotten: the player
        /// would be sitting in a lobby the screen has stopped showing, occupying one of its four
        /// slots, on a Relay connection nothing is reading. <c>LeaveAsync</c> already does the three
        /// things that fixes — forget the lobby, shut the transport down, hand the membership back —
        /// and handing it back is what stops the next attempt being refused with "player is already
        /// a member", the 409 this project already carries recovery code for.
        /// </summary>
        private async Task Abandon(bool joined)
        {
            if (!joined || session == null) return;

            await session.LeaveAsync();
        }

        // ────────────────────────────────────────────────────────────────── busy state

        /// <summary>
        /// Locks the page down, animates a caption, and returns the generation the caller should
        /// quote back when its work finishes.
        ///
        /// <paramref name="caption"/> is a stem without its ellipsis — the dots are animated.
        /// </summary>
        private int BeginAttempt(BusyScope scope, string caption, string activeRowId)
        {
            attempt++;

            ApplyBusy(scope, activeRowId);

            StopDots();
            busyDots = MenuBusy.Dots(message, caption);

            return attempt;
        }

        /// <summary>
        /// Gives the page back.
        ///
        /// The status line is only cleared when the animated caption is still the thing on it. A
        /// failure reported through <see cref="Warn"/> mid-flight has already gone through
        /// <see cref="SetMessage"/>, which stops the dots — so finding them already stopped is how
        /// this knows to leave the reason where the player can read it.
        /// </summary>
        private void EndAttempt()
        {
            bool captionWasShowing = busyDots != null;

            StopDots();
            ApplyBusy(BusyScope.None, null);

            if (captionWasShowing && message != null) message.text = string.Empty;
        }

        /// <summary>True while <paramref name="mine"/> is still the attempt the screen is waiting on.</summary>
        private bool Owns(int mine) => this != null && current == Page.Join && attempt == mine;

        private void ApplyBusy(BusyScope busyScope, string activeRowId)
        {
            // Recorded because the automatic refresh reads it: a query landing mid-join would
            // rewrite the row under the "Joining…" caption.
            scope = busyScope;

            BusyState state = BusyState.For(busyScope);

            Set(codeGroup, state.LockCodeColumn, state.LockCodeColumn);
            Set(refreshGroup, state.LockRefresh, state.LockRefresh);

            // The frame is locked but never dimmed — its rows carry their own alpha, and dimming
            // here would multiply with theirs and take the active one down with the rest.
            Set(browserGroup, state.LockBrowser, dim: false);

            foreach (KeyValuePair<string, BrowserRow> entry in rows)
                Set(entry.Value.Group, state.LockBrowser,
                    dim: state.LockBrowser && entry.Key != activeRowId);

            SetRowCaption(activeRowId);

            // Back and Cancel share a slot: only one of them is ever a sensible thing to press.
            if (backAction != null) backAction.SetActive(!state.OfferCancel);
            if (cancelAction != null) cancelAction.SetActive(state.OfferCancel);

            SetBusyRule(busyScope);
        }

        /// <summary>
        /// Moves the "Joining…" caption onto a row, and puts the previous one's occupancy back.
        ///
        /// This is the cue that actually answers the original complaint. The status line along the
        /// bottom says the same thing, but it is a long way from the row that was just clicked, and
        /// a player who has pressed a session name is looking at the session name. Written into the
        /// trailing slot the occupancy already uses, so the row does not change shape.
        /// </summary>
        private void SetRowCaption(string rowId)
        {
            if (captionedRow == rowId) return;

            if (rowDots != null) { rowDots.Stop(); rowDots = null; }

            if (captionedRow != null
                && rows.TryGetValue(captionedRow, out BrowserRow previous)
                && previous.State != null)
            {
                previous.State.text = previous.Occupancy;
            }

            captionedRow = rowId;

            if (rowId != null && rows.TryGetValue(rowId, out BrowserRow row))
                rowDots = MenuBusy.Dots(row.State, "Joining");
        }

        /// <summary>
        /// Puts the sweeping rule under whichever column the wait belongs to — the code field for a
        /// code join, the session list for everything else.
        /// </summary>
        private void SetBusyRule(BusyScope scope)
        {
            if (busyRule != null) { busyRule.Stop(); busyRule = null; }

            RectTransform slot = scope switch
            {
                BusyScope.JoiningByCode => codeRuleSlot,
                BusyScope.SigningIn or BusyScope.Querying or BusyScope.JoiningRow => browserRuleSlot,
                _ => null
            };

            if (slot != null) busyRule = MenuBusy.Rule(slot);
        }

        /// <summary>
        /// Gives up on a join in flight.
        ///
        /// <para>
        /// The request itself keeps running. Nothing in <see cref="LobbySession"/> takes a
        /// cancellation token, and threading one down through <c>SessionLauncher</c> into the Relay
        /// allocation and Netcode's own connection handshake is a far larger change than this screen
        /// is. What happens instead is that the attempt is renumbered, so the result arrives
        /// unclaimed and <see cref="Settle"/> hands the session straight back.
        /// </para>
        ///
        /// <para>
        /// The page therefore stays locked, and the caption changes rather than clearing. Unlocking
        /// here would be a lie in both directions: the join has not stopped, and
        /// <c>LobbySession</c>'s one-at-a-time guard would refuse the next one for as long as this
        /// one is still going — silently, which is the failure this whole busy state exists to end.
        /// </para>
        /// </summary>
        private void CancelJoin()
        {
            attempt++;

            StopDots();
            busyDots = MenuBusy.Dots(message, "Cancelling");

            // Pressed once and no more. Everything else on the page is already locked.
            Set(cancelAction != null ? cancelAction.GetComponent<CanvasGroup>() : null, true, true);
        }

        private void StopDots()
        {
            if (busyDots == null) return;

            busyDots.Stop();
            busyDots = null;
        }

        /// <summary>
        /// Drops every reference to the current list of rows.
        ///
        /// The caption is stopped rather than restored: the label it was writing to is either gone
        /// already or about to be, and putting an occupancy back on a destroyed row is how a clean
        /// teardown becomes a MissingReferenceException.
        /// </summary>
        private void ForgetRows()
        {
            if (rowDots != null) { rowDots.Stop(); rowDots = null; }

            captionedRow = null;
            rows.Clear();
        }

        private static void Set(CanvasGroup group, bool locked, bool dim)
        {
            if (group == null) return;

            group.interactable = !locked;
            group.blocksRaycasts = !locked;
            group.alpha = dim ? DimmedAlpha : 1f;
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

        /// <summary>
        /// Steps the local player's suit colour by one swatch.
        ///
        /// Three things happen, in this order and for three different reasons. The preference is
        /// stored first, because it is the player's outfit and it has to survive them backing out of
        /// the lobby without starting anything. Our own astronaut is repainted second, synchronously,
        /// because a cycler that waits on a service call before showing anything feels broken.
        /// Everyone else is told last, through a debounced publish, because Lobby rate-limits player
        /// updates and browsing the whole palette is a dozen presses in a couple of seconds.
        /// </summary>
        private void StepSuitColor(int direction)
        {
            int next = SuitPalette.Step(GameSettings.SuitColorIndex, direction);

            GameSettings.SuitColorIndex = next;
            GameSettings.Save();

            roster?.SetLocalColor(next);
            session.PublishSuitColor(next);
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
                Refresh();
                return;
            }

            if (current != Page.Roster) return;

            roster.Render(session.Current, session.IsHost,
                          IsHosting ? WorldSession.DisplayName : null, session.LocalSlot);
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

            // The code side gets a rect of its own so one CanvasGroup can lock and dim its heading,
            // its field and its button together. It fills the page rather than boxing the column,
            // which means every row below keeps measuring from the same top-left corner it always
            // did and none of the offsets change. Nothing is drawn on it, so it catches no clicks
            // and the browser beside it is unaffected.
            // ── the sessions, which is what this page is for ──────────────────────

            listHeading = UIBuilder.Label(PinnedRow(root, top, ListWidth, 44f, fromLeft: ListX),
                                          "OPEN SESSIONS", SectionSize, MenuEntry.Caption,
                                          TextAlignmentOptions.Left, FontStyles.Bold);

            browserRuleSlot = PinnedRow(root, top - ListRuleDrop, ListWidth,
                                        MenuBusy.RuleThickness, fromLeft: ListX);

            BuildBrowser(root);

            // ── the code, which is the way in when you were told one ─────────────

            RectTransform code = UIBuilder.Fill(UIBuilder.Rect("CodeColumn", root));
            codeGroup = code.gameObject.AddComponent<CanvasGroup>();

            RectTransform head = PinnedRow(code, top, CodeWidth, 44f, fromLeft: CodeX);
            UIBuilder.Label(head, "Have a code?", MenuEntry.CaptionSize, MenuEntry.Caption);

            RectTransform codeRow = PinnedRow(code, top - 46f, CodeWidth, MenuField.Height,
                                              fromLeft: CodeX);
            codeField = MenuField.Rule(codeRow, "CodeField", "Lobby code…", FieldWidth,
                                       _ => JoinByCode(), characterLimit: 12);

            // On the page, not in the column it sits under. The column is what gets dimmed while a
            // code join runs, and a busy rule faded to a third of its alpha along with the controls
            // it is explaining is the one thing on the page that must not recede.
            codeRuleSlot = PinnedRow(root, top - CodeRuleDrop, FieldWidth, MenuBusy.RuleThickness,
                                     fromLeft: CodeX);

            RectTransform joinRow = PinnedRow(code, top - 152f, 420f, ActionHeight, fromLeft: CodeX);
            MenuEntry.Create(EntryPrefab, joinRow, "JoinButton", "Join",
                             MenuEntry.ActionSize, ActionHeight, JoinByCode, out _);

            BuildJoinFooter(root);

            SetMessage(pendingMessage);
            pendingMessage = null;
        }

        private void BuildBrowser(RectTransform root)
        {
            // The wide left column, running from just under its heading down to the message line.
            // Anchored to both edges vertically so the list uses whatever the band actually is
            // rather than a height computed here and left to drift.
            RectTransform frame = UIBuilder.Rect("Browser", root);
            browserGroup = frame.gameObject.AddComponent<CanvasGroup>();
            frame.anchorMin = new Vector2(0f, 0f);
            frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 0.5f);
            frame.offsetMin = new Vector2(ListX, MenuEntry.MessageBottom + ListBottomGap);
            frame.offsetMax = new Vector2(ListX + ListWidth, MenuEntry.ContentTop - ListTopDrop);

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

            // In the list's own area, where the player is already looking, rather than as a caption
            // in the far corner of the screen. A sibling of the viewport so the scroll content can
            // stay empty — a placeholder inside the layout group would be measured as a row.
            RectTransform empty = UIBuilder.Fill(UIBuilder.Rect("Empty", frame));
            UIBuilder.Label(empty, "Nothing open right now.\nHost a game, or join with a code.",
                            MenuEntry.CaptionSize, MenuEntry.Caption, TextAlignmentOptions.TopLeft)
                     .enableWordWrapping = true;

            emptyState = empty.gameObject;
            emptyState.SetActive(false);
        }

        // ────────────────────────────────────────────────────────── reconciliation

        /// <summary>
        /// Brings the browser into line with a freshly-queried list.
        ///
        /// <para>
        /// Rows are matched by lobby id and updated where they already exist, so a session that was
        /// on screen a second ago keeps the same object — and with it its hover state, its place in
        /// the scroll, and the click the player is halfway through making. Only arrivals cost an
        /// Instantiate and only departures a Destroy.
        /// </para>
        /// </summary>
        private void ApplyLobbies(List<Lobby> lobbies)
        {
            if (browser == null) return;

            var seen = new HashSet<string>();
            foreach (Lobby lobby in lobbies) seen.Add(lobby.Id);

            // Gone first, so the survivors' sibling indices below are set against the final list.
            // Collected before anything is destroyed: mutating the dictionary inside its own
            // enumeration throws.
            var departed = new List<string>();
            foreach (string id in rows.Keys)
                if (!seen.Contains(id))
                    departed.Add(id);

            foreach (string id in departed) RemoveRow(id);

            for (int i = 0; i < lobbies.Count; i++)
            {
                Lobby lobby = lobbies[i];

                if (!rows.TryGetValue(lobby.Id, out BrowserRow row))
                {
                    row = BuildRow(lobby);
                    rows[lobby.Id] = row;
                }

                UpdateRow(row, lobby);

                // Newest first is the query's order, and a list that reorders itself under the
                // pointer is worse than one that is slightly stale — but sessions filling up and
                // emptying is exactly what the player is watching for, so the order is honoured.
                if (row.Root != null) row.Root.SetSiblingIndex(i);
            }

            if (listHeading != null)
                listHeading.text = lobbies.Count > 0
                    ? $"OPEN SESSIONS · {lobbies.Count}"
                    : "OPEN SESSIONS";

            // Held back until the first query has actually landed, so the page does not open by
            // announcing there is nothing there before it has looked.
            if (emptyState != null) emptyState.SetActive(listArrived && lobbies.Count == 0);
        }

        private void RemoveRow(string id)
        {
            if (!rows.TryGetValue(id, out BrowserRow row)) return;

            rows.Remove(id);

            // A row cannot vanish from under a join in flight — the auto refresh stands down while
            // one is running — but the caption reference would outlive the label if it did.
            if (captionedRow == id)
            {
                if (rowDots != null) { rowDots.Stop(); rowDots = null; }
                captionedRow = null;
            }

            if (row.Root == null) return;

            // Unparented as well as destroyed: Destroy does not take effect until the end of the
            // frame, so a departing row would still be measured by the layout group until then and
            // the list would visibly settle a frame later.
            row.Root.SetParent(null, false);
            Destroy(row.Root.gameObject);
        }

        /// <summary>
        /// Builds one session row: the name, then its state, occupancy and whether it has started,
        /// laid out from the right edge inwards.
        /// </summary>
        private BrowserRow BuildRow(Lobby lobby)
        {
            // Captured into locals so every row's handler closes over its own session.
            string id = lobby.Id;
            string name = lobby.Name;

            Button button = MenuEntry.Create(EntryPrefab, browser, "SessionRow", name,
                                             SessionNameSize, RowHeight,
                                             () => JoinById(id, name), out TextMeshProUGUI label);

            var root = (RectTransform)button.transform;

            var row = new BrowserRow
            {
                Root = root,
                // Its own group, so this one row can stay lit while the rest of the list recedes
                // behind the join it started.
                Group = button.gameObject.AddComponent<CanvasGroup>()
            };

            row.State = TrailingLabel(root, "State", StateWidth, 0f, MenuEntry.Caption,
                                      MenuEntry.CaptionSize);

            float pipsWidth = LobbySession.MaxPlayers * PipWidth + (LobbySession.MaxPlayers - 1) * PipGap;
            float pipsRight = StateWidth + PipsGap;
            row.Pips = BuildPips(root, pipsRight, pipsWidth);

            float playingRight = pipsRight + pipsWidth + PipsGap;
            row.Playing = TrailingLabel(root, "Playing", PlayingWidth, playingRight, MenuEntry.Idle,
                                        MenuEntry.CaptionSize);

            // The prefab's label fills the whole row, so it has to be told to stop before the
            // furniture starts or the name runs underneath all of it.
            MenuEntry.InsetLabel(label, 0f, playingRight + PlayingWidth + 24f);

            return row;
        }

        /// <summary>
        /// Writes the parts of a row that change: how full it is, and whether it has started.
        ///
        /// The name is not rewritten. A lobby cannot be renamed in this game, and the row's own
        /// label is the one thing on it the button prefab's animator also touches.
        /// </summary>
        private void UpdateRow(BrowserRow row, Lobby lobby)
        {
            int taken = lobby.MaxPlayers - lobby.AvailableSlots;

            row.Occupancy = LobbySession.DescribeOccupancy(lobby.MaxPlayers, lobby.AvailableSlots);

            // Left alone while it is carrying the "Joining…" caption, which is animated and owns
            // this label until the attempt settles.
            if (row.State != null && captionedRow != lobby.Id) row.State.text = row.Occupancy;

            // Sessions already in progress are labelled rather than hidden: joining one works —
            // Netcode synchronises a late arrival into the running world — and a session that
            // vanishes from the list the moment friends start playing is the opposite of useful.
            if (row.Playing != null)
                row.Playing.text = LobbySession.IsPlaying(lobby) ? "PLAYING" : string.Empty;

            if (row.Pips == null) return;

            for (int i = 0; i < row.Pips.Length; i++)
            {
                if (row.Pips[i] == null) continue;
                row.Pips[i].color = i < taken ? MenuEntry.Idle : PipEmpty;
            }
        }

        /// <summary>What an unfilled player slot is drawn in — the same navy, barely there.</summary>
        private static readonly Color PipEmpty =
            new(MenuEntry.Caption.r, MenuEntry.Caption.g, MenuEntry.Caption.b, 0.22f);

        /// <summary>
        /// One small bar per player slot, filled from the left.
        ///
        /// Bars rather than glyphs. The obvious characters for this — filled and hollow circles —
        /// are not in LiberationSans, the same gap that shipped the suit cycler's chevrons as
        /// nothing; and a rule is what the rest of this menu is made of anyway.
        /// </summary>
        private static Image[] BuildPips(RectTransform row, float fromRight, float totalWidth)
        {
            RectTransform strip = UIBuilder.Rect("Pips", row);
            strip.anchorMin = new Vector2(1f, 0.5f);
            strip.anchorMax = new Vector2(1f, 0.5f);
            strip.pivot = new Vector2(1f, 0.5f);
            strip.anchoredPosition = new Vector2(-fromRight, 0f);
            strip.sizeDelta = new Vector2(totalWidth, PipHeight);

            var pips = new Image[LobbySession.MaxPlayers];

            for (int i = 0; i < pips.Length; i++)
            {
                RectTransform pip = UIBuilder.Rect($"Pip{i}", strip);
                pip.anchorMin = new Vector2(0f, 0.5f);
                pip.anchorMax = new Vector2(0f, 0.5f);
                pip.pivot = new Vector2(0f, 0.5f);
                pip.anchoredPosition = new Vector2(i * (PipWidth + PipGap), 0f);
                pip.sizeDelta = new Vector2(PipWidth, PipHeight);

                pips[i] = UIBuilder.Solid(pip, PipEmpty);
            }

            return pips;
        }

        /// <summary>
        /// A right-aligned label inside a row, <paramref name="fromRight"/> in from its right edge.
        ///
        /// Its own object every time, never a tint or a write on the row's own label: the button
        /// prefab's animator rewrites that one on every state change, so anything said there lasts
        /// until the next frame. <see cref="MenuEntry"/> documents the same trap.
        /// </summary>
        private static TextMeshProUGUI TrailingLabel(RectTransform row, string name, float width,
            float fromRight, Color color, int size)
        {
            RectTransform rect = UIBuilder.Rect(name, row);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-(fromRight + width), 0f);
            rect.offsetMax = new Vector2(-fromRight, 0f);

            return UIBuilder.Label(rect, string.Empty, size, color, TextAlignmentOptions.Right,
                                   FontStyles.Bold);
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

            refreshGroup = FooterAction(footer, "RefreshButton", "Refresh", 340f, Refresh);

            backAction = FooterAction(footer, "BackButton", "Back", 260f, Close).gameObject;

            // Takes Back's place for the duration of a join and is the only live control on the page
            // while one is running. Built here rather than swapped in later so the footer's layout
            // has always measured it.
            cancelAction = FooterAction(footer, "CancelButton", "Cancel", 300f, CancelJoin).gameObject;
            cancelAction.SetActive(false);
        }

        /// <summary>
        /// A footer button that can be locked on its own. Each carries its own group because the
        /// three of them are not switched off together — Refresh goes quiet during a query that
        /// leaves Back alone, and Cancel stays live through a join that kills everything else.
        /// </summary>
        private CanvasGroup FooterAction(RectTransform footer, string name, string label, float width,
            UnityEngine.Events.UnityAction onClick)
        {
            Button button = MenuEntry.Create(EntryPrefab, footer, name, label, MenuEntry.ActionSize,
                                             ActionHeight, onClick, out _);
            MenuEntry.Width(button, width);
            return button.gameObject.AddComponent<CanvasGroup>();
        }

        private void ShowRoster()
        {
            RectTransform root = NewPage(Page.Roster, null);

            roster = new LobbyRosterView(root, EntryPrefab,
                new LobbyRosterView.Actions(StartGame, Leave, CopyCode, SetPrivacy, StepSuitColor));

            roster.Render(session.Current, session.IsHost,
                          IsHosting ? WorldSession.DisplayName : null, session.LocalSlot);
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
            // Before the page goes, because the roster owns four astronauts standing in the menu
            // scene rather than in the page's own hierarchy. Destroying the page alone left them
            // behind, and reopening the roster then built a second rank standing inside the first.
            if (roster != null) roster.Dispose();

            if (page != null)
            {
                page.gameObject.SetActive(false);
                Destroy(page.gameObject);
            }

            // Both animations are inside the page and would go with it, but a reference to a
            // destroyed one is what a later Stop() would trip over.
            StopDots();
            if (busyRule != null) { busyRule.Stop(); busyRule = null; }

            roster = null;
            codeField = null;
            browser = null;
            message = null;

            codeGroup = null;
            browserGroup = null;
            refreshGroup = null;
            backAction = null;
            cancelAction = null;
            codeRuleSlot = null;
            browserRuleSlot = null;
            listHeading = null;
            emptyState = null;
            ForgetRows();

            // The new page has not looked yet, so it must not open by announcing there is nothing
            // there — and the back-off belonged to the page being torn down.
            listArrived = false;
            pollFailures = 0;
            pollTimer = AutoRefreshSeconds;
            scope = BusyScope.None;

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

        /// <summary>
        /// Writes the status line, stopping any animated caption first.
        ///
        /// The stop is not optional. <see cref="MenuBusy.Dots"/> owns this label's text while it is
        /// running and rewrites it every time the dot count changes, so anything written underneath
        /// it survives at most a third of a second — which is how a failure reported mid-join used
        /// to vanish before it could be read.
        /// </summary>
        private void SetMessage(string text)
        {
            StopDots();

            if (message != null) message.text = text ?? string.Empty;
        }
    }
}
