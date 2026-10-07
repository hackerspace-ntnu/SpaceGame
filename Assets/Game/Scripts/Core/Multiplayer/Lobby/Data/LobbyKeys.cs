namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// The keys a lobby and its players carry in Lobby-service data, and the fixed values some of
    /// them take.
    ///
    /// <para>
    /// Visibility is decided per key, and it is load-bearing. Anything the <b>browser</b> reads —
    /// a row the player has not joined yet — must be Public, or every row draws blank: the game
    /// state and the mode are read before anyone has joined anything. Everything else is Member,
    /// because it is only meaningful to people already standing in the lobby.
    /// </para>
    ///
    /// <para>
    /// There are no passwords anywhere in this data. A session is either listed in the browser or
    /// reachable only by its code, and the code is already the thing you have to be told; a second
    /// secret on top of it guarded nothing the code did not already guard.
    /// </para>
    /// </summary>
    public static class LobbyKeys
    {
        /// <summary>Relay join code, so a member can reach the server the host allocated. Member.</summary>
        public const string RelayJoinCode = "RelayJoinCode";

        /// <summary>The name other players see. Player data, Member.</summary>
        public const string PlayerName = "PlayerName";

        /// <summary>
        /// The player's suit colour, as an index into <c>SuitPalette.Swatches</c>. Player data,
        /// Member — only meaningful to people looking at the rank of astronauts inside the lobby.
        /// </summary>
        public const string SuitColor = "SuitColor";

        /// <summary>Whether the host is still in the lobby or already playing. Public: the browser labels rows.</summary>
        public const string GameState = "GameState";

        public const string StateWaiting = "waiting";
        public const string StateInGame = "in-game";

        /// <summary>
        /// Which kind of lobby this is: <see cref="ModeStory"/> or <see cref="ModeVersus"/>.
        ///
        /// Public, not Member, for the same reason <see cref="GameState"/> is: a VS joiner's list
        /// must not offer story lobbies (or a story joiner's list VS ones), and that filter has to
        /// run before anyone has joined anything — before the key could be Member-visible to them.
        /// </summary>
        public const string Mode = "Mode";

        public const string ModeStory = "story";
        public const string ModeVersus = "versus";

        /// <summary>How many teams this VS lobby is split into. Member: meaningless until you are in.</summary>
        public const string TeamCount = "TeamCount";

        /// <summary>How many seats each team of this VS lobby holds. Member.</summary>
        public const string TeamSize = "TeamSize";

        /// <summary>Which team this player stands on. Player data, Member.</summary>
        public const string Team = "Team";

        /// <summary>
        /// This player's opinion of their team's colour. Player data, Member, encoded by
        /// <see cref="TeamColorOpinion"/> — see that class for why a team's colour is an opinion
        /// on each player rather than a table on the lobby.
        /// </summary>
        public const string TeamColor = "TeamColor";
    }
}
