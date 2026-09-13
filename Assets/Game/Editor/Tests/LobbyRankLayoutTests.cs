// LobbyPreviewRank touches uGUI (RectTransform, Button, TextMeshProUGUI), so it lives in
// Assembly-CSharp. This test therefore goes in Assets/Game/Editor/Tests/, which compiles into
// Assembly-CSharp-Editor — the only test location that can see Assembly-CSharp types.
// Assets/Game/Tests/EditMode/ has its own asmdef and cannot reference it. MenuStepperTests.cs
// carries the same note.
using NUnit.Framework;
using SpaceGame.Presentation.Lobbies;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The rank's own arithmetic — which figure stands in which seat, and whether a team can be
    /// stood on at all.
    ///
    /// The figures themselves need a Resources prefab and a camera, so what is pinned here is the
    /// mapping the rank does before it touches either.
    /// </summary>
    public class LobbyRankLayoutTests
    {
        [Test]
        public void PlayersFillTheirOwnTeamsSeatsInLobbyOrder()
        {
            int[] teams = { 0, 1, 0 };

            Assert.AreEqual(0, LobbyPreviewRank.SeatOf(0, teams));
            Assert.AreEqual(0, LobbyPreviewRank.SeatOf(1, teams), "team two's first player is its seat 0");
            Assert.AreEqual(1, LobbyPreviewRank.SeatOf(2, teams));
        }

        [Test]
        public void AStoryLobbyPutsEveryoneOnOneTeam()
        {
            int[] teams = System.Array.Empty<int>();

            Assert.AreEqual(0, LobbyPreviewRank.SeatOf(0, teams));
            Assert.AreEqual(2, LobbyPreviewRank.SeatOf(2, teams));
        }

        [Test]
        public void ASeatBeyondTheTeamsSizeIsStillPlaced()
        {
            int[] teams = { 0, 0, 0 };

            Assert.AreEqual(2, LobbyPreviewRank.SeatOf(2, teams),
                "a player over the size cap has to stand somewhere, not vanish");
        }

        [Test]
        public void TheTeamYouAreOnIsNotOfferedAsSomewhereToGo()
        {
            Assert.IsFalse(LobbyPreviewRank.CanJoin(team: 1, localTeam: 1, headsOn: 0, teamSize: 2));
        }

        [Test]
        public void AFullTeamIsNotOfferedEither()
        {
            Assert.IsFalse(LobbyPreviewRank.CanJoin(team: 0, localTeam: 1, headsOn: 2, teamSize: 2));
        }

        [Test]
        public void ATeamWithRoomIsOffered()
        {
            Assert.IsTrue(LobbyPreviewRank.CanJoin(team: 0, localTeam: 1, headsOn: 1, teamSize: 2));
        }

        /// <summary>
        /// A spectator — someone in the lobby with no team yet — must be able to join any team with
        /// room, or they have nowhere at all to stand.
        /// </summary>
        [Test]
        public void SomeoneWithNoTeamCanJoinAnyTeamWithRoom()
        {
            Assert.IsTrue(LobbyPreviewRank.CanJoin(team: 0, localTeam: -1, headsOn: 0, teamSize: 2));
        }
    }
}
