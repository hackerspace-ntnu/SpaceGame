using System.Collections.Generic;

namespace SpaceGame.Gameplay
{
    // Pure win-condition evaluation for Team Deathmatch's 3 configurable variants
    // (design spec §6). Takes plain per-team-index dictionaries so MatchManager can
    // feed it real tracked state without this class depending on Netcode/HealthComponent.
    // Returns the winning team index, or null if the match should continue.
    public static class MatchWinEvaluator
    {
        public static int? EvaluateKillTarget(IReadOnlyDictionary<int, int> killsByTeam, int target)
        {
            int? best = null;
            int bestKills = -1;
            foreach (var pair in killsByTeam)
            {
                if (pair.Value < target) continue;
                if (pair.Value > bestKills)
                {
                    bestKills = pair.Value;
                    best = pair.Key;
                }
            }
            return best;
        }

        // A team is eliminated when its shared life pool hits 0. Winner is the last
        // team with lives remaining. If every team is simultaneously at 0 (edge case:
        // final exchange kills the last member of both teams at once), it's a draw —
        // returns null rather than an arbitrary team.
        public static int? EvaluateLivesExhausted(IReadOnlyDictionary<int, int> livesByTeam)
        {
            int teamsWithLives = 0;
            int? candidate = null;
            foreach (var pair in livesByTeam)
            {
                if (pair.Value > 0)
                {
                    teamsWithLives++;
                    candidate = pair.Key;
                }
            }
            return teamsWithLives == 1 ? candidate : null;
        }

        public static int? EvaluateLastStanding(IReadOnlyDictionary<int, int> livingCountByTeam)
        {
            int teamsAlive = 0;
            int? candidate = null;
            foreach (var pair in livingCountByTeam)
            {
                if (pair.Value > 0)
                {
                    teamsAlive++;
                    candidate = pair.Key;
                }
            }
            return teamsAlive == 1 ? candidate : null;
        }
    }
}
