using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    // Host-side match configuration, written by MinigameConfigUI before StartHost()
    // and read once by MatchManager after the arena scene loads (mirrors the
    // existing NetworkGameManager.PendingSceneNameToWaitFor static-field pattern
    // used by the minigame scene-load flow). Not networked — the host is always the
    // one who configures and starts the match.
    //
    // The enums themselves live in the Core assembly (MatchEnums.cs) so the rules
    // derived from these values stay unit-testable.
    public static class MatchSettings
    {
        public static MatchGameMode Mode = MatchGameMode.TeamDeathmatch;

        // Team Deathmatch: bots per side, on top of the human player(s).
        public static int AllyCount = 3;
        public static int EnemyCount = 4;

        // Free-For-All / Battle Royale: one total bot count, every bot its own faction.
        public static int SoloBotCount = 7;

        // Team Deathmatch only — the solo modes derive their condition from lives.
        public static WinCondition Condition = WinCondition.LastStanding;
        public static int KillTargetCount = 10;
        public static int LivesPerPlayerCount = 3;

        // Free-For-All only. Battle Royale forces 1 and hides the control.
        public static int SoloLivesCount = 3;

        public static WinCondition ResolvedCondition =>
            MatchRules.ResolveCondition(Mode, Condition, SoloLivesCount);

        public static int ResolvedLives =>
            MatchRules.ResolveLives(Mode, Condition, LivesPerPlayerCount, SoloLivesCount);

        public static bool RespawnsEnabled => MatchRules.RespawnsEnabled(ResolvedCondition);

        // Statics survive returning to the menu and starting a second match, so the
        // config screen reseeds them rather than trusting whatever the last match
        // left behind.
        public static void ResetToDefaults()
        {
            Mode = MatchGameMode.TeamDeathmatch;
            AllyCount = 3;
            EnemyCount = 4;
            SoloBotCount = 7;
            Condition = WinCondition.LastStanding;
            KillTargetCount = 10;
            LivesPerPlayerCount = 3;
            SoloLivesCount = 3;
        }

        // Applied on the way out of the config screen so MatchManager never sees a
        // count the authored faction pool cannot cover.
        public static void ClampToLimits()
        {
            AllyCount = MatchRules.ClampTeamBots(AllyCount);
            EnemyCount = MatchRules.ClampTeamBots(EnemyCount);
            SoloBotCount = MatchRules.ClampSoloBots(SoloBotCount);
            KillTargetCount = MatchRules.ClampKillTarget(KillTargetCount);
            LivesPerPlayerCount = MatchRules.ClampLives(LivesPerPlayerCount);
            SoloLivesCount = MatchRules.ClampLives(SoloLivesCount);
        }

        public static string Describe()
        {
            switch (Mode)
            {
                case MatchGameMode.TeamDeathmatch:
                    string goal = Condition switch
                    {
                        WinCondition.KillTarget => $"first to {KillTargetCount} kills",
                        WinCondition.LivesPerPlayer => $"{LivesPerPlayerCount} lives each",
                        _ => "last team standing",
                    };
                    return $"Team Deathmatch — {AllyCount} allied bots vs {EnemyCount} enemy bots, {goal}";
                case MatchGameMode.BattleRoyale:
                    return $"Battle Royale — {SoloBotCount} bots, one life, last one standing";
                default:
                    return $"Free-For-All — {SoloBotCount} bots, {SoloLivesCount} lives each";
            }
        }
    }
}
