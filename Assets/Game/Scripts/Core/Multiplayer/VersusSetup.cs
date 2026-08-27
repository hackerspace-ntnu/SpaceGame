using SpaceGame.Gameplay;

namespace SpaceGame.Core
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
    /// <b>NOT</b> what Relay allocates: a later task allocates Relay for
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
        public readonly int TeamCount;
        public readonly int TeamSize;

        public VersusSetup(int teamCount, int teamSize)
        {
            TeamCount = VersusRules.ClampTeams(teamCount, teamSize);
            TeamSize = VersusRules.ClampTeamSize(teamSize, TeamCount);
            IsVersus = true;
        }

        /// <summary>
        /// What the lobby advertises as its maximum — NOT what Relay allocates. See the type doc.
        /// </summary>
        public int Seats => VersusRules.Seats(TeamCount, TeamSize);
    }
}
