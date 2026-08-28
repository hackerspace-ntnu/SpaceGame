using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using SpaceGame.Core.Lobbies;
using SpaceGame.Gameplay;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The pure half of VS's lobby data: keys, encoding, and the readers that turn a
    /// <see cref="Lobby"/> into a <see cref="RosterSnapshot"/>. No service calls — see
    /// <see cref="LobbyOptionsTests"/> for the sibling coverage of the non-VS option builders.
    /// </summary>
    public class VersusLobbyDataTests
    {
        // ─────────────────────────────────────────────
        //  Mode
        // ─────────────────────────────────────────────

        [Test]
        public void IsVersus_TrueForAModeKeySetToVersus()
        {
            Assert.IsTrue(LobbyTeams.IsVersus(LobbyWithMode(LobbyKeys.ModeVersus)));
        }

        [Test]
        public void IsVersus_FalseForAStoryLobby()
        {
            Assert.IsFalse(LobbyTeams.IsVersus(LobbyWithMode(LobbyKeys.ModeStory)));
        }

        [Test]
        public void IsVersus_FalseWhenTheModeKeyIsMissingEntirely()
        {
            // A lobby created by a build that shipped before VS existed carries no Mode key at
            // all. It must keep reading as the story lobby it always was.
            Lobby lobby = new(id: "l", data: new Dictionary<string, DataObject>());
            Assert.IsFalse(LobbyTeams.IsVersus(lobby));
        }

        // ─────────────────────────────────────────────
        //  VersusSetup and lobby creation
        // ─────────────────────────────────────────────
        //
        // The path a VS lobby is actually born through: VersusSetup's own clamp, and the
        // if (versus.IsVersus) branch of BuildCreateOptions that stamps the mode and team-rule
        // keys at creation. Every other test in this file exercises what happens once a lobby
        // already carries those keys — this section is what puts them there in the first place.

        [Test]
        public void AVersusSetupClampsThePairIntoTheCeiling()
        {
            var setup = new VersusSetup(teamCount: 99, teamSize: 99);

            Assert.LessOrEqual(setup.Seats, VersusRules.MaxSeats);
            Assert.GreaterOrEqual(setup.TeamCount, VersusRules.MinTeams);
            Assert.GreaterOrEqual(setup.TeamSize, VersusRules.MinTeamSize);
            Assert.IsTrue(setup.IsVersus);
        }

        [Test]
        public void TheDefaultSetupIsNotAVersusMatch()
        {
            Assert.IsFalse(VersusSetup.None.IsVersus);
        }

        /// <summary>
        /// The mode and the rules are stamped at creation rather than by a follow-up update, for
        /// the reason the relay code is: a client polling in the gap between the two reads a lobby
        /// that is missing them and draws the wrong thing for that poll.
        /// </summary>
        [Test]
        public void CreatingAVersusLobbyStampsItsModeAndRules()
        {
            CreateLobbyOptions options = LobbyOptions.Create(
                isPrivate: false, relayJoinCode: "RELAY", playerName: "Pilot", suitColor: 0,
                versus: new VersusSetup(teamCount: 3, teamSize: 4));

            Assert.AreEqual(LobbyKeys.ModeVersus, options.Data[LobbyKeys.Mode].Value);
            Assert.AreEqual("3", options.Data[LobbyKeys.TeamCount].Value);
            Assert.AreEqual("4", options.Data[LobbyKeys.TeamSize].Value);
        }

        [Test]
        public void CreatingAStoryLobbyStampsTheStoryModeAndNoTeamRules()
        {
            CreateLobbyOptions options = LobbyOptions.Create(
                isPrivate: false, relayJoinCode: "RELAY", playerName: "Pilot", suitColor: 0,
                versus: VersusSetup.None);

            Assert.AreEqual(LobbyKeys.ModeStory, options.Data[LobbyKeys.Mode].Value);
            Assert.IsFalse(options.Data.ContainsKey(LobbyKeys.TeamCount),
                           "a story lobby has no teams to describe");
        }

        /// <summary>
        /// The mode key has to be readable by someone who has NOT joined — it is what filters the
        /// browser — so it cannot be Member-visible like the rules are.
        /// </summary>
        [Test]
        public void TheModeIsVisibleToPlayersWhoHaveNotJoined()
        {
            CreateLobbyOptions options = LobbyOptions.Create(
                false, "RELAY", "Pilot", 0, new VersusSetup(2, 2));

            Assert.AreEqual(DataObject.VisibilityOptions.Public,
                            options.Data[LobbyKeys.Mode].Visibility);
        }

        // ─────────────────────────────────────────────
        //  Team rules
        // ─────────────────────────────────────────────

        [Test]
        public void TeamRules_RoundTripThroughBuildAndRead()
        {
            UpdateLobbyOptions options = LobbyOptions.TeamRules(3, 4);
            Lobby lobby = new(id: "l", data: options.Data);

            Assert.AreEqual(3, LobbyTeams.TeamCountOf(lobby));
            Assert.AreEqual(4, LobbyTeams.TeamSizeOf(lobby));
        }

        [Test]
        public void TeamRules_SetMaxPlayersToTheSeatProduct()
        {
            UpdateLobbyOptions options = LobbyOptions.TeamRules(3, 4);
            Assert.AreEqual(12, options.MaxPlayers);
        }

        [Test]
        public void TeamRules_FallBackToDefaultsWhenTheKeysAreAbsent()
        {
            Lobby lobby = new(id: "l", data: new Dictionary<string, DataObject>());

            Assert.AreEqual(VersusRules.DefaultTeams, LobbyTeams.TeamCountOf(lobby));
            Assert.AreEqual(VersusRules.DefaultTeamSize, LobbyTeams.TeamSizeOf(lobby));
        }

        [Test]
        public void TeamRules_FallBackToDefaultsWhenTheValueIsUnparseable()
        {
            Lobby lobby = new(id: "l", data: new Dictionary<string, DataObject>
            {
                { LobbyKeys.TeamCount, new DataObject(DataObject.VisibilityOptions.Member, "not-a-number") },
                { LobbyKeys.TeamSize, new DataObject(DataObject.VisibilityOptions.Member, "also garbage") }
            });

            Assert.AreEqual(VersusRules.DefaultTeams, LobbyTeams.TeamCountOf(lobby));
            Assert.AreEqual(VersusRules.DefaultTeamSize, LobbyTeams.TeamSizeOf(lobby));
        }

        // ─────────────────────────────────────────────
        //  Teams
        // ─────────────────────────────────────────────

        [Test]
        public void Teams_ReadInLobbyOrder()
        {
            Lobby lobby = LobbyWithPlayers(
                PlayerWithTeam("a", 0),
                PlayerWithTeam("b", 1),
                PlayerWithTeam("c", 0));

            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, LobbyTeams.Teams(lobby));
        }

        [Test]
        public void Teams_APlayerWithNoTeamKeyIsTeamZero()
        {
            Lobby lobby = LobbyWithPlayers(new Player(id: "a"));
            CollectionAssert.AreEqual(new[] { 0 }, LobbyTeams.Teams(lobby));
        }

        [Test]
        public void Teams_AnOutOfRulesIndexFoldsBackIn()
        {
            // TeamCount defaults to 2 (no KeyTeamCount on this lobby). A peer from a build that
            // allows more teams sends team 7, which has to land inside [0, 2) — by modulus, not by
            // being clamped to 0, which would dump every stray index onto the same team.
            Lobby lobby = LobbyWithPlayers(PlayerWithTeam("a", 7));

            CollectionAssert.AreEqual(new[] { 1 }, LobbyTeams.Teams(lobby));
        }

        // ─────────────────────────────────────────────
        //  Occupancy
        // ─────────────────────────────────────────────

        [Test]
        public void Occupancy_CountsHeadsPerTeam()
        {
            Lobby lobby = LobbyWithPlayers(
                PlayerWithTeam("a", 0),
                PlayerWithTeam("b", 1),
                PlayerWithTeam("c", 1));

            CollectionAssert.AreEqual(new[] { 1, 2 }, LobbyTeams.Occupancy(lobby));
        }

        // ─────────────────────────────────────────────
        //  Team colour encoding
        // ─────────────────────────────────────────────

        [Test]
        public void TeamColor_RoundTripsThroughEncodeAndDecode()
        {
            string encoded = TeamColorOpinion.Encode(5, 123456789L);

            Assert.IsTrue(TeamColorOpinion.TryDecode(encoded, out int swatch, out long stampMs));
            Assert.AreEqual(5, swatch);
            Assert.AreEqual(123456789L, stampMs);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("garbage")]
        [TestCase("5")]
        [TestCase("five:123")]
        [TestCase("5:not-a-number")]
        [TestCase(":123")]
        [TestCase("5:")]
        public void TeamColor_UnrecognisedValuesDecodeAsFalseRatherThanThrowing(string value)
        {
            Assert.DoesNotThrow(() => TeamColorOpinion.TryDecode(value, out _, out _));
            Assert.IsFalse(TeamColorOpinion.TryDecode(value, out _, out _));
        }

        // ─────────────────────────────────────────────
        //  TeamColorsOf
        // ─────────────────────────────────────────────

        [Test]
        public void TeamColorsOf_TheLatestWriterDecidesATeamsColour()
        {
            // THE regression test for the stamp: this is the whole point of storing colour on
            // player data instead of a shared lobby table.
            Lobby lobby = LobbyWithPlayers(
                PlayerWithTeamColor("a", team: 0, swatch: 2, stampMs: 100),
                PlayerWithTeamColor("b", team: 0, swatch: 5, stampMs: 200));

            int[] colors = LobbyTeams.TeamColorsOf(lobby, swatchCount: 12);
            Assert.AreEqual(5, colors[0]);
        }

        [Test]
        public void TeamColorsOf_TiesGoToTheEarlierPlayerInLobbyOrder()
        {
            // Two peers racing the arrow keys can publish the identical stamp. Both machines have
            // to resolve the tie the same way, or they paint the team's rank in different colours.
            Lobby lobby = LobbyWithPlayers(
                PlayerWithTeamColor("a", team: 0, swatch: 2, stampMs: 100),
                PlayerWithTeamColor("b", team: 0, swatch: 5, stampMs: 100));

            int[] colors = LobbyTeams.TeamColorsOf(lobby, swatchCount: 12);
            Assert.AreEqual(2, colors[0], "An exact tie must leave the earlier player's colour standing.");
        }

        [Test]
        public void TeamColorsOf_ATeamNobodyRecolouredWearsItsDefault()
        {
            Lobby lobby = LobbyWithPlayers(
                PlayerWithTeamColor("a", team: 0, swatch: 5, stampMs: 100),
                PlayerWithTeam("b", 1));

            int[] colors = LobbyTeams.TeamColorsOf(lobby, swatchCount: 12);
            int[] defaults = TeamColorRules.DefaultColors(2, 12);

            Assert.AreEqual(5, colors[0]);
            Assert.AreEqual(defaults[1], colors[1]);
        }

        [Test]
        public void TeamColorsOf_OneEntryPerTeamEvenWithNobodyInTheLobby()
        {
            Lobby lobby = new(id: "l", data: new Dictionary<string, DataObject>
            {
                { LobbyKeys.TeamCount, new DataObject(DataObject.VisibilityOptions.Member, "5") }
            });

            Assert.AreEqual(5, LobbyTeams.TeamColorsOf(lobby, swatchCount: 12).Length);
        }

        [Test]
        public void TeamColorsOf_NeverReadsOutOfBoundsWithAnOutOfRulesTeamIndex()
        {
            // TeamCount defaults to 2; the player claims team 7. Teams() folds it to 1 before
            // TeamColorsOf ever indexes with it, so a 2-entry colour array is never asked for
            // index 7.
            Lobby lobby = LobbyWithPlayers(PlayerWithTeamColor("a", team: 7, swatch: 3, stampMs: 100));

            int[] colors = null;
            Assert.DoesNotThrow(() => colors = LobbyTeams.TeamColorsOf(lobby, swatchCount: 12));
            Assert.AreEqual(2, colors.Length);
            Assert.AreEqual(3, colors[1], "Team 7 folds to team 1 of 2 — the same slot Teams() reports.");
        }

        // ─────────────────────────────────────────────
        //  Snapshot
        // ─────────────────────────────────────────────

        [Test]
        public void Snapshot_CarriesWhatAViewNeeds()
        {
            Lobby lobby = new(
                id: "l",
                hostId: "host",
                data: new Dictionary<string, DataObject>
                {
                    { LobbyKeys.Mode, new DataObject(DataObject.VisibilityOptions.Public, LobbyKeys.ModeVersus) },
                    { LobbyKeys.TeamCount, new DataObject(DataObject.VisibilityOptions.Member, "2") },
                    { LobbyKeys.TeamSize, new DataObject(DataObject.VisibilityOptions.Member, "2") }
                },
                players: new List<Player>
                {
                    PlayerWithTeamAndColor("host", team: 0, suit: 1, swatch: 3, stampMs: 10),
                    PlayerWithTeamAndColor("guest", team: 1, suit: 4, swatch: 6, stampMs: 20)
                });

            RosterSnapshot snapshot = LobbyRoster.Snapshot(lobby, localSlot: 1, swatchCount: 12);

            Assert.IsTrue(snapshot.IsVersus);
            Assert.AreEqual(2, snapshot.TeamCount);
            Assert.AreEqual(2, snapshot.TeamSize);
            Assert.AreEqual(1, snapshot.LocalSlot);
            Assert.AreEqual(0, snapshot.HostSlot);
            Assert.AreEqual(1, snapshot.LocalTeam);
            CollectionAssert.AreEqual(new[] { 1, 4 }, snapshot.SuitColors);
            CollectionAssert.AreEqual(new[] { 0, 1 }, snapshot.Teams);
            Assert.AreEqual(3, snapshot.ColorOfTeam(0));
            Assert.AreEqual(6, snapshot.ColorOfTeam(1));
            Assert.AreEqual(1, snapshot.HeadsOn(0));
            Assert.IsTrue(snapshot.HasRoomOn(0));
        }

        [Test]
        public void Snapshot_AStorySnapshotStillHasSuitColors()
        {
            Lobby lobby = LobbyWithPlayers(PlayerWithSuit("a", suit: 3));

            RosterSnapshot snapshot = LobbyRoster.Snapshot(lobby, localSlot: 0, swatchCount: 12);

            Assert.IsFalse(snapshot.IsVersus);
            CollectionAssert.AreEqual(new[] { 3 }, snapshot.SuitColors);
        }

        [Test]
        public void Snapshot_ANullLobbyGivesASafeEmptySnapshot()
        {
            RosterSnapshot snapshot = LobbyRoster.Snapshot(null, localSlot: -1, swatchCount: 12);

            Assert.IsFalse(snapshot.IsVersus);
            CollectionAssert.IsEmpty(snapshot.Names);
            CollectionAssert.IsEmpty(snapshot.SuitColors);
            CollectionAssert.IsEmpty(snapshot.Teams);
            Assert.AreEqual(-1, snapshot.HostSlot);
            Assert.AreEqual(-1, snapshot.LocalSlot);
            Assert.AreEqual(-1, snapshot.LocalTeam);
            Assert.AreEqual(VersusRules.DefaultTeams, snapshot.TeamColors.Length);
            Assert.AreEqual(0, snapshot.HeadsOn(0));
            Assert.AreEqual(0, snapshot.ColorOfTeam(0));
        }

        [Test]
        public void Snapshot_ColorsOfOtherTeamsLeavesOurOwnOut()
        {
            // What the colour cycler hands TeamColorRules.Step so a team never lands on a rival's
            // swatch. Our own team's colour must not be in it, or stepping would refuse to stay put.
            var snapshot = new RosterSnapshot(new[] { "a" }, null, new[] { 1 }, new[] { 3, 6, 9 }, null,
                                              teamCount: 3, teamSize: 2, localSlot: 0, hostSlot: 0,
                                              isVersus: true);

            CollectionAssert.AreEqual(new[] { 3, 9 }, snapshot.ColorsOfOtherTeams(1));
            CollectionAssert.AreEqual(new[] { 3, 6, 9 }, snapshot.ColorsOfOtherTeams(-1),
                                      "a player with no team yet is barred from every team's colour");
        }

        // ─────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────

        private static Lobby LobbyWithMode(string mode) => new(id: "l", data: new Dictionary<string, DataObject>
        {
            { LobbyKeys.Mode, new DataObject(DataObject.VisibilityOptions.Public, mode) }
        });

        private static Lobby LobbyWithPlayers(params Player[] players) =>
            new(id: "l", players: new List<Player>(players));

        private static Player PlayerWithTeam(string id, int team) => new(id: id,
            data: new Dictionary<string, PlayerDataObject>
            {
                { LobbyKeys.Team, TextData(team) }
            });

        private static Player PlayerWithSuit(string id, int suit) => new(id: id,
            data: new Dictionary<string, PlayerDataObject>
            {
                { LobbyKeys.SuitColor, TextData(suit) }
            });

        private static Player PlayerWithTeamColor(string id, int team, int swatch, long stampMs) => new(id: id,
            data: new Dictionary<string, PlayerDataObject>
            {
                { LobbyKeys.Team, TextData(team) },
                { LobbyKeys.TeamColor, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member,
                    TeamColorOpinion.Encode(swatch, stampMs)) }
            });

        private static Player PlayerWithTeamAndColor(string id, int team, int suit, int swatch, long stampMs) =>
            new(id: id, data: new Dictionary<string, PlayerDataObject>
            {
                { LobbyKeys.Team, TextData(team) },
                { LobbyKeys.SuitColor, TextData(suit) },
                { LobbyKeys.TeamColor, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member,
                    TeamColorOpinion.Encode(swatch, stampMs)) }
            });

        private static PlayerDataObject TextData(int value) =>
            new(PlayerDataObject.VisibilityOptions.Member, value.ToString(CultureInfo.InvariantCulture));
    }
}
