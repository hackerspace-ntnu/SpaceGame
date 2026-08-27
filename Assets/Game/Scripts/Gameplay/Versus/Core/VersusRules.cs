namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The seat arithmetic behind VS: how many teams, how big, and what the host is allowed to
    /// change once people are standing in them.
    ///
    /// <para>
    /// Kept free of Unity types and in its own assembly so the EditMode tests can reach it, the
    /// same split <see cref="MatchRules"/> already uses. The limits are hard ceilings rather than
    /// preferences: <see cref="MaxSeats"/> is what a VS host allocates on Relay, and Relay's
    /// allocation size cannot be changed after the fact.
    /// </para>
    ///
    /// <para>
    /// Teams and team size are <b>not independent</b>. Their product is what is capped, so each
    /// clamp takes the other axis as an argument — clamping them separately is how a host ends up
    /// with 8×12 and a lobby that can seat a third of it.
    /// </para>
    /// </summary>
    public static class VersusRules
    {
        /// <summary>Two is the floor because one team is not a versus.</summary>
        public const int MinTeams = 2;

        public const int MinTeamSize = 1;

        /// <summary>
        /// The largest a team may be while two teams alone still fit the seat ceiling —
        /// 2 × 12 = <see cref="MaxSeats"/>. That product is what actually pins this number, not an
        /// arbitrary round figure.
        /// </summary>
        public const int MaxTeamSize = 12;

        /// <summary>
        /// What a VS host allocates on Relay, and therefore the hard ceiling on teams × size.
        ///
        /// Relay's allocation is sized once and for all when it is made, so the host allocates for
        /// this many and the lobby's advertised max follows the rules underneath it. A host who
        /// could grow past this would be advertising seats nobody can actually connect to.
        /// </summary>
        public const int MaxSeats = 24;

        /// <summary>Two teams of two — the smallest thing that is recognisably a match.</summary>
        public const int DefaultTeams = 2;

        public const int DefaultTeamSize = 2;

        /// <summary>
        /// The team names, in order. Spelled out rather than "TEAM 1" because these are read at a
        /// glance across a rank of astronauts, and a word is quicker to tell apart than a digit.
        /// </summary>
        private static readonly string[] Names =
        {
            "TEAM ONE", "TEAM TWO", "TEAM THREE", "TEAM FOUR",
            "TEAM FIVE", "TEAM SIX", "TEAM SEVEN", "TEAM EIGHT"
        };

        /// <summary>
        /// As many teams as there are names for them.
        ///
        /// Derived rather than hand-copied: a constant beside the array is a number somebody has to
        /// remember to change, and the tests cannot catch the drift — one missing name falls back to
        /// the generic <c>"TEAM"</c> in <see cref="TeamName"/>, which is still non-empty and still
        /// unique, so both name tests keep passing while the extra team has no name of its own.
        ///
        /// Declaration order below is load-bearing: this reads <see cref="Names"/>.Length at
        /// static-init time, so it must stay AFTER the <see cref="Names"/> field. Moving it above
        /// Names would read a null array and throw a <see cref="System.TypeInitializationException"/>
        /// — do not "tidy" the constants back to the top.
        /// </summary>
        public static readonly int MaxTeams = Names.Length;

        public static string TeamName(int team) =>
            team >= 0 && team < Names.Length ? Names[team] : "TEAM";

        public static int Seats(int teams, int teamSize) => teams * teamSize;

        /// <summary>
        /// Holds a team count inside its own limits and inside the seat ceiling.
        ///
        /// <para>
        /// The result is safe to pair with a size that has itself been through
        /// <see cref="ClampTeamSize"/> — that pairing is the contract, and callers get it by
        /// feeding this result straight into the other clamp. It is NOT a guarantee about a raw
        /// value the caller kept hold of: this returns a count and nothing else, so
        /// <c>ClampTeams(5, 1000)</c> answers 2 and a caller who then pairs that 2 with their own
        /// 1000 is still far over <see cref="MaxSeats"/>.
        /// </para>
        ///
        /// <para>
        /// <paramref name="teamSize"/> is clamped rather than merely floored before the ceiling is
        /// computed, which today changes no answer at all — <see cref="MaxTeamSize"/> ×
        /// <see cref="MinTeams"/> is exactly <see cref="MaxSeats"/>, so an oversized value already
        /// drove the ceiling down to <see cref="MinTeams"/> by a different route. It is here so the
        /// arithmetic stays sane if those constants ever stop lining up.
        /// </para>
        /// </summary>
        public static int ClampTeams(int teams, int teamSize)
        {
            int size = Clamp(teamSize, MinTeamSize, MaxTeamSize);
            int ceiling = MaxSeats / size;

            if (ceiling > MaxTeams) ceiling = MaxTeams;
            if (ceiling < MinTeams) ceiling = MinTeams;

            return Clamp(teams, MinTeams, ceiling);
        }

        /// <summary>
        /// Holds a team size inside its own limits and inside the seat ceiling. Symmetric with
        /// <see cref="ClampTeams"/>, and carrying the same caveat: this returns a size, so pairing
        /// it with a count that never went through <see cref="ClampTeams"/> proves nothing about
        /// the total.
        /// </summary>
        public static int ClampTeamSize(int teamSize, int teams)
        {
            int count = Clamp(teams, MinTeams, MaxTeams);
            int ceiling = MaxSeats / count;

            if (ceiling > MaxTeamSize) ceiling = MaxTeamSize;
            if (ceiling < MinTeamSize) ceiling = MinTeamSize;

            return Clamp(teamSize, MinTeamSize, ceiling);
        }

        /// <summary>
        /// Whether the host may set this team size, given who is already standing where.
        ///
        /// Refused rather than reassigned: a player moved out of the team they chose, by someone
        /// else, with no warning, is worse than a host being told no. <paramref name="refusal"/> is
        /// a sentence fit to put straight on the lobby's status line.
        /// </summary>
        public static bool CanSetTeamSize(int teamSize, int[] occupancy, out string refusal)
        {
            refusal = null;
            if (occupancy == null) return true;

            for (int team = 0; team < occupancy.Length; team++)
            {
                if (occupancy[team] <= teamSize) continue;

                refusal = $"{TeamName(team)} has {occupancy[team]} players.";
                return false;
            }

            return true;
        }

        /// <summary>Whether the host may drop to this many teams. A team with anyone in it stays.</summary>
        public static bool CanSetTeamCount(int teams, int[] occupancy, out string refusal)
        {
            refusal = null;
            if (occupancy == null) return true;

            // A negative count is not a real request, but this class exists to BE the guard — a
            // rulebook that throws is worse than one that refuses, so the loop floor never goes
            // below the array's start.
            for (int team = teams < 0 ? 0 : teams; team < occupancy.Length; team++)
            {
                if (occupancy[team] <= 0) continue;

                refusal = $"{TeamName(team)} has {occupancy[team]} players.";
                return false;
            }

            return true;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
