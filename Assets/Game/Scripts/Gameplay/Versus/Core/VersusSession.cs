namespace SpaceGame.Gameplay
{
    /// <summary>
    /// What a VS match carries across the load into the world scene: which team this peer is on,
    /// how many there are, and what colour each one wears.
    ///
    /// <para>
    /// A static for the same reason <c>MatchSettings</c> and <c>WorldSession</c> are: the lobby
    /// that knows these values is destroyed by the very load that needs them, so there is no object
    /// to hang them off. Statics outlive returning to the menu, which is why
    /// <see cref="Clear"/> exists and why every route out of a match calls it — a session left
    /// standing is how the next match starts wearing the last one's colours.
    /// </para>
    ///
    /// <para>
    /// Only the LOCAL peer's team is here. Everyone else's arrives over the wire on
    /// <c>PlayerIdentity</c>, which is already the thing that replicates who a player is and what
    /// colour they are painted.
    /// </para>
    /// </summary>
    public static class VersusSession
    {
        /// <summary>Whether the world being entered is a versus match rather than a story world.</summary>
        public static bool IsActive { get; private set; }

        public static int TeamCount { get; private set; }

        public static int TeamSize { get; private set; }

        /// <summary>Which team this peer stands on, or -1 before that is known.</summary>
        public static int LocalTeam { get; private set; } = -1;

        private static int[] colors = System.Array.Empty<int>();

        /// <summary>
        /// Records the match the local peer is about to load into.
        ///
        /// <paramref name="teamColors"/> is copied rather than aliased: the caller is lobby UI,
        /// and lobby UI keeps its own colour array around to redraw the rules page — a team
        /// recolouring after <see cref="Begin"/> has already been called must not reach back into
        /// this static and change what the world scene reads.
        /// </summary>
        public static void Begin(int teamCount, int teamSize, int localTeam, int[] teamColors)
        {
            IsActive = true;
            TeamCount = teamCount;
            TeamSize = teamSize;
            LocalTeam = localTeam;
            colors = teamColors == null ? System.Array.Empty<int>() : (int[])teamColors.Clone();
        }

        public static void Clear()
        {
            IsActive = false;
            TeamCount = 0;
            TeamSize = 0;
            LocalTeam = -1;
            colors = System.Array.Empty<int>();
        }

        /// <summary>
        /// The swatch a team wears, or swatch 0 for a team this build has never heard of.
        ///
        /// Guarded rather than indexed because the team index can arrive from a peer — over the
        /// wire, from a build with a different team count — and a suit that is the wrong orange is
        /// a great deal easier to understand than a player who failed to spawn.
        /// </summary>
        public static int ColorOf(int team) =>
            team >= 0 && team < colors.Length ? colors[team] : 0;
    }
}
