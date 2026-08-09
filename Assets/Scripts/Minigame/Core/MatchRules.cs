// The gamemode rulebook: the limits the config screen enforces, and the
// derivation from "what the host picked" to "what the match actually runs".
//
// Free-For-All and Battle Royale share one implementation — every entity in its
// own solo faction, hostile to all the others — and differ only in lives.
// Battle Royale forces exactly 1 (no respawns, last one standing wins), so it
// exposes no lives control at all; Free-For-All lets the host pick, and only
// collapses to last-standing when that pick happens to be 1.
//
// Kept free of UnityEngine references so the EditMode tests can reach it.
public static class MatchRules
{
    // Caps come straight from the pre-authored faction pool — 4 team factions
    // (2 used per Team Deathmatch match) and 16 solo factions. Nothing is
    // created at runtime, so these are hard ceilings, not preferences.
    public const int MaxTeamBotsPerSide = 4;
    public const int SoloFactionCount = 16;

    // One solo faction is held back for the human host, who occupies a slot in
    // the same pool as the bots.
    public const int MaxSoloBots = SoloFactionCount - 1;

    public const int MinKillTarget = 1;
    public const int MaxKillTarget = 50;
    public const int MinLives = 1;
    public const int MaxLives = 10;

    public static bool UsesTeams(MatchGameMode mode) => mode == MatchGameMode.TeamDeathmatch;

    public static int ClampTeamBots(int count) => Clamp(count, 0, MaxTeamBotsPerSide);

    // At least 1 bot: a solo match against nobody has no opponent to fight and
    // would be won the instant it started.
    public static int ClampSoloBots(int count) => Clamp(count, 1, MaxSoloBots);

    public static int ClampKillTarget(int target) => Clamp(target, MinKillTarget, MaxKillTarget);

    public static int ClampLives(int lives) => Clamp(lives, MinLives, MaxLives);

    public static WinCondition ResolveCondition(MatchGameMode mode, WinCondition selected, int soloLives)
    {
        switch (mode)
        {
            case MatchGameMode.TeamDeathmatch:
                return selected;
            case MatchGameMode.BattleRoyale:
                return WinCondition.LastStanding;
            default:
                // Solo modes have no team kill counter to race to, so the only
                // meaningful ending is "everyone else is out". With more than
                // one life that's the lives-exhausted evaluator; with exactly
                // one it's plain last-standing and no respawn machinery at all.
                return ClampLives(soloLives) > 1 ? WinCondition.LivesPerPlayer : WinCondition.LastStanding;
        }
    }

    public static int ResolveLives(MatchGameMode mode, WinCondition selected, int teamLives, int soloLives)
    {
        switch (mode)
        {
            case MatchGameMode.BattleRoyale:
                return 1;
            case MatchGameMode.FreeForAll:
                return ClampLives(soloLives);
            default:
                return selected == WinCondition.LivesPerPlayer ? ClampLives(teamLives) : 1;
        }
    }

    // Last Standing is the one condition that leaves the existing
    // despawn-on-death behaviour alone; the other two put entities back in.
    public static bool RespawnsEnabled(WinCondition resolved) => resolved != WinCondition.LastStanding;

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        return value > max ? max : value;
    }
}
