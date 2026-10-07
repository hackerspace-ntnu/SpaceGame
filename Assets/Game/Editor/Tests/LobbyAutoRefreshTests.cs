using NUnit.Framework;
using SpaceGame.Presentation.Lobbies;

namespace SpaceGame.Tests
{
    /// <summary>
    /// <see cref="LobbyAutoRefresh"/>: the cadence of the session list's automatic refresh, which
    /// sits right on Lobby's one-query-per-second ceiling and must back off when refused.
    /// </summary>
    public class LobbyAutoRefreshTests
    {
        [Test]
        public void AQueryIsDueOnceTheIntervalHasPassed()
        {
            var refresh = new LobbyAutoRefresh();

            Assert.IsFalse(refresh.Advance(LobbyAutoRefresh.Interval * 0.5f));
            Assert.IsTrue(refresh.Advance(LobbyAutoRefresh.Interval * 0.5f));
        }

        [Test]
        public void RefusalsBackOffByDoublingUpToTheCap()
        {
            // A service that is refusing us is not asked again at full rate. Beyond the cap the
            // wait stays put rather than growing without bound.
            var refresh = new LobbyAutoRefresh();

            refresh.Refused();
            Assert.AreEqual(LobbyAutoRefresh.Interval * 2f, refresh.SecondsUntilDue, 0.001f);

            refresh.Refused();
            Assert.AreEqual(LobbyAutoRefresh.Interval * 4f, refresh.SecondsUntilDue, 0.001f);

            for (int i = 0; i < 10; i++) refresh.Refused();
            Assert.AreEqual(LobbyAutoRefresh.MaxBackoff, refresh.SecondsUntilDue, 0.001f);
        }

        [Test]
        public void ASuccessResetsTheBackoffAndMarksTheListAsLanded()
        {
            var refresh = new LobbyAutoRefresh();
            Assert.IsFalse(refresh.HasLanded, "the page must not announce an empty list before it has looked");

            refresh.Refused();
            refresh.Refused();
            refresh.Landed();

            Assert.IsTrue(refresh.HasLanded);
            Assert.AreEqual(LobbyAutoRefresh.Interval, refresh.SecondsUntilDue, 0.001f);
        }
    }
}
