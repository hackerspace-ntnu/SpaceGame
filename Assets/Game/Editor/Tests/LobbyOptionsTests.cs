using NUnit.Framework;
using SpaceGame.Core.Lobbies;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The option objects <see cref="LobbySession"/> hands to the Lobby service.
    ///
    /// Creating and joining need Unity Gaming Services and two machines, and are covered by playing
    /// the game. The option objects handed to those calls do not — and every bug that made the
    /// lobby unusable lived in one of them. The VS-specific builders are covered beside the VS
    /// readers in <see cref="VersusLobbyDataTests"/>.
    /// </summary>
    public class LobbyOptionsTests
    {
        [Test]
        public void CreateOptions_CarriesTheRelayCodeAtCreation()
        {
            // Written at creation rather than by a follow-up UpdateLobbyAsync. A client that polled
            // in the gap between the two saw a lobby with no join code and read straight past the
            // missing key.
            CreateLobbyOptions options = LobbyOptions.Create(false, "RELAY99", "Ferdinand", 0, VersusSetup.None);

            Assert.IsTrue(options.Data.ContainsKey(LobbyKeys.RelayJoinCode));
            Assert.AreEqual("RELAY99", options.Data[LobbyKeys.RelayJoinCode].Value);
        }

        [Test]
        public void CreateOptions_StartsInTheWaitingState()
        {
            CreateLobbyOptions options = LobbyOptions.Create(false, "RELAY99", "Ferdinand", 0, VersusSetup.None);

            Assert.AreEqual(LobbyKeys.StateWaiting, options.Data[LobbyKeys.GameState].Value);
        }

        [Test]
        public void CreateOptions_PublishesGameStateToNonMembers()
        {
            // The lobby browser labels rows the player has not joined, so this key has to be
            // visible to non-members. Member visibility would render every row blank.
            CreateLobbyOptions options = LobbyOptions.Create(false, "RELAY99", "Ferdinand", 0, VersusSetup.None);

            Assert.AreEqual(DataObject.VisibilityOptions.Public,
                options.Data[LobbyKeys.GameState].Visibility);
        }

        [Test]
        public void CreateOptions_CarriesThePlayerName()
        {
            CreateLobbyOptions options = LobbyOptions.Create(false, "RELAY99", "Ferdinand", 0, VersusSetup.None);

            Assert.AreEqual("Ferdinand", options.Player.Data[LobbyKeys.PlayerName].Value);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CreateOptions_NeverSetAPassword(bool isPrivate)
        {
            // Sessions are reached by their code or from the browser, never by a password. Lobby
            // also rejects an empty-string password outright rather than ignoring it, so the only
            // safe value to send is none at all.
            CreateLobbyOptions options = LobbyOptions.Create(isPrivate, "RELAY99", "Ferdinand", 0, VersusSetup.None);

            Assert.IsNull(options.Password);
        }

        [Test]
        public void CreateOptions_DelistAPrivateLobby()
        {
            // Compared against true/false rather than asserted directly: IsPrivate is a bool?, and
            // spelling the comparison out keeps this off NUnit's nullable overloads.
            Assert.IsTrue(LobbyOptions.Create(true, "RELAY99", "Ferdinand", 0, VersusSetup.None).IsPrivate == true);
            Assert.IsTrue(LobbyOptions.Create(false, "RELAY99", "Ferdinand", 0, VersusSetup.None).IsPrivate == false);
        }

        [Test]
        public void BeginGameOptions_NeverLockTheLobby()
        {
            // THE regression test for this feature. Locking the lobby at game start is what made it
            // impossible to join a session already in progress: a locked lobby refuses every join,
            // and the host is usually playing alone when the first friend tries.
            UpdateLobbyOptions options = LobbyOptions.BeginGame();

            Assert.IsTrue(options.IsLocked == null || options.IsLocked == false,
                "Locking the lobby at start makes late join impossible.");
        }

        [Test]
        public void BeginGameOptions_MarkTheLobbyInGame()
        {
            UpdateLobbyOptions options = LobbyOptions.BeginGame();

            Assert.AreEqual(LobbyKeys.StateInGame, options.Data[LobbyKeys.GameState].Value);
        }

        [Test]
        public void PrivacyOptions_DelistTheLobbyWhenTurnedPrivate()
        {
            UpdateLobbyOptions options = LobbyOptions.Privacy(true);

            Assert.IsTrue(options.IsPrivate == true);
        }

        [Test]
        public void PrivacyOptions_ListTheLobbyAgainWhenTurnedPublic()
        {
            UpdateLobbyOptions options = LobbyOptions.Privacy(false);

            Assert.IsTrue(options.IsPrivate == false);
        }

        [Test]
        public void PrivacyOptions_NeverSendAPassword()
        {
            // There are no passwords in this flow. Sending one here would be worse than useless:
            // UpdateLobbyOptions reads null as "leave it alone", so a stray value would lock a lobby
            // behind a secret nothing in the UI can collect and nothing in the UI can clear.
            Assert.IsNull(LobbyOptions.Privacy(true).Password);
            Assert.IsNull(LobbyOptions.Privacy(false).Password);
        }

        [Test]
        public void Occupancy_CountsTakenSlotsNotFreeOnes()
        {
            // Lobby reports FREE slots. Reading that as the taken count is how every row in the
            // browser came to claim it was empty.
            Assert.AreEqual("3/4", LobbyRoster.DescribeOccupancy(4, 1));
            Assert.AreEqual("0/4", LobbyRoster.DescribeOccupancy(4, 4));
            Assert.AreEqual("4/4", LobbyRoster.DescribeOccupancy(4, 0));
        }
    }
}
