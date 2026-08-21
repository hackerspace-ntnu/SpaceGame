// The parts of leaving a session that hold without a live service or a second machine.
//
// Connecting, disconnecting and the scene load out of a world all need a running NetworkManager and
// somebody on the other end of a wire, and are covered by playing the game and by
// MultiplayerAutotest. What can be pinned here is the handful of decisions that were wrong, and each
// of them is a decision that produced no error when it was wrong: a disconnect reason that arrives
// empty (which is the NORMAL case — a host that quits sends no reason at all), a subscription that
// silently attaches to nothing, and a "leave the lobby" that used to be skipped entirely.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    public class SessionExitTests
    {
        // ── The reason a player is shown ──────────────────────────────────────────

        [Test]
        public void Describe_TurnsAnEmptyReasonIntoWhatActuallyHappened()
        {
            // NetworkManager.DisconnectReason only carries text when a server chose to refuse
            // somebody. A host that quits, crashes or drops off the network sends nothing, which is
            // the common case and must not surface as a blank screen.
            Assert.AreEqual("Lost connection to the host.", SessionWatchdog.Describe(null));
            Assert.AreEqual("Lost connection to the host.", SessionWatchdog.Describe(string.Empty));
            Assert.AreEqual("Lost connection to the host.", SessionWatchdog.Describe("   "));
        }

        [Test]
        public void Describe_KeepsAReasonTheServerBotheredToSend()
        {
            // When the host does say why, that is the more useful sentence and it goes through
            // verbatim. Nothing here matches on the text to decide whether the session is
            // recoverable — it never is, which is the point LobbySession.HandleClientDisconnect
            // already makes about the bug that matching caused there.
            Assert.AreEqual("The host kicked you.", SessionWatchdog.Describe("The host kicked you."));
        }

        [Test]
        public void Describe_TrimsWhatItPassesOn()
        {
            Assert.AreEqual("Server full.", SessionWatchdog.Describe("  Server full.\n"));
        }

        // ── Handing the lobby membership back ─────────────────────────────────────

        [Test]
        public void LeaveInBackground_DoesNotConjureASessionToLeaveNothing()
        {
            // LobbySession.Instance is a lazy factory, so the obvious spelling of this would create
            // a DontDestroyOnLoad LobbySession on every singleplayer exit purely to be told there is
            // no lobby to leave. Reading the backing field is what makes "there was never a lobby"
            // free, and this is what stops a later tidy-up from swapping it back to Instance.
            int before = CountSessions();

            Assert.DoesNotThrow(LobbySession.LeaveInBackground);

            Assert.AreEqual(before, CountSessions(),
                "Leaving a lobby nobody is in must not bring a LobbySession into existence.");
        }

        private static int CountSessions() =>
            Object.FindObjectsByType<LobbySession>(FindObjectsInactive.Include,
                                                   FindObjectsSortMode.None).Length;

        // ── Staying subscribed to a manager that may not exist yet ────────────────

        [Test]
        public void DisconnectHook_IsQuietWithNoNetworkManager()
        {
            // The state every unit test, and every scene opened straight from the editor, is in.
            // The old code's `if (NetworkManager.Singleton != null)` was correct here and wrong one
            // frame later, when NetworkBootstrap's backfill arrived and nothing looked again.
            var hook = new DisconnectHook(_ => { });

            Assert.DoesNotThrow(hook.Poll);
            Assert.IsFalse(hook.IsAttached, "There is nothing to attach to yet.");

            Assert.DoesNotThrow(hook.Poll, "Asking again is the whole point, and must stay cheap.");
            Assert.DoesNotThrow(hook.Detach);
            Assert.DoesNotThrow(hook.Detach, "Detaching twice happens on any ordinary teardown.");
        }

        [Test]
        public void DisconnectHook_RefusesAHandlerItCanNeverCall()
        {
            // A null handler would make Poll a no-op that looks exactly like a working
            // subscription — the same silence this class exists to remove.
            Assert.Throws<System.ArgumentNullException>(() => new DisconnectHook(null));
        }
    }
}
