namespace SpaceGame.Gameplay
{
    // Gamemode and win-condition enums live in the dependency-free Core assembly so
    // MatchRules can be exercised by the EditMode tests, which cannot reference the
    // predefined Assembly-CSharp. Assembly-CSharp auto-references this assembly, so
    // every existing unqualified use of these names keeps compiling unchanged.
    public enum MatchGameMode { TeamDeathmatch, FreeForAll, BattleRoyale }

    public enum WinCondition { KillTarget, LivesPerPlayer, LastStanding }
}
