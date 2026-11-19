using NUnit.Framework;
using SpaceGame.Presentation.Lobbies;

namespace SpaceGame.Tests
{
    /// <summary>
    /// <see cref="LobbyRouteExtensions"/>: the fixed answers a <see cref="LobbyRoute"/> gives to
    /// "am I hosting", "am I VS" and "am I arena", and the browser filter built from the last two.
    /// An arena route counts as VS — it has teams — and the filter still keeps the two lists apart.
    /// </summary>
    public class LobbyRouteTests
    {
        [TestCase(LobbyRoute.StoryHost, ExpectedResult = true)]
        [TestCase(LobbyRoute.VersusHost, ExpectedResult = true)]
        [TestCase(LobbyRoute.ArenaHost, ExpectedResult = true)]
        [TestCase(LobbyRoute.StoryJoin, ExpectedResult = false)]
        [TestCase(LobbyRoute.VersusJoin, ExpectedResult = false)]
        [TestCase(LobbyRoute.ArenaJoin, ExpectedResult = false)]
        public bool IsHosting_MatchesTheRoute(LobbyRoute route) => route.IsHosting();

        [TestCase(LobbyRoute.StoryHost, ExpectedResult = false)]
        [TestCase(LobbyRoute.StoryJoin, ExpectedResult = false)]
        [TestCase(LobbyRoute.VersusHost, ExpectedResult = true)]
        [TestCase(LobbyRoute.VersusJoin, ExpectedResult = true)]
        [TestCase(LobbyRoute.ArenaHost, ExpectedResult = true)]
        [TestCase(LobbyRoute.ArenaJoin, ExpectedResult = true)]
        public bool IsVersus_MatchesTheRoute(LobbyRoute route) => route.IsVersus();

        [TestCase(LobbyRoute.StoryJoin, ExpectedResult = false)]
        [TestCase(LobbyRoute.VersusJoin, ExpectedResult = false)]
        [TestCase(LobbyRoute.ArenaHost, ExpectedResult = true)]
        [TestCase(LobbyRoute.ArenaJoin, ExpectedResult = true)]
        public bool IsArena_MatchesTheRoute(LobbyRoute route) => route.IsArena();

        [Test]
        public void Accepts_AStoryRouteAcceptsOnlyStoryLobbies()
        {
            Assert.IsTrue(LobbyRoute.StoryJoin.Accepts(lobbyIsVersus: false, lobbyIsArena: false));
            Assert.IsFalse(LobbyRoute.StoryJoin.Accepts(lobbyIsVersus: true, lobbyIsArena: false));
            Assert.IsFalse(LobbyRoute.StoryJoin.Accepts(lobbyIsVersus: true, lobbyIsArena: true));
        }

        [Test]
        public void Accepts_AVersusRouteAcceptsOnlyVersusLobbies()
        {
            Assert.IsTrue(LobbyRoute.VersusJoin.Accepts(lobbyIsVersus: true, lobbyIsArena: false));
            Assert.IsFalse(LobbyRoute.VersusJoin.Accepts(lobbyIsVersus: false, lobbyIsArena: false));
            Assert.IsFalse(LobbyRoute.VersusJoin.Accepts(lobbyIsVersus: true, lobbyIsArena: true));
        }

        [Test]
        public void Accepts_AnArenaRouteAcceptsOnlyArenaLobbies()
        {
            Assert.IsTrue(LobbyRoute.ArenaJoin.Accepts(lobbyIsVersus: true, lobbyIsArena: true));
            Assert.IsFalse(LobbyRoute.ArenaJoin.Accepts(lobbyIsVersus: true, lobbyIsArena: false));
            Assert.IsFalse(LobbyRoute.ArenaJoin.Accepts(lobbyIsVersus: false, lobbyIsArena: false));
        }

        [Test]
        public void Accepts_AHostRouteAgreesWithItsJoinCounterpart()
        {
            // A host's own route never queries the browser, but Accepts still has to answer
            // consistently for it — the same rule a joiner on the matching route gets.
            Assert.AreEqual(LobbyRoute.StoryJoin.Accepts(false, false), LobbyRoute.StoryHost.Accepts(false, false));
            Assert.AreEqual(LobbyRoute.VersusJoin.Accepts(true, false), LobbyRoute.VersusHost.Accepts(true, false));
            Assert.AreEqual(LobbyRoute.ArenaJoin.Accepts(true, true), LobbyRoute.ArenaHost.Accepts(true, true));
        }
    }
}
