using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Arena;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// The team shape a VS lobby is created or retuned with: how many teams, how big each one is.
    ///
    /// <para>
    /// Both numbers are clamped through <see cref="VersusRules"/> in the constructor, in the order
    /// its docs pin as the pairing contract — <see cref="VersusRules.ClampTeams"/> first, then
    /// <see cref="VersusRules.ClampTeamSize"/> fed that result — so a <see cref="VersusSetup"/> that
    /// exists at all is already inside both the per-axis limits and the seat ceiling. Nothing
    /// downstream has to re-clamp it.
    /// </para>
    ///
    /// <para>
    /// <see cref="Seats"/> is what the lobby advertises as its <c>MaxPlayers</c>. It is
    /// <b>NOT</b> what Relay allocates: <see cref="LobbySession.CreateAsync"/> allocates Relay for
    /// <see cref="VersusRules.MaxSeats"/> — the hard ceiling — once, at creation, because Relay's
    /// allocation size cannot change afterward. A host who grew the roster past whatever this
    /// struct's product happened to be at creation would be advertising seats nobody could
    /// actually connect to if Relay had been sized off this instead of off the ceiling.
    /// </para>
    /// </summary>
    public readonly struct VersusSetup
    {
        /// <summary>
        /// Not a versus match — a story lobby. Every field is the type's default, which is exactly
        /// what makes this safe: a default <see cref="bool"/> is false, so a <see cref="VersusSetup"/>
        /// nobody built on purpose reads as "not versus" without a separate flag that could go out
        /// of sync with the counts.
        /// </summary>
        public static readonly VersusSetup None = default;

        public readonly bool IsVersus;

        /// <summary>An arena match: versus rules locked to two teams. See <see cref="LobbyKeys.ModeArena"/>.</summary>
        public readonly bool IsArena;

        public readonly int TeamCount;
        public readonly int TeamSize;

        public VersusSetup(int teamCount, int teamSize) : this(teamCount, teamSize, arena: false) { }

        private VersusSetup(int teamCount, int teamSize, bool arena)
        {
            TeamCount = VersusRules.ClampTeams(teamCount, teamSize);
            TeamSize = VersusRules.ClampTeamSize(teamSize, TeamCount);
            IsVersus = true;
            IsArena = arena;
        }

        /// <summary>An arena lobby of two teams of <paramref name="teamSize"/>.</summary>
        public static VersusSetup Arena(int teamSize) => new(ArenaRules.TeamCount, teamSize, arena: true);

        /// <summary>The <see cref="LobbyKeys.Mode"/> value this setup is advertised under.</summary>
        public string Mode => IsArena ? LobbyKeys.ModeArena : IsVersus ? LobbyKeys.ModeVersus : LobbyKeys.ModeStory;

        /// <summary>
        /// What the lobby advertises as its maximum — NOT what Relay allocates. See the type doc.
        /// </summary>
        public int Seats => VersusRules.Seats(TeamCount, TeamSize);
    }
}
