using NUnit.Framework;
using SpaceGame.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The parts of <see cref="LobbySession"/> that hold without a live Lobby service.
    ///
    /// Creating and joining need Unity Gaming Services and two machines, and are covered by playing
    /// the game. The option objects handed to those calls do not — and every bug that made the
    /// lobby unusable lived in one of them.
    /// </summary>
    public class LobbySessionTests
    {
        [Test]
        public void CreateOptions_CarriesTheRelayCodeAtCreation()
        {
            // Written at creation rather than by a follow-up UpdateLobbyAsync. A client that polled
            // in the gap between the two saw a lobby with no join code and read straight past the
            // missing key.
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, null, "RELAY99", "Ferdinand");

            Assert.IsTrue(options.Data.ContainsKey(LobbySession.KeyRelayJoinCode));
            Assert.AreEqual("RELAY99", options.Data[LobbySession.KeyRelayJoinCode].Value);
        }

        [Test]
        public void CreateOptions_StartsInTheWaitingState()
        {
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, null, "RELAY99", "Ferdinand");

            Assert.AreEqual(LobbySession.StateWaiting, options.Data[LobbySession.KeyGameState].Value);
        }

        [Test]
        public void CreateOptions_PublishesGameStateToNonMembers()
        {
            // The lobby browser labels rows the player has not joined, so this key has to be
            // visible to non-members. Member visibility would render every row blank.
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, null, "RELAY99", "Ferdinand");

            Assert.AreEqual(DataObject.VisibilityOptions.Public,
                options.Data[LobbySession.KeyGameState].Visibility);
        }

        [Test]
        public void CreateOptions_CarriesThePlayerName()
        {
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, null, "RELAY99", "Ferdinand");

            Assert.AreEqual("Ferdinand", options.Player.Data[LobbySession.KeyPlayerName].Value);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void CreateOptions_TreatsABlankPasswordAsNoPassword(string blank)
        {
            // Lobby rejects an empty-string password outright rather than ignoring it, so a private
            // lobby created with the password field untouched failed to create at all.
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(true, blank, "RELAY99", "Ferdinand");

            Assert.IsNull(options.Password);
        }

        [Test]
        public void CreateOptions_KeepsARealPassword()
        {
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(true, "hunter2", "RELAY99", "Ferdinand");

            Assert.AreEqual("hunter2", options.Password);
        }

        [Test]
        public void BeginGameOptions_NeverLockTheLobby()
        {
            // THE regression test for this feature. Locking the lobby at game start is what made it
            // impossible to join a session already in progress: a locked lobby refuses every join,
            // and the host is usually playing alone when the first friend tries.
            UpdateLobbyOptions options = LobbySession.BuildBeginGameOptions();

            Assert.IsTrue(options.IsLocked == null || options.IsLocked == false,
                "Locking the lobby at start makes late join impossible.");
        }

        [Test]
        public void BeginGameOptions_MarkTheLobbyInGame()
        {
            UpdateLobbyOptions options = LobbySession.BuildBeginGameOptions();

            Assert.AreEqual(LobbySession.StateInGame, options.Data[LobbySession.KeyGameState].Value);
        }

        [Test]
        public void Occupancy_CountsTakenSlotsNotFreeOnes()
        {
            // Lobby reports FREE slots. Reading that as the taken count is how every row in the
            // browser came to claim it was empty.
            Assert.AreEqual("3/4", LobbySession.DescribeOccupancy(4, 1));
            Assert.AreEqual("0/4", LobbySession.DescribeOccupancy(4, 4));
            Assert.AreEqual("4/4", LobbySession.DescribeOccupancy(4, 0));
        }
    }
}
