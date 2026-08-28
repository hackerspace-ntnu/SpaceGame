using NUnit.Framework;
using SpaceGame.Presentation.Lobbies;

namespace SpaceGame.Tests
{
    /// <summary>
    /// <see cref="LobbyRouteExtensions"/>: the four fixed answers a <see cref="LobbyRoute"/> gives
    /// to "am I hosting" and "am I VS", and the browser filter built from the second one.
    /// </summary>
    public class LobbyRouteTests
    {
        [TestCase(LobbyRoute.StoryHost, ExpectedResult = true)]
        [TestCase(LobbyRoute.VersusHost, ExpectedResult = true)]
        [TestCase(LobbyRoute.StoryJoin, ExpectedResult = false)]
        [TestCase(LobbyRoute.VersusJoin, ExpectedResult = false)]
        public bool IsHosting_MatchesTheRoute(LobbyRoute route) => route.IsHosting();

        [TestCase(LobbyRoute.StoryHost, ExpectedResult = false)]
        [TestCase(LobbyRoute.StoryJoin, ExpectedResult = false)]
        [TestCase(LobbyRoute.VersusHost, ExpectedResult = true)]
        [TestCase(LobbyRoute.VersusJoin, ExpectedResult = true)]
        public bool IsVersus_MatchesTheRoute(LobbyRoute route) => route.IsVersus();

        [Test]
        public void Accepts_AStoryRouteAcceptsOnlyStoryLobbies()
        {
            Assert.IsTrue(LobbyRoute.StoryJoin.Accepts(lobbyIsVersus: false));
            Assert.IsFalse(LobbyRoute.StoryJoin.Accepts(lobbyIsVersus: true));
        }

        [Test]
        public void Accepts_AVersusRouteAcceptsOnlyVersusLobbies()
        {
            Assert.IsTrue(LobbyRoute.VersusJoin.Accepts(lobbyIsVersus: true));
            Assert.IsFalse(LobbyRoute.VersusJoin.Accepts(lobbyIsVersus: false));
        }

        [Test]
        public void Accepts_AHostRouteAgreesWithItsJoinCounterpart()
        {
            // A host's own route never queries the browser, but Accepts still has to answer
            // consistently for it — the same rule a joiner on the matching route gets.
            Assert.AreEqual(LobbyRoute.StoryJoin.Accepts(false), LobbyRoute.StoryHost.Accepts(false));
            Assert.AreEqual(LobbyRoute.VersusJoin.Accepts(true), LobbyRoute.VersusHost.Accepts(true));
        }
    }
}
