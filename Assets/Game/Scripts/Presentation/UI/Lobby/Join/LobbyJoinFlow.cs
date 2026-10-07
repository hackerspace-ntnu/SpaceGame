using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using SpaceGame.Core.Lobbies;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// What the join page does: signs in, keeps the session list fresh, and turns a press into a
    /// join — saying so the whole way. Owns the <see cref="LobbyJoinPage"/> it drives.
    ///
    /// <para>
    /// <b>Every service call this makes is slow enough to need saying so.</b> Joining is four
    /// round trips — sign in, clear any stale membership, join the lobby, connect to Relay — and
    /// for a long time the only sign any of it was happening was one muted caption nowhere near
    /// the row that had just been clicked. Worse, the page stayed live underneath: a second click
    /// hit <see cref="LobbySession"/>'s one-operation-at-a-time guard, which returns false silently,
    /// and painted "Could not join that session" over a join that was still on its way to
    /// succeeding. So every wait is an <i>attempt</i> that locks the page — see
    /// <see cref="LobbyBusyState"/> — and nothing on it can be pressed while one is in flight.
    /// </para>
    ///
    /// <para>
    /// Cancellation is a generation counter, not a token. Nothing in <see cref="LobbySession"/>
    /// takes one, and threading one down through <c>SessionLauncher</c> into the Relay allocation
    /// and Netcode's own handshake is a far larger change than this screen is. Instead the attempt
    /// is renumbered, so a result that lands after the player has moved on can recognise itself as
    /// stale and hand the session straight back — see <see cref="Settle"/>.
    /// </para>
    /// </summary>
    public sealed class LobbyJoinFlow
    {
        private readonly LobbySession session;
        private readonly LobbyRoute route;
        private readonly LobbyJoinPage page;
        private readonly Action onJoined;

        private readonly LobbyAutoRefresh refresh = new();

        /// <summary>The scope in force, so the auto refresh can stand down while anything is in flight.</summary>
        private LobbyBusyScope scope;

        /// <summary>Stops <see cref="Tick"/> queuing a second query behind the one already running.</summary>
        private bool polling;

        /// <summary>
        /// Which attempt is the current one. Bumped when an attempt starts and again when one is
        /// cancelled or the page goes away, so a late result can tell it is unclaimed.
        /// </summary>
        private int attempt;

        private bool disposed;

        /// <param name="onJoined">Called once a join has landed and the page should give way to the roster.</param>
        /// <param name="onBack">What the footer's Back does.</param>
        public LobbyJoinFlow(LobbySession session, LobbyRoute route, RectTransform root, GameObject entryPrefab,
            Action onJoined, Action onBack)
        {
            this.session = session;
            this.route = route;
            this.onJoined = onJoined;

            page = new LobbyJoinPage(root, entryPrefab,
                new LobbyJoinPage.Actions(JoinByCode, JoinRow, Refresh, onBack, Cancel));
        }

        /// <summary>The page's status line, for the screen to write session failures to.</summary>
        public MenuStatusLine Status => page.Status;

        /// <summary>
        /// Signing in is the first thing that happens and the first thing that used to happen
        /// silently: the page was built, then this awaited Unity Services with nothing written
        /// anywhere, so "Join a game" opened onto an empty list and no explanation.
        /// </summary>
        public async void Begin()
        {
            int mine = BeginAttempt(LobbyBusyScope.SigningIn, "Signing in", null);

            bool ready = await session.EnsureReadyAsync();

            if (!Owns(mine)) return;
            EndAttempt();

            if (ready) Refresh();
        }

        /// <summary>
        /// Drives the automatic refresh.
        ///
        /// Held off entirely while anything else is in flight: a query landing mid-join would
        /// rewrite the row under the "Joining…" caption, and one landing during sign-in would be
        /// refused anyway because nobody is signed in yet.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (disposed || polling || scope != LobbyBusyScope.None) return;
            if (!refresh.Advance(deltaTime)) return;

            Query(announce: false);
        }

        /// <summary>What the Refresh button does: the same query, but said out loud.</summary>
        public void Refresh() => Query(announce: true);

        /// <summary>
        /// Anything still in flight belongs to nobody now. <see cref="Settle"/> reads this as
        /// abandonment and hands back a join that lands after the page has gone.
        /// </summary>
        public void Dispose()
        {
            disposed = true;
            attempt++;
            page.Dispose();
        }

        private async void JoinByCode()
        {
            string typed = page.TypedCode;

            if (string.IsNullOrWhiteSpace(typed))
            {
                page.Status.Say("Enter a code first.");
                return;
            }

            int mine = BeginAttempt(LobbyBusyScope.JoiningByCode, "Joining", null);

            await Settle(mine, await session.JoinByCodeAsync(typed), "That code did not work.");
        }

        private async void JoinRow(string lobbyId, string lobbyName)
        {
            int mine = BeginAttempt(LobbyBusyScope.JoiningRow, $"Joining {lobbyName}", lobbyId);

            await Settle(mine, await session.JoinByIdAsync(lobbyId), "Could not join that session.");
        }

        /// <summary>
        /// Gives up on a join in flight.
        ///
        /// The request itself keeps running; the attempt is renumbered so its result arrives
        /// unclaimed. The page therefore stays locked, and the caption changes rather than
        /// clearing. Unlocking here would be a lie in both directions: the join has not stopped,
        /// and <see cref="LobbySession"/>'s one-at-a-time guard would refuse the next one for as
        /// long as this one is still going — silently, which is the failure this whole busy state
        /// exists to end.
        /// </summary>
        private void Cancel()
        {
            attempt++;

            page.Status.BeginWait("Cancelling");
            page.LockCancel();
        }

        /// <summary>
        /// Fetches the session list and reconciles the browser against it.
        ///
        /// <paramref name="announce"/> separates the two callers. The button and the first load lock
        /// the page and animate a caption, because the player asked and is waiting. The automatic
        /// refresh does neither: a list that dimmed itself and repainted a caption once a second
        /// would be unusable.
        /// </summary>
        private async void Query(bool announce)
        {
            if (disposed || polling) return;

            polling = true;

            int mine = announce
                ? BeginAttempt(LobbyBusyScope.Querying, "Looking for open sessions", null)
                : attempt;

            List<Lobby> lobbies = await session.QueryAsync();

            polling = false;

            // A silent refresh has no claim on the page, so it checks only that the page it is
            // about to write to is still the one it queried for. An announced one owns an attempt,
            // and gives it back here.
            if (disposed) return;
            if (announce)
            {
                if (!Owns(mine)) return;
                EndAttempt();
            }

            // Null means the query failed, which is not the same as finding nothing — see
            // LobbySession.QueryAsync. The list on screen is the last one known to be true, so it
            // stays, and the reason is already on the status line.
            if (lobbies == null)
            {
                refresh.Refused();
                return;
            }

            refresh.Landed();

            // Filtered to the route here rather than in the query itself: Lobby's own query
            // filters cannot reliably express "this custom key equals this value" across SDK
            // versions, and a filter that quietly matched nothing would read as "nobody is hosting"
            // on the one screen whose entire job is to say otherwise.
            page.Browser.Apply(lobbies.FindAll(lobby => route.Accepts(LobbyTeams.IsVersus(lobby))),
                               refresh.HasLanded);
        }

        /// <summary>
        /// Locks the page, animates a caption, and returns the generation the caller should quote
        /// back when its work finishes. <paramref name="caption"/> is a stem without its ellipsis.
        /// </summary>
        private int BeginAttempt(LobbyBusyScope busyScope, string caption, string activeRowId)
        {
            attempt++;
            scope = busyScope;

            page.SetBusy(busyScope, activeRowId);
            page.Status.BeginWait(caption);

            return attempt;
        }

        private void EndAttempt()
        {
            scope = LobbyBusyScope.None;

            page.SetBusy(LobbyBusyScope.None, null);
            page.Status.EndWait();
        }

        /// <summary>True while <paramref name="mine"/> is still the attempt the page is waiting on.</summary>
        private bool Owns(int mine) => !disposed && attempt == mine;

        /// <summary>
        /// Sends a finished join somewhere, having first worked out whether anyone is still waiting
        /// for it.
        /// </summary>
        private async Task Settle(int mine, bool joined, string failureMessage)
        {
            if (!Owns(mine))
            {
                await Abandon(joined);

                if (disposed) return;

                EndAttempt();
                page.Status.Say("Cancelled.");
                return;
            }

            EndAttempt();
            Finish(joined, failureMessage);
        }

        /// <summary>
        /// Hands back a session nobody is waiting for any more.
        ///
        /// A cancelled join that turns out to have succeeded cannot simply be forgotten: the player
        /// would be sitting in a lobby the screen has stopped showing, occupying one of its slots,
        /// on a Relay connection nothing is reading. <c>LeaveAsync</c> does the three things that
        /// fixes — forget the lobby, shut the transport down, hand the membership back — and handing
        /// it back is what stops the next attempt being refused with "player is already a member".
        /// </summary>
        private async Task Abandon(bool joined)
        {
            if (!joined || session == null) return;

            await session.LeaveAsync();
        }

        private void Finish(bool joined, string failureMessage)
        {
            if (joined)
            {
                onJoined();
                return;
            }

            // LobbySession already reported the specific reason through Failed, which the screen
            // has written to this page. Only say something when it did not.
            if (page.Status.IsEmpty) page.Status.Say(failureMessage);
        }
    }
}
