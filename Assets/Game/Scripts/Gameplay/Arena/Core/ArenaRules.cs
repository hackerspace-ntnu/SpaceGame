namespace SpaceGame.Gameplay.Arena
{
    /// <summary>Where an arena match stands. Replicated as a byte, so append only.</summary>
    public enum ArenaPhase : byte
    {
        /// <summary>Not every side has someone on it yet. Nobody can be hurt.</summary>
        Waiting = 0,

        /// <summary>Everyone is stood at their team's spawn; the round starts when the clock runs out.</summary>
        Countdown = 1,

        /// <summary>The round is live: damage lands and a death is an elimination.</summary>
        Fighting = 2,

        /// <summary>A round was decided; the result is on screen before the next countdown.</summary>
        RoundOver = 3,

        /// <summary>A team reached the round target. Terminal — the match ends when people leave.</summary>
        MatchOver = 4
    }

    /// <summary>
    /// The arithmetic of an arena match: two teams, rounds of last-team-standing, first to a round
    /// target wins. Unity-free so the EditMode suite can pin it without a scene.
    /// </summary>
    public static class ArenaRules
    {
        /// <summary>An arena is always two sides — the lobby forces it, and every per-team array here is this long.</summary>
        public const int TeamCount = 2;

        /// <summary>What <see cref="EvaluateRound"/> answers when the round is not decided yet.</summary>
        public const int Undecided = -1;

        /// <summary>What <see cref="EvaluateRound"/> answers when both sides went down together.</summary>
        public const int Draw = -2;

        /// <summary>
        /// Decides a live round from how many players each side still has standing.
        ///
        /// Returns the winning team's index when exactly one side has anyone left,
        /// <see cref="Draw"/> when nobody is left at all — a rocket that took the last of both
        /// teams, say — and <see cref="Undecided"/> while both sides still have someone.
        /// </summary>
        public static int EvaluateRound(int aliveTeam0, int aliveTeam1)
        {
            bool zeroStanding = aliveTeam0 > 0;
            bool oneStanding = aliveTeam1 > 0;

            if (zeroStanding && oneStanding) return Undecided;
            if (zeroStanding) return 0;
            if (oneStanding) return 1;
            return Draw;
        }

        /// <summary>
        /// The team whose score has reached <paramref name="roundsToWin"/>, or -1 when nobody has.
        /// A target below one is read as one, so a misconfigured match still ends.
        /// </summary>
        public static int MatchWinner(int scoreTeam0, int scoreTeam1, int roundsToWin)
        {
            int target = roundsToWin < 1 ? 1 : roundsToWin;

            if (scoreTeam0 >= target) return 0;
            if (scoreTeam1 >= target) return 1;
            return -1;
        }

        /// <summary>Whether a round can start: both sides need at least one player in the arena.</summary>
        public static bool CanStartRound(int playersTeam0, int playersTeam1) =>
            playersTeam0 > 0 && playersTeam1 > 0;
    }
}
